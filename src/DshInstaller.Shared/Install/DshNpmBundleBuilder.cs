// Shared verbatim with the installer's Install/DshNpmBundleBuilder.cs.
// Only callers that have received a missing manifest (404/410) may use this path.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DafeiyuGo.Shared
{
    public static class DshNpmBundleBuilder
    {
        private const string Package = "@deepseek-ai/dsh";
        private const string Entry = "node_modules/@deepseek-ai/dsh/lib/bin.js";

        public static string Build(string exactVersion, string root, string nodeExe, string registry,
            IDictionary<string, string> environment, Action<string, double> progress, CancellationToken token, Action<string> log = null)
        {
            if (string.IsNullOrWhiteSpace(exactVersion) || !Regex.IsMatch(exactVersion, @"^[0-9]+[.][0-9]+[.][0-9]+(?:-[0-9A-Za-z.-]+)?(?:[+][0-9A-Za-z.-]+)?$"))
                throw new ArgumentException("DSH 构包需要精确版本。");
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) throw new ArgumentException("DSH 安装目录必须是绝对路径。");
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            string parent = Path.GetDirectoryName(root);
            if (string.IsNullOrEmpty(parent)) throw new IOException("不能将 DSH 部署到磁盘根目录。");
            GuardAncestors(root);
            if (string.IsNullOrWhiteSpace(nodeExe) || !Path.IsPathFullyQualified(nodeExe) || !File.Exists(nodeExe))
                throw new FileNotFoundException("未找到所选 Node.js，请选择 Node 22 或 24。", nodeExe);
            nodeExe = Path.GetFullPath(nodeExe);
            if (!Uri.TryCreate(registry, UriKind.Absolute, out var registryUri) || registryUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(registryUri.UserInfo) || !string.IsNullOrEmpty(registryUri.Query) || !string.IsNullOrEmpty(registryUri.Fragment))
                throw new ArgumentException("DSH npm 下载源必须是有效 HTTPS 地址。");
            token.ThrowIfCancellationRequested();
            string work = DshBundleDeployment.CreateWorkDirectory(root, "prepare", exactVersion, log);
            bool complete = false;
            try
            {
                string stage = Path.Combine(work, "package");
                Directory.CreateDirectory(stage);
                var env = CreateEnvironment(nodeExe, work, environment);
                string originalPath = environment != null && environment.TryGetValue("PATH", out string suppliedPath)
                    ? suppliedPath : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                log?.Invoke("安装子进程 PATH 长度：" + originalPath.Length + " → " + env["PATH"].Length + "；所选 Node 目录置首。");
                string nodeVersion = Run(nodeExe, new[] { "--version" }, stage, env, TimeSpan.FromSeconds(15), token, null).Trim();
                var majorMatch = Regex.Match(nodeVersion, @"^v(22|24)[.]");
                if (!majorMatch.Success) throw new InvalidDataException("DSH 安装需要 Node 22/24；所选运行时为 " + nodeVersion + "。请在设置中更换 Node.js 路径。");
                int nodeMajor = int.Parse(majorMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                string npm = FindNpmCli(nodeExe, env);
                File.WriteAllText(Path.Combine(stage, "package.json"), JsonSerializer.Serialize(new { name = "dafeiyu-dsh-install", version = "1.0.0", @private = true }), new UTF8Encoding(false));
                log?.Invoke("隔离 npm 构包：DSH " + exactVersion + "；Node " + nodeExe + "；暂存 " + work);
                using (var written = new WrittenBytesTracker(stage, bytes => progress?.Invoke("准备依赖（已写入"
                    + (bytes / 1048576.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " MB）", -1), log))
                {
                Run(nodeExe, new[] { npm, "install", Package + "@" + exactVersion, "--save-exact", "--no-audit", "--no-fund",
                    "--include=optional", "--omit=dev", "--ignore-scripts=false", "--package-lock=true", "--global=false",
                    "--install-strategy=hoisted", "--foreground-scripts", "--loglevel=notice", "--registry", registryUri.AbsoluteUri },
                    stage, env, TimeSpan.FromMinutes(20), token, log, written.Pulse);
                    written.Complete();
                }
                File.WriteAllText(Path.Combine(stage, "dsh-bundle.json"), JsonSerializer.Serialize(new {
                    package = Package, version = exactVersion, platform = "win-x64", nodeMajor, entryPoint = Entry
                }), new UTF8Encoding(false));
                progress?.Invoke("正在检查安装包…", 80);
                DshBundleDeployment.ValidateLayout(stage, exactVersion);
                DshBundleDeployment.ValidateRuntime(stage, nodeExe, exactVersion, work, token, log);
                string fileName = "dsh-" + exactVersion + "-win-x64-node" + nodeMajor + ".zip";
                string archive = Path.Combine(work, fileName);
                progress?.Invoke("正在整理安装包…", 90);
                CreateArchive(stage, archive, token);
                string sha256;
                using (var input = File.OpenRead(archive)) sha256 = Convert.ToHexString(SHA256.HashData(input));
                File.WriteAllText(archive + ".manifest.json", JsonSerializer.Serialize(new { version = exactVersion, platform = "win-x64",
                    nodeMajor, fileName, sha256, sizeBytes = new FileInfo(archive).Length }), new UTF8Encoding(false));
                token.ThrowIfCancellationRequested();
                progress?.Invoke("安装包已就绪", 100);
                complete = true;
                return archive;
            }
            finally
            {
                if (!complete)
                {
                    DshBundleDeployment.CleanupWorkDirectory(work, log, (done, total) => progress?.Invoke("清理（" + done + "/" + total + "）", -1));
                }
            }
        }

        private static Dictionary<string, string> CreateEnvironment(string node, string work, IDictionary<string, string> environment)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (environment != null)
            {
                foreach (var pair in environment) if (pair.Value != null) result[pair.Key] = pair.Value;
            }
            else
            {
                foreach (System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables()) result[(string)pair.Key] = (string)pair.Value;
            }
            result.TryGetValue("PATH", out string path);
            result.Remove("PATH");
            result["Path"] = BuildPath(node, path);
            result.Remove("NODE_OPTIONS");
            result["NODE"] = node; result["NODE_EXE"] = node; result["npm_node_execpath"] = node;
            string temp = Path.Combine(work, "temp"); Directory.CreateDirectory(temp);
            result["TEMP"] = temp; result["TMP"] = temp;
            string home = Path.Combine(work, "home"); Directory.CreateDirectory(home);
            result["HOME"] = home; result["USERPROFILE"] = home; result["APPDATA"] = home; result["LOCALAPPDATA"] = home;
            result["DSH_HOME"] = Path.Combine(home, ".dsh");
            result["npm_config_cache"] = Path.Combine(work, "npm-cache");
            result["npm_config_update_notifier"] = "false";
            // A public, exact package install must not execute user-supplied npm hooks/config.
            string npmrc = Path.Combine(work, "isolated.npmrc"); File.WriteAllText(npmrc, string.Empty);
            result["npm_config_userconfig"] = npmrc; result["npm_config_globalconfig"] = Path.Combine(work, "isolated-global.npmrc");
            File.WriteAllText(result["npm_config_globalconfig"], string.Empty);
            result["npm_config_script_shell"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            return result;
        }

        internal static string BuildPath(string node, string inherited)
        {
            // cmd.exe ignores an expanded PATH over 8191 characters. npm adds .bin
            // paths for every ancestor, so leave ample space for its lifecycle shell.
            string nodeDirectory = Path.GetDirectoryName(node);
            string value = nodeDirectory + Path.PathSeparator + (inherited ?? string.Empty);
            if (value.Length <= 4096) return value;
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int length = 0;
            void Add(string directory)
            {
                if (string.IsNullOrWhiteSpace(directory)) return;
                directory = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
                if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory) || !seen.Add(directory)) return;
                if (length + directory.Length + 1 > 4096) return;
                paths.Add(directory); length += directory.Length + 1;
            }
            Add(nodeDirectory);
            Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            var inheritedPaths = (inherited ?? string.Empty).Split(Path.PathSeparator);
            // Keep command providers first, ahead of SDK/cache duplicates.
            foreach (string directory in inheritedPaths)
            {
                string candidate = directory.Trim().Trim('"');
                if (!Path.IsPathFullyQualified(candidate)) continue;
                if (new[] { "git.exe", "npm.cmd", "cmake.exe", "python.exe", "powershell.exe", "pwsh.exe" }
                    .Any(command => File.Exists(Path.Combine(candidate, command)))) Add(candidate);
            }
            foreach (string directory in inheritedPaths) Add(directory);
            return string.Join(Path.PathSeparator.ToString(), paths);
        }

        private static string FindNpmCli(string node, IDictionary<string, string> environment)
        {
            var directories = new List<string>();
            string directory = Path.GetDirectoryName(node);
            for (int depth = 0; depth < 4 && !string.IsNullOrEmpty(directory); depth++, directory = Path.GetDirectoryName(directory)) directories.Add(directory);
            if (environment.TryGetValue("PATH", out string path)) directories.AddRange(path.Split(Path.PathSeparator));
            foreach (string value in directories)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                directory = value.Trim().Trim('"');
                if (!Path.IsPathFullyQualified(directory)) continue;
                foreach (string relative in new[] { "node_modules/npm/bin/npm-cli.js", "npm/bin/npm-cli.js", "npm-cli.js", "lib/node_modules/npm/bin/npm-cli.js" })
                {
                    string candidate = Path.Combine(directory, relative);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            throw new FileNotFoundException("未找到 npm-cli.js，请安装包含 npm 的 Node 22/24 运行时，或选择已有完整包的 DSH 版本。");
        }

        private static string Run(string file, IEnumerable<string> arguments, string working, IDictionary<string, string> environment,
            TimeSpan timeout, CancellationToken token, Action<string> log, Action pulse = null)
        {
            var info = new ProcessStartInfo(file) { WorkingDirectory = working, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            info.Environment.Clear();
            foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
            using var process = Process.Start(info) ?? throw new IOException("无法启动所选 Node.js。");
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            var stdout = new StringBuilder(); var stderr = new StringBuilder();
            Task output = ReadOutput(process.StandardOutput, stdout, log);
            Task errors = ReadOutput(process.StandardError, stderr, log);
            var watch = Stopwatch.StartNew();
            try
            {
                while (!process.WaitForExit(150))
                {
                    token.ThrowIfCancellationRequested();
                    pulse?.Invoke();
                    if (watch.Elapsed > timeout) throw new TimeoutException("DSH 安装依赖超时，请检查下载源后重试。");
                }
                token.ThrowIfCancellationRequested();
                Task.WhenAll(output, errors).GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new IOException("DSH 安装依赖失败（退出码 " + process.ExitCode + "）：" + ErrorSummary(stderr.ToString(), stdout.ToString()));
                return stdout.ToString();
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                try { process.WaitForExit(5000); Task.WhenAll(output, errors).Wait(5000); } catch { }
                throw;
            }
        }

        // File system notifications only queue paths. Metadata is sampled at most once per
        // second on the existing process-wait worker, never on a recursive polling timer.
        private sealed class WrittenBytesTracker : IDisposable
        {
            private readonly string root;
            private readonly Action<long> progress;
            private readonly Action<string> log;
            private readonly ConcurrentDictionary<string, byte> changed = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, long> sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            private readonly Stopwatch update = Stopwatch.StartNew();
            private FileSystemWatcher watcher;
            private long bytes;
            private int overflow;
            public WrittenBytesTracker(string root, Action<long> progress, Action<string> log)
            {
                this.root = root; this.progress = progress; this.log = log;
                try
                {
                    watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, InternalBufferSize = 64 * 1024 };
                    watcher.Created += Changed; watcher.Changed += Changed; watcher.Deleted += Changed;
                    watcher.Renamed += (sender, item) => { Queue(item.OldFullPath); Queue(item.FullPath); };
                    watcher.Error += (sender, item) => Interlocked.Exchange(ref overflow, 1);
                    watcher.EnableRaisingEvents = true;
                }
                catch (Exception error) when (error is IOException || error is ArgumentException || error is PlatformNotSupportedException)
                { watcher?.Dispose(); watcher = null; Interlocked.Exchange(ref overflow, 1); }
                foreach (string path in Directory.EnumerateFiles(root)) Queue(path);
                Drain();
                progress?.Invoke(bytes);
            }
            private void Changed(object sender, FileSystemEventArgs item) => Queue(item.FullPath);
            private void Queue(string path) { changed.TryAdd(path, 0); }
            public void Pulse()
            {
                if (update.ElapsedMilliseconds < 1000) return;
                Drain();
                progress?.Invoke(bytes);
                update.Restart();
            }
            private void Drain()
            {
                foreach (string path in changed.Keys)
                {
                    if (!changed.TryRemove(path, out _)) continue;
                    long length = 0;
                    try
                    {
                        if (File.Exists(path))
                        {
                            GuardAncestors(path);
                            length = new FileInfo(path).Length;
                        }
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { continue; }
                    sizes.TryGetValue(path, out long previous);
                    bytes += length - previous;
                    if (length == 0) sizes.Remove(path); else sizes[path] = length;
                }
            }
            public void Complete()
            {
                if (watcher != null) watcher.EnableRaisingEvents = false;
                // One final walk reconciles dropped notifications and files moved as a
                // whole directory; normal progress never rescans node_modules.
                long actual = 0; var pending = new Stack<string>(); pending.Push(root);
                while (pending.Count > 0)
                {
                    string directory = pending.Pop(); GuardAncestors(directory);
                    foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                        else actual += new FileInfo(path).Length;
                    }
                }
                bytes = actual;
                if (Volatile.Read(ref overflow) != 0) log?.Invoke("依赖写入通知不完整，已在安装结束时单次核对实际文件大小；未启用递归轮询。");
                progress?.Invoke(bytes);
            }
            public void Dispose() { watcher?.Dispose(); }
        }

        private static async Task ReadOutput(StreamReader reader, StringBuilder tail, Action<string> log)
        {
            string line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                tail.AppendLine(line);
                if (tail.Length > 32768) tail.Remove(0, tail.Length - 24576);
                if (log != null) { try { log(line); } catch { } }
            }
        }

        public static string ErrorSummary(string stderr, string stdout)
        {
            string combined = (stderr ?? string.Empty) + Environment.NewLine + (stdout ?? string.Empty);
            var lines = combined.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var errors = lines.Where(line => Regex.IsMatch(line, @"(?:npm (?:error|ERR!)|ERR_[A-Z]|Error:|not recognized|不是内部或外部命令)", RegexOptions.IgnoreCase)).ToList();
            string text = string.Join(Environment.NewLine, errors.Count > 0 ? errors : lines.TakeLast(12));
            if (errors.Count > 0 && !string.IsNullOrWhiteSpace(stderr))
                text += Environment.NewLine + "错误输出末尾：" + Environment.NewLine
                    + string.Join(Environment.NewLine, stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).TakeLast(12));
            if (!string.IsNullOrWhiteSpace(stdout))
                text += Environment.NewLine + "安装脚本输出：" + Environment.NewLine
                    + string.Join(Environment.NewLine, stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).TakeLast(18));
            return text.Length > 4000 ? text.Substring(text.Length - 4000) : text;
        }

        private static void CreateArchive(string stage, string archive, CancellationToken token)
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            var pending = new Stack<string>(); pending.Push(stage);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("构包目录不能包含链接：" + path);
                    if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                    string name = Path.GetRelativePath(stage, path).Replace(Path.DirectorySeparatorChar, '/');
                    // npm can leave transient root files; only deployment-owned content is packaged.
                    if (!name.StartsWith("node_modules/", StringComparison.Ordinal)
                        && name != "package.json" && name != "package-lock.json" && name != "dsh-bundle.json") continue;
                    var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                    using var input = File.OpenRead(path); using var output = entry.Open();
                    byte[] buffer = new byte[128 * 1024]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                }
            }
        }

        private static void GuardAncestors(string path)
        {
            while (!string.IsNullOrEmpty(path))
            {
                if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("DSH 构包路径不能穿过目录链接：" + path);
                path = Path.GetDirectoryName(path);
            }
        }

        private static void DeleteOwnedTree(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry); else File.Delete(entry);
                }
                else if ((attributes & FileAttributes.Directory) != 0) DeleteOwnedTree(entry); else File.Delete(entry);
            }
            Directory.Delete(path);
        }
    }
}
