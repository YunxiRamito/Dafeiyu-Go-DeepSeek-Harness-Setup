using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DafeiyuGo.Shared;

namespace DshInstaller.Shared.Install
{
    public sealed class DshBundleManifest
    {
        internal DshBundleDeployment.Manifest Value { get; private set; }
        public string Version => Value.Version;
        public string Sha256 => Value.Sha256;
        public long SizeBytes => Value.SizeBytes;
        public string FileName => Value.FileName;
        public static DshBundleManifest Parse(string text, string expectedVersion)
            => new DshBundleManifest { Value = DshBundleDeployment.ParseManifest(text, expectedVersion) };
    }

    // Product download adapter only; deployment is identical to the launcher helper.
    public static class DshOfflineBundle
    {
        public static async Task InstallAsync(InstallContext context, string exactVersion, string nodeDirectory, CancellationToken token)
        {
            string root = Path.GetFullPath(context.Options.DshRoot);
            if (!Path.IsPathFullyQualified(context.Options.DshRoot)) throw new ArgumentException("DSH directory must be absolute.");
            if (InstallPaths.HasReparseAncestor(root)) throw new IOException("DSH 安装路径不能穿过目录链接。");
            string node = Path.Combine(nodeDirectory, "node.exe");
            if (!File.Exists(node)) throw new FileNotFoundException("未找到 Node.js，无法验证完整包。", node);
            string baseUrl = BackendDownloadSource.BaseUrl + "/api/dsh/bundles/" + Uri.EscapeDataString(exactVersion);
            context.Report("检查 DSH 安装包", 0);
            context.Log("DSH " + exactVersion + "；安装目录：" + root + "；完整包地址：" + baseUrl);
            string json;
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
            {
                ProxySupport.Apply(handler);
                BackendDownloadSource.Apply(handler);
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                json = await ReadManifestAsync(client, baseUrl + "/manifest", token).ConfigureAwait(false);
            }
            string archive = null;
            string buildDirectory = null;
            string downloadDirectory = null;
            bool existed = Directory.Exists(root);
            try
            {
                if (json == null)
                {
                    context.Log("服务器尚未提供 DSH " + exactVersion + " 完整包（HTTP 404/410）；在隔离目录构建同一精确版本，验证成功后再部署。");
                    var environment = CreateBuildEnvironment(context.Options.SourcePreference);
                    string registry = MirrorSource.NpmRegistry(context.Options.SourcePreference);
                    archive = await Task.Run(() => DshNpmBundleBuilder.Build(exactVersion, root, node, registry, environment,
                        (detail, percent) => context.Report(detail, percent < 0 ? -1 : percent * .60), token, context.Log), token).ConfigureAwait(false);
                    buildDirectory = Path.GetDirectoryName(archive);
                    json = File.ReadAllText(archive + ".manifest.json");
                }
                var manifest = DshBundleManifest.Parse(json, exactVersion);
                if (archive == null)
                {
                    downloadDirectory = DshBundleDeployment.CreateWorkDirectory(root, "download", exactVersion, context.Log);
                    archive = Path.Combine(downloadDirectory, manifest.FileName);
                    await Task.Run(() => DownloadEngine.Download(new List<string> { baseUrl + "/archive" }, archive,
                    p =>
                    {
                        if (p.ReceivedBytes > manifest.SizeBytes) throw new InvalidDataException("DSH 完整包超过清单声明大小。");
                        double fraction = Math.Min(1, (double)p.ReceivedBytes / manifest.SizeBytes);
                        context.Report("下载 DSH · " + DownloadProgress.FormatBytes(p.ReceivedBytes) + "/"
                            + DownloadProgress.FormatBytes(manifest.SizeBytes) + " · " + p.SpeedText, fraction * 60);
                    }, () => token.IsCancellationRequested,
                    onNotice: message => context.Log(message), allowSegmented: false, validateCompleted: path => VerifyArchive(path, manifest)), token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                await Task.Run(() => DshBundleDeployment.Deploy(archive, json, exactVersion, root, node,
                    (detail, percent) => context.Report(percent >= 100 ? "安装包已部署" : detail,
                        percent >= 100 ? 99.99 : 60 + percent * .40), token, context.Log), token).ConfigureAwait(false);
                if (!existed) context.NoteCreatedDirectory(root);
            }
            finally
            {
                if (buildDirectory != null) DshBundleDeployment.CleanupWorkDirectory(buildDirectory, context.Log,
                    (done, total) => context.Report("清理（" + done + "/" + total + "）", 99.99));
                if (downloadDirectory != null) DshBundleDeployment.CleanupWorkDirectory(downloadDirectory, context.Log,
                    (done, total) => context.Report("清理（" + done + "/" + total + "）", 99.99));
            }
            context.Report("安装完成", 100);
        }

        // Only an absent server artifact permits local construction. Auth/network/server and
        // integrity failures must remain failures rather than changing installation channels.
        internal static async Task<string> ReadManifestAsync(HttpClient client, string url, CancellationToken token)
        {
            using var response = await client.GetAsync(url, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone) return null;
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("DSH 完整包清单为空：" + url);
            return json;
        }

        internal static IDictionary<string, string> CreateBuildEnvironment(string sourcePreference)
        {
            var process = new ProcessStartInfo();
            var environment = ProxySupport.ProcessEnvironment(new Dictionary<string, string>(process.Environment, StringComparer.OrdinalIgnoreCase));
            process.Environment.Clear();
            foreach (var pair in environment) process.Environment[pair.Key] = pair.Value;
            var packageEnvironment = PackageDownloadEnvironment.Create(sourcePreference);
            if (packageEnvironment != null)
                foreach (var pair in packageEnvironment) process.Environment[pair.Key] = pair.Value;
            PackageDownloadEnvironment.Apply(process, packageEnvironment);
            process.Environment["npm_config_registry"] = MirrorSource.NpmRegistry(sourcePreference);
            return new Dictionary<string, string>(process.Environment, StringComparer.OrdinalIgnoreCase);
        }

        public static void VerifyArchive(string archive, DshBundleManifest manifest)
            => DshBundleDeployment.VerifyArchive(archive, manifest.Value);
        public static void ExtractAndValidate(string archive, string stage, DshBundleManifest manifest, CancellationToken token,
            Action<int, long> progress = null)
            => DshBundleDeployment.ExtractAndValidate(archive, stage, manifest.Value, token, (count, bytes, total, files) => progress?.Invoke(count, bytes));
        public static void ValidateLayout(string root, string version) => DshBundleDeployment.ValidateLayout(root, version);
        private static void ValidateRuntime(string root, string node, string version, string work, CancellationToken token, Action<string> log)
            => DshBundleDeployment.ValidateRuntime(root, node, version, work, token, log);
        public static void Commit(string stage, string target, CancellationToken token)
            => DshBundleDeployment.Commit(stage, target, token);
    }
}
