using System;
using System.Collections.Generic;

namespace DshInstaller
{
    /// <summary>
    /// 开发期与无人值守用的命令行开关。发布版留着也无害(不传就没影响)。
    ///
    ///   --page=3          直接跳到第 N 页(0 起,见 WizardPage)
    ///   --lang=en         强制英文 / --lang=zh 强制中文
    ///   --dry-run         演练模式:只走流程不落盘
    ///   --install         明确允许"带 --page 跳页时也真安装"
    ///   --plan=&lt;文件&gt;    提权实例用它读回向导里填好的选项
    ///
    /// 静默安装(无人值守,给虚拟机测试和批处理用):
    ///   --silent
    ///   --dsh-root=&lt;目录&gt;  --launcher-root=&lt;目录&gt;  --components-root=&lt;目录&gt;
    ///   --scope=machine|user          (默认 user)
    ///   --source=china|official       (默认 china)
    ///   --git  --pnpm  --python       装对应可选组件
    ///   --no-shortcut  --no-autostart  --no-launch
    ///   --no-runtime                  不装运行库(默认会装 .NET 8 桌面运行时与 Windows App Runtime)
    ///   --report=&lt;文件&gt;              把结果 JSON 写到这个文件
    ///
    /// 安全约定:
    ///   带 --page 就是"预览界面"的用法,一律强制演练模式,不落盘。
    ///   要真装必须显式给 --install 或 --silent —— 这样开发时在本机翻页看样式绝不会误装东西。
    /// </summary>
    internal static class DevOptions
    {
        public static int? StartPage { get; private set; }

        public static bool HasLanguage { get; private set; }

        /// <summary>演练模式:不下载、不安装、不写环境变量。</summary>
        public static bool DryRun { get; private set; }

        /// <summary>是否显式允许在跳页模式下真安装。</summary>
        public static bool AllowInstallWithPage { get; private set; }

        /// <summary>
        /// 提权实例通过它拿到"已经填好的安装选项"。
        /// 提权是另起进程,内存里的选择传不过去,只能落成文件再用参数传路径。
        /// </summary>
        public static string PlanPath { get; private set; }

        // ---------------------------------------------------------------- 静默安装

        /// <summary>无人值守:跳过向导直接装。</summary>
        public static bool Silent { get; private set; }

        /// <summary>以卸载模式启动(--uninstall)。</summary>
        public static bool Uninstall { get; private set; }


        public static string DshRoot { get; private set; }
        public static string LauncherRoot { get; private set; }
        public static string ComponentsRoot { get; private set; }
        public static bool AllUsers { get; private set; }
        public static string Source { get; private set; } = "china";
        public static string PreviewStyle { get; private set; } = "auto";
        public static bool WantGit { get; private set; }
        public static bool WantPnpm { get; private set; }
        public static bool WantPython { get; private set; }
        public static bool NoShortcut { get; private set; }
        public static bool NoAutostart { get; private set; }
        public static bool NoLaunch { get; private set; }

        /// <summary>
        /// 不装运行库(--no-runtime)。默认是要装的 —— 它们和 Node 一样是硬前置。
        /// 这个开关是给"机器上确认已经装好 / 纯离线演练"用的。
        /// </summary>
        public static bool NoRuntime { get; private set; }

        public static string ReportPath { get; private set; }

        /// <summary>代码里强制指定起始页(提权实例读回计划后直接进进度页用)。</summary>
        public static void ForceStartPage(int page)
        {
            StartPage = page;
        }

        public static void Parse(string[] args)
        {
            if (args == null)
            {
                return;
            }

            // 第一遍:先把"是哪种模式"收齐。
            // 不能边扫边判断 —— "--page=7 --install" 与 "--install --page=7" 必须等价,
            // 一次遍历的话前者会先因为没看到 --install 就把演练模式锁上。
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.IsNullOrEmpty(argument))
                {
                    continue;
                }

                if (string.Equals(argument, "--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    DryRun = true;
                }
                else if (string.Equals(argument, "--install", StringComparison.OrdinalIgnoreCase))
                {
                    AllowInstallWithPage = true;
                }
                else if (string.Equals(argument, "--silent", StringComparison.OrdinalIgnoreCase))
                {
                    Silent = true;
                }
                else if (string.Equals(argument, "--uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    Uninstall = true;
                }
                else if (string.Equals(argument, "--git", StringComparison.OrdinalIgnoreCase))
                {
                    WantGit = true;
                }
                else if (string.Equals(argument, "--pnpm", StringComparison.OrdinalIgnoreCase))
                {
                    WantPnpm = true;
                }
                else if (string.Equals(argument, "--python", StringComparison.OrdinalIgnoreCase))
                {
                    WantPython = true;
                }
                else if (string.Equals(argument, "--no-shortcut", StringComparison.OrdinalIgnoreCase))
                {
                    NoShortcut = true;
                }
                else if (string.Equals(argument, "--no-autostart", StringComparison.OrdinalIgnoreCase))
                {
                    NoAutostart = true;
                }
                else if (string.Equals(argument, "--no-launch", StringComparison.OrdinalIgnoreCase))
                {
                    NoLaunch = true;
                }
                else if (string.Equals(argument, "--no-runtime", StringComparison.OrdinalIgnoreCase))
                {
                    NoRuntime = true;
                }
            }

            // 第二遍:取值
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.IsNullOrEmpty(argument))
                {
                    continue;
                }

                int separator = argument.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = argument.Substring(0, separator).TrimStart('-', '/').ToLowerInvariant();
                string value = argument.Substring(separator + 1).Trim().Trim('"');

                switch (key)
                {
                    case "page":
                        int page;
                        if (int.TryParse(value, out page) && page >= 0 && page <= (int)WizardPage.UninstallItems)
                        {
                            StartPage = page;
                        }

                        break;

                    case "lang":
                        HasLanguage = true;
                        Localization.Current = value.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                            ? Localization.Language.English
                            : Localization.Language.Chinese;
                        break;

                    case "plan":
                        PlanPath = value;
                        break;

                    case "dsh-root":
                        DshRoot = value;
                        break;

                    case "launcher-root":
                        LauncherRoot = value;
                        break;

                    case "components-root":
                        ComponentsRoot = value;
                        break;

                    case "scope":
                        AllUsers = value.StartsWith("machine", StringComparison.OrdinalIgnoreCase)
                            || value.StartsWith("all", StringComparison.OrdinalIgnoreCase);
                        break;

                    case "source":
                        Source = value.StartsWith("off", StringComparison.OrdinalIgnoreCase) ? "official" : "china";
                        break;

                    case "preview-style":
                        PreviewStyle = value.StartsWith("win10", StringComparison.OrdinalIgnoreCase)
                            ? "win10"
                            : (value.StartsWith("win11", StringComparison.OrdinalIgnoreCase)
                                ? "win11"
                                : "auto");
                        break;

                    case "report":
                        ReportPath = value;
                        break;
                }
            }

            // 静默模式本来就是"真装",不强制演练
            if (Silent)
            {
                return;
            }

            // 保险:跳页 = 看界面,强制演练,避免开发时在本机误装
            if (StartPage.HasValue && !AllowInstallWithPage)
            {
                DryRun = true;
            }
        }
    }
}
