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

        /// <summary>
        /// DSH 本体要装的版本(空 = 用 <see cref="WellKnown.DshPackageVersion"/> 那个默认)。
        ///
        /// 值就是 npm 的版本说明:可以是 `latest` / `next` 这类 tag,也可以是
        /// `0.2.0-rc.1` / `^0.1.5-rc.1` 这样的版本或范围。由版本页填,安装时原样拼进 npm install。
        /// </summary>
        public string DshVersion { get; set; }

        /// <summary>
        /// 启动器要装的版本(空 = 跟默认)。
        ///
        /// **暂未实现**:版本页上这一项是禁用的(界面上写明「暂未开放」)。
        /// 字段先留着,是为了让"升级通道"两边对齐 —— 以后接上只需要在这里填值。
        /// </summary>
        public string LauncherVersion { get; set; }

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

        /// <summary>
        /// 本机已经够新、可以直接用的组件(检测结果里挑出来的)。
        /// 只放"这次要复用的",没挑出来的组件照旧下载安装。
        /// </summary>
        public List<ReusableComponent> ReusableComponents { get; set; } =
            new List<ReusableComponent>();

        /// <summary>
        /// 强制重装:忽略"本机已有",把勾选的组件重新下一份便携版。
        /// 给"本机那份能用但我不想要它"和测试用。
        /// </summary>
        public bool ForceReinstall { get; set; }

        /// <summary>
        /// 修复模式:先核对一遍装过的东西,缺的补齐。
        /// 步骤本身幂等,所以修复就是"整条安装流程再跑一遍,有的跳过、没的装上"。
        /// </summary>
        public bool Repair { get; set; }

        /// <summary>找一个可以复用的组件,没有返回 null。</summary>
        public ReusableComponent Reusable(string id)
        {
            if (ReusableComponents == null)
            {
                return null;
            }

            for (int i = 0; i < ReusableComponents.Count; i++)
            {
                ReusableComponent item = ReusableComponents[i];
                if (item != null && string.Equals(item.Id, id, System.StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// 这次到底复不复用。除了开关,还要**再确认一次文件真的在** ——
        /// 检测到安装之间隔着好几页,用户完全可能中间把 Node 卸了。
        /// 文件没了就当没复用,老老实实去下(不然进度页会报"未找到 npm")。
        /// </summary>
        public bool CanReuse(string id)
        {
            if (ForceReinstall)
            {
                return false;
            }

            ReusableComponent item = Reusable(id);
            if (item == null || string.IsNullOrWhiteSpace(item.ExePath))
            {
                return false;
            }

            try
            {
                return System.IO.File.Exists(item.ExePath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>复用路径(没复用就是 null)。</summary>
        public string ReusePath(string id)
        {
            return CanReuse(id) ? Reusable(id).ExePath : null;
        }

        /// <summary>复用组件的目录(没复用或没目录就是 null)。</summary>
        public string ReuseDirectory(string id)
        {
            if (!CanReuse(id))
            {
                return null;
            }

            return Reusable(id).PathDirectory;
        }

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

            if (problems.Count == 0)
            {
                string layout = InstallPaths.ValidateLayout(DshRoot, LauncherRoot, ComponentsRoot);
                if (layout != null) problems.Add(layout);
            }
            return problems.Count == 0 ? null : string.Join("; ", problems.ToArray());
        }
    }
}
