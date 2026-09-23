using System.Collections.Generic;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 一次安装要用到的全部选择。
    ///
    /// 刻意做成"哑"数据:共享库不认识界面上的安装会话(InstallSession),
    /// 由界面层填好再传进来,这样共享库可以独立测试。
    /// </summary>
    public sealed class InstallOptions
    {
        /// <summary>装给所有用户(false = 仅当前用户)。</summary>
        public bool AllUsers { get; set; }

        /// <summary>便携组件的落地目录(Node / Git / Python 等)。</summary>
        public string ComponentsRoot { get; set; }

        /// <summary>DSH 本体目录(node_modules 就在这儿)。</summary>
        public string DshRoot { get; set; }

        /// <summary>启动器目录。</summary>
        public string LauncherRoot { get; set; }

        /// <summary>本机已有 DSH,直接用现有的、跳过下载。</summary>
        public bool UseExistingDsh { get; set; }

        /// <summary>下载源偏好(见 <see cref="MirrorSource"/>)。</summary>
        public string SourcePreference { get; set; } = MirrorSource.China;

        /// <summary>需要装的组件(Node 是硬前置,必然要装)。</summary>
        public bool InstallNode { get; set; } = true;
        public bool InstallDsh { get; set; } = true;
        public bool InstallLauncher { get; set; } = true;
        public bool InstallGit { get; set; }
        public bool InstallPnpm { get; set; }
        public bool InstallPython { get; set; }

        /// <summary>推荐插件页选中的安装表达式；安装器会自动启用 pnpm。</summary>
        public List<string> RecommendedPluginSpecs { get; set; } =
            new List<string>();

        /// <summary>
        /// 必装运行库。默认开 —— 它们是硬前置:安装器自己和启动器都跑在 WinUI3 上,
        /// 少一个就直接起不来,所以不是"可选组件"。
        ///
        /// 关掉只有两种正当理由:机器上已经确认装好了、或者在做纯离线的演练。
        /// </summary>
        public bool InstallDotNetRuntime { get; set; } = true;

        public bool InstallWindowsAppRuntime { get; set; } = true;

        /// <summary>
        /// 部署独立卸载程序并注册到"应用和功能"。默认开,
        /// 关掉之后用户就只能靠"再跑一次安装包"来卸载了。
        /// </summary>
        public bool DeployUninstaller { get; set; } = true;

        /// <summary>装完要不要建桌面快捷方式 / 开机自启。</summary>
        public bool CreateDesktopShortcut { get; set; } = true;

        /// <summary>要不要在开始菜单里建一份(默认要,方便"所有应用"里搜到)。</summary>
        public bool CreateStartMenuShortcut { get; set; } = true;
        public bool EnableAutostart { get; set; } = true;

        /// <summary>装完立刻启动托盘启动器。这一步会拉起需要管理员权限的进程。</summary>
        public bool LaunchAfterwards { get; set; } = true;

        /// <summary>演练模式:只走流程不落盘(给界面调试用)。</summary>
        public bool DryRun { get; set; }

        /// <summary>解压/下载用的临时目录。</summary>
        public string TempRoot { get; set; }

        /// <summary>把选项里涉及目录的字段做一次合理性检查,返回错误说明(空 = 没问题)。</summary>
        public string Validate()
        {
            List<string> problems = new List<string>();

            if (string.IsNullOrWhiteSpace(DshRoot))
            {
                problems.Add("DSH 目录为空");
            }

            if (string.IsNullOrWhiteSpace(LauncherRoot))
            {
                problems.Add("启动器目录为空");
            }

            if (string.IsNullOrWhiteSpace(ComponentsRoot))
            {
                problems.Add("组件目录为空");
            }

            return problems.Count == 0 ? null : string.Join("; ", problems.ToArray());
        }
    }
}
