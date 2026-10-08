using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                AdditionalPrefixes = new[] { ".dsh/plugins" },
                ByDefault = true,
            },
            new GroupTemplate
            {
                Id = GroupSessions,
                NameZh = "会话与缓存",
                NameEn = "Sessions and cache",
                RelativePath = @".dsh\storages",
                ArchivePrefix = ".dsh/storages",
                AdditionalPrefixes = new[] { ".dsh/sessions" },
                ByDefault = false,
            },
        };

        /// <summary>列出这台机器上有哪些可备份的数据(不存在的组不会出现)。</summary>
        public static List<BackupGroup> Describe(string dshRoot)
        {
            List<BackupGroup> groups = new List<BackupGroup>();
            if (String.IsNullOrWhiteSpace(dshRoot))
            {
                return groups;
            }

            for (int index = 0; index < Templates.Length; index++)
            {
                GroupTemplate template = Templates[index];
                string path = Prefixes(template).Select(prefix => Path.Combine(dshRoot, prefix.Replace('/', Path.DirectorySeparatorChar)))
                    .FirstOrDefault(Directory.Exists);

                bool exists = false;
                try
                {
                    exists = Directory.Exists(path);
                }
                catch
                {
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
                        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
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
            CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(dshRoot)
                || chosen == null
                || chosen.Count == 0
                || String.IsNullOrWhiteSpace(archivePath))
            {
                return false;
            }

            List<string> relativePaths = new List<string>();
            for (int index = 0; index < chosen.Count; index++)
            {
                BackupGroup group = chosen[index];
                if (group == null || !group.Exists)
                {
                    continue;
                }

                foreach (string prefix in group.ArchivePrefixes ?? new[] { group.ArchivePrefix })
                    if (Directory.Exists(Path.Combine(dshRoot, prefix.Replace('/', Path.DirectorySeparatorChar)))) relativePaths.Add(prefix);
            }

            if (relativePaths.Count == 0)
            {
                return false;
            }

            if (log != null)
            {
                log("开始打包:" + String.Join(", ", relativePaths.ToArray()));
            }

            // 关键:工作目录设成 DSH 根目录,传相对路径 —— 这样 7z 存进去的就是相对路径
            bool ok = DymArchive.Create(
                archivePath,
                relativePaths,
                dshRoot,
                delegate(string text, double percent)
                {
                    if (report != null)
                    {
                        report("正在打包:" + archivePath, percent);
                    }
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
            out string error)
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

            string staging = Path.Combine(
                Path.GetTempPath(),
                "Dafeiyu-Go-restore-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (log != null)
                {
                    log("正在解包到临时目录:" + staging);
                }

                bool extracted = DymArchive.Extract(
                    archivePath,
                    staging,
                    delegate(string text, double percent)
                    {
                        if (report != null)
                        {
                            report("正在解包备份", percent * 0.3);
                        }
                    },
                    log,
                    token);

                if (!extracted)
                {
                    error = "备份包解不开(文件可能损坏,或者根本不是 .dym)。";
                    return false;
                }

                return MergeStaging(staging, dshRoot, chosen, policy, ask, report, log, token, out error);
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
            out string error)
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
                groups = Describe(dshRoot);
            }

            // 先把每个组要搬的文件列出来(为了能报 "第 3 / 共 12 个")
            List<RestoreItem> items = new List<RestoreItem>();
            for (int index = 0; index < groups.Count; index++)
            {
                BackupGroup group = groups[index];
                foreach (string prefix in group.ArchivePrefixes ?? new[] { group.ArchivePrefix })
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
                string targetDirectory = Path.Combine(
                    dshRoot,
                    item.Prefix.Replace('/', Path.DirectorySeparatorChar));
                string target = Path.Combine(targetDirectory, item.Relative);

                if (report != null)
                {
                    report(
                        "正在恢复 " + item.Group.NameZh
                            + " (" + (index + 1) + "/" + items.Count + ") · "
                            + item.Relative.Replace('\\', '/'),
                        30 + (index + 1) * 70.0 / items.Count);
                }

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
            try
            {
                foreach (string directory in Directory.GetDirectories(root))
                {
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
