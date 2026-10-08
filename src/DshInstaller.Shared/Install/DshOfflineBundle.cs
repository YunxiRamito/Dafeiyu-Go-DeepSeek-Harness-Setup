using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DshInstaller.Shared.Install
{
    public sealed class DshBundleManifest
    {
        public string Version { get; private set; }
        public string Sha256 { get; private set; }
        public long SizeBytes { get; private set; }
        public string FileName { get; private set; }
        public static DshBundleManifest Parse(string text, string expectedVersion)
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            string version = root.GetProperty("version").GetString();
            string hash = root.GetProperty("sha256").GetString();
            string file = root.GetProperty("fileName").GetString();
            long size = root.GetProperty("sizeBytes").GetInt64();
            if (!DshVersionResolver.IsExact(version) || version != expectedVersion
                || root.GetProperty("platform").GetString() != "win-x64" || root.GetProperty("nodeMajor").GetInt32() != 22
                || hash == null || !Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$") || size < 1 || size > 1024L * 1024 * 1024
                || file == null || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file != Path.GetFileName(file)
                || !file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DSH 完整本地包元数据无效，要求 Windows x64 / Node 22 / 精确版本及 SHA-256。");
            return new DshBundleManifest { Version = version, Sha256 = hash, SizeBytes = size, FileName = file };
        }
    }

    public static class DshOfflineBundle
    {
        private static readonly string[] BundleItems = { "node_modules", "package.json", "package-lock.json", "dsh-bundle.json" };
        public static async Task InstallAsync(InstallContext context, string exactVersion, string nodeDirectory, CancellationToken token)
        {
            string root = Path.GetFullPath(context.Options.DshRoot);
            if (!Path.IsPathFullyQualified(context.Options.DshRoot)) throw new ArgumentException("DSH directory must be absolute.");
            if (InstallPaths.HasReparseAncestor(root)) throw new IOException("DSH 安装路径不能穿过目录链接。");
            string node = Path.Combine(nodeDirectory, "node.exe");
            if (!File.Exists(node)) throw new FileNotFoundException("未找到 Node.js，无法验证完整本地包。", node);
            string baseUrl = BackendDownloadSource.BaseUrl + "/api/dsh/bundles/" + Uri.EscapeDataString(exactVersion);
            context.Report("正在获取 DSH " + exactVersion + " 完整本地包", 5);
            string json = await Task.Run(() => DownloadEngine.DownloadText(new List<string> { baseUrl + "/manifest" }, 8000,
                p => context.Report("正在读取完整包清单 · " + p.SourceLabel), () => token.IsCancellationRequested), token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) throw new IOException("后端尚未提供 DSH " + exactVersion + " 的 Windows 完整本地包，请选择其它版本或下载源。");
            var manifest = DshBundleManifest.Parse(json, exactVersion);
            Directory.CreateDirectory(context.Options.TempRoot);
            string archive = Path.Combine(context.Options.TempRoot, "dsh-bundle-" + exactVersion + ".zip");
            await Task.Run(() => DownloadEngine.Download(new List<string> { baseUrl + "/archive" }, archive,
                p =>
                {
                    if (p.ReceivedBytes > manifest.SizeBytes) throw new InvalidDataException("DSH 完整包超过清单声明大小。");
                    context.Report("下载完整包 · " + DownloadProgress.FormatBytes(p.ReceivedBytes) + " / "
                        + DownloadProgress.FormatBytes(manifest.SizeBytes) + " · " + p.SpeedText,
                        10 + Math.Min(1, (double)p.ReceivedBytes / manifest.SizeBytes) * 55);
                }, () => token.IsCancellationRequested,
                onNotice: message => context.Report(message),
                validateCompleted: path => VerifyArchive(path, manifest)), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            string work = Path.Combine(Path.GetDirectoryName(root), ".dsh-bundle-stage-" + Guid.NewGuid().ToString("N"));
            string stage = Path.Combine(work, "package");
            Directory.CreateDirectory(work);
            bool preserveRecovery = false;
            try
            {
                context.Report("正在解压完整包 · DSH " + exactVersion, 70);
                await Task.Run(() => ExtractAndValidate(archive, stage, manifest, token,
                    (count, total) => context.Report("正在解压 · " + count + " 个文件 · " + DownloadProgress.FormatBytes(total), -1)), token).ConfigureAwait(false);
                context.Report("正在离线验证 DSH " + exactVersion, 85);
                await Task.Run(() => ValidateRuntime(stage, node, exactVersion, work, token, context.Log), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                bool existed = Directory.Exists(root);
                Commit(stage, root, token);
                if (!existed) context.NoteCreatedDirectory(root);
                context.Log("完整本地部署成功：DSH " + exactVersion + "；客户端未运行 npm install。");
                context.Report("DSH " + exactVersion + " 已安装", 100);
            }
            catch (DshBundleRecoveryException)
            {
                preserveRecovery = true;
                context.Log("DSH 完整包恢复未完成，旧文件保留在：" + work);
                throw;
            }
            finally
            {
                if (!preserveRecovery && Directory.Exists(work)) Directory.Delete(work, true);
                if (File.Exists(archive)) File.Delete(archive);
            }
        }

        public static void VerifyArchive(string archive, DshBundleManifest manifest)
        {
            if (new FileInfo(archive).Length != manifest.SizeBytes
                || !string.Equals(DownloadEngine.ComputeSha256(archive), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DSH 完整包大小或 SHA-256 校验失败。");
        }

        public static void ExtractAndValidate(string archive, string stage, DshBundleManifest manifest, CancellationToken token,
            Action<int, long> progress = null)
        {
            VerifyArchive(archive, manifest);
            if (!Path.IsPathFullyQualified(stage) || Directory.Exists(stage)) throw new ArgumentException("Bundle stage must be a new absolute directory.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            using var zip = ZipFile.OpenRead(archive);
            if (zip.Entries.Count < 4 || zip.Entries.Count > 150000) throw new InvalidDataException("DSH 完整包条目数量异常。");
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                string path = ValidateEntry(entry);
                if (!names.Add(path.TrimEnd('/'))) throw new InvalidDataException("DSH 完整包包含重复路径。");
                total = checked(total + entry.Length);
                if (total > 4L * 1024 * 1024 * 1024 || entry.Length > 512L * 1024 * 1024)
                    throw new InvalidDataException("DSH 完整包解压大小超过限制。");
            }
            Directory.CreateDirectory(stage);
            long received = 0;
            int count = 0;
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
                    var buffer = new byte[128 * 1024];
                    long bytes = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        bytes += read;
                        if (bytes > entry.Length) throw new InvalidDataException("ZIP 文件字节数与条目声明不一致。");
                        output.Write(buffer, 0, read);
                    }
                    if (bytes != entry.Length) throw new InvalidDataException("DSH 完整包文件截断。");
                    received += bytes;
                }
                count++;
                if (count % 250 == 0) progress?.Invoke(count, received);
            }
            ValidateLayout(stage, manifest.Version);
            progress?.Invoke(count, received);
        }

        private static string ValidateEntry(ZipArchiveEntry entry)
        {
            string path = entry.FullName.Replace('\\', '/');
            int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains(':') || path.Contains('\0')
                || unixMode == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("DSH 完整包包含绝对路径或链接。");
            string[] parts = path.TrimEnd('/').Split('/');
            if (parts.Length == 0 || !BundleItems.Contains(parts[0], StringComparer.Ordinal)
                || (parts[0] != "node_modules" && parts.Length != 1))
                throw new InvalidDataException("DSH 完整包包含未知顶层内容。");
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part) || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ")
                    || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || Regex.IsMatch(part.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw new InvalidDataException("DSH 完整包包含无效或越界路径。");
            }
            return path;
        }

        public static void ValidateLayout(string root, string exactVersion)
        {
            foreach (string item in BundleItems)
                if (!File.Exists(Path.Combine(root, item)) && !Directory.Exists(Path.Combine(root, item)))
                    throw new InvalidDataException("DSH 完整包缺少 " + item);
            using var bundle = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "dsh-bundle.json")));
            var info = bundle.RootElement;
            if (info.GetProperty("version").GetString() != exactVersion || info.GetProperty("package").GetString() != WellKnown.DshPackage
                || info.GetProperty("platform").GetString() != "win-x64" || info.GetProperty("nodeMajor").GetInt32() != 22
                || info.GetProperty("entryPoint").GetString() != "node_modules/@deepseek-ai/dsh/lib/bin.js")
                throw new InvalidDataException("DSH 包内清单不匹配。");
            string packagePath = Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "package.json");
            using var package = JsonDocument.Parse(File.ReadAllText(packagePath));
            if (package.RootElement.GetProperty("name").GetString() != WellKnown.DshPackage
                || package.RootElement.GetProperty("version").GetString() != exactVersion
                || !File.Exists(Path.Combine(root, WellKnown.DshMarker)))
                throw new InvalidDataException("DSH 实际包版本或 CLI 入口不匹配。");
            using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "package-lock.json")));
            if (lockFile.RootElement.GetProperty("packages").GetProperty("node_modules/@deepseek-ai/dsh").GetProperty("version").GetString() != exactVersion)
                throw new InvalidDataException("DSH 依赖锁版本不匹配。");
            using var rootPackage = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "package.json")));
            if (rootPackage.RootElement.GetProperty("dependencies").GetProperty(WellKnown.DshPackage).GetString() != exactVersion)
                throw new InvalidDataException("DSH 根包未固定相同的精确版本。");
        }

        private static void ValidateRuntime(string root, string node, string version, string work, CancellationToken token, Action<string> log)
        {
            var environment = new Dictionary<string, string> { ["DSH_HOME"] = Path.Combine(work, "validation-home"), ["NPM_CONFIG_OFFLINE"] = "true" };
            var nodeResult = ProcessRunner.Run(node, "--version", root, 15000, environment: environment, cancellationToken: token);
            if (!nodeResult.Ok || !nodeResult.StandardOutput.Trim().StartsWith("v22.", StringComparison.Ordinal))
                throw new InvalidDataException("该完整包需要 Node.js 22，当前 Node 版本不匹配。");
            string entry = Path.Combine(root, WellKnown.DshMarker);
            foreach (string command in new[] { "--version", "--help" })
            {
                var result = ProcessRunner.Run(node, "\"" + entry + "\" " + command, root, 60000,
                    environment: environment, cancellationToken: token);
                token.ThrowIfCancellationRequested();
                if (!result.Ok || (command == "--version" && !result.Combined.Contains(version, StringComparison.Ordinal)))
                    throw new InvalidDataException("DSH 完整包离线 CLI 验证失败：" + result.Combined);
            }
            string nativeCheck = Path.Combine(work, "validate-native.cjs");
            File.WriteAllText(nativeCheck,
                "const path=require('path');const {createRequire}=require('module');const req=createRequire(path.join(process.argv[2],'package.json'));"
                + "for(const name of ['koffi','node-pty','sharp']){req(name);console.log('native-load '+name);}");
            var natives = ProcessRunner.Run(node, "\"" + nativeCheck + "\" \"" + root + "\"", root, 60000,
                environment: environment, cancellationToken: token);
            token.ThrowIfCancellationRequested();
            if (!natives.Ok) throw new InvalidDataException("DSH Windows 原生组件离线验证失败：" + natives.Combined);
            log("DSH CLI --version / --help 离线检查通过；Node 22 / Windows x64。");
        }

        public static void Commit(string stage, string target, CancellationToken token)
        {
            if (!Path.IsPathFullyQualified(stage) || !Path.IsPathFullyQualified(target)) throw new ArgumentException("Deployment directories must be absolute.");
            if (InstallPaths.HasReparseAncestor(stage) || InstallPaths.HasReparseAncestor(target))
                throw new IOException("DSH 部署路径不能穿过目录链接。");
            if (InstallPaths.Overlaps(stage, target)) throw new IOException("DSH 暂存目录不能与安装目标重叠。");
            Directory.CreateDirectory(target);
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("DSH 根目录不能是链接。");
            string backup = Path.Combine(Path.GetDirectoryName(stage), "previous");
            Directory.CreateDirectory(backup);
            var movedOld = new List<string>();
            var movedNew = new List<string>();
            try
            {
                foreach (string item in BundleItems)
                {
                    token.ThrowIfCancellationRequested();
                    string current = Path.Combine(target, item);
                    if (File.Exists(current) || Directory.Exists(current))
                    {
                        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("现有 DSH 依赖目录不能是链接。");
                        Move(current, Path.Combine(backup, item));
                        movedOld.Add(item);
                    }
                    Move(Path.Combine(stage, item), current);
                    movedNew.Add(item);
                }
            }
            catch (Exception deploymentError)
            {
                var recoveryErrors = new List<Exception>();
                foreach (string item in movedNew.AsEnumerable().Reverse())
                    try { Move(Path.Combine(target, item), Path.Combine(stage, item)); } catch (Exception error) { recoveryErrors.Add(error); }
                foreach (string item in movedOld.AsEnumerable().Reverse())
                    try { Move(Path.Combine(backup, item), Path.Combine(target, item)); } catch (Exception error) { recoveryErrors.Add(error); }
                if (recoveryErrors.Count > 0)
                    throw new DshBundleRecoveryException("DSH 部署失败且旧文件恢复未完成；旧文件保留在 " + backup,
                        new AggregateException(new[] { deploymentError }.Concat(recoveryErrors)));
                throw;
            }
        }

        private static void Move(string source, string target)
        { if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target); }
    }
    internal sealed class DshBundleRecoveryException : IOException
    {
        internal DshBundleRecoveryException(string message, Exception inner) : base(message, inner) { }
    }
}
