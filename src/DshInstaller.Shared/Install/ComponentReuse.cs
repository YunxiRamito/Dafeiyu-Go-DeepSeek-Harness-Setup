using System;
using System.Collections.Generic;
using System.IO;
using DshInstaller.Shared.Detection;

namespace DshInstaller.Shared.Install
{
    /// <summary>本机已经能用的一个组件。</summary>
    public sealed class ReusableComponent
    {
        /// <summary>node / git / pnpm / python。</summary>
        public string Id { get; set; }

        /// <summary>界面显示名。</summary>
        public string DisplayName { get; set; }

        /// <summary>可以直接用的可执行文件全路径。</summary>
        public string ExePath { get; set; }

        /// <summary>检测到的版本。</summary>
        public string Version { get; set; }

        /// <summary>
        /// 需要写进 PATH 的目录。已经能用的组件多数本来就在 PATH 里
        /// (PathEditor 会去重),这里给的是"万一不在"的兜底目录。
        /// </summary>
        public string PathDirectory { get; set; }
    }

    /// <summary>
    /// 从检测结果里挑出"本机已经够新、可以直接用"的组件。
    ///
    /// 为什么要有这一层:检测页只回答"这个组件就绪没有",而安装的时候还要回答
    /// "既然它已经就绪,我还下不下?"。不挑出来的话,用户明明装了 Node 22,
    /// 组件页照样显示"将安装",进度页照样下 30 MB —— 白等一场(实测反馈)。
    ///
    /// 判定用的是 <see cref="ComponentStatus.IsSatisfied"/>,也就是**必须真跑通版本命令**:
    /// 文件在、跑不起来(半装 / 被杀软隔离)的不算,那种情况还是要装。
    ///
    /// Python 也参与复用,但不写 PATH —— 和安装便携版时的规矩保持一致,
    /// 不能盖掉用户自己那份。
    /// </summary>
    public static class ComponentReuse
    {
        public static List<ReusableComponent> Detect(ProbeReport probe)
        {
            List<ReusableComponent> found = new List<ReusableComponent>();
            if (probe == null)
            {
                return found;
            }

            Add(found, probe, "node", "Node.js", true);
            Add(found, probe, "git", "Git", true);
            Add(found, probe, "pnpm", "pnpm", true);
            Add(found, probe, "python", "Python", false);
            return found;
        }

        private static void Add(
            List<ReusableComponent> found,
            ProbeReport probe,
            string id,
            string displayName,
            bool pathDirectory)
        {
            ComponentStatus status = probe[id];
            if (status == null || !status.IsSatisfied || string.IsNullOrWhiteSpace(status.Path))
            {
                return;
            }

            // 版本命令没跑出结果的,IsSatisfied 已经拦住了;这里再挡一次空版本,
            // 免得界面上出现"已检测到，将直接使用 · 版本未知"。
            if (string.IsNullOrWhiteSpace(status.DetectedVersion))
            {
                return;
            }

            string directory = null;
            if (pathDirectory)
            {
                try
                {
                    directory = Path.GetDirectoryName(status.Path);
                }
                catch
                {
                    directory = null;
                }
            }

            found.Add(new ReusableComponent
            {
                Id = id,
                DisplayName = displayName,
                ExePath = status.Path,
                Version = status.DetectedVersion,
                PathDirectory = directory,
            });
        }
    }
}
