using System;

namespace DshInstaller.Shared
{
    /// <summary>全局常量。装机脚本、检测、下载全部从这里取值,避免各写一份。</summary>
    public static class WellKnown
    {
        public const string ProductName = "Dafeiyu-Go";
        public const string LegacyProductName = "DeepSeek Harness";
        public const string ProductNameChinese = "大肥鱼Go";
        public const string InstallerName = "Dafeiyu-Go Setup";

        /// <summary>
        /// 安装器版本号。
        ///
        /// **从程序集版本读,不手写** —— 以前这里硬编码一份、Directory.Build.props 里
        /// 又写一份,改版本号得改两处,漏一处就会出现"界面写着 1.0.1、注册表 DisplayVersion
        /// 却记着 1.0.0"这种事。程序集版本由 csproj 从 Directory.Build.props 注入,
        /// 所以那边才是唯一来源。
        /// </summary>
        public static string InstallerVersion
        {
            get
            {
                try
                {
                    Version version = typeof(WellKnown).Assembly.GetName().Version;
                    return version == null ? "0.0.0" : version.ToString(3);
                }
                catch
                {
                    return "0.0.0";
                }
            }
        }

        public const string LauncherVersion = "1.4.9.1";

        /// <summary>DSH 的 npm 包名。</summary>
        public const string DshPackage = "@deepseek-ai/dsh";

        /// <summary>DSH 主版本范围。</summary>
        public const string DshPackageVersion = "^0.1.5-rc.1";

        /// <summary>DSH 本体目录的标志文件(相对 DSH 根)。</summary>
        public const string DshMarker = @"node_modules\@deepseek-ai\dsh\lib\bin.js";

        /// <summary>
        /// 启动器子目录名。
        ///
        /// 叫 "launcher" 而不是 "DeepSeek Harness":安装根目录本身就叫
        /// "DeepSeek Harness",再套一层同名目录就成了
        /// `C:\Program Files\DeepSeek Harness\DeepSeek Harness` —— 看着像套娃(用户点名过)。
        /// 启动器自己的可执行文件仍然叫 "DeepSeek Harness.exe",不受影响。
        /// </summary>
        public const string LauncherFolder = "launcher";

        /// <summary>启动器外层引导程序文件名。</summary>
        public const string LauncherExe = "DeepSeek Harness.exe";

        /// <summary>DSH Web 服务地址。</summary>
        public const string ServiceUrl = "http://127.0.0.1:8787/";

        public const int ServicePort = 8787;

        /// <summary>最低支持的 Windows 生成号(Windows 10 1809)。</summary>
        public const int MinimumWindowsBuild = 17763;

        /// <summary>DSH 要求的最低 Node 版本。</summary>
        public const string MinimumNodeVersion = "22.13.0";

        /// <summary>缺 Node 时装哪个大版本(偶数 LTS 线)。</summary>
        public const int PreferredNodeMajor = 22;

        /// <summary>注册表/快捷方式里用的名字。</summary>
        public const string RunValueName = "DeepSeek Harness";

        public const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RegistryUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeepSeekHarness";
        public const string RegistryStartupApprovedFolder =
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        /// <summary>
        /// 开机自启的计划任务名。
        /// 必须和启动器自己注册时用的名字一致(DeepSeekHarnessAutostart),
        /// 否则会出现两个任务、互相覆盖不走。
        /// </summary>
        public const string AutostartTaskName = "DeepSeekHarnessAutostart";

        /// <summary>开机自启统一用的参数:静默驻留托盘,不弹浏览器。</summary>
        public const string SilentArgument = "--no-browser";

        /// <summary>提权 worker 的开关参数。</summary>
        public const string WorkerArgument = "--dsh-installer-worker";

        /// <summary>演练模式:只走流程不落盘。</summary>
        public const string DryRunArgument = "--dry-run";

        public const string ConfigFolderName = "DeepSeekHarness";
        public const string ConfigFileName = "installer-state.json";

        // ---------------------------------------------------------------- 启动器发布源

        /// <summary>
        /// 启动器仓库(owner/repo)。安装器默认从它的 manifest.json 查最新版再下载,
        /// 这样启动器发新版时安装器不用跟着重发。
        /// 留空 = 只用本地 payload\launcher.zip。
        /// </summary>
        public const string LauncherRepository = "YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run";

        /// <summary>过渡期旧仓库，仍作为清单和 Release 的回退候选。</summary>
        public const string LegacyLauncherRepository = "YunxiRamito/DSH-Launcher";

        /// <summary>启动器清单所在分支。</summary>
        public const string LauncherBranch = "main";

        /// <summary>清单文件名(放在启动器仓库根目录)。</summary>
        public const string LauncherManifestFile = "manifest.json";

        /// <summary>DSH 本体仓库(仅用于展示和文档,安装走 npm)。</summary>
        public const string DshRepository = "deepseek-ai/deepseek-harness";

        // ---------------------------------------------------------------- 必装运行库
        //
        // 这两个库**不打包进安装器**(打进去就是上百 MB,强迫所有人先下完才能开始装)。
        // 改成必选组件:装的时候检测,缺了才去下载 + 静默安装。见 BuiltInSteps 的 Runtime* 步骤。

        /// <summary>要求的 .NET 桌面运行时大版本。</summary>
        public const int DotNetDesktopMajor = 8;

        /// <summary>要求的 Windows App Runtime 版本(和 csproj 里的 WindowsAppSDK 包版本保持一致)。</summary>
        public const string WindowsAppRuntimeVersion = "1.8.260804001";

        /// <summary>
        /// Windows App Runtime 的最低 **MSIX 框架包**版本。
        ///
        /// 为什么非要卡这个:光看"有没有装"是不够的 —— aka.ms 上的 1.8 包有好几个小版本,
        /// 装了个旧的上去,安装器照样起不来,弹的还是那句
        /// "Required components of the Windows App Runtime are missing ... MSIX package version >= 8000.946.1701.0"
        /// (实测踩过:自动装上了 8000.921.1539.0,而安装器要 8000.946.1701.0)。
        /// 这个数字就是 WindowsAppSDK 1.8.260804001 对应的框架包版本。
        /// </summary>
        public const string WindowsAppRuntimeMinimumVersion = "8000.946.1701.0";

        /// <summary>Windows App Runtime 1.8 的最低 Windows 生成号。</summary>
        public const int WindowsAppRuntimeMinimumBuild = 17763;

        /// <summary>运行库安装程序的落地文件名。</summary>
        public const string DotNetRuntimeInstallerFile = "windowsdesktop-runtime-win-x64.exe";
        public const string WindowsAppRuntimeInstallerFile = "windowsappruntimeinstall-x64.exe";

        /// <summary>独立卸载程序的文件名(装完会放到启动器目录,并注册到"应用和功能")。</summary>
        public const string UninstallerExe = "DSH-Uninstall.exe";

        /// <summary>安装器自身的落地目录(引导程序解压到这里,卸载时也靠它再拉起来)。</summary>
        public const string InstallerHomeFolder = "Boot";
    }
}
