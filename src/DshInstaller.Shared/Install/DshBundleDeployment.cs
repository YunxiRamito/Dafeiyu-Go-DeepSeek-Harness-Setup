// Shared with the launcher's source/DshBundleDeployment.cs. Keep both copies identical.
// BCL-only: downloading/trust policy belongs to the calling product.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DafeiyuGo.Shared
{
    public static class DshBundleDeployment
    {
        private const string PackageName = "@deepseek-ai/dsh";
        private const string EntryPoint = "node_modules/@deepseek-ai/dsh/lib/bin.js";
        private static readonly string[] AllowedItems = { "node_modules", "package.json", "package-lock.json", "dsh-bundle.json" };
        public sealed class Manifest
        {
            public string Version { get; internal set; }
            public string Sha256 { get; internal set; }
            public long SizeBytes { get; internal set; }
            public string FileName { get; internal set; }
            public int NodeMajor { get; internal set; }
        }
        public sealed class RecoveryException : IOException
        { public RecoveryException(string message, Exception inner) : base(message, inner) { } }

        private const string OwnerFile = ".dsh-update-owner.json";
        private const string RecoveryFile = "recovery-required.txt";
        private const string OwnerKind = "DafeiyuGo.DshUpdate";
        private static readonly string[] WorkPhases = { "prepare", "download", "extract", "runtime-check" };

        public static string CreateWorkDirectory(string root, string phase, string version, Action<string> log = null)
        {
            root = Absolute(root);
            if (string.IsNullOrEmpty(Path.GetDirectoryName(root))) throw new IOException("不能将 DSH 部署到磁盘根目录。");
            if (!WorkPhases.Contains(phase, StringComparer.Ordinal) || string.IsNullOrEmpty(version)
                || version.Length > 100 || !Regex.IsMatch(version, @"^[0-9A-Za-z][0-9A-Za-z.+-]*$"))
                throw new ArgumentException("更新临时目录名称无效。");
            string container = Path.Combine(root, "temp", "updatepackage");
            GuardAncestors(container);
            Directory.CreateDirectory(container);
            foreach (string stale in Directory.EnumerateDirectories(container))
            {
                if (!TryReadOwner(stale, out int pid, out long started) || File.Exists(Path.Combine(stale, RecoveryFile))) continue;
                bool active;
                try { using var process = Process.GetProcessById(pid); active = !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started; }
                catch (ArgumentException) { active = false; }
                catch { active = true; }
                if (!active) CleanupWorkDirectory(stale, log);
            }
            // A stale cleanup may have removed the now-empty container.
            Directory.CreateDirectory(container);
            for (int attempt = 1; attempt <= 1000; attempt++)
            {
                string name = phase + "-" + version + (attempt == 1 ? string.Empty : "-" + attempt);
                string work = Path.Combine(container, name);
                if (Directory.Exists(work) || File.Exists(work)) continue;
                Directory.CreateDirectory(work);
                try
                {
                    using var process = Process.GetCurrentProcess();
                    using var owner = new FileStream(Path.Combine(work, OwnerFile), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    JsonSerializer.Serialize(owner, new { kind = OwnerKind, phase, version, pid = process.Id, started = process.StartTime.ToUniversalTime().Ticks });
                    log?.Invoke("更新临时目录：" + work);
                    return work;
                }
                catch (IOException) when (File.Exists(Path.Combine(work, OwnerFile))) { }
            }
            throw new IOException("无法创建更新临时目录，请稍后重试。");
        }

        public static bool CleanupWorkDirectory(string work, Action<string> log = null, Action<int, int> progress = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(work)) return false;
                work = Absolute(work);
                if (!Directory.Exists(work)) return false;
                GuardAncestors(work);
                string container = Path.GetDirectoryName(work);
                if (!string.Equals(Path.GetFileName(container), "updatepackage", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetFileName(Path.GetDirectoryName(container)), "temp", StringComparison.OrdinalIgnoreCase)
                    || File.Exists(Path.Combine(work, RecoveryFile)) || !TryReadOwner(work, out _, out _)) return false;
                var cleanup = new CleanupPlan(work, progress);
                for (int attempt = 0; ; attempt++)
                {
                    try { cleanup.Delete(); break; }
                    catch (Exception error) when ((error is IOException || error is UnauthorizedAccessException) && attempt < 3)
                    { Thread.Sleep(100 * (attempt + 1)); }
                }
                // Only remove empty containers; unrelated temporary files are never touched.
                try { Directory.Delete(container); Directory.Delete(Path.GetDirectoryName(container)); } catch { }
                return true;
            }
            catch (Exception error)
            {
                try { log?.Invoke("更新临时目录清理失败：" + work + "；" + error.Message); } catch { }
                return false;
            }
        }

        private static bool TryReadOwner(string work, out int pid, out long started)
        {
            pid = 0; started = 0;
            try
            {
                GuardAncestors(work);
                string owner = Path.Combine(work, OwnerFile);
                if (!File.Exists(owner) || (File.GetAttributes(owner) & FileAttributes.ReparsePoint) != 0 || new FileInfo(owner).Length > 4096) return false;
                using var document = JsonDocument.Parse(File.ReadAllText(owner));
                var value = document.RootElement;
                string phase = value.GetProperty("phase").GetString(), version = value.GetProperty("version").GetString();
                string name = Path.GetFileName(work), prefix = phase + "-" + version;
                if (value.GetProperty("kind").GetString() != OwnerKind || !WorkPhases.Contains(phase, StringComparer.Ordinal)
                    || string.IsNullOrEmpty(version) || !Regex.IsMatch(version, @"^[0-9A-Za-z][0-9A-Za-z.+-]*$")
                    || (name != prefix && (!name.StartsWith(prefix + "-", StringComparison.Ordinal)
                        || !int.TryParse(name.Substring(prefix.Length + 1), out int suffix) || suffix < 2))) return false;
                pid = value.GetProperty("pid").GetInt32(); started = value.GetProperty("started").GetInt64();
                return pid > 0 && started > 0;
            }
            catch { return false; }
        }

        public static Manifest ParseManifest(string json, string exactVersion)
        {
            using var document = JsonDocument.Parse(json);
            var value = document.RootElement;
            string version = value.GetProperty("version").GetString();
            string hash = value.GetProperty("sha256").GetString();
            string file = value.GetProperty("fileName").GetString();
            long size = value.GetProperty("sizeBytes").GetInt64();
            int node = value.GetProperty("nodeMajor").GetInt32();
            if (version != exactVersion || version == null || !Regex.IsMatch(version, @"^[0-9]+[.][0-9]+[.][0-9]+(?:-[0-9A-Za-z.-]+)?(?:[+][0-9A-Za-z.-]+)?$")
                || value.GetProperty("platform").GetString() != "win-x64" || (node != 22 && node != 24)
                || hash == null || !Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$") || size < 1 || size > 1024L * 1024 * 1024
                || file == null || file != Path.GetFileName(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DSH 完整包清单无效：需要 Windows x64、Node 22/24、精确版本、大小和 SHA-256。");
            return new Manifest { Version = version, Sha256 = hash, SizeBytes = size, FileName = file, NodeMajor = node };
        }

        public static void VerifyArchive(string archive, Manifest manifest)
        {
            if (new FileInfo(archive).Length != manifest.SizeBytes) throw new InvalidDataException("DSH 完整包大小与清单不一致。");
            using var stream = File.OpenRead(archive);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DSH 完整包 SHA-256 校验失败。");
        }

        public static void Deploy(string archive, string manifestJson, string exactVersion, string root, string nodeExe,
            Action<string, double> progress, CancellationToken token, Action<string> log = null)
        {
            var manifest = ParseManifest(manifestJson, exactVersion);
            root = Absolute(root);
            GuardAncestors(root);
            string parent = Path.GetDirectoryName(root);
            if (string.IsNullOrEmpty(parent)) throw new IOException("不能将 DSH 部署到磁盘根目录。");
            token.ThrowIfCancellationRequested();
            string work = CreateWorkDirectory(root, "extract", exactVersion, log);
            string stage = Path.Combine(work, "package");
            bool preserve = false;
            try
            {
                progress?.Invoke("正在校验…", 0);
                log?.Invoke("校验完整包，部署目标：" + root + "；解压暂存：" + stage);
                ExtractAndValidate(archive, stage, manifest, token,
                    (count, bytes, total, files) => {
                        progress?.Invoke("解压文件（" + count + "/" + files + "）", Percent(bytes, total) * .80);
                        log?.Invoke("解压至 " + stage + " · " + count + "/" + files + " 文件 · "
                            + Bytes(bytes) + "/" + Bytes(total) + " · " + Percent(bytes, total).ToString("F1") + "%");
                    });
                progress?.Invoke("正在检查组件…", 82);
                ValidateRuntime(stage, nodeExe, exactVersion, work, token, log);
                token.ThrowIfCancellationRequested();
                var protectedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string runtimeDirectory = Path.GetDirectoryName(Absolute(nodeExe));
                foreach (string item in DeploymentItems(stage))
                    if (Contains(Path.Combine(root, item), runtimeDirectory)) protectedPackages.Add(item);
                // An interrupted commit may need its previous files on the next run.
                // Mark recovery before moving anything so stale cleanup cannot erase them.
                File.WriteAllText(Path.Combine(work, RecoveryFile), "正在提交安装；如操作中断，请保留 previous 目录。");
                var commitUpdate = Stopwatch.StartNew();
                bool commitReported = false;
                Commit(stage, root, token, (detail, percent) =>
                {
                    log?.Invoke(detail);
                    if (!commitReported || percent >= 100 || commitUpdate.ElapsedMilliseconds >= 1000)
                    {
                        progress?.Invoke("正在安装…", 85 + percent * .14);
                        commitReported = true; commitUpdate.Restart();
                    }
                },
                    () => ValidateRuntime(root, nodeExe, exactVersion, work, token, log), protectedPackages);
                File.Delete(Path.Combine(work, RecoveryFile));
                log?.Invoke("完整包部署完成；本体安装未启动 npm：" + root);
            }
            catch (RecoveryException)
            {
                preserve = true;
                File.WriteAllText(Path.Combine(work, RecoveryFile), "部署恢复未完成，请保留 previous 目录。");
                log?.Invoke("部署回滚未完成，恢复文件保留在：" + work);
                throw;
            }
            finally
            {
                if (!preserve)
                {
                    File.Delete(Path.Combine(work, RecoveryFile));
                    CleanupWorkDirectory(work, log, (done, total) => progress?.Invoke("清理（" + done + "/" + total + "）", 99.6 + Percent(done, total) * .003));
                }
            }
            progress?.Invoke("安装完成", 100);
        }

        public static void ExtractAndValidate(string archive, string stage, Manifest manifest, CancellationToken token,
            Action<int, long, long, int> progress = null)
        {
            token.ThrowIfCancellationRequested();
            VerifyArchive(archive, manifest);
            stage = Absolute(stage);
            GuardAncestors(stage);
            if (Directory.Exists(stage) || File.Exists(stage)) throw new ArgumentException("完整包暂存目录必须是新的空路径。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0; int files = 0;
            using var zip = ZipFile.OpenRead(archive);
            if (zip.Entries.Count < 4 || zip.Entries.Count > 150000) throw new InvalidDataException("DSH 完整包条目数量异常。");
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                string relative = ValidateEntry(entry);
                if (!names.Add(relative.TrimEnd('/'))) throw new InvalidDataException("DSH 完整包包含重复路径。");
                total = checked(total + entry.Length);
                if (!relative.EndsWith("/", StringComparison.Ordinal)) files++;
                if (total > 4L * 1024 * 1024 * 1024 || entry.Length > 512L * 1024 * 1024)
                    throw new InvalidDataException("DSH 完整包解压大小超过限制。");
            }
            Directory.CreateDirectory(stage);
            long received = 0; int count = 0;
            byte[] buffer = new byte[128 * 1024];
            var update = Stopwatch.StartNew();
            var yield = Stopwatch.StartNew();
            progress?.Invoke(0, 0, total, files);
            // One extraction worker, one reusable buffer, no rescans of node_modules.
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                string relative = ValidateEntry(entry);
                string destination = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                if (relative.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using (var input = entry.Open())
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                {
                    long bytes = 0; int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        bytes += read;
                        if (bytes > entry.Length) throw new InvalidDataException("ZIP 字节数超过条目声明。");
                        output.Write(buffer, 0, read);
                        received += read;
                        if (update.ElapsedMilliseconds >= 1000)
                        { progress?.Invoke(count, received, total, files); update.Restart(); }
                        if (yield.ElapsedMilliseconds >= 32)
                        { Thread.Sleep(4); yield.Restart(); }
                    }
                    if (bytes != entry.Length) throw new InvalidDataException("DSH 完整包文件截断。");
                }
                count++;
                if (update.ElapsedMilliseconds >= 1000)
                { progress?.Invoke(count, received, total, files); update.Restart(); }
                if (yield.ElapsedMilliseconds >= 32)
                { Thread.Sleep(4); yield.Restart(); }
            }
            ValidateLayout(stage, manifest.Version);
            progress?.Invoke(count, received, total, files);
        }

        private static string ValidateEntry(ZipArchiveEntry entry)
        {
            string path = entry.FullName.Replace((char)92, '/');
            int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains(':') || path.Contains((char)0)
                || unixMode == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("完整包包含绝对路径或链接。");
            string[] parts = path.TrimEnd('/').Split('/');
            if (parts.Length == 0 || !AllowedItems.Contains(parts[0], StringComparer.Ordinal)
                || (parts[0] != "node_modules" && (parts.Length != 1 || path.EndsWith("/", StringComparison.Ordinal))))
                throw new InvalidDataException("完整包包含未知顶层内容。");
            foreach (string part in parts)
                if (string.IsNullOrEmpty(part) || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ")
                    || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || Regex.IsMatch(part.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw new InvalidDataException("完整包包含无效或越界路径。");
            return path;
        }

        public static void ValidateLayout(string root, string exactVersion)
        {
            foreach (string item in AllowedItems)
                if (!File.Exists(Path.Combine(root, item)) && !Directory.Exists(Path.Combine(root, item)))
                    throw new InvalidDataException("DSH 完整包缺少 " + item);
            using var bundle = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "dsh-bundle.json")));
            var info = bundle.RootElement;
            int node = info.GetProperty("nodeMajor").GetInt32();
            if (info.GetProperty("version").GetString() != exactVersion || info.GetProperty("package").GetString() != PackageName
                || info.GetProperty("platform").GetString() != "win-x64" || (node != 22 && node != 24)
                || info.GetProperty("entryPoint").GetString() != EntryPoint)
                throw new InvalidDataException("DSH 包内清单不匹配。");
            using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "package.json")));
            if (package.RootElement.GetProperty("name").GetString() != PackageName
                || package.RootElement.GetProperty("version").GetString() != exactVersion || !File.Exists(Path.Combine(root, EntryPoint)))
                throw new InvalidDataException("DSH 实际包版本或入口不匹配。");
            using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "package-lock.json")));
            if (lockFile.RootElement.GetProperty("packages").GetProperty("node_modules/@deepseek-ai/dsh").GetProperty("version").GetString() != exactVersion)
                throw new InvalidDataException("DSH 完整包依赖锁版本不匹配。");
            using var rootPackage = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "package.json")));
            if (rootPackage.RootElement.GetProperty("dependencies").GetProperty(PackageName).GetString() != exactVersion)
                throw new InvalidDataException("DSH 完整包根清单未固定精确版本。");
        }

        public static void ValidateRuntime(string root, string node, string version, string work, CancellationToken token, Action<string> log = null)
        {
            if (!File.Exists(node)) throw new FileNotFoundException("未找到 Node.js，无法离线验证完整包。", node);
            var home = Path.Combine(work, "validation-home");
            Directory.CreateDirectory(home);
            var environment = new Dictionary<string, string> { ["DSH_HOME"] = home, ["NPM_CONFIG_OFFLINE"] = "true",
                ["HOME"] = home, ["USERPROFILE"] = home, ["APPDATA"] = home, ["LOCALAPPDATA"] = home, ["TEMP"] = home, ["TMP"] = home };
            string nodeVersion = Run(node, new[] { "--version" }, root, environment, 15000, token).Trim();
            if (!nodeVersion.StartsWith("v22.", StringComparison.Ordinal) && !nodeVersion.StartsWith("v24.", StringComparison.Ordinal))
                throw new InvalidDataException("完整包目前支持 Node 22/24；实际为 " + nodeVersion + "。");
            string entry = Path.Combine(root, EntryPoint);
            string reportedVersion = Run(node, new[] { entry, "--version" }, root, environment, 60000, token);
            if (!reportedVersion.Contains(version, StringComparison.Ordinal)) throw new InvalidDataException("DSH CLI 版本验证失败：" + reportedVersion);
            Run(node, new[] { entry, "--help" }, root, environment, 60000, token);
            string script = Path.Combine(work, "validate-native.cjs");
            File.WriteAllText(script, "const path=require('node:path');const{createRequire}=require('node:module');const req=createRequire(path.join(process.argv[2],'package.json'));for(const name of ['koffi','node-pty','sharp']){req(name);console.log('native-load '+name);}");
            Run(node, new[] { script, root }, root, environment, 60000, token);
            log?.Invoke("离线 CLI --version/--help、koffi/node-pty/sharp 已通过；" + nodeVersion + "；" + root);
        }

        private static string Run(string file, string[] args, string working, Dictionary<string, string> environment, int timeout, CancellationToken token)
        {
            var info = new ProcessStartInfo(file) { WorkingDirectory = working, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
            // Do not execute machine/profile-specific Node preloads during an install probe.
            info.Environment.Remove("NODE_OPTIONS");
            info.Environment["NODE"] = file; info.Environment["NODE_EXE"] = file;
            foreach (string key in new List<string>(info.Environment.Keys))
                if (string.Equals(key, "PATH", StringComparison.OrdinalIgnoreCase)) info.Environment.Remove(key);
            info.Environment["Path"] = DshNpmBundleBuilder.BuildPath(file, Environment.GetEnvironmentVariable("PATH"));
            using var process = Process.Start(info);
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            var clock = Stopwatch.StartNew();
            try
            {
                while (!process.WaitForExit(100))
                {
                    token.ThrowIfCancellationRequested();
                    if (clock.ElapsedMilliseconds >= timeout) throw new TimeoutException("DSH 离线验证超时：" + string.Join(" ", args));
                }
                token.ThrowIfCancellationRequested();
                if (!Task.WaitAll(new Task[] { stdout, stderr }, 5000)) throw new TimeoutException("DSH 验证输出未关闭。");
                string output = stdout.Result + "\n" + stderr.Result;
                if (process.ExitCode != 0) throw new InvalidDataException("DSH 离线验证失败（退出码 " + process.ExitCode + "）：" + output);
                return output;
            }
            catch
            { try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } } catch { } throw; }
        }

        public static void Commit(string stage, string target, CancellationToken token, Action<string, double> progress = null, Action verifyCommitted = null,
            ISet<string> protectedPackages = null)
        {
            stage = Absolute(stage); target = Absolute(target);
            GuardAncestors(stage); GuardAncestors(target);
            string ownedWork = Path.GetDirectoryName(stage);
            bool ownedStage = string.Equals(stage, Path.Combine(ownedWork, "package"), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(ownedWork), Path.Combine(target, "temp", "updatepackage"), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(ownedWork).StartsWith("extract-", StringComparison.Ordinal) && TryReadOwner(ownedWork, out _, out _);
            if (Contains(stage, target) || (Contains(target, stage) && !ownedStage)) throw new IOException("DSH 暂存目录与部署目标不能重叠。");
            string bundleJson = File.ReadAllText(Path.Combine(stage, "dsh-bundle.json"));
            using var metadata = JsonDocument.Parse(bundleJson);
            string version = metadata.RootElement.GetProperty("version").GetString();
            ValidateLayout(stage, version);
            // Prepare merged package.json before any target writes. User dependencies/scripts remain.
            string targetPackage = Path.Combine(target, "package.json");
            if (File.Exists(targetPackage))
            {
                GuardAncestors(targetPackage);
                JsonObject document = JsonNode.Parse(File.ReadAllText(targetPackage)) as JsonObject
                    ?? throw new InvalidDataException("用户 package.json 必须为 JSON 对象；尚未修改安装目录。");
                if (document["dependencies"] == null) document["dependencies"] = new JsonObject();
                if (!(document["dependencies"] is JsonObject dependencies)) throw new InvalidDataException("用户 dependencies 字段无效。");
                dependencies[PackageName] = version;
                File.WriteAllText(Path.Combine(stage, "package.json"), document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            }
            var items = DeploymentItems(stage);
            items.RemoveAll(item => (protectedPackages != null && protectedPackages.Contains(item))
                || (Path.GetFileName(item).Equals("node", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(target, item))));
            var deployedPackages = new HashSet<string>(items.Select(item => item.Replace((char)92, '/')), StringComparer.OrdinalIgnoreCase);
            items.Add("package.json");
            // Merge supplied records while preserving unrelated root dependencies and lock entries.
            string targetLock = Path.Combine(target, "package-lock.json");
            if (File.Exists(targetLock))
            {
                GuardAncestors(targetLock);
                JsonObject current = JsonNode.Parse(File.ReadAllText(targetLock)) as JsonObject
                    ?? throw new InvalidDataException("用户 package-lock.json 必须为 JSON 对象。");
                JsonObject supplied = JsonNode.Parse(File.ReadAllText(Path.Combine(stage, "package-lock.json"))) as JsonObject;
                if (!(current["packages"] is JsonObject records) || !(supplied?["packages"] is JsonObject suppliedRecords))
                    throw new InvalidDataException("用户依赖锁不支持安全合并；尚未修改安装目录。");
                foreach (var pair in suppliedRecords)
                    if (pair.Key.StartsWith("node_modules/", StringComparison.Ordinal)
                        && deployedPackages.Contains(LockPackageRoot(pair.Key)))
                        records[pair.Key] = pair.Value?.DeepClone();
                if (records[""] is JsonObject rootRecord)
                {
                    if (rootRecord["dependencies"] == null) rootRecord["dependencies"] = new JsonObject();
                    if (!(rootRecord["dependencies"] is JsonObject rootDeps)) throw new InvalidDataException("用户依赖锁根依赖无效。");
                    rootDeps[PackageName] = version;
                }
                if (current["dependencies"] is JsonObject legacy && supplied["dependencies"] is JsonObject suppliedLegacy)
                    foreach (var pair in suppliedLegacy) legacy[pair.Key] = pair.Value?.DeepClone();
                File.WriteAllText(Path.Combine(stage, "package-lock.json"), current.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            }
            items.Add("package-lock.json");
            items.Add("dsh-bundle.json");
            foreach (string item in items) GuardAncestors(Path.Combine(target, item));
            string backup = Path.Combine(Path.GetDirectoryName(stage), "previous");
            if (Directory.Exists(backup) || File.Exists(backup)) throw new IOException("恢复目录已存在，拒绝覆盖。");
            Directory.CreateDirectory(backup);
            bool targetExisted = Directory.Exists(target);
            Directory.CreateDirectory(target);
            var old = new List<string>(); var added = new List<string>();
            try
            {
                int count = 0;
                foreach (string item in items)
                {
                    token.ThrowIfCancellationRequested();
                    string current = Path.Combine(target, item);
                    if (File.Exists(current) || Directory.Exists(current))
                    { Move(current, Path.Combine(backup, item)); old.Add(item); }
                    Move(Path.Combine(stage, item), current); added.Add(item);
                    count++;
                    progress?.Invoke("部署到 " + target + " · " + item.Replace((char)92, '/') + " · " + count + "/" + items.Count + " 项 · "
                        + Percent(count, items.Count).ToString("F1") + "%", Percent(count, items.Count));
                }
                token.ThrowIfCancellationRequested();
                verifyCommitted?.Invoke();
            }
            catch (Exception failure)
            {
                var errors = new List<Exception>();
                foreach (string item in added.AsEnumerable().Reverse())
                    try { Move(Path.Combine(target, item), Path.Combine(stage, item)); } catch (Exception error) { errors.Add(error); }
                foreach (string item in old.AsEnumerable().Reverse())
                    try { Move(Path.Combine(backup, item), Path.Combine(target, item)); } catch (Exception error) { errors.Add(error); }
                if (!targetExisted && Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target);
                if (errors.Count > 0) throw new RecoveryException("DSH 部署失败且恢复未完成。旧文件保留在：" + backup, new AggregateException(new[] { failure }.Concat(errors)));
                throw;
            }
        }

        private static List<string> DeploymentItems(string stage)
        {
            var items = new List<string>();
            string modules = Path.Combine(stage, "node_modules");
            foreach (string path in Directory.EnumerateFileSystemEntries(modules).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(path);
                if (name == ".package-lock.json") continue;
                if (name == ".bin")
                {
                    foreach (string bin in Directory.EnumerateFiles(path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                        if (Regex.IsMatch(Path.GetFileName(bin), @"^dsh(?:[.]cmd|[.]ps1)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                            items.Add(Path.GetRelativePath(stage, bin));
                }
                else if (name.StartsWith("@", StringComparison.Ordinal))
                {
                    foreach (string package in Directory.EnumerateDirectories(path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                        items.Add(Path.GetRelativePath(stage, package));
                }
                else items.Add(Path.GetRelativePath(stage, path));
            }
            return items;
        }
        private static string LockPackageRoot(string path)
        {
            string[] parts = path.Split('/');
            int length = parts.Length > 1 && parts[1].StartsWith("@", StringComparison.Ordinal) ? 3 : 2;
            return string.Join("/", parts.Take(length));
        }
        private static void Move(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
        }
        private static string Absolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("必须指定绝对路径。");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        private static bool Contains(string root, string path)
        {
            root = Absolute(root); path = Absolute(path);
            return string.Equals(root, path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        private static void GuardAncestors(string path)
        {
            string current = Absolute(path);
            while (!string.IsNullOrEmpty(current))
            {
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("DSH 部署路径不能穿过链接：" + current);
                current = Path.GetDirectoryName(current);
            }
        }
        private sealed class CleanupPlan
        {
            private readonly List<string> files = new List<string>();
            private readonly List<string> directories = new List<string>();
            private readonly Action<int, int> progress;
            private readonly Stopwatch update = Stopwatch.StartNew();
            private readonly Stopwatch yield = Stopwatch.StartNew();
            private int deleted;
            private int directoryIndex;
            private readonly string ownerContents;
            public CleanupPlan(string root, Action<int, int> progress)
            {
                this.progress = progress;
                var pending = new Stack<string>(); pending.Push(root);
                string owner = Path.Combine(root, OwnerFile);
                while (pending.Count > 0)
                {
                    string directory = pending.Pop();
                    GuardAncestors(directory);
                    directories.Add(directory);
                    foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0) pending.Push(path);
                        else if (!string.Equals(path, owner, StringComparison.OrdinalIgnoreCase)) files.Add(path);
                        Yield();
                    }
                }
                // Preserve proof of ownership until every payload file is removed, including retries.
                ownerContents = File.ReadAllText(owner);
                files.Add(owner);
                directoryIndex = directories.Count - 1;
                Notify();
            }
            public void Delete()
            {
                // The final file is the ownership marker. Keep it until all child
                // directories have also been removed so a failed cleanup can be retried.
                while (deleted < files.Count - 1)
                {
                    string path = files[deleted];
                    GuardAncestors(Path.GetDirectoryName(path));
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0 && (attributes & FileAttributes.Directory) != 0) Directory.Delete(path);
                        else
                        {
                            if ((attributes & FileAttributes.ReadOnly) != 0 && (attributes & FileAttributes.ReparsePoint) == 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                            File.Delete(path);
                        }
                    }
                    deleted++;
                    if (update.ElapsedMilliseconds >= 1000) Notify();
                    Yield();
                }
                while (directoryIndex >= 1)
                {
                    string path = directories[directoryIndex];
                    if (Directory.Exists(path))
                    {
                        GuardAncestors(path);
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                        Directory.Delete(path);
                    }
                    directoryIndex--;
                    Yield();
                }
                if (deleted < files.Count)
                {
                    string marker = files[deleted];
                    if (File.Exists(marker))
                    {
                        var attributes = File.GetAttributes(marker);
                        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(marker, attributes & ~FileAttributes.ReadOnly);
                        File.Delete(marker);
                    }
                    deleted++;
                }
                if (Directory.Exists(directories[0]))
                {
                    GuardAncestors(directories[0]);
                    var attributes = File.GetAttributes(directories[0]);
                    if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(directories[0], attributes & ~FileAttributes.ReadOnly);
                    try { Directory.Delete(directories[0]); }
                    catch
                    {
                        // Preserve ownership proof if a late write or open directory
                        // prevented removal after deleting the marker.
                        string marker = files[files.Count - 1];
                        if (!File.Exists(marker)) File.WriteAllText(marker, ownerContents);
                        deleted = Math.Min(deleted, files.Count - 1);
                        throw;
                    }
                }
                Notify();
            }
            private void Notify()
            {
                try { progress?.Invoke(deleted, files.Count); } catch { }
                update.Restart();
            }
            private void Yield()
            {
                if (yield.ElapsedMilliseconds >= 32) { Thread.Sleep(4); yield.Restart(); }
            }
        }
        private static double Percent(long done, long total) => total > 0 ? Math.Min(100, done * 100.0 / total) : 100;
        private static string Bytes(long value) => value >= 1024 * 1024 ? (value / 1048576.0).ToString("F1") + " MiB" : (value / 1024.0).ToString("F1") + " KiB";
    }
}
