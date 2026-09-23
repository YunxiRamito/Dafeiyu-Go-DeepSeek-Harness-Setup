using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace DshInstaller.Shared.Install
{
    public static class RecommendedPluginInstaller
    {
        public static void Install(
            InstallOptions options,
            IList<string> specs,
            Action<string, double> report,
            Action<string> log,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options == null
                || specs == null
                || specs.Count == 0)
            {
                Report(report, "没有选择推荐插件", 100);
                return;
            }

            string nodeDirectory = Path.Combine(
                options.ComponentsRoot,
                "node");
            string gitDirectory = Path.Combine(
                options.ComponentsRoot,
                "git",
                "cmd");
            string pnpmDirectory = Path.Combine(
                options.ComponentsRoot,
                "pnpm");
            string pnpm = FindPnpm(options.ComponentsRoot, nodeDirectory);
            if (String.IsNullOrWhiteSpace(pnpm))
            {
                throw new InvalidOperationException(
                    "未找到 pnpm，无法安装推荐插件。");
            }

            string profileDirectory = Path.Combine(
                options.DshRoot,
                ".dsh",
                "profiles",
                "web");
            Directory.CreateDirectory(profileDirectory);
            EnsureProfileFile(profileDirectory);

            string extraPath = nodeDirectory
                + ";"
                + gitDirectory
                + ";"
                + pnpmDirectory;
            string pluginsRoot = Path.Combine(
                options.DshRoot,
                "plugins");
            string tempRoot = Path.Combine(
                String.IsNullOrWhiteSpace(options.TempRoot)
                    ? Path.GetTempPath()
                    : options.TempRoot,
                "recommended-plugins");
            Directory.CreateDirectory(pluginsRoot);
            Directory.CreateDirectory(tempRoot);

            int total = specs.Count;
            List<string> linkedKeys = new List<string>();
            for (int index = 0; index < total; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string raw = specs[index];
                string display = DescribeSpecifier(raw);
                double startPercent = 5.0 + index * 60.0 / total;
                double endPercent = 5.0 + (index + 1) * 60.0 / total;
                if (log != null)
                {
                    log("下载插件中 ["
                        + (index + 1)
                        + "/"
                        + total
                        + "]: "
                        + display
                        + " <- "
                        + raw);
                }

                PluginDescriptor descriptor =
                    PluginDescriptor.Parse(raw);
                if (descriptor.IsGitHub)
                {
                    string key = InstallGitHubPlugin(
                        options,
                        descriptor,
                        display,
                        index + 1,
                        total,
                        startPercent,
                        endPercent,
                        pluginsRoot,
                        profileDirectory,
                        tempRoot,
                        extraPath,
                        report,
                        log,
                        cancellationToken);
                    if (!String.IsNullOrWhiteSpace(key))
                    {
                        linkedKeys.Add(key);
                    }
                }
                else if (!String.IsNullOrWhiteSpace(
                    descriptor.NpmPackage))
                {
                    InstallNpmPlugin(
                        profileDirectory,
                        descriptor.NpmPackage,
                        display,
                        startPercent,
                        endPercent,
                        extraPath,
                        options.SourcePreference,
                        report,
                        log,
                        cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException(
                        "无法识别推荐插件安装表达式: " + raw);
                }
            }

            Report(report, "下载插件完成 · 正在安装", 70);
            if (linkedKeys.Count > 0)
            {
                RunPnpmInstall(
                    pnpm,
                    profileDirectory,
                    "安装插件中 · 正在部署到 profile",
                    70,
                    100,
                    extraPath,
                    options.SourcePreference,
                    report,
                    log,
                    cancellationToken);
            }

            Report(report, "推荐插件安装完成", 100);
        }

        private static string InstallGitHubPlugin(
            InstallOptions options,
            PluginDescriptor descriptor,
            string display,
            int index,
            int total,
            double startPercent,
            double endPercent,
            string pluginsRoot,
            string profileDirectory,
            string tempRoot,
            string extraPath,
            Action<string, double> report,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            string folder = descriptor.FolderName;
            string target = Path.Combine(pluginsRoot, folder);
            string archive = Path.Combine(
                tempRoot,
                folder + "-" + Guid.NewGuid().ToString("N") + ".tar.gz");
            string extractRoot = archive + ".extract";

            try
            {
                bool official = !MirrorSource.IsChina(
                    options.SourcePreference);
                string reference = String.IsNullOrWhiteSpace(
                    descriptor.Revision)
                        ? "main"
                        : descriptor.Revision;
                bool pinned = !String.IsNullOrWhiteSpace(
                    descriptor.Revision);
                List<string> urls = BuildTarballUrls(
                    descriptor,
                    reference,
                    pinned,
                    official);

                if (log != null)
                {
                    log("插件下载源顺序: " + String.Join(" -> ", urls.ToArray()));
                }

                double downloadStart = startPercent;
                double downloadEnd = startPercent
                    + (endPercent - startPercent) * 0.82;
                Action<DownloadProgress> onProgress =
                    delegate(DownloadProgress progress)
                    {
                        double fraction = progress.Fraction;
                        if (fraction < 0)
                        {
                            fraction = 0;
                        }

                        Report(
                            report,
                            "下载插件中 · "
                                + display
                                + " ["
                                + index
                                + "/"
                                + total
                                + "] · "
                                + DownloadProgress.FormatBytes(
                                    progress.ReceivedBytes)
                                + " / "
                                + DownloadProgress.FormatBytes(
                                    progress.TotalBytes),
                            downloadStart
                                + fraction
                                * (downloadEnd - downloadStart));
                    };
                Func<bool> cancelled =
                    delegate { return cancellationToken.IsCancellationRequested; };
                Action<string> notice =
                    delegate(string text)
                    {
                        if (log != null)
                        {
                            log(text);
                        }
                    };

                string selected = null;
                if (SegmentedDownloader.TryDownload(
                    urls,
                    archive,
                    onProgress,
                    cancelled,
                    notice,
                    128L * 1024,
                    4))
                {
                    selected = "分段下载(4 线程)";
                    if (log != null)
                    {
                        log("插件分段下载完成: " + display);
                    }
                }
                else
                {
                    selected = DownloadEngine.Download(
                        urls,
                        archive,
                        onProgress,
                        cancelled,
                        delegate(string failedUrl)
                        {
                            if (log != null)
                            {
                                log("插件下载源失败: " + failedUrl);
                            }

                            return true;
                        },
                        notice,
                        false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (log != null)
                {
                    log("插件下载成功: " + selected);
                }

                Report(
                    report,
                    "安装插件中 · 正在解压 " + display,
                    downloadEnd);
                DeleteDirectory(extractRoot);
                ArchiveExtractor.Extract(archive, extractRoot);

                string source = FindPluginSource(extractRoot);
                if (String.IsNullOrWhiteSpace(source))
                {
                    throw new InvalidOperationException(
                        "插件压缩包里没有 package.json: " + display);
                }

                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }

                Directory.CreateDirectory(target);
                CopyDirectory(source, target);

                string key;
                bool declaresBundle;
                bool hasDependencies;
                ReadPluginMetadata(
                    target,
                    folder,
                    out key,
                    out declaresBundle,
                    out hasDependencies);
                string relative = Path.GetRelativePath(
                    profileDirectory,
                    target).Replace('\\', '/');
                AddLinkedPlugin(
                    profileDirectory,
                    key,
                    "link:" + relative,
                    declaresBundle);

                if (hasDependencies)
                {
                    Report(
                        report,
                        "安装插件中 · 正在安装 " + display + " 的依赖",
                        downloadEnd);
                    RunPnpmInstall(
                        FindPnpm(options.ComponentsRoot, Path.Combine(
                            options.ComponentsRoot,
                            "node")),
                        target,
                        "安装插件中 · " + display + " 依赖",
                        downloadEnd,
                        endPercent,
                        extraPath,
                        options.SourcePreference,
                        report,
                        log,
                        cancellationToken);
                }

                return key;
            }
            finally
            {
                TryDeleteFile(archive);
                TryDeleteDirectory(extractRoot);
            }
        }

        private static void InstallNpmPlugin(
            string profileDirectory,
            string packageSpec,
            string display,
            double startPercent,
            double endPercent,
            string extraPath,
            string sourcePreference,
            Action<string, double> report,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            string pnpm = FindPnpmFromPath(extraPath);
            if (String.IsNullOrWhiteSpace(pnpm))
            {
                throw new InvalidOperationException(
                    "未找到 pnpm，无法安装推荐插件。");
            }

            DateTime startedUtc = DateTime.UtcNow;
            ProcessRunner.Result result;
            using (Timer heartbeat = new Timer(
                delegate
                {
                    double seconds = Math.Max(
                        0.0,
                        (DateTime.UtcNow - startedUtc).TotalSeconds);
                    Report(
                        report,
                        "下载插件中 · "
                            + display
                            + " · "
                            + (int)Math.Round(seconds)
                            + " 秒",
                        Math.Min(
                            endPercent - 0.5,
                            startPercent + seconds * 6.0));
                },
                null,
                500,
                1000))
            {
                Dictionary<string, string> environment =
                    new Dictionary<string, string>
                    {
                        ["npm_config_registry"] =
                            MirrorSource.NpmRegistry(sourcePreference),
                        ["GIT_TERMINAL_PROMPT"] = "0"
                    };
                result = ProcessRunner.Run(
                    "cmd.exe",
                    "/d /s /c \"\"" + pnpm + "\" add \""
                        + packageSpec.Replace("\"", "\\\"")
                        + "\" --reporter=append-only\"",
                    profileDirectory,
                    30 * 60 * 1000,
                    delegate(string line)
                    {
                        if (!String.IsNullOrWhiteSpace(line) && log != null)
                        {
                            log("  [" + display + "] " + line.Trim());
                        }
                    },
                    extraPath,
                    environment,
                    cancellationToken);
            }

            if (result.Cancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    "下载插件失败: "
                    + display
                    + "（退出码 "
                    + result.ExitCode
                    + "）");
            }
        }

        private static void RunPnpmInstall(
            string pnpm,
            string workingDirectory,
            string label,
            double startPercent,
            double endPercent,
            string extraPath,
            string sourcePreference,
            Action<string, double> report,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            if (String.IsNullOrWhiteSpace(pnpm))
            {
                throw new InvalidOperationException(
                    "未找到 pnpm，无法安装推荐插件。");
            }

            DateTime startedUtc = DateTime.UtcNow;
            ProcessRunner.Result result;
            using (Timer heartbeat = new Timer(
                delegate
                {
                    double seconds = Math.Max(
                        0.0,
                        (DateTime.UtcNow - startedUtc).TotalSeconds);
                    Report(
                        report,
                        label + " · " + (int)Math.Round(seconds) + " 秒",
                        Math.Min(
                            endPercent - 0.5,
                            startPercent + seconds * 5.0));
                },
                null,
                500,
                1000))
            {
                Dictionary<string, string> environment =
                    new Dictionary<string, string>
                    {
                        ["npm_config_registry"] =
                            MirrorSource.NpmRegistry(sourcePreference),
                        ["GIT_TERMINAL_PROMPT"] = "0"
                    };
                result = ProcessRunner.Run(
                    "cmd.exe",
                    "/d /s /c \"\"" + pnpm
                        + "\" install --reporter=append-only\"",
                    workingDirectory,
                    30 * 60 * 1000,
                    delegate(string line)
                    {
                        if (!String.IsNullOrWhiteSpace(line) && log != null)
                        {
                            log("  " + line.Trim());
                        }
                    },
                    extraPath,
                    environment,
                    cancellationToken);
            }

            if (result.Cancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    label
                    + "失败（退出码 "
                    + result.ExitCode
                    + "）");
            }
        }

        private static List<string> BuildTarballUrls(
            PluginDescriptor descriptor,
            string reference,
            bool pinned,
            bool official)
        {
            string archiveReference = pinned
                ? Uri.EscapeDataString(reference)
                : "refs/heads/" + Uri.EscapeDataString(reference);
            string archive = "https://github.com/"
                + descriptor.Owner + "/" + descriptor.Repository
                + "/archive/" + archiveReference + ".tar.gz";
            string codeload = "https://codeload.github.com/"
                + descriptor.Owner + "/" + descriptor.Repository
                + "/tar.gz/" + archiveReference;

            List<string> urls = new List<string>();
            if (official)
            {
                urls.Add(codeload);
                urls.Add(archive);
            }
            else
            {
                urls.AddRange(MirrorSource.GitHubProxyUrls(
                    archive,
                    MirrorSource.China));
            }

            return urls;
        }

        private static void ReadPluginMetadata(
            string directory,
            string fallbackKey,
            out string key,
            out bool declaresBundle,
            out bool hasDependencies)
        {
            key = fallbackKey;
            declaresBundle = false;
            hasDependencies = false;

            string path = Path.Combine(directory, "package.json");
            using (JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                JsonElement value;
                if (root.TryGetProperty("name", out value)
                    && value.ValueKind == JsonValueKind.String
                    && !String.IsNullOrWhiteSpace(value.GetString()))
                {
                    key = value.GetString();
                }

                JsonElement dependencies;
                hasDependencies =
                    root.TryGetProperty("dependencies", out dependencies)
                    && dependencies.ValueKind == JsonValueKind.Object
                    && dependencies.EnumerateObject().MoveNext();

                JsonElement dsh;
                if (root.TryGetProperty("dsh", out dsh)
                    && dsh.ValueKind == JsonValueKind.Object)
                {
                    JsonElement bundle;
                    JsonElement patch;
                    declaresBundle =
                        dsh.TryGetProperty("bundle", out bundle)
                        && bundle.ValueKind == JsonValueKind.Object
                        && bundle.TryGetProperty("patch", out patch);
                }
            }
        }

        private static void AddLinkedPlugin(
            string profileDirectory,
            string key,
            string dependency,
            bool declaresBundle)
        {
            string path = Path.Combine(profileDirectory, "package.json");
            JsonObject root = JsonNode.Parse(
                File.ReadAllText(path)) as JsonObject;
            JsonObject dependencies = root["dependencies"] as JsonObject;
            if (dependencies == null)
            {
                dependencies = new JsonObject();
                root["dependencies"] = dependencies;
            }

            dependencies[key] = dependency;
            if (declaresBundle)
            {
                AddBundle(root, key);
            }

            WriteJson(path, root);
        }

        private static void AddBundle(JsonObject root, string key)
        {
            JsonObject dsh = root["dsh"] as JsonObject;
            if (dsh == null)
            {
                dsh = new JsonObject();
                root["dsh"] = dsh;
            }

            JsonObject profile = dsh["profile"] as JsonObject;
            if (profile == null)
            {
                profile = new JsonObject();
                dsh["profile"] = profile;
            }

            JsonArray bundles = profile["bundles"] as JsonArray;
            if (bundles == null)
            {
                bundles = new JsonArray();
                profile["bundles"] = bundles;
            }

            for (int index = 0; index < bundles.Count; index++)
            {
                string value = bundles[index]?.GetValue<string>();
                if (String.Equals(
                    value,
                    key,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            bundles.Add(key);
        }

        private static void EnsureProfileFile(string profileDirectory)
        {
            string path = Path.Combine(profileDirectory, "package.json");
            if (File.Exists(path))
            {
                return;
            }

            JsonObject root = new JsonObject
            {
                ["name"] = "dsh-profile-web",
                ["private"] = true,
                ["dependencies"] = new JsonObject(),
                ["dsh"] = new JsonObject
                {
                    ["profile"] = new JsonObject
                    {
                        ["bundles"] = new JsonArray(
                            "@deepseek-ai/dsh-base",
                            "@deepseek-ai/dsh-web-app"),
                        ["patchReload"] = "live"
                    }
                }
            };
            WriteJson(path, root);
        }

        private static void WriteJson(string path, JsonObject root)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(
                temporary,
                root.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder
                        .UnsafeRelaxedJsonEscaping
                }),
                new System.Text.UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        private static string FindPluginSource(string extractRoot)
        {
            if (File.Exists(Path.Combine(
                extractRoot,
                "package.json")))
            {
                return extractRoot;
            }

            string[] children = Directory.GetDirectories(extractRoot);
            for (int index = 0; index < children.Length; index++)
            {
                if (File.Exists(Path.Combine(
                    children[index],
                    "package.json")))
                {
                    return children[index];
                }
            }

            return null;
        }

        private static void CopyDirectory(
            string source,
            string target)
        {
            string[] files = Directory.GetFiles(
                source,
                "*",
                SearchOption.AllDirectories);
            for (int index = 0; index < files.Length; index++)
            {
                string relative = Path.GetRelativePath(
                    source,
                    files[index]);
                string destination = Path.Combine(
                    target,
                    relative);
                string parent = Path.GetDirectoryName(destination);
                if (!String.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(files[index], destination, true);
            }
        }

        private static string FindPnpm(
            string componentsRoot,
            string nodeDirectory)
        {
            string[] candidates =
            {
                Path.Combine(componentsRoot, "pnpm", "pnpm.cmd"),
                Path.Combine(componentsRoot, "pnpm", "pnpm.exe"),
                Path.Combine(nodeDirectory, "pnpm.cmd"),
                Path.Combine(nodeDirectory, "pnpm.exe")
            };
            for (int index = 0; index < candidates.Length; index++)
            {
                if (File.Exists(candidates[index]))
                {
                    return candidates[index];
                }
            }

            return null;
        }

        private static string FindPnpmFromPath(string extraPath)
        {
            string[] directories = (extraPath ?? String.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < directories.Length; index++)
            {
                string cmd = Path.Combine(
                    directories[index].Trim(),
                    "pnpm.cmd");
                if (File.Exists(cmd))
                {
                    return cmd;
                }

                string exe = Path.Combine(
                    directories[index].Trim(),
                    "pnpm.exe");
                if (File.Exists(exe))
                {
                    return exe;
                }
            }

            return null;
        }

        private static string DescribeSpecifier(string spec)
        {
            PluginDescriptor descriptor = PluginDescriptor.Parse(spec);
            if (descriptor.IsGitHub)
            {
                return descriptor.Owner
                    + "/"
                    + descriptor.Repository;
            }

            return String.IsNullOrWhiteSpace(descriptor.NpmPackage)
                ? "未知插件"
                : descriptor.NpmPackage;
        }

        private static void Report(
            Action<string, double> report,
            string text,
            double progress)
        {
            if (report != null)
            {
                report(text, progress);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            Directory.Delete(path, true);
        }

        private sealed class PluginDescriptor
        {
            public string Owner { get; set; } = String.Empty;
            public string Repository { get; set; } = String.Empty;
            public string Revision { get; set; } = String.Empty;
            public string NpmPackage { get; set; } = String.Empty;

            public bool IsGitHub
            {
                get
                {
                    return !String.IsNullOrWhiteSpace(Owner)
                        && !String.IsNullOrWhiteSpace(Repository);
                }
            }

            public string FolderName
            {
                get
                {
                    return Sanitize(
                        IsGitHub
                            ? Repository
                            : NpmPackage);
                }
            }

            public static PluginDescriptor Parse(string input)
            {
                PluginDescriptor descriptor = new PluginDescriptor();
                if (String.IsNullOrWhiteSpace(input))
                {
                    return descriptor;
                }

                string value = input.Trim();
                if (value.StartsWith(
                    "npm:",
                    StringComparison.OrdinalIgnoreCase))
                {
                    descriptor.NpmPackage = value.Substring(4).Trim();
                    return descriptor;
                }

                if (value.StartsWith(
                    "github:",
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(7);
                }
                else if (value.StartsWith(
                    "https://github.com/",
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(19);
                }

                int hash = value.IndexOf('#');
                if (hash >= 0)
                {
                    descriptor.Revision = value
                        .Substring(hash + 1)
                        .Trim('/');
                    value = value.Substring(0, hash);
                }

                value = value.TrimEnd('/');
                if (value.EndsWith(
                    ".git",
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(0, value.Length - 4);
                }

                int slash = value.IndexOf('/');
                if (slash > 0)
                {
                    descriptor.Owner = value.Substring(0, slash).Trim();
                    string repository = value
                        .Substring(slash + 1)
                        .Trim();
                    int extra = repository.IndexOf('/');
                    if (extra >= 0)
                    {
                        repository = repository.Substring(0, extra);
                    }

                    descriptor.Repository = repository;
                    return descriptor;
                }

                descriptor.NpmPackage = value;
                return descriptor;
            }

            private static string Sanitize(string value)
            {
                System.Text.StringBuilder builder =
                    new System.Text.StringBuilder();
                for (int index = 0; index < (value ?? String.Empty).Length; index++)
                {
                    char ch = value[index];
                    if (Char.IsLetterOrDigit(ch)
                        || ch == '.'
                        || ch == '-'
                        || ch == '_')
                    {
                        builder.Append(ch);
                    }
                    else if (ch == '/' || ch == '@' || ch == ' ')
                    {
                        builder.Append('-');
                    }
                }

                string result = builder.ToString().Trim('-');
                return result.Length == 0 ? "plugin" : result;
            }
        }
    }
}
