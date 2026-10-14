using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace DshInstaller.Shared.Backup.Official
{
    public sealed class DataGroup
    {
        public string Id, Name, EnglishName, Path;
        public bool Default;
    }
    public static class Migration
    {
        private static readonly string[] CoreBundles = { "@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app", "@deepseek-ai/dsh-desktop-app",
            "@deepseek-ai/dsh-headless", "@deepseek-ai/dsh-acp-app", "@deepseek-ai/dsh-sdk-app", "@deepseek-ai/dsh-sdk-minimal" };
        private static readonly HashSet<string> Excluded = new HashSet<string>(new[] { "node_modules", ".git", ".cache", ".pnpm", "cache", "lock", "pnpm-lock.yaml",
            "aries", "aries-identity", "aries-groups", "connection-identity", "connection-cache", "push-tokens", "identity.json", "device-identity.json",
            "pairing.json", "node-pin.json", "private-key.json", "private_key.pem", "id_ed25519", "id_rsa", "aries-identity.dpapi" }, StringComparer.OrdinalIgnoreCase);
        private sealed class SourceFile
        {
            public string Source, Target, Group;
            public byte[] Content;
            public long Length;
        }
        private sealed class Scan
        {
            public string Home, Root, Agents;
            public long Bytes;
            public int Visited;
            public bool ProfileCollected;
            public List<SourceFile> Files = new List<SourceFile>();
            public HashSet<string> Targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<string> Warnings = new List<string>();
            public CancellationToken Token;
            public Action<string, double> Progress;
            public void Add(string source, string target, string group, byte[] content)
            {
                Token.ThrowIfCancellationRequested(); Protocol.Relative(target); ValidateTarget(target, group);
                if (!Targets.Add(target)) return;
                if (content == null) { Protocol.Plain(source); if (!File.Exists(source)) throw new FileNotFoundException("Source disappeared: " + source); }
                long size = content == null ? new FileInfo(source).Length : content.Length;
                Bytes = checked(Bytes + size);
                if (Bytes > Protocol.MaxBytes || Files.Count >= Protocol.MaxFiles) throw new InvalidDataException("Export size/count limit exceeded.");
                Files.Add(new SourceFile { Source = source, Target = target, Group = group, Content = content, Length = size });
            }
        }
        public static bool Desktop(string root)
        { return !String.IsNullOrWhiteSpace(root) && File.Exists(System.IO.Path.Combine(root, "resources", "runtime", "cli", "bin", "dsh.cmd")); }
        public static string ResolveHome(string root, string explicitHome)
        {
            // An explicit source never falls back to another user's home or another installation.
            if (!String.IsNullOrWhiteSpace(explicitHome)) return Protocol.Full(explicitHome);
            if (String.IsNullOrWhiteSpace(root)) root = LauncherRoot();
            if (!String.IsNullOrWhiteSpace(root))
            {
                root = Protocol.Full(root);
                if (!Desktop(root)) return Protocol.Full(System.IO.Path.Combine(root, ".dsh"));
            }
            string environment = Environment.GetEnvironmentVariable("DSH_HOME");
            return Protocol.Full(!String.IsNullOrWhiteSpace(environment) ? environment : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"));
        }
        public static string AgentsHome()
        {
            string value = Environment.GetEnvironmentVariable("DSH_AGENTS_HOME");
            return Protocol.Full(!String.IsNullOrWhiteSpace(value) ? value : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agents"));
        }
        private static string LauncherRoot()
        {
            string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepSeekHarness", "LauncherSettings.json");
            if (!File.Exists(path)) return null;
            Protocol.Plain(path);
            Dictionary<string, object> json = Json.Object(File.ReadAllText(path));
            return Json.String(json, "dshRoot");
        }
        public static List<DataGroup> Describe(string root, string explicitHome)
        {
            string home = ResolveHome(root, explicitHome); Protocol.Plain(home);
            if (!Directory.Exists(home)) throw new DirectoryNotFoundException("The selected DSH_HOME does not exist: " + home);
            var groups = new List<DataGroup>();
            AddGroup(groups, "profiles", "配置与插件清单（配置可能含敏感数据）", "Settings and profile manifests (may contain secrets)", System.IO.Path.Combine(home, "profiles"), true);
            AddGroup(groups, "sessions", "会话、存储与附件", "Sessions, storage and attachments", System.IO.Path.Combine(home, "sessions"), false, System.IO.Path.Combine(home, "storages"), System.IO.Path.Combine(home, "attachments"));
            AddGroup(groups, "official-attachments", "附件", "Attachments", System.IO.Path.Combine(home, "attachments"), false);
            AddGroup(groups, "skills", "用户 Skill（HOME 与共享技能）", "User skills (HOME and shared skills)", System.IO.Path.Combine(home, "skills"), true, System.IO.Path.Combine(AgentsHome(), "skills"), Legacy(root, "skills"));
            AddGroup(groups, "official-shared-skills", "共享 Skill（DSH_AGENTS_HOME）", "Shared skills (DSH_AGENTS_HOME)", System.IO.Path.Combine(AgentsHome(), "skills"), false);
            AddGroup(groups, "plugins", "插件源码与启用清单", "Plugin sources and enabled manifest", System.IO.Path.Combine(home, "plugins"), true, System.IO.Path.Combine(home, "profiles"), Legacy(root, "plugins"));
            AddGroup(groups, "official-credentials", "账号配置（含 API key / token，敏感数据）", "Account configuration (API keys/tokens, sensitive)", System.IO.Path.Combine(home, ".credentials.yaml"), false, System.IO.Path.Combine(home, ".env"));
            if (!String.IsNullOrWhiteSpace(root)) AddGroup(groups, "official-compatibility", "Dafeiyu 兼容偏好（官方不直接读取，含敏感数据）", "Dafeiyu compatibility preferences (not read by official DSH, sensitive)", System.IO.Path.Combine(root, ".dafeiyu"), false);
            return groups;
        }
        private static string Legacy(string root, string name)
        { return String.IsNullOrWhiteSpace(root) || Desktop(root) ? null : System.IO.Path.Combine(root, name); }
        private static void AddGroup(List<DataGroup> list, string id, string name, string english, string path, bool selected, params string[] others)
        {
            if (new[] { path }.Concat(others).Any(p => !String.IsNullOrWhiteSpace(p) && (Directory.Exists(p) || File.Exists(p))))
                list.Add(new DataGroup { Id = id, Name = name, EnglishName = english, Path = path, Default = selected });
        }
        public static Outcome Export(string root, string explicitHome, IList<string> selected, string outputExe,
            Action<string, double> progress, Action<string> log, CancellationToken token)
        {
            var result = new Outcome(); string staging = null;
            try
            {
                token.ThrowIfCancellationRequested();
                string home = ResolveHome(root, explicitHome), output = Protocol.Full(outputExe), agents = AgentsHome();
                Protocol.Plain(home); Protocol.Plain(output);
                if (!Directory.Exists(home)) throw new DirectoryNotFoundException("The selected DSH_HOME does not exist: " + home);
                if (System.IO.Path.GetExtension(output).ToLowerInvariant() != ".exe") throw new ArgumentException("Export output must be an EXE.");
                if (Protocol.Contains(root, output) || Protocol.Contains(home, output) || Protocol.Contains(agents, output)) throw new InvalidDataException("Output must be outside install root, DSH_HOME and DSH_AGENTS_HOME.");
                if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output already exists and will not be replaced.");
                if (selected == null || selected.Count == 0) throw new ArgumentException("Select at least one data group.");
                var ids = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
                var scan = new Scan { Home = home, Root = root, Agents = agents, Token = token, Progress = progress };
                foreach (string id in ids) Collect(scan, id);
                if (scan.Files.Count == 0) throw new InvalidDataException("The selected source/groups contain no exportable official DSH data.");
                result.Warnings.AddRange(scan.Warnings.Distinct());
                string directory = System.IO.Path.GetDirectoryName(output);
                Directory.CreateDirectory(directory); Protocol.Plain(directory);
                staging = System.IO.Path.Combine(directory, ".official-dsh-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
                string zipPath = System.IO.Path.Combine(staging, "payload.zip"), partial = System.IO.Path.Combine(staging, "output.partial");
                PayloadManifest manifest = new PayloadManifest { Warnings = result.Warnings }; long done = 0;
                using (FileStream file = new FileStream(zipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create, false, Encoding.UTF8))
                {
                    foreach (SourceFile item in scan.Files)
                    {
                        token.ThrowIfCancellationRequested(); if (item.Content == null) Protocol.Plain(item.Source);
                        ZipArchiveEntry entry = zip.CreateEntry("data/" + item.Target, CompressionLevel.Fastest); byte[] hash;
                        using (Stream source = item.Content == null ? (Stream)new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read) : new MemoryStream(item.Content))
                        using (Stream destination = entry.Open())
                        {
                            if (source.Length != item.Length) throw new IOException("Source changed during export: " + item.Source);
                            hash = Protocol.CopyHash(source, destination, item.Length, token, delegate(long n) { done += n; if (progress != null) progress("Packing " + item.Target, 10 + done * 65d / Math.Max(1, scan.Bytes)); });
                            if (source.Position != item.Length) throw new IOException("Source changed during export.");
                        }
                        manifest.Files.Add(new PayloadFile { Path = entry.FullName, Target = item.Target, Group = item.Group, Hash = Protocol.Hex(hash), Length = item.Length });
                    }
                    using (Stream destination = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open()) Protocol.ManifestWrite(destination, manifest);
                }
                using (Stream template = Template())
                using (FileStream destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { Protocol.CopyHash(template, destination, 16 * 1024 * 1024, token, null); using (FileStream zip = File.OpenRead(zipPath)) { byte[] hash = Protocol.CopyHash(zip, destination, Protocol.MaxBytes, token, null); Protocol.Footer(destination, zip.Length, hash); } destination.Flush(true); }
                if (progress != null) progress("Verifying complete self-extracting archive", 80);
                Protocol.Validate(partial, token); token.ThrowIfCancellationRequested(); Protocol.Plain(output);
                File.Move(partial, output); result.Ok = true; result.OutputPath = output;
                result.Summary = "Exported " + scan.Files.Count + " files / " + scan.Bytes.ToString("N0") + " bytes. " + result.Warnings.Count + " warnings.";
            }
            catch (OperationCanceledException) { result.Canceled = true; result.Error = "Export canceled; existing/completed output was preserved."; }
            catch (Exception ex) { result.Error = ex.Message; }
            finally { if (staging != null) Cleanup(staging, result); }
            // UI callbacks cannot turn an already committed output into a reported failure.
            try { if (log != null) log(result.Ok ? result.Summary : result.Error); if (progress != null && result.Ok) progress(result.Summary, 100); } catch { }
            return result;
        }
        private static Stream Template()
        {
            Stream resource = typeof(Migration).Assembly.GetManifestResourceStream("DshInstaller.OfficialDshRestore.exe");
            if (resource != null) return resource;
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OfficialDshRestore.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("Missing compiled OfficialDshRestore.exe template.");
            Protocol.Plain(path); return File.OpenRead(path);
        }
        private static void Collect(Scan scan, string id)
        {
            scan.Token.ThrowIfCancellationRequested(); if (scan.Progress != null) scan.Progress("Scanning " + id, Double.NaN);
            switch (id.ToLowerInvariant())
            {
                case "sessions": foreach (string name in new[] { "sessions", "storages", "attachments" }) Tree(scan, System.IO.Path.Combine(scan.Home, name), "home/" + name, id, null, 0); break;
                case "official-attachments": Tree(scan, System.IO.Path.Combine(scan.Home, "attachments"), "home/attachments", id, null, 0); break;
                case "skills":
                    Tree(scan, System.IO.Path.Combine(scan.Home, "skills"), "home/skills", id, null, 0);
                    Tree(scan, Legacy(scan.Root, "skills"), "home/skills", id, null, 0);
                    Tree(scan, System.IO.Path.Combine(scan.Agents, "skills"), "agents/skills", id, null, 0); break;
                case "official-shared-skills": Tree(scan, System.IO.Path.Combine(scan.Agents, "skills"), "agents/skills", id, null, 0); break;
                case "official-credentials":
                    foreach (string name in new[] { ".credentials.yaml", ".env" })
                        if (File.Exists(System.IO.Path.Combine(scan.Home, name))) scan.Add(System.IO.Path.Combine(scan.Home, name), "home/" + name, id, null);
                    scan.Warnings.Add("Account configuration contains sensitive API keys/tokens. Existing target credentials are always preserved."); break;
                case "portable": case "official-compatibility":
                    if (!String.IsNullOrWhiteSpace(scan.Root))
                        Tree(scan, System.IO.Path.Combine(scan.Root, ".dafeiyu"), "compat/.dafeiyu", "official-compatibility", null, 0);
                    scan.Warnings.Add("Dafeiyu compatibility preferences may contain secrets. Official DSH cannot read them; restored only to DSH_HOME/.dafeiyu-compat-import."); break;
                case "profiles": case "plugins":
                    if (id == "plugins")
                    { Tree(scan, System.IO.Path.Combine(scan.Home, "plugins"), "home/plugins", id, null, 0); Tree(scan, Legacy(scan.Root, "plugins"), "home/plugins", id, null, 0); }
                    Profile(scan, id); break;
                default: throw new InvalidDataException("Unknown export group: " + id);
            }
        }
        private static void Tree(Scan scan, string path, string target, string group, HashSet<string> ancestors, int depth)
        {
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            scan.Token.ThrowIfCancellationRequested(); if (depth > 64 || ++scan.Visited > 1000000) throw new InvalidDataException("Source traversal depth/count limit exceeded.");
            Protocol.Plain(path);
            foreach (string child in Directory.EnumerateFileSystemEntries(path))
            {
                scan.Token.ThrowIfCancellationRequested(); if (++scan.Visited > 1000000) throw new InvalidDataException("Source entry count exceeded.");
                FileAttributes attributes = File.GetAttributes(child); string name = System.IO.Path.GetFileName(child);
                if (Excluded.Contains(name)) continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0) { scan.Warnings.Add("Skipped source link: " + child); continue; }
                string mapped = target + "/" + name;
                if ((attributes & FileAttributes.Directory) != 0) Tree(scan, child, mapped, group, ancestors, depth + 1);
                else
                {
                    if (group == "official-compatibility" && !CompatibilityPath(mapped.Substring("compat/".Length))) continue;
                    scan.Add(child, mapped, group, null);
                }
            }
        }
        private static void Profile(Scan scan, string group)
        {
            if (scan.ProfileCollected) return;
            scan.ProfileCollected = true;
            string profiles = System.IO.Path.Combine(scan.Home, "profiles"); if (!Directory.Exists(profiles)) return;
            Protocol.Plain(profiles);
                string primary = Desktop(scan.Root) ? "desktop" : "web";
            if (!Directory.Exists(System.IO.Path.Combine(profiles, primary))) primary = primary == "web" ? "desktop" : "web";
            string directory = System.IO.Path.Combine(profiles, primary); if (!Directory.Exists(directory)) return;
            Protocol.Plain(directory); string manifestPath = System.IO.Path.Combine(directory, "package.json");
            if (File.Exists(manifestPath))
            {
                Protocol.Plain(manifestPath); if (new FileInfo(manifestPath).Length > 2 * 1024 * 1024) throw new InvalidDataException("Oversized plugin manifest.");
                Dictionary<string, object> manifest = Json.Object(File.ReadAllText(manifestPath));
                Dictionary<string, object> dependencies = Json.Child(manifest, "dependencies", true);
                var unavailable = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in dependencies.Keys.ToList())
                {
                    if (CoreBundles.Contains(name)) continue; ModuleName(name);
                    string spec = dependencies[name] as string; if (spec == null) throw new InvalidDataException("Invalid plugin dependency specification.");
                    if (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || spec.StartsWith("link:", StringComparison.OrdinalIgnoreCase))
                    {
                        string source = Protocol.Full(System.IO.Path.Combine(directory, spec.Substring(spec.IndexOf(':') + 1)));
                        string localTarget = "home/plugins/migration/" + name;
                        try
                        {
                            bool safe = Protocol.Contains(scan.Home, source) || Protocol.Contains(Legacy(scan.Root, "plugins"), source);
                            if (!safe || !Directory.Exists(source)) throw new InvalidDataException("Local plugin is missing or outside selected data roots.");
                            Protocol.Plain(source); int before = scan.Files.Count;
                            Tree(scan, source, localTarget, group, null, 0);
                            if (scan.Files.Count == before) throw new InvalidDataException("Local plugin has no portable files.");
                            dependencies[name] = "file:../../plugins/migration/" + name;
                            scan.Warnings.Add("Local plugin " + name + " source is portable; its npm runtime dependencies still require official CLI rebuild.");
                        }
                        catch (Exception ex)
                        {
                            if (!(ex is IOException) && !(ex is InvalidDataException) && !(ex is UnauthorizedAccessException) && !(ex is ArgumentException)) throw;
                            dependencies.Remove(name); unavailable.Add(name); scan.Warnings.Add("Unavailable local plugin removed from enabled manifest: " + name + ": " + ex.Message);
                        }
                    }
                    else if (spec.IndexOf(':') >= 0 && !spec.StartsWith("npm:", StringComparison.OrdinalIgnoreCase)
                        || spec.IndexOf('/') >= 0 || spec.IndexOf('\\') >= 0)
                    { dependencies.Remove(name); unavailable.Add(name); scan.Warnings.Add("Nonportable plugin specification removed: " + name + ". Re-add using official CLI."); }
                }
                Dictionary<string, object> dsh = Json.Child(manifest, "dsh", true), profile = Json.Child(dsh, "profile", true);
                List<object> bundles = Json.Array(profile, "bundles", true); bundles.RemoveAll(value => unavailable.Contains(value as string ?? ""));
                scan.Add(null, "home/profiles/desktop/package.json", group, Encoding.UTF8.GetBytes(Json.Serialize(manifest)));
            }
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string name = System.IO.Path.GetFileName(file);
                if (name == "package.json" || Excluded.Contains(name)) continue;
                if (name != "cordis.yml" && name != "cordis.patch.yml" && name != "pnpm-workspace.yaml" && name != "compatibility.json") continue;
                scan.Add(file, "home/profiles/desktop/" + name, group, null);
            }
            scan.Warnings.Add("Profile " + primary + " maps to official desktop. Config files can contain secrets. node_modules is not copied; use official dsh plugin --profile desktop install to rebuild. Version approvals/native dependencies may require manual revalidation.");
        }
        private static void ModuleName(string name)
        { Protocol.Relative(name); string[] parts = name.Split('/'); if (parts.Length > 2 || parts.Length == 2 && !parts[0].StartsWith("@")) throw new InvalidDataException("Invalid npm package name."); }
        private static bool CompatibilityPath(string path)
        {
            return new[] { ".dafeiyu/preferences/pc.json", ".dafeiyu/preferences/android.json", ".dafeiyu/config/providers.json", ".dafeiyu/config/credentials.json", ".dafeiyu/android/workspace.json" }.Contains(path, StringComparer.Ordinal)
                || path.StartsWith(".dafeiyu/android/model-history/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal);
        }
        public static void ValidateTarget(string target, string group)
        {
            Protocol.Relative(target); string lower = target.ToLowerInvariant(); bool ok = false;
            if (group == "sessions") ok = new[] { "home/sessions/", "home/storages/", "home/attachments/" }.Any(lower.StartsWith);
            else if (group == "official-attachments") ok = lower.StartsWith("home/attachments/");
            else if (group == "skills" || group == "official-shared-skills") ok = lower.StartsWith("home/skills/") || lower.StartsWith("agents/skills/");
            else if (group == "profiles" || group == "plugins") ok = lower.StartsWith("home/plugins/") || lower.StartsWith("home/profiles/desktop/");
            else if (group == "official-credentials") ok = lower == "home/.credentials.yaml" || lower == "home/.env";
            else if (group == "official-compatibility") ok = target.StartsWith("compat/", StringComparison.Ordinal) && CompatibilityPath(target.Substring(7));
            if (!ok || target.Split('/').Any(Excluded.Contains)) throw new InvalidDataException("Disallowed official data target: " + target);
        }
        public static bool OfficialRunning(string officialRoot)
        {
            foreach (Process process in Process.GetProcesses())
                using (process)
                {
                    try
                    {
                        if (process.Id == Process.GetCurrentProcess().Id) continue;
                        if (!String.IsNullOrWhiteSpace(officialRoot) && Protocol.Contains(officialRoot, process.MainModule.FileName)) return true;
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        string image = ProcessImage(process.Id);
                        if (image != null && Protocol.Contains(officialRoot, image)) return true;
                    }
                    catch (InvalidOperationException) { }
                }
            return false;
        }
        public static List<string> DetectOfficialRoots()
        {
            var roots = new List<string>();
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (RegistryView view in new[] { RegistryView.Default, RegistryView.Registry64, RegistryView.Registry32 }.Distinct())
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (string name in uninstall.GetSubKeyNames())
                            using (RegistryKey item = uninstall.OpenSubKey(name))
                            {
                                string display = item == null ? null : item.GetValue("DisplayName") as string;
                                if (display == null || display.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                string root = item.GetValue("InstallLocation") as string;
                                if (String.IsNullOrWhiteSpace(root))
                                {
                                    string icon = item.GetValue("DisplayIcon") as string;
                                    if (!String.IsNullOrWhiteSpace(icon))
                                    {
                                        string value = icon.Trim();
                                        int quote = value.StartsWith("\"") ? value.IndexOf('"', 1) : -1;
                                        root = quote > 0 ? value.Substring(1, quote - 1) : value.EndsWith(",0") ? value.Substring(0, value.Length - 2) : value;
                                    }
                                    if (!String.IsNullOrWhiteSpace(root) && File.Exists(root)) root = Path.GetDirectoryName(root);
                                }
                                if (IsOfficialRoot(root) && !roots.Any(existing => String.Equals(existing, Protocol.Full(root), StringComparison.OrdinalIgnoreCase))) roots.Add(Protocol.Full(root));
                            }
                        }
                    }
                    catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
                }
            return roots;
        }
        public static string DetectOfficialRoot()
        {
            List<string> roots = DetectOfficialRoots(); return roots.Count == 1 ? roots[0] : null;
        }
        private static bool IsOfficialRoot(string root)
        {
            if (String.IsNullOrWhiteSpace(root)) return false;
            try { Protocol.Plain(root); return File.Exists(Path.Combine(root, "DeepSeek Harness.exe")) && Desktop(root); }
            catch (IOException) { return false; } catch (InvalidDataException) { return false; } catch (ArgumentException) { return false; }
        }
        private static string ProcessImage(int pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid); if (handle == IntPtr.Zero) return null;
            try { var text = new StringBuilder(32768); int length = text.Capacity; return QueryFullProcessImageName(handle, 0, text, ref length) ? text.ToString() : null; }
            finally { CloseHandle(handle); }
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder text, ref int length);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        public static Outcome Restore(string exe, string home, string agents, string officialRoot, IList<string> groups,
            bool mergePlugins, bool confirmedClosed, Action<string, double> progress, CancellationToken token)
        {
            var result = new Outcome(); string staging = null; bool held = false;
            string mutexId;
            using (var sha = System.Security.Cryptography.SHA256.Create()) mutexId = Protocol.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(Protocol.Full(home).ToUpperInvariant())));
            using (Mutex mutex = new Mutex(false, "Dafeiyu-OfficialRestore-" + mutexId))
            try
            {
                if (!confirmedClosed) throw new InvalidOperationException("Confirm that official DSH is closed before restoring.");
                if (!Desktop(officialRoot)) throw new InvalidDataException("Select an official DSH installation containing resources/runtime/cli/bin/dsh.cmd.");
                if (OfficialRunning(officialRoot)) throw new IOException("Official DSH is still running. Close it normally and retry.");
                home = Protocol.Full(home); agents = Protocol.Full(agents); Protocol.Plain(home); Protocol.Plain(agents); Protocol.Plain(exe);
                if (Protocol.Contains(officialRoot, home) || Protocol.Contains(officialRoot, agents)) throw new InvalidDataException("Data directories must be outside official installation resources.");
                if (Protocol.Contains(home, exe) || Protocol.Contains(agents, exe)) throw new InvalidDataException("The archive must be outside restore data homes.");
                try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held) throw new IOException("Another restore is running for this data home.");
                using (FileStream file = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (Stream segment = Protocol.Segment(file, token))
                using (ZipArchive zip = new ZipArchive(segment, ZipArchiveMode.Read, false, Encoding.UTF8))
                {
                    if (progress != null) progress("Verifying every archive entry before writing", 0);
                    PayloadManifest manifest = Protocol.ValidateZip(zip, token);
                    if (manifest.Warnings != null) result.Warnings.AddRange(manifest.Warnings);
                    var selected = new HashSet<string>(groups ?? new string[0], StringComparer.Ordinal);
                    var files = manifest.Files.Where(f => selected.Contains(f.Group)).ToList();
                    if (files.Count == 0) throw new ArgumentException("Select at least one nonempty data group.");
                    // Preflight every destination before creating staging or writing user data.
                    foreach (PayloadFile entry in files) Destination(home, agents, entry.Target);
                    staging = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Protocol.Full(exe)), ".official-dsh-restore-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Protocol.Plain(staging); Directory.CreateDirectory(staging);
                    int index = 0;
                    foreach (PayloadFile entry in files)
                    {
                        token.ThrowIfCancellationRequested(); string staged = Protocol.Within(staging, entry.Target);
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(staged));
                        using (Stream source = zip.GetEntry(entry.Path).Open())
                        using (FileStream target = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            if (Protocol.Hex(Protocol.CopyHash(source, target, entry.Length, token, null)) != entry.Hash) throw new InvalidDataException("Entry changed after verification.");
                        if (progress != null) progress("Prepared " + entry.Target, 30 + ++index * 40d / files.Count);
                    }
                    if (OfficialRunning(officialRoot)) throw new IOException("Official DSH started during restore; no files were committed.");
                    foreach (PayloadFile entry in files)
                    {
                        token.ThrowIfCancellationRequested();
                        string target = Destination(home, agents, entry.Target), staged = Protocol.Within(staging, entry.Target);
                        if (entry.Target == "home/profiles/desktop/package.json" && mergePlugins)
                        { MergePackage(staged, target, result, token); continue; }
                        if (File.Exists(target) || Directory.Exists(target)) { result.Skipped++; result.Warnings.Add("Preserved existing target: " + entry.Target); continue; }
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)); Protocol.Plain(target);
                        AtomicNew(staged, target, token); result.Imported++;
                    }
                    result.Ok = result.Imported > 0; if (!result.Ok) result.Error = "No data was imported; all selected targets already existed and were preserved.";
                }
            }
            catch (OperationCanceledException) { result.Canceled = true; result.Error = "Restore canceled. Already committed files were retained."; }
            catch (Exception ex) { result.Error = ex.Message; }
            finally { if (staging != null) Cleanup(staging, result); if (held) mutex.ReleaseMutex(); }
            result.Summary = "Imported " + result.Imported + "; preserved/skipped " + result.Skipped + "; warnings " + result.Warnings.Count + ".";
            return result;
        }
        private static string Destination(string home, string agents, string target)
        {
            if (target.StartsWith("home/")) return Protocol.Within(home, target.Substring(5));
            if (target.StartsWith("agents/")) return Protocol.Within(agents, target.Substring(7));
            if (target.StartsWith("compat/")) return Protocol.Within(home, ".dafeiyu-compat-import/" + target.Substring(7));
            throw new InvalidDataException("Unknown destination mapping.");
        }
        private static void AtomicNew(string staged, string target, CancellationToken token)
        {
            string partial = target + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                Protocol.Plain(target);
                using (FileStream input = File.OpenRead(staged))
                using (FileStream output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { Protocol.CopyHash(input, output, Protocol.MaxBytes, token, null); output.Flush(true); }
                token.ThrowIfCancellationRequested(); Protocol.Plain(target); File.Move(partial, target);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        private static void MergePackage(string staged, string target, Outcome result, CancellationToken token)
        {
            Protocol.Plain(target); string original = File.Exists(target) ? File.ReadAllText(target) : null;
            Dictionary<string, object> source = Json.Object(File.ReadAllText(staged));
            Dictionary<string, object> dest = original == null ? Json.Object("{\"name\":\"dsh-profile-desktop\",\"private\":true,\"dependencies\":{},\"dsh\":{\"profile\":{\"bundles\":[\"@deepseek-ai/dsh-base\",\"@deepseek-ai/dsh-web-app\"]}}}") : Json.Object(original);
            Dictionary<string, object> deps = Json.Child(dest, "dependencies", true), sourceDeps = Json.Child(source, "dependencies", true);
            HashSet<string> conflicts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in sourceDeps)
            { if (CoreBundles.Contains(pair.Key)) continue; if (deps.ContainsKey(pair.Key)) { conflicts.Add(pair.Key); result.Warnings.Add("Existing plugin dependency preserved: " + pair.Key); } else deps.Add(pair.Key, pair.Value); }
            List<object> bundles = Json.Array(Json.Child(Json.Child(dest, "dsh", true), "profile", true), "bundles", true);
            foreach (object value in Json.Array(Json.Child(Json.Child(source, "dsh", true), "profile", true), "bundles", true))
            { string name = value as string; if (name == null) throw new InvalidDataException("Invalid profile bundle name."); if (!CoreBundles.Contains(name) && !conflicts.Contains(name) && !bundles.Contains(name)) bundles.Add(name); }
            string merged = Json.Serialize(dest);
            if (original != null && Json.Serialize(Json.Object(original)) == merged) { result.Skipped++; return; }
            File.WriteAllText(staged, merged, new UTF8Encoding(false)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
            if (original == null) AtomicNew(staged, target, token);
            else
            {
                string partial = target + ".partial-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(staged, partial, false); token.ThrowIfCancellationRequested(); Protocol.Plain(target);
                    if (File.ReadAllText(target) != original) throw new IOException("Desktop manifest changed during restore.");
                    string backup = target + ".before-official-restore-" + Guid.NewGuid().ToString("N") + ".json";
                    File.Replace(partial, target, backup); result.Warnings.Add("Desktop plugin manifest merged; original retained at " + backup);
                }
                finally { if (File.Exists(partial)) File.Delete(partial); }
            }
            result.Imported++;
        }
        private static void Cleanup(string staging, Outcome result)
        { try { Protocol.Plain(staging); if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (Exception ex) { result.Warnings.Add("Staging cleanup failed: " + ex.Message); } }
    }
}
