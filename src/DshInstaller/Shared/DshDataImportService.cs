using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace DeepSeekHarnessLauncher.Backup
{
    internal sealed class OfficialImportGroup
    {
        internal string Id, Name;
        internal int Files, ExistingUnits;
        internal long Bytes;
    }

    internal sealed class OfficialImportPlan
    {
        internal string SourceHome, TargetHome, SourceProfile, TargetProfile, LegacyRoot;
        internal List<OfficialImportGroup> Groups = new List<OfficialImportGroup>();
        internal List<OfficialImportFile> Files = new List<OfficialImportFile>();
        internal string Manifest;
        internal bool ManifestExists;
        internal List<string> Warnings = new List<string>();
        internal HashSet<string> UnavailableModules = new HashSet<string>(StringComparer.Ordinal);
        internal bool ForExport;
    }

    internal sealed class OfficialImportFile
    {
        internal string Group, Source, Relative, Unit;
        internal long Length;
        internal byte[] Hash;
    }

    internal sealed class OfficialImportResult
    {
        internal bool Ok, Canceled;
        internal int Imported, Skipped;
        internal string Error;
        internal string Summary => "已导入 " + Imported + " 个文件，保留并跳过 " + Skipped + " 个已有文件。";
    }

    internal static class DshDataImportService
    {
        private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private static readonly HashSet<string> DefaultBundles = new HashSet<string>(StringComparer.Ordinal)
        {
            "@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app", "@deepseek-ai/dsh-desktop-app",
            "@deepseek-ai/dsh-headless", "@deepseek-ai/dsh-acp-app", "@deepseek-ai/dsh-sdk-app", "@deepseek-ai/dsh-sdk-minimal"
        };

        internal static string ResolveSourceHome(string selected)
        {
            string root = Path.GetFullPath(selected ?? throw new ArgumentNullException(nameof(selected)));
            RequirePlainAncestors(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("所选目录不存在。");
            string nested = Path.Combine(root, ".dsh");
            if (Directory.Exists(nested)) root = nested;
            if (!new[] { "profiles", "sessions", "storages", "skills", "plugins" }.Any(name => Directory.Exists(Path.Combine(root, name))))
                throw new InvalidDataException("没有找到官方 DSH 数据，请选择包含 profiles、sessions 或 skills 的 .dsh / DSH_HOME 目录。");
            RequirePlainAncestors(root);
            return root;
        }

        internal static List<string> ListProfiles(string sourceHome)
        {
            string profiles = Path.Combine(sourceHome, "profiles");
            if (!Directory.Exists(profiles)) return new List<string>();
            RequirePlainAncestors(profiles);
            return Directory.EnumerateDirectories(profiles).Where(path => Path.GetFileName(path) != "node_modules"
                && File.Exists(Path.Combine(path, "package.json"))).Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static OfficialImportPlan Preview(string selected, string targetHome, string sourceProfile, string targetProfile, CancellationToken token, string legacyRoot = null, bool forExport = false)
        {
            var plan = new OfficialImportPlan { SourceHome = ResolveSourceHome(selected), TargetHome = Path.GetFullPath(targetHome),
                SourceProfile = ValidateProfile(sourceProfile), TargetProfile = ValidateProfile(targetProfile), ForExport = forExport };
            string selectedRoot = Path.GetFullPath(legacyRoot ?? selected);
            if (!PathComparer.Equals(selectedRoot, plan.SourceHome))
            {
                RequirePlainAncestors(selectedRoot);
                if (!PathComparer.Equals(Path.Combine(selectedRoot, ".dsh"), plan.SourceHome))
                    throw new InvalidDataException("安装目录必须包含所选的 .dsh 数据目录。");
                plan.LegacyRoot = selectedRoot;
            }
            if (!forExport) RequirePlainAncestors(plan.TargetHome);
            if (!forExport && (Contains(plan.SourceHome, plan.TargetHome) || Contains(plan.TargetHome, plan.SourceHome)))
                throw new InvalidDataException("来源和目标目录不能相同，也不能互相包含。");
            AddGroup(plan, "sessions", "对话记录", new[] { "sessions", "storages" }, token);
            AddGroup(plan, "skills", "技能", new[] { "skills" }, token);
            AddGroup(plan, "plugins", "插件文件与启用清单", new[] { "plugins" }, token);
            if (plan.LegacyRoot != null)
            {
                AddGroup(plan, "skills", "技能", new[] { "skills" }, token, plan.LegacyRoot);
                AddGroup(plan, "plugins", "插件文件与启用清单", new[] { "plugins" }, token, plan.LegacyRoot);
            }
            string profile = Path.Combine(plan.SourceHome, "profiles", plan.SourceProfile);
            string manifest = Path.Combine(profile, "package.json");
            if (File.Exists(manifest))
            {
                string resolved = ResolveWithinSource(manifest, plan.SourceHome, plan.LegacyRoot);
                using var input = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > 2 * 1024 * 1024) throw new InvalidDataException("插件清单过大。");
                using var reader = new StreamReader(input);
                plan.Manifest = reader.ReadToEnd();
                ParseManifest(plan.Manifest);
                plan.ManifestExists = true;
                AddPluginModules(plan, Path.Combine(profile, "node_modules"), token);
                AddPluginModules(plan, Path.Combine(plan.SourceHome, "profiles", "node_modules"), token);
            }
            if (plan.ManifestExists && !plan.Groups.Any(group => group.Id == "plugins"))
                plan.Groups.Add(new OfficialImportGroup { Id = "plugins", Name = "插件文件与启用清单" });
            if (plan.Groups.Count == 0) throw new InvalidDataException("此目录没有可导入的对话、技能或插件。");
            foreach (var group in plan.Groups)
            {
                var files = plan.Files.Where(file => file.Group == group.Id).ToList();
                group.Files = files.Count;
                group.Bytes = files.Sum(file => file.Length);
                group.ExistingUnits = forExport ? 0 : files.Select(file => file.Unit).Distinct(PathComparer).Count(unit => Exists(Target(plan, unit)));
            }
            return plan;
        }

        private static void AddGroup(OfficialImportPlan plan, string id, string name, IEnumerable<string> roots, CancellationToken token, string sourceRoot = null)
        {
            int before = plan.Files.Count;
            foreach (string root in roots)
            {
                string source = Path.Combine(sourceRoot ?? plan.SourceHome, root);
                if (!Directory.Exists(source)) continue;
                foreach (string entry in Directory.EnumerateFileSystemEntries(source))
                {
                    string relative = Path.Combine(root, Path.GetFileName(entry));
                    if (plan.Files.Any(file => PathComparer.Equals(file.Unit, relative))) continue;
                    Collect(plan, id, entry, relative, root == "storages" ? null : relative, new HashSet<string>(PathComparer), token);
                }
            }
            if (plan.Files.Count > before && !plan.Groups.Any(group => group.Id == id)) plan.Groups.Add(new OfficialImportGroup { Id = id, Name = name });
        }

        private static void AddPluginModules(OfficialImportPlan plan, string modules, CancellationToken token)
        {
            if (!Directory.Exists(modules)) return;
            string resolved;
            try { resolved = ResolveWithinSource(modules, plan.SourceHome, plan.LegacyRoot); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
            {
                MarkScopeUnavailable(plan, String.Empty, exception);
                return;
            }
            foreach (string entry in Directory.EnumerateFileSystemEntries(resolved))
            {
                string name = Path.GetFileName(entry);
                if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                if (name.StartsWith("@", StringComparison.Ordinal))
                {
                    string scope;
                    try { scope = ResolveWithinSource(entry, plan.SourceHome, plan.LegacyRoot); }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
                    {
                        MarkScopeUnavailable(plan, name, exception);
                        continue;
                    }
                    foreach (string package in Directory.EnumerateFileSystemEntries(scope))
                        AddModule(plan, package, name + "/" + Path.GetFileName(package), token);
                }
                else AddModule(plan, entry, name, token);
            }
        }

        private static void AddModule(OfficialImportPlan plan, string source, string name, CancellationToken token)
        {
            if (DefaultBundles.Contains(name)) return;
            string unit = Path.Combine("profiles", plan.TargetProfile, "node_modules", name.Replace('/', Path.DirectorySeparatorChar));
            if (plan.Files.Any(file => PathComparer.Equals(file.Unit, unit))) return;
            int before = plan.Files.Count;
            try
            {
                Collect(plan, "plugins", source, unit, unit, new HashSet<string>(PathComparer), token);
                plan.UnavailableModules.Remove(name);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
            {
                plan.Files.RemoveRange(before, plan.Files.Count - before);
                plan.UnavailableModules.Add(name);
                AddWarning(plan, name, exception);
            }
        }

        private static void MarkScopeUnavailable(OfficialImportPlan plan, string scope, Exception exception)
        {
            JsonObject manifest = ParseManifest(plan.Manifest);
            foreach (var dependency in manifest["dependencies"] as JsonObject ?? new JsonObject())
                if ((scope.Length == 0 || dependency.Key.StartsWith(scope + "/", StringComparison.Ordinal))
                    && ((dependency.Value?.GetValue<string>() ?? String.Empty).StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                        || (dependency.Value?.GetValue<string>() ?? String.Empty).StartsWith("link:", StringComparison.OrdinalIgnoreCase)))
                    plan.UnavailableModules.Add(dependency.Key);
            AddWarning(plan, scope, exception);
        }

        private static void AddWarning(OfficialImportPlan plan, string name, Exception exception)
        {
            string warning = "插件 " + (String.IsNullOrWhiteSpace(name) ? "node_modules" : name)
                + " 无法迁移：" + exception.Message;
            if (!plan.Warnings.Contains(warning, StringComparer.Ordinal)) plan.Warnings.Add(warning);
        }

        private static void Collect(OfficialImportPlan plan, string group, string path, string relative, string unit, HashSet<string> ancestors, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string resolved = ResolveWithinSource(path, plan.SourceHome, plan.LegacyRoot);
            if (Directory.Exists(resolved))
            {
                if (!ancestors.Add(resolved)) throw new InvalidDataException("来源目录包含循环链接：" + relative);
                foreach (string child in Directory.EnumerateFileSystemEntries(resolved))
                    Collect(plan, group, child, Path.Combine(relative, Path.GetFileName(child)), unit, ancestors, token);
                ancestors.Remove(resolved);
                return;
            }
            if (plan.Files.Count >= 250000) throw new InvalidDataException("来源文件数量超过 250000 个，请分开导入。");
            if (!plan.ForExport) Target(plan, relative);
            using var input = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
            plan.Files.Add(new OfficialImportFile { Group = group, Source = resolved, Relative = relative, Unit = unit ?? relative,
                Length = input.Length, Hash = SHA256.HashData(input) });
        }

        internal static OfficialImportResult Import(OfficialImportPlan plan, IEnumerable<string> selectedGroups, Action<string, double> progress, CancellationToken token)
        {
            var result = new OfficialImportResult();
            var selected = new HashSet<string>(selectedGroups ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0) { result.Error = "请至少选择一项内容。"; return result; }
            if (selected.Any(id => !plan.Groups.Any(group => group.Id == id))) { result.Error = "导入类别无效，请重新预览。"; return result; }
            string staging = null;
            string lockName = "Dafeiyu-OfficialImport-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plan.TargetHome.ToUpperInvariant())));
            using var mutex = new Mutex(false, lockName);
            bool held = false;
            try
            {
                try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held) throw new IOException("另一项数据导入正在运行，请完成后重试。");
                token.ThrowIfCancellationRequested();
                RequirePlainAncestors(plan.SourceHome);
                if (plan.LegacyRoot != null) RequirePlainAncestors(plan.LegacyRoot);
                RequirePlainAncestors(plan.TargetHome);
                var files = plan.Files.Where(file => selected.Contains(file.Group)).ToList();
                var skippedUnits = new HashSet<string>(files.Select(file => file.Unit).Where(unit => Exists(Target(plan, unit))), PathComparer);
                result.Skipped = files.Count(file => skippedUnits.Contains(file.Unit));
                files.RemoveAll(file => skippedUnits.Contains(file.Unit));
                Directory.CreateDirectory(plan.TargetHome);
                staging = Path.Combine(plan.TargetHome, ".dafeiyu-import-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                for (int index = 0; index < files.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var file = files[index];
                    string source = ResolveWithinSource(file.Source, plan.SourceHome, plan.LegacyRoot);
                    string staged = Path.Combine(staging, file.Relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(staged));
                    using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                    }
                    using var verify = File.OpenRead(staged);
                    if (verify.Length != file.Length || !SHA256.HashData(verify).SequenceEqual(file.Hash))
                        throw new IOException("来源文件在预览后发生变化，请停止 DSH 后重新预览：" + file.Relative);
                    progress?.Invoke("正在准备 " + file.Relative, files.Count == 0 ? 70 : (index + 1) * 70.0 / files.Count);
                }
                string merged = null;
                string targetManifest = Target(plan, Path.Combine("profiles", plan.TargetProfile, "package.json"));
                string originalManifest = null;
                if (selected.Contains("plugins") && plan.ManifestExists)
                {
                    string sourceManifest = Path.Combine(plan.SourceHome, "profiles", plan.SourceProfile, "package.json");
                    if (File.ReadAllText(ResolveWithinSource(sourceManifest, plan.SourceHome, plan.LegacyRoot)) != plan.Manifest)
                        throw new IOException("来源插件清单已变化，请重新预览。");
                    if (File.Exists(targetManifest)) originalManifest = File.ReadAllText(targetManifest);
                    merged = MergeManifest(PortableManifest(plan), originalManifest, plan.TargetProfile);
                }
                token.ThrowIfCancellationRequested();
                foreach (string unit in files.Select(file => file.Unit).Distinct(PathComparer))
                {
                    if (token.IsCancellationRequested) { result.Canceled = true; break; }
                    string target = Target(plan, unit);
                    string staged = Path.Combine(staging, unit);
                    var unitFiles = files.Where(file => PathComparer.Equals(file.Unit, unit)).ToList();
                    if (Exists(target)) { result.Skipped += unitFiles.Count; continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    RequirePlainAncestors(target);
                    try { if (Directory.Exists(staged)) Directory.Move(staged, target); else File.Move(staged, target, false); }
                    catch (IOException) when (Exists(target)) { result.Skipped += unitFiles.Count; continue; }
                    result.Imported += unitFiles.Count;
                    progress?.Invoke("已导入 " + unit, 70 + (result.Imported + result.Skipped) * 30.0 / Math.Max(1, plan.Files.Count));
                }
                token.ThrowIfCancellationRequested();
                if (merged != null && !result.Canceled)
                {
                    RequirePlainAncestors(targetManifest);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetManifest));
                    string temporary = Path.Combine(Path.GetDirectoryName(targetManifest), ".dafeiyu-manifest-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(temporary, merged);
                    try
                    {
                        if (originalManifest == null)
                        {
                            File.Move(temporary, targetManifest, false);
                        }
                        else
                        {
                            if (File.ReadAllText(targetManifest) != originalManifest) throw new IOException("目标插件清单正在被修改，已导入文件保留，请停止 DSH 后重试。");
                            string backup = targetManifest + ".before-import-" + Guid.NewGuid().ToString("N") + ".json";
                            File.Replace(temporary, targetManifest, backup);
                        }
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                result.Ok = !result.Canceled;
                progress?.Invoke(result.Canceled ? "已取消，已导入的数据保留。" + result.Summary : result.Summary, result.Ok ? 100 : 70);
            }
            catch (OperationCanceledException) { result.Canceled = true; }
            catch (Exception exception) { result.Error = exception.Message; }
            finally
            {
                if (staging != null && Directory.Exists(staging))
                {
                    try { RequirePlainAncestors(staging); Directory.Delete(staging, true); }
                    catch (Exception exception) { if (!result.Ok) result.Error = (result.Error ?? String.Empty) + "\n无法清理临时导入目录：" + exception.Message; }
                }
                if (held) mutex.ReleaseMutex();
            }
            return result;
        }

        internal static string PortableManifest(OfficialImportPlan plan)
        {
            JsonObject manifest = ParseManifest(plan.Manifest);
            if (manifest["dependencies"] is JsonObject dependencies)
            {
                foreach (string name in plan.UnavailableModules)
                    if (dependencies[name]?.GetValue<string>() is string spec
                        && (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || spec.StartsWith("link:", StringComparison.OrdinalIgnoreCase)))
                        dependencies.Remove(name);
            }
            if (manifest["dsh"]?["profile"]?["bundles"] is JsonArray bundles)
                for (int index = bundles.Count - 1; index >= 0; index--)
                    if (plan.UnavailableModules.Contains(bundles[index]?.GetValue<string>() ?? String.Empty)) bundles.RemoveAt(index);
            return manifest.ToJsonString();
        }

        private static string MergeManifest(string sourceText, string targetText, string targetProfile)
        {
            JsonObject source = ParseManifest(sourceText);
            JsonObject target = targetText == null ? new JsonObject { ["name"] = "dsh-profile-" + targetProfile, ["private"] = true,
                ["dsh"] = new JsonObject { ["profile"] = new JsonObject { ["bundles"] = new JsonArray("@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app") } } } : ParseManifest(targetText);
            var dependencies = target["dependencies"] as JsonObject ?? new JsonObject();
            if (target["dependencies"] == null || target["dependencies"] is not JsonObject) target["dependencies"] = dependencies;
            var existingDependencies = new HashSet<string>(dependencies.Select(pair => pair.Key), StringComparer.Ordinal);
            foreach (var pair in source["dependencies"] as JsonObject ?? new JsonObject())
            {
                if (DefaultBundles.Contains(pair.Key)) continue;
                string spec = pair.Value?.GetValue<string>() ?? String.Empty;
                if (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || spec.StartsWith("link:", StringComparison.OrdinalIgnoreCase))
                    spec = "file:node_modules/" + pair.Key;
                if (!dependencies.ContainsKey(pair.Key)) dependencies[pair.Key] = spec;
            }
            var dsh = target["dsh"] as JsonObject ?? new JsonObject();
            if (target["dsh"] == null || target["dsh"] is not JsonObject) target["dsh"] = dsh;
            var profile = dsh["profile"] as JsonObject ?? new JsonObject();
            if (dsh["profile"] == null || dsh["profile"] is not JsonObject) dsh["profile"] = profile;
            var bundles = profile["bundles"] as JsonArray ?? new JsonArray();
            if (profile["bundles"] == null || profile["bundles"] is not JsonArray) profile["bundles"] = bundles;
            foreach (var entry in source["dsh"]?["profile"]?["bundles"] as JsonArray ?? new JsonArray())
            {
                string name = entry?.GetValue<string>();
                if (String.IsNullOrWhiteSpace(name) || DefaultBundles.Contains(name) || existingDependencies.Contains(name)) continue;
                if (!bundles.Any(existing => existing?.GetValue<string>() == name)) bundles.Add(name);
            }
            return target.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true });
        }

        private static JsonObject ParseManifest(string text)
        {
            JsonObject manifest = JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("插件 package.json 不是 JSON 对象。");
            if (manifest["dependencies"] != null && manifest["dependencies"] is not JsonObject) throw new InvalidDataException("插件 dependencies 无效。");
            if (manifest["dsh"]?["profile"]?["bundles"] != null && manifest["dsh"]?["profile"]?["bundles"] is not JsonArray)
                throw new InvalidDataException("插件 bundles 无效。");
            return manifest;
        }

        private static string ValidateProfile(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') || name == "." || name == "..")
                throw new InvalidDataException("DSH 配置名称无效。");
            return name;
        }

        private static string Target(OfficialImportPlan plan, string relative)
        {
            string target = Path.GetFullPath(Path.Combine(plan.TargetHome, relative));
            if (!Contains(plan.TargetHome, target) || PathComparer.Equals(target, plan.TargetHome)) throw new InvalidDataException("导入路径超出目标数据目录。");
            RequirePlainAncestors(target);
            return target;
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        private static bool Contains(string root, string path) => PathComparer.Equals(root, path) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, PathComparison);

        internal static string ResolveWithinSource(string path, string root, string legacyRoot = null)
        {
            RequirePlainAncestors(legacyRoot ?? root);
            string resolved = Path.GetFullPath(path);
            bool Allowed(string candidate) => Contains(root, candidate) || legacyRoot != null
                && (Contains(Path.Combine(legacyRoot, "plugins"), candidate) || Contains(Path.Combine(legacyRoot, "skills"), candidate));
            if (!Allowed(resolved)) throw new InvalidDataException("来源路径超出所选数据目录。");
            string traversalRoot = legacyRoot ?? root;
            var segments = Path.GetRelativePath(traversalRoot, resolved).Split(Path.DirectorySeparatorChar);
            string current = traversalRoot;
            foreach (string segment in segments)
            {
                current = Path.Combine(current, segment);
                var info = Directory.Exists(current) ? (FileSystemInfo)new DirectoryInfo(current) : new FileInfo(current);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    current = info.ResolveLinkTarget(true)?.FullName ?? throw new InvalidDataException("无法解析来源链接。");
                    if (!Allowed(current)) throw new InvalidDataException("来源链接指向所选目录之外：" + path);
                }
            }
            return current;
        }

        internal static void RequirePlainAncestors(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("目标或数据根目录不能包含符号链接 / 目录联接：" + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
            }
        }
    }
}
