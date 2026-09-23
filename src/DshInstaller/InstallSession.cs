using System;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller
{
    /// <summary>这次跑的是安装还是卸载。两个流程共用同一套向导与执行器。</summary>
    internal enum SessionMode
    {
        Install,
        Uninstall,
    }

    /// <summary>安装范围。</summary>
    internal enum InstallScope
    {
        CurrentUser,
        AllUsers,
    }

    /// <summary>
    /// 本次安装的所有选择。各页往里写,确认页和进度页读它。
    /// 单例就够了 —— 一个安装器一次只跑一遍。
    /// </summary>
    internal sealed class InstallSession
    {
        private static readonly InstallSession Instance = new InstallSession();

        public static InstallSession Current
        {
            get { return Instance; }
        }

        /// <summary>当前是安装还是卸载。</summary>
        public SessionMode Mode { get; set; } = SessionMode.Install;

        /// <summary>卸载时的选择(装的时候没用)。</summary>
        public Shared.Install.UninstallOptions UninstallOptions { get; set; }

        /// <summary>装给谁。</summary>
        public InstallScope Scope { get; set; } = InstallScope.CurrentUser;

        /// <summary>各组件根目录(便携组件解压到这儿)。</summary>
        public string ComponentsRoot { get; set; }

        /// <summary>DSH 本体目录。</summary>
        public string DshRoot { get; set; }

        /// <summary>启动器目录。</summary>
        public string LauncherRoot { get; set; }

        /// <summary>本机已有 DSH,直接用现有的、跳过下载。</summary>
        public bool UseExistingDsh { get; set; }

        /// <summary>下载源偏好:国内镜像还是官方。</summary>
        public string SourcePreference { get; set; } = MirrorSource.China;

        /// <summary>勾选要装的便携组件。</summary>
        public bool InstallGit { get; set; }
        public bool InstallPnpm { get; set; }
        public bool InstallPython { get; set; }

        /// <summary>
        /// Git 和 pnpm 都已具备（本机已有或本次会安装）。
        /// 推荐插件下载不满足这个条件时必须整页跳过。
        /// </summary>
        public bool PluginToolsAvailable { get; set; }

        /// <summary>官方推荐插件页选中的安装表达式。</summary>
        public System.Collections.Generic.List<string> RecommendedPluginSpecs
        {
            get;
        } = new System.Collections.Generic.List<string>();

        /// <summary>装完要不要建桌面快捷方式 / 开机自启。</summary>
        public bool CreateDesktopShortcut { get; set; } = true;

        /// <summary>要不要在开始菜单里建一份(默认要)。</summary>
        public bool CreateStartMenuShortcut { get; set; } = true;

        /// <summary>
        /// 开机自启。默认**不勾** —— 它要注册"最高权限"的计划任务,必然弹 UAC,
        /// 而默认范围是"仅为本用户安装"。选了"给所有用户"时范围页会把它默认勾上。
        /// </summary>
        public bool EnableAutostart { get; set; }

        /// <summary>
        /// 用户是否已经在启动器那一页确认过"开机自启"这一项。
        ///
        /// 用来避免"回到范围页改一下,把人家的勾选冲掉" ——
        /// 只有他还没做过这个决定时,范围页才去动默认值。
        /// </summary>
        public bool AutostartChosen { get; set; }

        /// <summary>用户是否自己定过 DSH 本体目录。定过之后,别的页面就不再改它。</summary>
        public bool DshRootChosen { get; set; }

        /// <summary>用户是否自己定过启动器目录。</summary>
        public bool LauncherChosen { get; set; }

        /// <summary>用户是否自己定过组件目录。</summary>
        public bool ComponentsChosen { get; set; }

        public bool LaunchAfterwards { get; set; } = true;

        /// <summary>检测结果,检测页填。</summary>
        public ProbeReport Report { get; set; }

        /// <summary>本次安装的结果,进度页填、完成页读。</summary>
        public Shared.Install.InstallReport Result { get; set; }

        /// <summary>回滚时撤销掉的东西(给失败页显示,让用户知道"现在是什么状态")。</summary>
        public System.Collections.Generic.List<string> RolledBack { get; set; } =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// 这次安装是不是需要管理员权限。
        ///
        /// 两种情况:
        ///   1. 装给所有用户 —— 要写 Program Files,必然要;
        ///   2. 需要开机自启 —— 注册"最高权限"的计划任务需要管理员。
        ///      启动器是 requireAdministrator,用 Run 键/启动文件夹在登录阶段会被静默拦掉,
        ///      只能走计划任务,而那个任务要管理员才能注册。
        ///
        /// 仅为本用户、且不要自启时全程不需要管理员 —— 这是那个选项的卖点。
        /// </summary>
        public bool NeedsElevation
        {
            get
            {
                if (Scope == InstallScope.AllUsers)
                {
                    return true;
                }

                if (EnableAutostart)
                {
                    return true;
                }

                // 运行库是机器级的:缺了就得下载 + 静默安装,而那就必须管理员。
                // 这一步不能漏 —— 漏了的话"仅为本用户"的用户会在进度页看到
                // "安装失败(退出码 1638)"这种看不懂的东西。
                if (Shared.Detection.RuntimeProbe.AnyMissing)
                {
                    return true;
                }

                // 还有第三种:"仅为本用户安装",可目录挑了 Program Files 之类的。
                // 不提权的话会一路写失败(实测:Node 和 DSH 本体双双"失败",而用户
                // 以为是自己哪里选错了)。界面上那时已经给过提示,所以这里该提就提。
                if (!PathAccess.CanWriteWithoutElevation(ComponentsRoot)
                    || !PathAccess.CanWriteWithoutElevation(DshRoot)
                    || !PathAccess.CanWriteWithoutElevation(LauncherRoot))
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>用户目录(仅为我安装时的落地位置)。</summary>
        public static string UserRoot
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeek Harness");
            }
        }

        /// <summary>公共目录(为所有用户安装时的落地位置)。</summary>
        public static string MachineRoot
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "DeepSeek Harness");
            }
        }

        /// <summary>按当前范围给出默认根目录。</summary>
        public string DefaultRoot
        {
            get { return Scope == InstallScope.AllUsers ? MachineRoot : UserRoot; }
        }
    }
}
