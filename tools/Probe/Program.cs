using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DshInstaller.Shared;
using DshInstaller.Shared.Detection;
using DshInstaller.Shared.Install;

namespace DshInstaller.Probe
{
    /// <summary>
    /// 开发用的命令行体检工具:把 EnvironmentProbe / DshLocator 的结果打出来。
    /// 安装器 UI 还没做的时候,靠它验证检测逻辑。
    /// 用法: dotnet run -- [dshRootHint]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string hint = arguments.Length > 0 ? arguments[0] : null;

            Console.WriteLine("=== 系统 ===");
            ProbeReport report = EnvironmentProbe.Run(hint);
            Console.WriteLine("  系统: " + report.WindowsName + "  build=" + report.WindowsBuild
                + "  支持=" + report.IsWindowsSupported);
            Console.WriteLine("  管理员: " + report.IsElevated);
            Console.WriteLine();

            Console.WriteLine("=== 组件 ===");
            Console.WriteLine(string.Format("  {0,-10} {1,-26} {2,-12} {3,-10} {4}",
                "ID", "名称", "状态", "必需", "版本 / 路径"));
            for (int index = 0; index < report.Components.Count; index++)
            {
                ComponentStatus item = report.Components[index];
                Console.WriteLine(string.Format("  {0,-10} {1,-26} {2,-12} {3,-10} {4}",
                    item.Id,
                    item.DisplayName,
                    item.State.ToString(),
                    item.Required ? "是" : "否",
                    (item.DetectedVersion ?? "-") + (item.Path != null ? "   " + item.Path : string.Empty)));
            }

            Console.WriteLine();
            Console.WriteLine("必需组件全部就绪: " + report.AllRequiredReady);
            List<ComponentStatus> pending = report.PendingRequired();
            if (pending.Count > 0)
            {
                Console.WriteLine("还缺:");
                for (int index = 0; index < pending.Count; index++)
                {
                    Console.WriteLine("  - " + pending[index].DisplayName + " : " + pending[index].Notes);
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== DSH 定位 ===");
            List<string> candidates = DshLocator.FindCandidates();
            Console.WriteLine("  候选 " + candidates.Count + " 个:");
            for (int index = 0; index < candidates.Count && index < 12; index++)
            {
                bool ok = DshLocator.LooksLikeDshRoot(candidates[index]);
                Console.WriteLine("    " + (ok ? "[命中] " : "       ") + candidates[index]);
            }

            Console.WriteLine();
            Console.WriteLine("=== 下载源可用性 ===");
            string nodeVersion = MirrorSource.ResolveLatestNodeVersion(MirrorSource.China, 22);
            Console.WriteLine("  Node 22 最新 LTS(镜像优先): " + (nodeVersion ?? "查不到"));
            Console.WriteLine("  日志: " + InstallLogger.Path);
            return 0;
        }
    }
}
