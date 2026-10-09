using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace DshInstaller.Shared.Backup
{
    /// <summary>一组可以单独导出/导入的用户数据。</summary>
    public sealed class BackupGroup
    {
        /// <summary>稳定 id(存档里也用它判断这一项属于谁)。</summary>
        public string Id { get; set; }

        public string NameZh { get; set; }

        public string NameEn { get; set; }

        /// <summary>来源绝对路径(导出时看这个)。</summary>
        public string SourcePath { get; set; }

        /// <summary>压缩包里的相对前缀(导入时按它认组)。</summary>
        public string ArchivePrefix { get; set; }

        public string[] ArchivePrefixes { get; set; }

        /// <summary>默认勾不勾。</summary>
        public bool SelectedByDefault { get; set; }

        /// <summary>来源存在吗(不存在就别列出来了)。</summary>
        public bool Exists { get; set; }

        public string Display(bool chinese)
        {
            return chinese ? NameZh : NameEn;
        }
    }

    /// <summary>导入时碰到同名文件怎么办。</summary>
    public enum ConflictPolicy
    {
        /// <summary>直接覆盖(默认)。</summary>
        Overwrite,

        /// <summary>已有的不动,只补缺的。</summary>
        Skip,

        /// <summary>问调用方(界面上弹选择)。</summary>
        Ask
    }

    /// <summary>问到之后用户选的。</summary>
    public enum ConflictChoice
    {
        Overwrite,

        Skip,

        /// <summary>两个都留:新文件改名成 xxx.imported。</summary>
        KeepBoth
    }

    /// <summary>
    /// 用户数据的导出与导入。
    ///
    /// 只做"哪些目录 / 怎么搬 / 撞车怎么办",不碰界面 —— 三个入口
    /// (安装器装完的导入页、卸载器的导出页、启动器的导入导出)共用这一份。
    ///
    /// 分组是刻意的:用户要能只带"配置 + 技能 + 插件"而不背上一堆会话历史,
    /// 所以每组单独勾、单独报进度。
    /// </summary>
    public static class UserDataBackup
    {
        public const string GroupProfiles = "profiles";

        public const string GroupSkills = "skills";

        public const string GroupPlugins = "plugins";

        public const string GroupSessions = "sessions";

        /// <summary>组定义的唯一出处:哪些目录算用户数据、包内前缀是什么、默认勾不勾。</summary>
        private sealed class GroupTemplate
        {
            public string Id;
            public string NameZh;
            public string NameEn;
            public string RelativePath;
            public string ArchivePrefix;
            public string[] AdditionalPrefixes;
            public bool ByDefault;
        }

        private static readonly GroupTemplate[] Templates =
        {
            new GroupTemplate
            {
                Id = GroupProfiles,
                NameZh = "配置与插件清单",
                NameEn = "Settings and plug-in list",
                RelativePath = @".dsh\profiles",
                ArchivePrefix = ".dsh/profiles",
                AdditionalPrefixes = new[] { "profiles" },
                ByDefault = true,
            },
            new GroupTemplate
            {
                Id = GroupSkills,
                NameZh = "技能",
                NameEn = "Skills",
                RelativePath = "skills",
                ArchivePrefix = "skills",
                AdditionalPrefixes = new[] { ".dsh/skills" },
                ByDefault = true,
            },
            new GroupTemplate
            {
                Id = GroupPlugins,
                NameZh = "插件文件",
                NameEn = "Plug-in files",
                RelativePath = "plugins",
                ArchivePrefix = "plugins",
                AdditionalPrefixes = new[] { ".dsh/plugins", ".dsh/plugin-profiles" },
                ByDefault = true,
            },
            new GroupTemplate
            {
                Id = GroupSessions,
                NameZh = "会话与缓存",
                NameEn = "Sessions and cache",
                RelativePath = @".dsh\storages",
                ArchivePrefix = ".dsh/storages",
                AdditionalPrefixes = new[] { ".dsh/sessions", "storages", "sessions" },
                ByDefault = false,
            },
        };

        /// <summary>列出这台机器上有哪些可备份的数据(不存在的组不会出现)。</summary>
        public static List<BackupGroup> Describe(string dshRoot, string dshHome = null)
        {
            List<BackupGroup> groups = new List<BackupGroup>();
            if (String.IsNullOrWhiteSpace(dshRoot))
            {
                return groups;
            }

            for (int index = 0; index < Templates.Length; index++)
            {
                GroupTemplate template = Templates[index];
                string path = Prefixes(template).Select(prefix => ResolveDataPath(dshRoot, dshHome, prefix))
                    .FirstOrDefault(Directory.Exists);

                bool exists = false;
                try
                {
                    exists = Directory.Exists(path);
                }
                catch
                {
                }

                if (!exists && template.Id == GroupPlugins)
                {
                    string profiles = ResolveDataPath(dshRoot, dshHome, ".dsh/profiles");
                    exists = Directory.Exists(profiles) && Directory.GetDirectories(profiles)
                        .Any(profile => File.Exists(Path.Combine(profile, "package.json")));
                }
                if (!exists)
                {
                    continue;
                }

                groups.Add(ToGroup(template, path));
            }

            return groups;
        }

        /// <summary>
        /// 列出**备份包里**有哪些组(装完导入时用这个)。
        ///
        /// 为什么不能拿 Describe:全新机器上 .dsh/skills/plugins 还不存在,
        /// 按磁盘状态列会一个都列不出来 —— 而用户手里正好拿着一个装满东西的备份包。
        /// </summary>
        public static List<BackupGroup> DescribeFromArchive(
            string archivePath,
            Action<string> log)
        {
            List<BackupGroup> groups = new List<BackupGroup>();

            string error;
            List<DymEntry> entries = DymArchive.List(archivePath, log, out error);
            if (entries.Count == 0)
            {
                return groups;
            }

            for (int index = 0; index < Templates.Length; index++)
            {
                GroupTemplate template = Templates[index];
                bool found = false;

                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    string path = entries[entryIndex].Path;
                    if (String.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    if (Prefixes(template).Any(prefix => String.Equals(path, prefix, StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                        || template.Id == GroupPlugins && IsDymPluginProfilePath(path))
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    groups.Add(ToGroup(template, null));
                }
            }

            return groups;
        }

        private static BackupGroup ToGroup(GroupTemplate template, string sourcePath)
        {
            return new BackupGroup
            {
                Id = template.Id,
                NameZh = template.NameZh,
                NameEn = template.NameEn,
                SourcePath = sourcePath,
                ArchivePrefix = template.ArchivePrefix,
                ArchivePrefixes = Prefixes(template).ToArray(),
                SelectedByDefault = template.ByDefault,
                Exists = true
            };
        }

        /// <summary>
        /// 把选中的组打包成 .dym。
        ///
        /// 包内路径用**相对路径**(以 DSH 根目录为基准),这样换台机器、换个盘符都能还原 ——
        /// 存绝对路径的话,换个用户名就全废了(实测这种坑在别家工具上很常见)。
        /// </summary>
        public static bool Export(
            string dshRoot,
            IList<BackupGroup> chosen,
            string archivePath,
            Action<string, double> report,
            Action<string> log,
            CancellationToken token,
            string dshHome = null)
        {
            if (String.IsNullOrWhiteSpace(dshRoot)
                || chosen == null
                || chosen.Count == 0
                || String.IsNullOrWhiteSpace(archivePath))
            {
                return false;
            }

            var exportItems = new List<ExportItem>();
            HashSet<string> seenSourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenSourceDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            for (int index = 0; index < chosen.Count; index++)
            {
                BackupGroup group = chosen[index];
                if (group == null || !group.Exists)
                {
                    continue;
                }

                foreach (string prefix in group.ArchivePrefixes ?? new[] { group.ArchivePrefix })
                {
                    if (prefix == ".dsh/plugin-profiles") continue;
                    string path = ResolveDataPath(dshRoot, dshHome, prefix);
                    if (!Directory.Exists(path) || !seenSourceDirectories.Add(Path.GetFullPath(path))) continue;
                    if (log != null) log("正在扫描 " + path);
                    if (report != null) report("正在扫描 " + prefix, 0);
                    string archivePrefix = prefix;
                    if (String.IsNullOrWhiteSpace(dshHome) && Directory.Exists(Path.Combine(dshRoot, "profiles")))
                        archivePrefix = prefix.StartsWith(".dsh/", StringComparison.Ordinal) ? prefix.Substring(5) : prefix;
                    CollectExportFiles(path, archivePrefix, exportItems, seenSourceFiles, ref totalBytes, log, token);
                    if (report != null) report("已扫描 " + exportItems.Count + " 个文件 · " + FormatSize(totalBytes), 0);
                }
            }
            if (chosen.Any(group => group?.Id == GroupPlugins))
                CollectProfilePlugins(dshRoot, dshHome, exportItems, seenSourceFiles, ref totalBytes, log, token);

            if (exportItems.Count == 0)
            {
                if (log != null) log("所选备份组里没有可归档文件(空目录、node_modules 和链接不会打包)。");
                return false;
            }

            if (log != null)
            {
                log("开始打包：" + exportItems.Count + " 个文件，" + FormatSize(totalBytes));
            }
            string staging = Path.Combine(DymArchive.GetWritableCacheRoot(), "export-" + Guid.NewGuid().ToString("N"));
            try
            {
                var sourceFiles = new List<string>();
                long stagedBytes = 0;
                var progressClock = System.Diagnostics.Stopwatch.StartNew();
                long lastProgress = -100;
                for (int index = 0; index < exportItems.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    ExportItem item = exportItems[index];
                    string target = Path.Combine(staging, item.Relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = item.Content == null
                        ? (Stream)new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read)
                        : new MemoryStream(System.Text.Encoding.UTF8.GetBytes(item.Content)))
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                            stagedBytes += read;
                            if (progressClock.ElapsedMilliseconds - lastProgress >= 100)
                            {
                                lastProgress = progressClock.ElapsedMilliseconds;
                                report?.Invoke("正在准备 (" + (index + 1) + "/" + exportItems.Count + ") · "
                                    + FormatSize(stagedBytes) + "/" + FormatSize(totalBytes) + " · " + item.Relative,
                                    totalBytes == 0 ? 0 : stagedBytes * 30d / totalBytes);
                            }
                        }
                    }
                    sourceFiles.Add(item.Relative);
                }
                if (report != null) report("准备压缩 " + sourceFiles.Count + " 个文件 · " + FormatSize(totalBytes), 30);

            int packed = 0;
            double currentPercent = 30;
            bool ok = DymArchive.Create(
                archivePath,
                sourceFiles,
                staging,
                delegate(string text, double percent)
                {
                    if (report != null)
                    {
                        if (!Double.IsNaN(percent))
                        {
                            currentPercent = 30 + percent * 0.7;
                            // 7z 的百分比是整个压缩阶段的总体进度。
                            report("正在压缩 " + sourceFiles.Count + " 个文件 · " + FormatSize(totalBytes)
                                + " · " + Math.Round(percent) + "%", currentPercent);
                        }
                        else if (!String.IsNullOrWhiteSpace(text))
                        {
                            string file = DescribePackingLine(text);
                            if (file != null) packed++;
                            else if (!text.StartsWith("Add new data", StringComparison.OrdinalIgnoreCase)) return;
                            report("正在压缩 (" + Math.Min(packed, sourceFiles.Count) + "/" + sourceFiles.Count + ") "
                                + (file ?? text) + " · " + FormatSize(totalBytes), currentPercent);
                        }
                    }
                    if (!String.IsNullOrWhiteSpace(text) && log != null) log(text);
                },
                log,
                token,
                false);

            if (ok && report != null)
            {
                report("打包完成", 100);
            }

            return ok;
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch (Exception exception) { log?.Invoke("无法清理临时备份目录：" + exception.Message); }
            }
        }

        private static void CollectProfilePlugins(string root, string home, List<ExportItem> items,
            HashSet<string> seen, ref long bytes, Action<string> log, CancellationToken token)
        {
            string actualHome = String.IsNullOrWhiteSpace(home)
                ? Directory.Exists(Path.Combine(root, "profiles")) ? root : Path.Combine(root, ".dsh") : home;
            string profiles = Path.Combine(actualHome, "profiles");
            if (!Directory.Exists(profiles)) return;
            foreach (string profile in Directory.GetDirectories(profiles))
            {
                token.ThrowIfCancellationRequested();
                string manifestFile = Path.Combine(profile, "package.json");
                if (!File.Exists(manifestFile)) continue;
                JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestFile)) as JsonObject
                    ?? throw new IOException("插件清单不是 JSON 对象：" + manifestFile);
                if (manifest["dependencies"] is not JsonObject dependencies) continue;
                string pluginProfilePrefix = ".dsh/plugin-profiles/" + Path.GetFileName(profile);
                var unavailable = new HashSet<string>(StringComparer.Ordinal);
                foreach (var dependency in dependencies.ToList())
                {
                    token.ThrowIfCancellationRequested();
                    string name = dependency.Key;
                    if (name.StartsWith("@deepseek-ai/dsh-", StringComparison.Ordinal)) continue;
                    if (name.Contains("..") || name.Contains('\\') || name.Contains(':') || name.StartsWith('/'))
                        throw new IOException("插件名称无效：" + name);
                    int itemStart = items.Count;
                    long bytesBefore = bytes;
                    try
                    {
                        string module = Path.Combine(profile, "node_modules", name.Replace('/', Path.DirectorySeparatorChar));
                        if (!Directory.Exists(module)) module = Path.Combine(profiles, "node_modules", name.Replace('/', Path.DirectorySeparatorChar));
                        if (!Directory.Exists(module))
                        {
                            string spec = dependency.Value?.GetValue<string>() ?? String.Empty;
                            if (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || spec.StartsWith("link:", StringComparison.OrdinalIgnoreCase))
                                throw new IOException("本地插件文件不存在。");
                            continue;
                        }
                        MaterializePlugin(module, pluginProfilePrefix + "/node_modules/" + name, actualHome, root, items, seen,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase), ref bytes, token);
                        string installedSpec = dependency.Value?.GetValue<string>() ?? String.Empty;
                        if (installedSpec.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                            || installedSpec.StartsWith("link:", StringComparison.OrdinalIgnoreCase))
                            dependencies[name] = "file:./node_modules/" + name;
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        for (int rollback = items.Count - 1; rollback >= itemStart; rollback--) seen.Remove(items[rollback].Relative);
                        items.RemoveRange(itemStart, items.Count - itemStart);
                        bytes = bytesBefore;
                        unavailable.Add(name);
                        if (dependency.Value?.GetValue<string>() is string spec
                            && (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || spec.StartsWith("link:", StringComparison.OrdinalIgnoreCase)))
                            dependencies.Remove(name);
                        log?.Invoke("跳过无法迁移的插件 " + name + "：" + exception.Message);
                    }
                }
                if (manifest["dsh"]?["profile"]?["bundles"] is JsonArray bundles)
                    for (int index = bundles.Count - 1; index >= 0; index--)
                        if (unavailable.Contains(bundles[index]?.GetValue<string>() ?? String.Empty)) bundles.RemoveAt(index);
                string manifestRelative = pluginProfilePrefix + "/package.json";
                string manifestText = manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                long manifestBytes = System.Text.Encoding.UTF8.GetByteCount(manifestText);
                foreach (ExportItem item in items.Where(item => String.Equals(item.Source, manifestFile, StringComparison.OrdinalIgnoreCase)))
                {
                    bytes += manifestBytes - new FileInfo(manifestFile).Length;
                    item.Content = manifestText;
                }
                if (seen.Add(manifestRelative))
                {
                    items.Add(new ExportItem { Relative = manifestRelative, Content = manifestText });
                    bytes += manifestBytes;
                }
            }
        }

        private static void MaterializePlugin(string path, string relative, string home, string root,
            List<ExportItem> items, HashSet<string> seen, HashSet<string> ancestors, ref long bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FileSystemInfo info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                path = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("插件链接无法解析：" + path);
            string full = Path.GetFullPath(path);
            bool Inside(string candidate) => full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate))
                + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!Inside(home) && !Inside(Path.Combine(root, "plugins")))
                throw new IOException("插件链接指向所选数据目录之外，无法完整备份：" + path);
            if (Directory.Exists(full))
            {
                if (!ancestors.Add(full)) throw new IOException("插件目录包含循环链接：" + relative);
                foreach (string child in Directory.EnumerateFileSystemEntries(full))
                    MaterializePlugin(child, relative + "/" + Path.GetFileName(child), home, root, items, seen, ancestors, ref bytes, token);
                ancestors.Remove(full);
            }
            else if (seen.Add(relative))
            {
                items.Add(new ExportItem { Source = full, Relative = relative });
                bytes += new FileInfo(full).Length;
            }
        }

        private sealed class ExportItem
        {
            internal string Source, Relative;
            internal string Content;
        }

        private static void CollectExportFiles(string root, string archiveRoot, List<ExportItem> files, HashSet<string> seen,
            ref long bytes,
            Action<string> log, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FileAttributes rootAttributes = File.GetAttributes(root);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                log?.Invoke("跳过链接目录：" + root);
                return;
            }
            foreach (string directory in Directory.GetDirectories(root))
            {
                token.ThrowIfCancellationRequested();
                if (archiveRoot.StartsWith(".dsh/profiles", StringComparison.OrdinalIgnoreCase)
                    || archiveRoot.StartsWith("profiles", StringComparison.OrdinalIgnoreCase))
                {
                    if (String.Equals(Path.GetFileName(directory), "node_modules", StringComparison.OrdinalIgnoreCase))
                    {
                        log?.Invoke("跳过可重新安装的 profile node_modules：" + directory);
                        continue;
                    }
                }
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    log?.Invoke("跳过符号链接 / junction：" + directory);
                    continue;
                }
                CollectExportFiles(directory, archiveRoot + "/" + Path.GetFileName(directory), files, seen, ref bytes, log, token);
            }
            foreach (string file in Directory.GetFiles(root))
            {
                token.ThrowIfCancellationRequested();
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    log?.Invoke("跳过文件链接：" + file);
                    continue;
                }
                var info = new FileInfo(file);
                string relative = archiveRoot + "/" + Path.GetFileName(file);
                if (seen.Add(relative))
                {
                    files.Add(new ExportItem { Source = file, Relative = relative });
                    bytes += info.Length;
                }
            }
        }

        private static string DescribePackingLine(string message)
        {
            if (String.IsNullOrWhiteSpace(message) || !message.TrimStart().StartsWith("Compressing", StringComparison.OrdinalIgnoreCase)) return null;
            string path = message.Trim().Substring("Compressing".Length).Trim();
            try { return Path.GetFileName(path); } catch { return path; }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / 1073741824d).ToString("0.00") + " GB";
            if (bytes >= 1024L * 1024L) return (bytes / 1048576d).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0") + " KB";
            return bytes + " B";
        }

        /// <summary>
        /// 从 .dym 还原回 DSH 根目录。
        ///
        /// 流程刻意分两步:**先解到临时目录,再逐个文件搬**。
        /// 直接解到目标目录做不到"跳过已存在的"和"每个文件报一次进度",
        /// 而这两件事正是用户要的(「正在恢复 XXX 3/N」)。
        /// </summary>
        public static bool Import(
            string archivePath,
            string dshRoot,
            IList<BackupGroup> chosen,
            ConflictPolicy policy,
            Func<string, ConflictChoice> ask,
            Action<string, double> report,
            Action<string> log,
            CancellationToken token,
            out string error,
            string dshHome = null)
        {
            error = null;

            if (String.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                error = "备份包不存在:" + archivePath;
                return false;
            }

            if (String.IsNullOrWhiteSpace(dshRoot))
            {
                error = "没有 DSH 目录,无法还原。";
                return false;
            }

            string staging;
            try
            {
                staging = Path.Combine(DymArchive.GetWritableCacheRoot(), "restore-" + Guid.NewGuid().ToString("N"));
            }
            catch (Exception exception)
            {
                error = "无法准备备份恢复目录：" + exception.Message;
                return false;
            }
            try
            {
                EnsureNoReparsePoints(dshRoot);
                if (log != null)
                {
                    log("正在解包到临时目录:" + staging);
                }

                string extractionError = null;
                bool extracted = DymArchive.Extract(
                    archivePath,
                    staging,
                    delegate(string text, double percent)
                    {
                        if (report != null)
                        {
                            if (!Double.IsNaN(percent)) report("正在解包备份 · " + Math.Round(percent) + "%", percent * 0.3);
                        }
                    },
                    delegate(string message)
                    {
                        if (!String.IsNullOrWhiteSpace(message)
                            && (message.Contains("失败") || message.Contains("拒绝") || message.Contains("不安全")))
                            extractionError = message;
                        log?.Invoke(message);
                    },
                    token);

                if (!extracted)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                    error = extractionError ?? "备份包解不开(文件可能损坏,或者根本不是 .dym)。";
                    return false;
                }

                return MergeStaging(staging, dshRoot, chosen, policy, ask, report, log, token, out error, dshHome);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(staging))
                    {
                        Directory.Delete(staging, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static bool MergeStaging(
            string staging,
            string dshRoot,
            IList<BackupGroup> chosen,
            ConflictPolicy policy,
            Func<string, ConflictChoice> ask,
            Action<string, double> report,
            Action<string> log,
            CancellationToken token,
            out string error,
            string dshHome = null)
        {
            error = null;

            // 只搬用户选了的组
            List<BackupGroup> groups = new List<BackupGroup>();
            if (chosen != null)
            {
                for (int index = 0; index < chosen.Count; index++)
                {
                    if (chosen[index] != null)
                    {
                        groups.Add(chosen[index]);
                    }
                }
            }

            if (groups.Count == 0)
            {
                // 没说就全搬
                groups = DescribeFromArchiveDefinitions();
            }

            // 先把每个组要搬的文件列出来(为了能报 "第 3 / 共 12 个")
            List<RestoreItem> items = new List<RestoreItem>();
            for (int index = 0; index < groups.Count; index++)
            {
                BackupGroup group = groups[index];
                foreach (string prefix in RestorePrefixes(group, staging))
                {
                string sourceRoot = Path.Combine(staging, prefix.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(sourceRoot))
                {
                    continue;
                }

                List<string> files = new List<string>();
                CollectFiles(sourceRoot, files);
                files.Sort(StringComparer.OrdinalIgnoreCase);

                for (int fileIndex = 0; fileIndex < files.Count; fileIndex++)
                {
                    items.Add(new RestoreItem
                    {
                        Group = group,
                        Prefix = prefix,
                        SourceFile = files[fileIndex],
                        SourceRoot = sourceRoot,
                        Relative = Path.GetRelativePath(sourceRoot, files[fileIndex])
                    });
                }
                }
            }

            if (items.Count == 0)
            {
                error = "备份包里没有可还原的内容(可能这份备份是空的)。";
                return false;
            }

            int overwritten = 0;
            int skipped = 0;
            int keptBoth = 0;

            for (int index = 0; index < items.Count; index++)
            {
                if (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }

                RestoreItem item = items[index];
                string targetDirectory = String.IsNullOrWhiteSpace(dshHome)
                    ? Path.Combine(dshRoot, MapTargetPrefix(dshRoot, item.Prefix).Replace('/', Path.DirectorySeparatorChar))
                    : ResolveDataPath(dshRoot, dshHome, item.Prefix);
                string target = Path.Combine(targetDirectory, item.Relative);

                if (report != null)
                {
                    report(
                        "正在恢复 " + item.Group.NameZh
                            + " (" + (index + 1) + "/" + items.Count + ") · "
                            + item.Relative.Replace('\\', '/'),
                        30 + (index + 1) * 70.0 / items.Count);
                }

                token.ThrowIfCancellationRequested();
                bool exists = File.Exists(target);
                ConflictChoice choice = ConflictChoice.Overwrite;

                if (exists)
                {
                    if (policy == ConflictPolicy.Skip)
                    {
                        choice = ConflictChoice.Skip;
                    }
                    else if (policy == ConflictPolicy.Ask && ask != null)
                    {
                        choice = ask(item.Relative);
                    }
                }

                try
                {
                    EnsureNoReparsePoints(target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));

                    if (!exists || choice == ConflictChoice.Overwrite)
                    {
                        File.Copy(item.SourceFile, target, true);
                        if (exists)
                        {
                            overwritten++;
                        }
                    }
                    else if (choice == ConflictChoice.KeepBoth)
                    {
                        string alternative = target + ".imported";
                        int suffix = 1;
                        while (File.Exists(alternative))
                        {
                            alternative = target + ".imported" + suffix;
                            suffix++;
                        }

                        File.Copy(item.SourceFile, alternative, false);
                        keptBoth++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception exception)
                {
                    error = "还原 " + item.Relative + " 失败:" + exception.Message;
                    return false;
                }
            }

            if (log != null)
            {
                log("还原完成:覆盖 " + overwritten
                    + " 个、跳过 " + skipped
                    + " 个、另存 " + keptBoth + " 个");
            }

            if (report != null)
            {
                report("恢复完成", 100);
            }

            return true;
        }

        private static void CollectFiles(string root, List<string> found)
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("备份包中的目录链接不能还原：" + root);
            foreach (string directory in Directory.GetDirectories(root))
            {
                CollectFiles(directory, found);
            }
            foreach (string file in Directory.GetFiles(root))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("备份包中的文件链接不能还原：" + file);
                found.Add(file);
            }
        }

        private static List<BackupGroup> DescribeFromArchiveDefinitions()
            => Templates.Select(template => ToGroup(template, null)).ToList();

        private static bool IsProfileModulePath(string path) => (path.StartsWith(".dsh/profiles/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase)) && path.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase);

        private static bool IsProfileManifestPath(string path) => (path.StartsWith(".dsh/profiles/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase))
            && path.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<string> RestorePrefixes(BackupGroup group, string staging)
        {
            foreach (string prefix in group.ArchivePrefixes ?? new[] { group.ArchivePrefix })
                if (prefix != ".dsh/plugin-profiles") yield return prefix;
            if (group.Id != GroupPlugins) yield break;
            string root = ".dsh/plugin-profiles";
            string profiles = Path.Combine(staging, root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(profiles)) yield break;
            foreach (string profile in Directory.GetDirectories(profiles))
            {
                yield return root + "/" + Path.GetFileName(profile);
            }
        }

        private static bool IsDymPluginProfilePath(string path)
            => path.StartsWith(".dsh/plugin-profiles/", StringComparison.OrdinalIgnoreCase);

        private static string MapTargetPrefix(string root, string prefix)
        {
            string normalized = prefix.Replace('\\', '/');
            if (normalized.StartsWith(".dsh/plugin-profiles/", StringComparison.OrdinalIgnoreCase))
            {
                string profile = normalized.Substring(".dsh/plugin-profiles/".Length).Split('/')[0];
                bool officialHome = Directory.Exists(Path.Combine(root, "profiles"))
                    && !Directory.Exists(Path.Combine(root, ".dsh", "profiles"));
                return (officialHome ? "profiles/" : ".dsh/profiles/") + profile;
            }
            string portableHome = Path.Combine(root, ".dsh");
            bool targetLooksLikeHome = Directory.Exists(Path.Combine(root, "profiles"))
                && !Directory.Exists(Path.Combine(portableHome, "profiles"));
            if (targetLooksLikeHome && normalized.StartsWith(".dsh/", StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(5);
            if (!targetLooksLikeHome && normalized == "profiles"
                && (Directory.Exists(portableHome) || !Directory.Exists(Path.Combine(root, "profiles")))) return ".dsh/profiles";
            if (!targetLooksLikeHome && (normalized == "storages" || normalized == "sessions")
                && (Directory.Exists(portableHome) || !Directory.Exists(Path.Combine(root, "profiles")))) return ".dsh/" + normalized;
            return normalized;
        }

        private static string ResolveDataPath(string root, string home, string prefix)
        {
            if (prefix.StartsWith(".dsh/plugin-profiles/", StringComparison.OrdinalIgnoreCase))
            {
                if (!String.IsNullOrWhiteSpace(home))
                    return Path.Combine(home, "profiles", prefix.Substring(".dsh/plugin-profiles/".Length).Replace('/', Path.DirectorySeparatorChar));
                return Path.Combine(root, MapTargetPrefix(root, prefix).Replace('/', Path.DirectorySeparatorChar));
            }
            if (!String.IsNullOrWhiteSpace(home))
            {
                if (prefix.StartsWith(".dsh/", StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(home, prefix.Substring(5).Replace('/', Path.DirectorySeparatorChar));
                if (prefix == "profiles" || prefix == "storages" || prefix == "sessions")
                    return Path.Combine(home, prefix);
                return Path.Combine(root, prefix.Replace('/', Path.DirectorySeparatorChar));
            }
            string path = Path.Combine(root, prefix.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path) && prefix.StartsWith(".dsh/", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(Path.Combine(root, "profiles")))
                path = Path.Combine(root, prefix.Substring(5).Replace('/', Path.DirectorySeparatorChar));
            return path;
        }

        private static void EnsureNoReparsePoints(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("目标目录包含符号链接或 junction，已停止以免覆盖到其他位置：" + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
            }
        }

        private sealed class RestoreItem
        {
            public BackupGroup Group;
            public string Prefix;

            public string SourceFile;

            public string SourceRoot;

            public string Relative;
        }

        private static IEnumerable<string> Prefixes(GroupTemplate template)
            => new[] { template.ArchivePrefix }.Concat(template.AdditionalPrefixes ?? Array.Empty<string>());
    }
}
