using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DshInstaller.Shared.Install
{
    /// <summary>清单里发现的一个问题。</summary>
    public sealed class ManifestProblem
    {
        /// <summary>给人看的路径(带"哪个根"的前缀)。</summary>
        public string Path { get; set; }

        /// <summary>缺失 / 大小不对 / 内容不一致 / 包不完整。</summary>
        public string Reason { get; set; }
    }

    /// <summary>校验结果。</summary>
    public sealed class InstallManifestCheck
    {
        /// <summary>清单文件在不在。</summary>
        public bool ManifestExists { get; set; }

        /// <summary>
        /// 快照过期:清单记的版本跟当前装的对不上。
        ///
        /// 过期**不等于损坏** —— 用户升级过 DSH 或启动器之后,文件本来就该变。
        /// 这种情况只说"该刷新清单了",不报一堆"文件不一致"吓人。
        /// </summary>
        public bool SnapshotStale { get; set; }

        public string RecordedInstallerVersion { get; set; }

        public string RecordedDshVersion { get; set; }

        public string RecordedLauncherVersion { get; set; }

        public int CheckedFiles { get; set; }

        /// <summary>只记了大小、没记哈希的大文件数量。</summary>
        public int SizeOnlyFiles { get; set; }

        public List<ManifestProblem> Problems { get; private set; } =
            new List<ManifestProblem>();

        public bool Ok
        {
            get
            {
                return ManifestExists && !SnapshotStale && Problems.Count == 0;
            }
        }
    }

    /// <summary>
    /// 安装清单与完整性校验。
    ///
    /// 为什么是"快照"而不是"构建期常量":清单在**装完之后**由安装器扫描实际落地的文件生成,
    /// 每次安装 / 修复都会重写。文件被替换过(升级了 DSH、启动器自更新)时,
    /// 清单里的版本号会跟当前对不上 —— 这时判"快照过期",重新生成,而不是报"文件被改坏了"。
    /// 只有**版本一致**时才逐文件比大小和哈希,比出来不一致才是真的出问题。
    ///
    /// 清单放在组件目录里(那是安装器自己独占的目录,卸载时整棵删掉,清单跟着走)。
    ///
    /// 覆盖范围:安装器铺下去的程序文件。用户的东西(.dsh 配置、plugins、skills、会话)
    /// **一律不进清单** —— 那些本来就该被用户改,记进哈希只会天天误报。
    ///
    /// 大文件策略:超过 <see cref="HashLimitBytes"/> 只记大小不记哈希。
    /// 被中断的下载最常见的表现就是大小不对,所以这一层仍能抓住主要故障,
    /// 而清单体积和校验耗时都控制得住。
    /// </summary>
    public static class InstallManifest
    {
        public const string FileName = "install-manifest.json";

        public const int SchemaVersion = 1;

        /// <summary>超过这个大小就只记大小(8 MB)。</summary>
        public const long HashLimitBytes = 8L * 1024 * 1024;

        /// <summary>这几个名字的目录属于用户,不进清单。</summary>
        private static readonly string[] ExcludedDirectoryNames =
        {
            ".dsh",
            "plugins",
            "skills",
            "logs",
            "log",
            "cache",
            "temp",
            "tmp",
            ".git"
        };

        private const string RootComponents = "components";

        private const string RootDsh = "dsh";

        private const string RootLauncher = "launcher";

        public static string ResolvePath(InstallOptions options)
        {
            if (options == null || String.IsNullOrWhiteSpace(options.ComponentsRoot))
            {
                return null;
            }

            return Path.Combine(options.ComponentsRoot, FileName);
        }

        /// <summary>
        /// 扫描 + 写清单。装完(收尾检查通过)之后调用。
        /// 返回写了多少个条目,失败返回 -1(异常不往外抛:清单坏了不该让安装失败)。
        /// </summary>
        public static int Write(
            InstallOptions options,
            string installerVersion,
            Action<string> log)
        {
            if (options == null || String.IsNullOrWhiteSpace(options.ComponentsRoot))
            {
                return -1;
            }

            try
            {
                string manifestPath = ResolvePath(options);

                JsonArray files = new JsonArray();
                int sizeOnly = 0;

                // 组件目录:整个记(那是安装器独占的,里面全是程序文件)
                AddRoot(
                    files,
                    RootComponents,
                    options.ComponentsRoot,
                    manifestPath,
                    ref sizeOnly);

                if (options.InstallLauncher
                    && !String.IsNullOrWhiteSpace(options.LauncherRoot))
                {
                    AddRoot(
                        files,
                        RootLauncher,
                        options.LauncherRoot,
                        null,
                        ref sizeOnly);
                }

                if (options.InstallDsh && !String.IsNullOrWhiteSpace(options.DshRoot))
                {
                    AddDshRoot(files, options.DshRoot, ref sizeOnly);
                }

                JsonObject versions = new JsonObject
                {
                    ["installer"] = installerVersion ?? String.Empty,
                    ["dsh"] = ReadDshVersion(options.DshRoot),
                    ["launcher"] = ReadLauncherVersion(options)
                };

                JsonObject root = new JsonObject
                {
                    ["schemaVersion"] = SchemaVersion,
                    ["generatedAtUtc"] = DateTime.UtcNow.ToString("o"),
                    ["sourcePreference"] = ConfigStore.NormalizeSourcePreference(options.SourcePreference),
                    ["versions"] = versions,
                    ["hashLimitBytes"] = HashLimitBytes,
                    ["fileCount"] = files.Count,
                    ["sizeOnlyCount"] = sizeOnly,
                    ["files"] = files
                };

                File.WriteAllText(
                    manifestPath,
                    root.ToJsonString(new JsonSerializerOptions
                    {
                        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
                        WriteIndented = false
                    }),
                    new UTF8Encoding(false));

                if (log != null)
                {
                    log("安装清单已生成:" + manifestPath
                        + "（" + files.Count + " 项）");
                }

                return files.Count;
            }
            catch (Exception exception)
            {
                if (log != null)
                {
                    log("生成安装清单失败(不影响安装):" + exception.Message);
                }

                return -1;
            }
        }

        /// <summary>
        /// 校验。找不到清单 → ManifestExists=false(界面据此提示"修复安装"可以重建)。
        /// </summary>
        public static InstallManifestCheck Check(
            InstallOptions options,
            string currentInstallerVersion)
        {
            InstallManifestCheck check = new InstallManifestCheck();
            if (options == null || String.IsNullOrWhiteSpace(options.ComponentsRoot))
            {
                return check;
            }

            string manifestPath = ResolvePath(options);
            if (String.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                return check;
            }

            check.ManifestExists = true;

            JsonObject root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
            }
            catch
            {
                check.Problems.Add(new ManifestProblem
                {
                    Path = FileName,
                    Reason = "清单读不出来(文件损坏)"
                });
                return check;
            }

            if (root == null)
            {
                check.Problems.Add(new ManifestProblem
                {
                    Path = FileName,
                    Reason = "清单是空的"
                });
                return check;
            }

            JsonObject versions = root["versions"] as JsonObject;
            if (versions != null)
            {
                check.RecordedInstallerVersion = versions["installer"]?.GetValue<string>();
                check.RecordedDshVersion = versions["dsh"]?.GetValue<string>();
                check.RecordedLauncherVersion = versions["launcher"]?.GetValue<string>();
            }

            // 先看快照过没过期:版本对不上就是"该重新生成",不是"文件坏了"。
            if (!String.IsNullOrWhiteSpace(currentInstallerVersion)
                && !String.IsNullOrWhiteSpace(check.RecordedInstallerVersion)
                && !String.Equals(
                    currentInstallerVersion,
                    check.RecordedInstallerVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                check.SnapshotStale = true;
                return check;
            }

            string currentDsh = ReadDshVersion(options.DshRoot);
            if (!String.IsNullOrWhiteSpace(currentDsh)
                && !String.IsNullOrWhiteSpace(check.RecordedDshVersion)
                && !String.Equals(
                    currentDsh,
                    check.RecordedDshVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                check.SnapshotStale = true;
                return check;
            }

            string currentLauncher = ReadLauncherVersion(options);
            if (!String.IsNullOrWhiteSpace(currentLauncher)
                && !String.IsNullOrWhiteSpace(check.RecordedLauncherVersion)
                && !String.Equals(
                    currentLauncher,
                    check.RecordedLauncherVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                check.SnapshotStale = true;
                return check;
            }

            JsonArray files = root["files"] as JsonArray;
            if (files == null)
            {
                check.Problems.Add(new ManifestProblem
                {
                    Path = FileName,
                    Reason = "清单里没有文件列表"
                });
                return check;
            }

            for (int index = 0; index < files.Count; index++)
            {
                JsonObject entry = files[index] as JsonObject;
                if (entry == null)
                {
                    continue;
                }

                string absolute = ResolveEntryPath(options, entry);
                if (String.IsNullOrWhiteSpace(absolute))
                {
                    continue;
                }

                string label = DescribeEntry(entry, absolute);
                string kind = entry["kind"]?.GetValue<string>() ?? "file";
                long expectedSize = ReadLong(entry, "size");
                string expectedHash = entry["sha256"]?.GetValue<string>();

                if (String.Equals(kind, "package", StringComparison.OrdinalIgnoreCase))
                {
                    // 包:只看目录在不在 + 它的 package.json 对不对
                    string packageJson = entry["packageJson"]?.GetValue<string>();
                    if (!Directory.Exists(absolute))
                    {
                        AddProblem(check, label, "缺失");
                        continue;
                    }

                    if (!String.IsNullOrWhiteSpace(packageJson))
                    {
                        string file = Path.Combine(absolute, packageJson);
                        if (!File.Exists(file))
                        {
                            AddProblem(check, label, "包不完整(没有 " + packageJson + ")");
                        }
                    }

                    continue;
                }

                FileInfo info = new FileInfo(absolute);
                if (!info.Exists)
                {
                    AddProblem(check, label, "缺失");
                    continue;
                }

                check.CheckedFiles++;

                if (expectedSize > 0 && info.Length != expectedSize)
                {
                    AddProblem(
                        check,
                        label,
                        "大小不对(应为 " + expectedSize + "，实际 " + info.Length + ")");
                    continue;
                }

                if (String.IsNullOrWhiteSpace(expectedHash))
                {
                    check.SizeOnlyFiles++;
                    continue;
                }

                string actual = TryHash(absolute);
                if (actual == null)
                {
                    continue;
                }

                if (!String.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    AddProblem(check, label, "内容不一致");
                }
            }

            return check;
        }

        /// <summary>判断本机有没有安装痕迹(界面拿它决定「修复安装」能不能点)。</summary>
        public static bool HasInstallTrace(InstallOptions options)
        {
            if (options == null)
            {
                return false;
            }

            if (!String.IsNullOrWhiteSpace(options.ComponentsRoot)
                && Directory.Exists(options.ComponentsRoot))
            {
                try
                {
                    if (Directory.GetFileSystemEntries(options.ComponentsRoot).Length > 0)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            if (!String.IsNullOrWhiteSpace(options.DshRoot)
                && File.Exists(Path.Combine(options.DshRoot, WellKnown.DshMarker)))
            {
                return true;
            }

            if (!String.IsNullOrWhiteSpace(options.LauncherRoot)
                && File.Exists(Path.Combine(options.LauncherRoot, WellKnown.LauncherExe)))
            {
                return true;
            }

            return false;
        }

        private static void AddProblem(
            InstallManifestCheck check,
            string path,
            string reason)
        {
            // 问题太多的清单没意义:前 50 条足够说明"这片坏了"
            if (check.Problems.Count >= 50)
            {
                return;
            }

            check.Problems.Add(new ManifestProblem
            {
                Path = path,
                Reason = reason
            });
        }

        private static void AddRoot(
            JsonArray files,
            string rootLabel,
            string rootPath,
            string skipPath,
            ref int sizeOnly)
        {
            if (String.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return;
            }

            List<string> found = new List<string>();
            CollectFiles(rootPath, found);

            found.Sort(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < found.Count; index++)
            {
                string absolute = found[index];
                if (skipPath != null
                    && String.Equals(
                        absolute,
                        skipPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                JsonObject entry = BuildFileEntry(rootLabel, rootPath, absolute, ref sizeOnly);
                if (entry != null)
                {
                    files.Add(entry);
                }
            }
        }

        /// <summary>
        /// DSH 根目录:自己那几个文件逐个记;node_modules 里**按包**记。
        ///
        /// 为什么不逐文件:DSH 的 node_modules 有几万个文件,逐文件记会让清单膨胀到几 MB,
        /// 每次校验都要重新读一遍。按包记(目录在不在 + 它自己的 package.json 对不对)
        /// 足够抓住"包装了一半 / 整个没了"这类真实故障。
        /// </summary>
        private static void AddDshRoot(JsonArray files, string dshRoot, ref int sizeOnly)
        {
            if (String.IsNullOrWhiteSpace(dshRoot) || !Directory.Exists(dshRoot))
            {
                return;
            }

            List<string> topFiles = new List<string>();
            try
            {
                foreach (string file in Directory.GetFiles(dshRoot))
                {
                    topFiles.Add(file);
                }
            }
            catch
            {
            }

            topFiles.Sort(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < topFiles.Count; index++)
            {
                JsonObject entry = BuildFileEntry(
                    RootDsh,
                    dshRoot,
                    topFiles[index],
                    ref sizeOnly);
                if (entry != null)
                {
                    files.Add(entry);
                }
            }

            string nodeModules = Path.Combine(dshRoot, "node_modules");
            if (!Directory.Exists(nodeModules))
            {
                return;
            }

            List<string> packages = new List<string>();
            try
            {
                foreach (string directory in Directory.GetDirectories(nodeModules))
                {
                    string name = Path.GetFileName(directory);
                    if (name.StartsWith("@", StringComparison.Ordinal))
                    {
                        try
                        {
                            foreach (string scoped in Directory.GetDirectories(directory))
                            {
                                packages.Add(scoped);
                            }
                        }
                        catch
                        {
                        }
                    }
                    else if (!String.Equals(name, ".bin", StringComparison.OrdinalIgnoreCase)
                        && !String.Equals(name, ".pnpm", StringComparison.OrdinalIgnoreCase))
                    {
                        packages.Add(directory);
                    }
                }
            }
            catch
            {
            }

            packages.Sort(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < packages.Count; index++)
            {
                string relative = Path.GetRelativePath(dshRoot, packages[index])
                    .Replace('\\', '/');
                JsonObject entry = new JsonObject
                {
                    ["root"] = RootDsh,
                    ["path"] = relative,
                    ["kind"] = "package",
                    ["packageJson"] = "package.json"
                };
                files.Add(entry);
            }
        }

        private static void CollectFiles(string root, List<string> found)
        {
            try
            {
                foreach (string directory in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(directory);
                    if (IsExcluded(name))
                    {
                        continue;
                    }

                    CollectFiles(directory, found);
                }

                foreach (string file in Directory.GetFiles(root))
                {
                    found.Add(file);
                }
            }
            catch
            {
            }
        }

        private static bool IsExcluded(string name)
        {
            for (int index = 0; index < ExcludedDirectoryNames.Length; index++)
            {
                if (String.Equals(
                    ExcludedDirectoryNames[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static JsonObject BuildFileEntry(
            string rootLabel,
            string rootPath,
            string absolute,
            ref int sizeOnly)
        {
            try
            {
                FileInfo info = new FileInfo(absolute);
                if (!info.Exists)
                {
                    return null;
                }

                JsonObject entry = new JsonObject
                {
                    ["root"] = rootLabel,
                    ["path"] = Path.GetRelativePath(rootPath, absolute).Replace('\\', '/'),
                    ["kind"] = "file",
                    ["size"] = info.Length
                };

                if (info.Length <= HashLimitBytes)
                {
                    string hash = TryHash(absolute);
                    if (hash != null)
                    {
                        entry["sha256"] = hash;
                    }
                }
                else
                {
                    sizeOnly++;
                }

                return entry;
            }
            catch
            {
                return null;
            }
        }

        private static string TryHash(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    StringBuilder builder = new StringBuilder(hash.Length * 2);
                    for (int index = 0; index < hash.Length; index++)
                    {
                        builder.Append(hash[index].ToString("x2"));
                    }

                    return builder.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveEntryPath(InstallOptions options, JsonObject entry)
        {
            string rootLabel = entry["root"]?.GetValue<string>() ?? RootComponents;
            string relative = entry["path"]?.GetValue<string>();
            if (String.IsNullOrWhiteSpace(relative))
            {
                return null;
            }

            string root;
            if (String.Equals(rootLabel, RootDsh, StringComparison.OrdinalIgnoreCase))
            {
                root = options.DshRoot;
            }
            else if (String.Equals(rootLabel, RootLauncher, StringComparison.OrdinalIgnoreCase))
            {
                root = options.LauncherRoot;
            }
            else
            {
                root = options.ComponentsRoot;
            }

            if (String.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string DescribeEntry(JsonObject entry, string absolute)
        {
            string rootLabel = entry["root"]?.GetValue<string>() ?? RootComponents;
            string relative = entry["path"]?.GetValue<string>() ?? absolute;
            return rootLabel + ":" + relative;
        }

        private static long ReadLong(JsonObject entry, string name)
        {
            try
            {
                JsonNode node = entry[name];
                return node == null ? 0 : node.GetValue<long>();
            }
            catch
            {
                return 0;
            }
        }

        private static string ReadDshVersion(string dshRoot)
        {
            if (String.IsNullOrWhiteSpace(dshRoot))
            {
                return String.Empty;
            }

            try
            {
                string packageFile = Path.Combine(
                    dshRoot,
                    "node_modules",
                    "@deepseek-ai",
                    "dsh",
                    "package.json");
                if (!File.Exists(packageFile))
                {
                    return String.Empty;
                }

                JsonObject package = JsonNode.Parse(
                    File.ReadAllText(packageFile)) as JsonObject;
                return package?["version"]?.GetValue<string>() ?? String.Empty;
            }
            catch
            {
                return String.Empty;
            }
        }

        private static string ReadLauncherVersion(InstallOptions options)
        {
            if (options == null || String.IsNullOrWhiteSpace(options.LauncherRoot))
            {
                return String.Empty;
            }

            try
            {
                string exe = Path.Combine(options.LauncherRoot, WellKnown.LauncherExe);
                if (!File.Exists(exe))
                {
                    return String.Empty;
                }

                return FileVersionInfo.GetVersionInfo(exe).FileVersion ?? String.Empty;
            }
            catch
            {
                return String.Empty;
            }
        }
    }
}
