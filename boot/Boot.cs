// DSH 安装程序的引导程序(真引导,不只是自解压)
//
// 它干三件事,顺序不能换:
//   1. 检查两个必装运行库(.NET 8 桌面运行时 / Windows App Runtime 1.8),
//      缺了就**自己下载 + 静默安装** —— 所以安装包不用把上百 MB 的运行时背在身上;
//   2. 把附加在自己末尾的 payload.zip 解压到 %LOCALAPPDATA%\DeepSeekHarness\Boot;
//   3. 拉起里面的 DSH-Installer.exe,把命令行原样传下去。
//
// 为什么用 .NET Framework 写:Windows 10/11 自带它,所以引导程序**没有任何前置依赖**。
// 如果这里也用 .NET 8,那就成了"装 .NET 才能运行装 .NET 的东西"。
//
// 进度条的设计:**全程只有一根**,从左到右一次性走完。
// (以前的写法是每下载/安装一个东西就让进度条从头走一遍 —— 用户看到"进度条反复重启",
//  会以为出问题了。现在按预先把每个阶段折算成总进度的一段,见 ProgressPlan。)
//
// 编译(由 pack-release.ps1 自动执行):
//   csc /target:winexe /platform:anycpu /optimize+ /out:Boot.exe ^
//       /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
//       /r:System.Windows.Forms.dll /r:System.Drawing.dll Boot.cs
//
// 命令行:
//   --runtimes-only   只把运行库装好就退出(提权实例用这个,避免把整个安装流程也提权)
//   其它参数          原样传给 DSH-Installer.exe(--silent 之类的无人值守开关)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Boot
{
    private const string PayloadName = "DSH-Installer.exe";

    /// <summary>Process.GetProcessesByName 要的是不带扩展名的名字。</summary>
    private const string PayloadNameNoExtension = "DSH-Installer";
    private const int DotNetMajor = 8;
    private const string WinAppSdkVersion = "1.8.260804001";

    /// <summary>
    /// Windows App Runtime 的最低 MSIX 框架包版本(和 Shared 里 WellKnown 那个常量保持一致)。
    ///
    /// 坑:只判断"有没有装"是不够的。aka.ms 的 1.8 包有好几个小版本,
    /// 装上去一个旧的,安装器照样弹 "Required components of the Windows App Runtime are missing"
    /// 然后起不来 —— 实测自动装成了 8000.921.1539.0,而 WindowsAppSDK 1.8.260804001 要 8000.946.1701.0。
    /// </summary>
    private const string MinWinAppRuntimeVersion = "8000.946.1701.0";

    /// <summary>
    /// .NET 桌面运行时的直连构建版本(兜底用)。
    /// 固定版本会过期,但它排在"解析最新版"**前面**只是为了一条能通的直连路 ——
    /// 解析那步在国内容易超时,而用户要的是"装上",不是"装上最新的"。
    /// </summary>
    private const string DirectDotNetVersion = "8.0.31";

    /// <summary>提权实例只装运行库,不进安装向导。</summary>
    private const string RuntimesOnlySwitch = "--runtimes-only";

    /// <summary>无人值守:这个模式下不弹任何框(包括"要不要装运行库"那个问句)。</summary>
    private const string SilentSwitch = "--silent";

    private const string DotNetDownloadPage = "https://dotnet.microsoft.com/download/dotnet/8.0";
    private const string WinAppRuntimeDownloadPage = "https://aka.ms/windowsappsdk/1.8/latest";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Log("=== boot started ===");
            // 这一行是排查的关键:虚拟机上说"装完报错"时,第一件事就是看这里到底认没认到运行库
            Log("运行库:" + DescribeRuntimes());

            // TLS 1.2 必须**在最开始**就设好。
            //
            // ServicePointManager 是进程级的,而 .NET Framework 在这台 1809 上的默认协议
            // 只到 TLS 1.0 —— aka.ms 那类 CDN 直接拒绝握手,报"未能创建 SSL/TLS 安全通道"。
            // 以前只在各下载方法里设,于是"探测分段支持"那一步永远失败,悄悄退回单连接;
            // 更糟的是报错信息是英文的,看着像网络问题,其实是我们自己没把协议抬起来(实测)。
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch
            {
            }

            Application.EnableVisualStyles();

            bool runtimesOnly = HasSwitch(args, RuntimesOnlySwitch);

            // ---- 先把"要不要装运行库"这件事解决掉。
            //      提权会另起一个进程,所以放在进度窗之前 —— 免得窗口闪两下。
            if (MissingAny())
            {
                Log("缺少运行库:" + DescribeMissing());

                // 先问一句再动手。装运行库要联网、要管理员、还要等一两分钟 ——
                // 不打招呼就直接弹 UAC 开始下载,用户会觉得"这程序自己乱动我电脑"。
                // 无人值守(--silent)和提权子进程不弹,否则会卡在一个没人点的框上。
                bool silent = HasSwitch(args, SilentSwitch);

                if (!runtimesOnly && !silent)
                {
                    if (!ConfirmRuntimeInstall())
                    {
                        Log("用户在「要不要装运行库」那里选了否,退出");
                        return 6;
                    }
                }

                if (!IsAdministrator() && !runtimesOnly)
                {
                    Log("当前不是管理员,申请一次提权来装运行库");
                    bool elevated = RelaunchElevatedForRuntimes();

                    if (!MissingAny())
                    {
                        Log("提权实例已把运行库装好:" + DescribeRuntimes());
                    }
                    else if (!elevated)
                    {
                        Log("用户取消了提权");
                    }
                    else
                    {
                        Log("提权实例跑完了,运行库还是不全:" + DescribeMissing());
                    }
                }
            }

            bool needDotNet = String.IsNullOrEmpty(DotNetDesktopVersion());
            bool needWinApp = !WindowsAppRuntimeOk();

            // ---- 规划:把要做的每一段折算成总进度的一部分
            ProgressPlan plan = new ProgressPlan(BuildStages(needDotNet, needWinApp));

            if (needDotNet || needWinApp || !runtimesOnly)
            {
                int exitCode = 0;
                using (ProgressWindow window = new ProgressWindow())
                {
                    window.Show();
                    Application.DoEvents();

                    if (!RunStages(window, plan, needDotNet, needWinApp, runtimesOnly, out exitCode))
                    {
                        return exitCode;
                    }
                }
            }

            if (runtimesOnly)
            {
                Log("runtimes-only 完成,退出");
                return 0;
            }

            // 到这儿进度已经走完(解压 + 启动都算在计划里了),直接拉安装器
            // 注意:用**解压时实际用的那个目录**,不是固定路径(见 _payloadDirectory 的说明)
            string payload = _payloadDirectory;

            if (string.IsNullOrEmpty(payload))
            {
                Fail("安装数据不完整,无法解压。\r\n\r\n请重新下载安装程序。");
                return 2;
            }

            string exe = Path.Combine(payload, PayloadName);
            if (!File.Exists(exe))
            {
                Fail("解压后没有找到安装程序。\r\n\r\n位置:" + exe);
                return 3;
            }

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = JoinArguments(RemoveSwitch(args, RuntimesOnlySwitch)),
                WorkingDirectory = payload,
                UseShellExecute = false,
            };

            Log("starting: " + exe);
            Process child = Process.Start(info);
            if (child == null)
            {
                Fail("无法启动安装程序。");
                return 4;
            }

            child.WaitForExit();
            Log("installer exited with " + child.ExitCode);

            // 解压出来的那几十 MB 是**临时**的,装完就该消失。
            //
            // 为什么以前不敢删:卸载器要靠这份副本把安装器再拉起来。现在不用怕了 ——
            // 长期副本由安装器自己放到 <DSH 根>\.installer(见 BuiltInSteps.InstallerHomeFor)。
            //
            // 但删之前必须确认**没有别的实例还在用这个目录**:
            // 实测过用户连点两次,第二个实例刚开始加载,第一个实例的清理就把 DLL 删了,
            // 于是它 FileNotFoundException 崩掉 —— 而且崩在错误处理里,连原因都看不到。
            if (Process.GetProcessesByName(PayloadNameNoExtension).Length > 0)
            {
                Log("还有安装器实例在跑,跳过临时目录清理");
            }
            else
            {
                TryDeleteDirectory(payload);
            }

            return child.ExitCode;
        }
        catch (Exception exception)
        {
            Log("FATAL: " + exception);
            Fail("启动失败:" + exception.Message);
            return 1;
        }
    }

    // ------------------------------------------------------------------ 阶段规划

    private sealed class Stage
    {
        public string Title;

        /// <summary>这一段大概占整条进度条的多少"时间"。实际时长不准没关系,比例对就行。</summary>
        public double Weight;
    }

    /// <summary>
    /// 按"这次到底要做哪些事"排出阶段表。
    ///
    /// 权重是拍脑袋估的**相对耗时**:.NET 装一次一两分钟,解压只要一秒 ——
    /// 要是平均分,进度条会在解压那一段"嗖"地跳完、前面却半天不动。
    /// </summary>
    private static List<Stage> BuildStages(bool needDotNet, bool needWinApp)
    {
        List<Stage> stages = new List<Stage>();

        if (needDotNet)
        {
            // 措辞按微软指南:"动词开头的短句 + 省略号",而且放在进度条**上方**
            stages.Add(new Stage { Title = "正在下载 .NET " + DotNetMajor + " 桌面运行时…", Weight = 30 });
            stages.Add(new Stage { Title = "正在安装 .NET " + DotNetMajor + " 桌面运行时…", Weight = 55 });
        }

        if (needWinApp)
        {
            stages.Add(new Stage { Title = "正在下载 Windows App Runtime…", Weight = 12 });
            stages.Add(new Stage { Title = "正在安装 Windows App Runtime…", Weight = 20 });
        }

        stages.Add(new Stage { Title = "正在展开安装文件…", Weight = 3 });
        stages.Add(new Stage { Title = "正在启动安装向导…", Weight = 1 });

        return stages;
    }

    /// <summary>
    /// 把"第几段、这一段走了百分之多少"折成一条总进度。
    /// 界面层只认 <see cref="Fraction"/>(0..1),所以进度条永远不会往回退。
    /// </summary>
    private sealed class ProgressPlan
    {
        private readonly List<Stage> _stages;
        private readonly double _totalWeight;
        private int _index = -1;
        private double _doneWeight;

        public ProgressPlan(List<Stage> stages)
        {
            _stages = stages;
            for (int i = 0; i < _stages.Count; i++)
            {
                _totalWeight += _stages[i].Weight;
            }

            if (_totalWeight <= 0)
            {
                _totalWeight = 1;
            }
        }

        public int Count
        {
            get { return _stages.Count; }
        }

        /// <summary>进入下一段,返回它的标题(给界面显示)。</summary>
        public string Next()
        {
            if (_index >= 0 && _index < _stages.Count)
            {
                _doneWeight += _stages[_index].Weight;
            }

            _index++;
            if (_index >= _stages.Count)
            {
                return null;
            }

            return _stages[_index].Title;
        }

        /// <summary>当前阶段内部的进度(0..1)。</summary>
        public void Report(double fraction)
        {
            _current = fraction < 0 ? 0 : (fraction > 1 ? 1 : fraction);
        }

        private double _current;

        /// <summary>整条进度的比例(0..1),只增不减。</summary>
        public double Fraction
        {
            get
            {
                double baseWeight = _doneWeight;
                if (_index >= 0 && _index < _stages.Count)
                {
                    baseWeight += _stages[_index].Weight * _current;
                }

                double value = baseWeight / _totalWeight;
                if (value < _lastFraction)
                {
                    value = _lastFraction;
                }

                _lastFraction = value;
                return value;
            }
        }

        private double _lastFraction;

        public string StageText
        {
            get
            {
                if (_index < 0 || _index >= _stages.Count)
                {
                    return String.Empty;
                }

                return "(" + (_index + 1) + "/" + _stages.Count + ") " + _stages[_index].Title;
            }
        }
    }

    /// <summary>按计划跑:补运行库 → 解压 → (留给外面拉起安装器)。</summary>
    private static bool RunStages(
        ProgressWindow window,
        ProgressPlan plan,
        bool needDotNet,
        bool needWinApp,
        bool runtimesOnly,
        out int exitCode)
    {
        exitCode = 0;

        if (needDotNet)
        {
            window.SetStatus(plan.Next());
            if (!InstallDotNet(window, plan))
            {
                window.Close();
                exitCode = 5;
                return false;
            }
        }

        if (needWinApp)
        {
            window.SetStatus(plan.Next());
            if (!InstallWinAppRuntime(window, plan))
            {
                window.Close();
                exitCode = 5;
                return false;
            }
        }

        if (runtimesOnly)
        {
            return true;
        }

        window.SetStatus(plan.Next());
        if (!ExtractPayload(Assembly.GetExecutingAssembly().Location, plan, window))
        {
            window.Close();
            Fail("安装数据不完整,无法解压。\r\n\r\n请重新下载安装程序。");
            exitCode = 2;
            return false;
        }

        window.SetStatus(plan.Next());
        plan.Report(1);
        window.SetProgress(plan.Fraction);
        Thread.Sleep(200);
        window.Hide();

        return true;
    }

    private static bool InstallDotNet(ProgressWindow window, ProgressPlan plan)
    {
        try
        {
            string file = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-win-x64.exe");

            // 顺序:先用 aka.ms 稳定通道,它**不需要额外解析**、永远指向 8.0 线最新补丁。
            // 以前是先拉 release-metadata 解析具体版本 —— 那一步在慢网下会把界面堵几十秒
            // (标题栏显示"未响应"),而且十有八九最后还是下同一个包。
            // 拿不到解析结果只是少一次优化,不该让用户先干等。
            string stable = "https://aka.ms/dotnet/" + DotNetMajor + ".0/windowsdesktop-runtime-win-x64.exe";
            bool got = Download(window, plan, stable, file);

            if (!got)
            {
                // 直连 CDN 的构建地址。aka.ms 在国内时常不通(实测:握手直接失败),
                // 而这条 2026-09-19 在 1809 虚拟机上跑过 —— 58 MB / 26 秒,稳。
                string direct = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/"
                    + DirectDotNetVersion + "/windowsdesktop-runtime-" + DirectDotNetVersion + "-win-x64.exe";

                Log("稳定通道失败,试直连构建地址:" + direct);
                got = Download(window, plan, direct, file);
            }

            if (!got)
            {
                Log("直连也失败,改用 release-metadata 解析具体版本");
                string resolved = ResolveDotNetUrl(window);
                if (!String.IsNullOrEmpty(resolved))
                {
                    got = Download(window, plan, resolved, file);
                }
            }

            if (!got)
            {
                Log("下载 .NET 运行库失败");
                return PromptManualDownload();
            }

            window.SetStatus(plan.Next());
            int code = RunInstaller(window, plan, file, "/install /quiet /norestart",
                delegate { return !String.IsNullOrEmpty(DotNetDesktopVersion()); });
            Log(".NET 运行库安装退出码 = " + code);
            TryDelete(file);

            if (String.IsNullOrEmpty(DotNetDesktopVersion()))
            {
                Log(".NET 运行库装完还是检测不到");
                return PromptManualDownload();
            }

            return true;
        }
        catch (Exception exception)
        {
            Log("装 .NET 运行库时出错:" + exception);
            return PromptManualDownload();
        }
    }

    private static bool InstallWinAppRuntime(ProgressWindow window, ProgressPlan plan)
    {
        try
        {
            string file = Path.Combine(Path.GetTempPath(), "windowsappruntimeinstall-x64.exe");

            // 顺序很重要:**先拿和安装器自身要求一致的精确版本**,再退到 latest。
            // aka.ms 的 latest 指的是"1.8 线当前最新",但它完全可能比我们的 SDK 包还旧 ——
            // 实测就是它给装上了 8000.921.1539.0,而 WindowsAppSDK 1.8.260804001 要 8000.946.1701.0,
            // 结果安装器照旧弹 "Required components ... missing"。
            string exact = "https://aka.ms/windowsappsdk/1.8/" + WinAppSdkVersion
                + "/windowsappruntimeinstall-x64.exe";
            string latest = "https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe";

            Log("下载 Windows App Runtime(精确版本 " + WinAppSdkVersion + ")");
            bool got = Download(window, plan, exact, file);

            if (!got)
            {
                Log("精确版本通道失败,退到 latest:" + latest);
                got = Download(window, plan, latest, file);
            }

            if (!got)
            {
                Log("下载 Windows App Runtime 失败");
                return PromptManualDownload();
            }

            window.SetStatus(plan.Next());
            int code = RunInstaller(window, plan, file, "--quiet", delegate { return WindowsAppRuntimeOk(); });
            Log("Windows App Runtime 退出码 = " + code);
            TryDelete(file);

            if (!WindowsAppRuntimeOk())
            {
                Log("Windows App Runtime 装完还是不达标:" + DescribeRuntimes());
                return PromptManualDownload();
            }

            return true;
        }
        catch (Exception exception)
        {
            Log("装 Windows App Runtime 时出错:" + exception);
            return PromptManualDownload();
        }
    }

    /// <summary>装不上时给条出路:打开官方下载页,用户自己装完再重跑。</summary>
    private static bool PromptManualDownload()
    {
        string message =
            "自动安装运行库未成功(网络不可用,或安装过程被安全软件拦截)。\r\n\r\n"
            + "安装程序依赖以下运行库,缺少任意一项都无法启动:\r\n"
            + "  · .NET " + DotNetMajor + " 桌面运行时\r\n"
            + "  · Windows App Runtime 1.8\r\n\r\n"
            + "是否打开官方下载页?安装完成后请重新运行本安装程序。";

        try
        {
            DialogResult answer = MessageBox.Show(
                message,
                "DeepSeek Harness 安装程序",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer == DialogResult.Yes)
            {
                Process.Start(DotNetDownloadPage);
                Process.Start(WinAppRuntimeDownloadPage);
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// 拿 .NET 桌面运行时的直链(备用路径)。
    /// 从官方 release-metadata 里解析出"当前 8.0.x 最新补丁"的地址;
    /// 解析不出来就返回 null,交给调用者走别的通道。
    ///
    /// 注意这里**必须**带消息泵:直接 WebClient.DownloadString 是同步阻塞,
    /// 慢网下一次就是几十秒,准备窗口会变成"未响应"(实测)。
    /// </summary>
    private static string ResolveDotNetUrl(ProgressWindow window)
    {
        string[] indexUrls = new string[]
        {
            "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/" + DotNetMajor + ".0/releases.json",
            "https://builds.dotnet.microsoft.com/dotnet/release-metadata/" + DotNetMajor + ".0/releases.json",
        };

        for (int i = 0; i < indexUrls.Length; i++)
        {
            window.SetDetail("正在解析下载地址…");

            string json = DownloadTextPumped(indexUrls[i], window, 10);
            string url = ParseDotNetUrl(json);
            if (!String.IsNullOrEmpty(url))
            {
                Log("从 release-metadata 解析到 .NET 下载地址:" + url);
                return url;
            }
        }

        return null;
    }

    /// <summary>
    /// 下载一小段文本(给 release-metadata 用),带消息泵和超时。
    ///
    /// 为什么要专门写一个:WebClient.DownloadString 是同步的,在 UI 线程上调用
    /// 期间一个消息都不处理 —— 系统直接给窗口盖上"未响应"(实测踩过)。
    /// 这里改成异步 + DoEvents 泵 + 硬超时,卡的是网络而不是界面。
    /// </summary>
    private static string DownloadTextPumped(string url, ProgressWindow window, int timeoutSeconds)
    {
        try
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            using (WebClient client = new WebClient())
            {
                client.Encoding = System.Text.Encoding.UTF8;

                string result = null;
                Exception failure = null;
                bool done = false;

                client.DownloadStringCompleted += delegate(object sender, DownloadStringCompletedEventArgs e)
                {
                    failure = e.Error;
                    if (e.Error == null)
                    {
                        result = e.Result;
                    }

                    done = true;
                };

                client.DownloadStringAsync(new Uri(url));

                DateTime started = DateTime.Now;
                while (!done)
                {
                    Application.DoEvents();

                    if ((DateTime.Now - started).TotalSeconds > timeoutSeconds)
                    {
                        try
                        {
                            client.CancelAsync();
                        }
                        catch
                        {
                        }

                        Log("读 " + url + " 超时(" + timeoutSeconds + " 秒)");
                        return null;
                    }

                    Thread.Sleep(30);
                }

                if (failure != null)
                {
                    Log("读 " + url + " 失败:" + failure.Message);
                    return null;
                }

                return result;
            }
        }
        catch (Exception exception)
        {
            Log("读 " + url + " 出错:" + exception.Message);
            return null;
        }
    }

    /// <summary>
    /// 从 release-metadata 里挑出 win-x64 的桌面运行时地址。
    /// 这里刻意不用 JSON 库(.NET Framework 自带的那个要额外引用),用最朴素的定位:
    /// 找 "windowsdesktop" 段后面第一个带 "win-x64" 的 url。
    /// </summary>
    private static string ParseDotNetUrl(string json)
    {
        if (String.IsNullOrEmpty(json))
        {
            return null;
        }

        int desktop = json.IndexOf("\"windowsdesktop\"", StringComparison.OrdinalIgnoreCase);
        if (desktop < 0)
        {
            return null;
        }

        int scan = desktop;
        while (true)
        {
            int ridIndex = json.IndexOf("\"rid\"", scan, StringComparison.OrdinalIgnoreCase);
            if (ridIndex < 0)
            {
                return null;
            }

            int urlIndex = json.IndexOf("\"url\"", ridIndex, StringComparison.OrdinalIgnoreCase);
            if (urlIndex < 0)
            {
                return null;
            }

            int valueStart = json.IndexOf('"', json.IndexOf(':', urlIndex) + 1);
            int valueEnd = valueStart < 0 ? -1 : json.IndexOf('"', valueStart + 1);
            if (valueStart < 0 || valueEnd < 0)
            {
                return null;
            }

            string url = json.Substring(valueStart + 1, valueEnd - valueStart - 1);
            if (url.IndexOf("win-x64", StringComparison.OrdinalIgnoreCase) >= 0
                && url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            scan = valueEnd;
        }
    }

    /// <summary>带进度条的下载。进度直接喂给总进度计划,所以条子只往一个方向走。</summary>
    /// <summary>
    /// 这次**实际**解压到哪儿了。
    ///
    /// 目标目录删不掉时(上次的残留被占用之类)会加个时间戳后缀换个目录解压,
    /// 所以主流程不能想当然去固定路径找安装器 ——
    /// 否则就是"解压成功了,却报解压后没有找到安装程序"(实测踩过,而且看着像解压坏了)。
    /// </summary>
    private static string _payloadDirectory;

    /// <summary>
    /// 大文件走 8 连接分段下载。
    ///
    /// 为什么单独写一份:Boot 是 .NET Framework 单文件(csc 直接编),
    /// 引用不了安装器那边 net8 的 SegmentedDownloader,只能自己来一遍。
    /// 而运行库那两个包是整条流程里最大的东西(.NET 58 MB、Windows App Runtime 102 MB),
    /// 国内单连接经常只有几十 KB/s —— 分段对它们收益最大。
    ///
    /// 任何一步不满足(不支持 Range、文件不够大、分段失败)都返回 false,
    /// 调用方原样落回单连接那条路,不会因为这条有问题就装不了。
    /// </summary>
    private static bool TrySegmentedDownload(ProgressWindow window, ProgressPlan plan, string url, string target)
    {
        const int Segments = 8;
        const long MinimumSize = 8L * 1024 * 1024;

        try
        {
            long total = ProbeRangeLength(url);
            if (total < MinimumSize)
            {
                return false;
            }

            window.SetDetail("正在分 " + Segments + " 条连接并行下载 " + FormatMb(total) + "…");

            long chunk = total / Segments;
            long[] received = new long[Segments];
            string[] parts = new string[Segments];
            Thread[] workers = new Thread[Segments];

            for (int i = 0; i < Segments; i++)
            {
                long start = i * chunk;
                long end = (i == Segments - 1) ? total - 1 : (start + chunk - 1);

                parts[i] = target + ".seg" + i;
                int index = i;
                long from = start;
                long to = end;

                workers[i] = new Thread(delegate()
                {
                    FetchRange(url, from, to, parts[index], received, index);
                });

                workers[i].IsBackground = true;
                workers[i].Start();
            }

            Stopwatch clock = Stopwatch.StartNew();
            double lastSeconds = 0;
            long lastDone = 0;

            for (int i = 0; i < Segments; i++)
            {
                while (workers[i].IsAlive)
                {
                    Application.DoEvents();
                    Thread.Sleep(60);

                    long done = 0;
                    for (int k = 0; k < Segments; k++)
                    {
                        done += Interlocked.Read(ref received[k]);
                    }

                    double elapsed = clock.Elapsed.TotalSeconds;
                    if (elapsed - lastSeconds >= 0.4)
                    {
                        double speed = (done - lastDone) / (elapsed - lastSeconds);
                        lastSeconds = elapsed;
                        lastDone = done;

                        plan.Report(done / (double)total);
                        window.SetProgress(plan.Fraction);

                        // 文案**统一走 DescribeDownload** —— 和单连接那条路一字不差
                        // (已下载 X / Y · 速度 · 预计还需…)。以前分段这条自己拼了一版
                        // "下载中 · 速度 · X / Y",同一个窗口里两种格式来回跳(用户点名过)。
                        window.SetDetail(DescribeDownload(speed, done, total));
                    }
                }
            }

            long finished = 0;
            for (int i = 0; i < Segments; i++)
            {
                finished += Interlocked.Read(ref received[i]);
            }

            if (finished < total)
            {
                Log("分段下载没拉全(" + finished + "/" + total + "),放弃并回落单连接");
                CleanParts(parts);
                return false;
            }

            using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write))
            {
                for (int i = 0; i < Segments; i++)
                {
                    using (FileStream input = File.OpenRead(parts[i]))
                    {
                        byte[] buffer = new byte[256 * 1024];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                        }
                    }
                }
            }

            CleanParts(parts);

            if (new FileInfo(target).Length != total)
            {
                Log("合并后大小对不上,放弃分段结果");
                return false;
            }

            plan.Report(1);
            window.SetProgress(plan.Fraction);
            Log("分段下载完成:" + target);
            return true;
        }
        catch (Exception exception)
        {
            Log("分段下载出错,回落单连接:" + exception.Message);
            return false;
        }
    }

    /// <summary>要一段 bytes=0-0,从 Content-Range 里读出总长度;不支持 Range 返回 -1。</summary>
    private static long ProbeRangeLength(string url)
    {
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "DSH-Installer-Boot";
            request.Timeout = 25000;
            request.ReadWriteTimeout = 25000;
            request.AddRange(0, 0);

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    return -1;
                }

                long first;
                long last;
                long length;
                string range = response.Headers["Content-Range"];
                if (string.IsNullOrEmpty(range) || !ParseContentRange(range, out first, out last, out length))
                {
                    return -1;
                }

                return length;
            }
        }
        catch (Exception exception)
        {
            // 失败要留痕。以前这里静默 return -1,于是"同一个 aka.ms,一个分包了一个没分"
            // 查起来毫无头绪 —— 日志里一个字都没有(实测)。
            Log("探测分段支持失败(这个源改走单连接):" + exception.Message);
            return -1;
        }
    }

    /// <summary>解析 "bytes 0-0/58715896" 这种头。</summary>
    private static bool ParseContentRange(string value, out long first, out long last, out long length)
    {
        first = 0;
        last = 0;
        length = -1;

        try
        {
            int slash = value.LastIndexOf('/');
            if (slash < 0)
            {
                return false;
            }

            length = Int64.Parse(value.Substring(slash + 1).Trim());
            return length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>拉一段。写不满就把它记的字节数退回去,免得进度虚高。</summary>
    private static void FetchRange(string url, long start, long end, string partPath, long[] received, int index)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = "DSH-Installer-Boot";
                request.Timeout = 60000;
                request.ReadWriteTimeout = 60000;
                request.AddRange(start, end);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream remote = response.GetResponseStream())
                using (FileStream local = new FileStream(partPath, FileMode.Create, FileAccess.Write))
                {
                    byte[] buffer = new byte[128 * 1024];
                    long written = 0;
                    long expected = end - start + 1;

                    while (written < expected)
                    {
                        int read = remote.Read(buffer, 0, (int)Math.Min(buffer.Length, expected - written));
                        if (read <= 0)
                        {
                            break;
                        }

                        local.Write(buffer, 0, read);
                        written += read;
                        Interlocked.Add(ref received[index], read);
                    }

                    if (written >= expected)
                    {
                        return;
                    }

                    Interlocked.Add(ref received[index], -written);
                }
            }
            catch
            {
                // 换下一轮重试(同一个源,分段本来就不换源)
            }

            Thread.Sleep(500);
        }
    }

    private static void CleanParts(string[] parts)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            TryDelete(parts[i]);
        }
    }

    private static string FormatMb(long bytes)
    {
        // 统一走 FormatBytes:B → KB → MB → GB,到 1024 就进位。
        // 以前这里写死 MB、FormatKb 写死 KB,于是同一个窗口里会出现
        // "18667 KB/s" 这种读着费劲的数(用户点名要求到 1024 KB 就换 MB)。
        return FormatBytes(bytes);
    }

    private static string FormatKb(long bytesPerSecond)
    {
        return FormatBytes(bytesPerSecond);
    }

    private static bool Download(ProgressWindow window, ProgressPlan plan, string url, string target)
    {
        // 大文件先试 8 连接分段(运行库那两个包正是最大的)。
        // 拿不到 Range 或者文件不大,它就返回 false,原样走下面的单连接逻辑。
        if (TrySegmentedDownload(window, plan, url, target))
        {
            return true;
        }

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                Log("下载 " + url + " (第 " + attempt + " 次)");
                plan.Report(0);
                window.SetProgress(plan.Fraction);

                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "DSH-Installer-Boot");

                    SpeedMeter meter = new SpeedMeter();
                    meter.Reset();
                    window.SetDetail("正在连接下载服务器…");

                    bool done = false;
                    Exception failure = null;

                    // WebClient **没有超时**。碰上"连上了但不发数据"的源(aka.ms 在国内
                    // 网络下很常见),它会一直挂着 —— 界面上就是永远停在 0%
                    // 「正在连接下载服务器…」(实测)。所以自己看门:40 秒没新数据就掐掉换源。
                    long lastReceived = -1;
                    long lastTotal = 0;
                    DateTime lastActivity = DateTime.Now;

                    client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                    {
                        plan.Report(e.ProgressPercentage / 100.0);
                        window.SetProgress(plan.Fraction);
                        window.SetDetail(DescribeDownload(
                            meter.Update(e.BytesReceived), e.BytesReceived, e.TotalBytesToReceive));

                        lastReceived = e.BytesReceived;
                        lastTotal = e.TotalBytesToReceive;
                        lastActivity = DateTime.Now;
                    };

                    client.DownloadFileCompleted += delegate(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
                    {
                        failure = e.Error;
                        done = true;
                    };

                    client.DownloadFileAsync(new Uri(url), target);

                    // 一边等一边泵消息,进度条才会动
                    while (!done)
                    {
                        Application.DoEvents();
                        Thread.Sleep(40);

                        if ((DateTime.Now - lastActivity).TotalSeconds > 40)
                        {
                            Log("下载 40 秒没有新数据,判定卡住并换源(已收 "
                                + lastReceived + " / " + lastTotal + ")");
                            window.SetDetail("连接超时,正在切换下载源…");

                            try
                            {
                                client.CancelAsync();
                            }
                            catch
                            {
                            }

                            break;
                        }
                    }

                    if (failure != null)
                    {
                        throw failure;
                    }

                    if (!done)
                    {
                        // 是被上面那个看门狗掐掉的,不算成功 —— 走下一轮换源
                        continue;
                    }
                }

                if (File.Exists(target) && new FileInfo(target).Length > 1024 * 1024)
                {
                    Log("下载完成:" + target + "(" + new FileInfo(target).Length + " 字节)");
                    plan.Report(1);
                    window.SetProgress(plan.Fraction);
                    return true;
                }

                Log("下下来的文件太小,当作失败:" + target);
            }
            catch (Exception exception)
            {
                Log("下载失败:" + exception.Message);
            }

            Thread.Sleep(800);
        }

        return false;
    }

    /// <summary>
    /// 下载中的细节行:已下载 / 总量 · 速度 · 预计还需多久。
    ///
    /// 微软的指南说进度页别塞"只有技术支持才关心"的东西(GUID、具体文件名都算),
    /// 但它同时明说用户想知道的就两件事:**该不该等**、**快完了吗** ——
    /// 字节数、速度、剩余时间回答的正好是这两件事,所以留在这儿,而且放小字淡色。
    /// </summary>
    private static string DescribeDownload(double speed, long received, long total)
    {
        string text = "已下载 " + FormatBytes(received);
        if (total > 0)
        {
            text += " / " + FormatBytes(total);
        }

        if (speed > 0)
        {
            text += " · " + FormatBytes((long)speed) + "/s";

            if (total > received && speed > 4096)
            {
                text += " · " + DescribeRemaining((total - received) / speed);
            }
        }

        return text;
    }

    private static string DescribeRemaining(double seconds)
    {
        if (seconds < 8)
        {
            return "即将完成";
        }

        if (seconds < 60)
        {
            return "预计还需不到 1 分钟";
        }

        return "预计还需约 " + (int)Math.Ceiling(seconds / 60.0) + " 分钟";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.0") + " GB";
        }

        if (bytes >= 1024 * 1024)
        {
            return (bytes / 1024.0 / 1024).ToString("0.0") + " MB";
        }

        if (bytes >= 1024)
        {
            return (bytes / 1024.0).ToString("0") + " KB";
        }

        return bytes + " B";
    }

    /// <summary>
    /// 跑运行库安装程序,等它结束 —— 或者等"运行库真的能用了"。
    ///
    /// 为什么不能死等退出:
    /// windowsappruntimeinstall.exe 会**装完不退出**。实测它跑了 111 秒、只用了 0.48 秒 CPU,
    /// 而 Main / Singleton 两个包早就注册好了 —— 就是挂在那儿。死等下去的结果是
    /// 进度条永远停在某个百分比上,用户以为卡死了(实测截图踩过)。
    /// 所以判据换成"运行库真的好用了",进程退没退只是次要信息。
    ///
    /// 另外还要有个硬超时兜底:第三方安装包出什么幺蛾子都不该把用户永远挂住。
    /// </summary>
    private static int RunInstaller(ProgressWindow window, ProgressPlan plan, string file,
        string arguments, Func<bool> satisfied)
    {
        if (!File.Exists(file))
        {
            Log("找不到安装包:" + file);
            return -1;
        }

        ProcessStartInfo info = new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // 先把这个阶段的"份额"走掉一半,剩下的等它真的装完
        plan.Report(0.5);
        window.SetProgress(plan.Fraction);
        window.SetMarquee(true);
        window.SetDetail("正在安装,请稍候…");

        Process process = Process.Start(info);
        if (process == null)
        {
            window.SetMarquee(false);
            return -1;
        }

        DateTime started = DateTime.Now;
        DateTime lastCheck = DateTime.Now;
        int shownSeconds = -1;

        while (!process.WaitForExit(200))
        {
            Application.DoEvents();

            // 一秒刷一次就够了,刷太勤反而抢 UI 线程
            int seconds = (int)(DateTime.Now - started).TotalSeconds;
            if (seconds != shownSeconds)
            {
                shownSeconds = seconds;
                window.SetDetail("安装进行中 · 已用时 " + seconds + " 秒");
            }

            // 每 2 秒问一次"装好了吗"。装好了就别再等那个不肯退出的进程了。
            if ((DateTime.Now - lastCheck).TotalSeconds >= 2)
            {
                lastCheck = DateTime.Now;

                if (satisfied())
                {
                    Log("运行库已就位(安装程序尚未退出,不再等待它)");
                    window.SetMarquee(false);
                    plan.Report(1);
                    window.SetProgress(plan.Fraction);
                    return 0;
                }
            }

            if (seconds > 15 * 60)
            {
                Log("运行库安装超过 15 分钟仍未见效,放弃等待");
                try
                {
                    process.Kill();
                }
                catch
                {
                }

                window.SetMarquee(false);
                return -1;
            }
        }

        window.SetMarquee(false);
        plan.Report(1);
        window.SetProgress(plan.Fraction);
        return process.ExitCode;
    }

    // ------------------------------------------------------------------ 探测

    private static bool MissingAny()
    {
        return String.IsNullOrEmpty(DotNetDesktopVersion()) || !WindowsAppRuntimeOk();
    }

    /// <summary>WinAppRuntime 装了、而且版本够。</summary>
    private static bool WindowsAppRuntimeOk()
    {
        return VersionAtLeast(WindowsAppRuntimeVersion(), MinWinAppRuntimeVersion);
    }

    /// <summary>逐段比数字版本(不用 Version 类:微软这套版本号段数不固定)。</summary>
    private static bool VersionAtLeast(string actual, string minimum)
    {
        if (String.IsNullOrEmpty(actual) || String.IsNullOrEmpty(minimum))
        {
            return false;
        }

        string[] left = actual.Split('.');
        string[] right = minimum.Split('.');
        int count = left.Length > right.Length ? left.Length : right.Length;

        for (int i = 0; i < count; i++)
        {
            int a = 0;
            int b = 0;
            if (i < left.Length)
            {
                Int32.TryParse(left[i], out a);
            }

            if (i < right.Length)
            {
                Int32.TryParse(right[i], out b);
            }

            if (a != b)
            {
                return a > b;
            }
        }

        return true;
    }

    private static string DescribeRuntimes()
    {
        string runtime = WindowsAppRuntimeVersion() ?? "(缺)";
        if (!String.IsNullOrEmpty(WindowsAppRuntimeVersion()) && !WindowsAppRuntimeOk())
        {
            runtime += "(低于要求的 " + MinWinAppRuntimeVersion + ")";
        }

        return ".NET=" + (DotNetDesktopVersion() ?? "(缺)")
            + " / WinAppRuntime=" + runtime;
    }

    private static string DescribeMissing()
    {
        string text = String.Empty;
        if (String.IsNullOrEmpty(DotNetDesktopVersion()))
        {
            text += " .NET " + DotNetMajor + " 桌面运行时";
        }

        if (!WindowsAppRuntimeOk())
        {
            text += " Windows App Runtime 1.8";
        }

        return text;
    }

    /// <summary>缺的东西列成一行一条,给弹窗用。</summary>
    private static string DescribeMissingLines()
    {
        string text = String.Empty;
        if (String.IsNullOrEmpty(DotNetDesktopVersion()))
        {
            text += "  · .NET " + DotNetMajor + " 桌面运行时\r\n";
        }

        if (!WindowsAppRuntimeOk())
        {
            // 装了旧版本的话要说清楚,否则用户看着"明明装了啊"会以为程序坏了
            string installed = WindowsAppRuntimeVersion();
            if (String.IsNullOrEmpty(installed))
            {
                text += "  · Windows App Runtime 1.8\r\n";
            }
            else
            {
                text += "  · Windows App Runtime 1.8(当前 " + installed
                    + " 版本过低,需 " + MinWinAppRuntimeVersion + " 或更高)\r\n";
            }
        }

        return text;
    }

    /// <summary>
    /// 缺运行库时先问一句,得到同意再往下走。
    ///
    /// 为什么非问不可:接下来会发生三件"动静不小"的事 —— 弹 UAC、联网下载几十 MB、静默安装。
    /// 不打招呼就一口气做完,用户会觉得这程序自作主张。
    /// </summary>
    private static bool ConfirmRuntimeInstall()
    {
        string message =
            "本机缺少安装程序必需的运行库:\r\n\r\n"
            + DescribeMissingLines()
            + "\r\n安装程序需要这些运行库安装完成后才能正常启动。\r\n"
            + "是否立即下载并安装?";

        try
        {
            DialogResult answer = MessageBox.Show(
                message,
                "缺少必要的运行库",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1);

            if (answer == DialogResult.Yes)
            {
                return true;
            }

            MessageBox.Show(
                "已取消,安装程序将退出。\r\n\r\n"
                + "安装 .NET " + DotNetMajor + " 桌面运行时与 Windows App Runtime 1.8 后,"
                + "重新运行本安装程序即可。",
                "DeepSeek Harness 安装程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return false;
        }
        catch (Exception exception)
        {
            // 弹不出框(比如没有交互桌面)时当作同意 —— 别因为一个对话框把安装卡死
            Log("问句弹不出来,按同意处理:" + exception.Message);
            return true;
        }
    }

    /// <summary>
    /// 找 .NET 桌面运行时。和 Shared 里 EnvironmentProbe 的判据保持一致:
    /// 看 %ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App 下有没有 8.x。
    /// </summary>
    private static string DotNetDesktopVersion()
    {
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"dotnet\shared\Microsoft.WindowsDesktop.App");

            if (!Directory.Exists(root))
            {
                return null;
            }

            string[] dirs = Directory.GetDirectories(root);
            for (int i = 0; i < dirs.Length; i++)
            {
                string name = Path.GetFileName(dirs[i]);
                if (name.StartsWith(DotNetMajor + ".", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// 找 Windows App Runtime。它在 Appx 的 AllUserStore 里以
    /// MicrosoftCorporationII.WinAppRuntime.Main.1.8_ 开头登记,取版本号部分。
    /// </summary>
    private static string WindowsAppRuntimeVersion()
    {
        try
        {
            const string keyPath =
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications";

            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
            {
                if (key == null)
                {
                    return null;
                }

                const string prefix = "MicrosoftCorporationII.WinAppRuntime.Main.1.8_";
                string[] names = key.GetSubKeyNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string remainder = names[i].Substring(prefix.Length);
                        int end = remainder.IndexOf('_');
                        return end >= 0 ? remainder.Substring(0, end) : remainder;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsAdministrator()
    {
        try
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>提权实例只装运行库,装完就退 —— 不把整个安装流程也提权了。</summary>
    private static bool RelaunchElevatedForRuntimes()
    {
        try
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = Assembly.GetExecutingAssembly().Location,
                Arguments = RuntimesOnlySwitch,
                UseShellExecute = true,
                Verb = "runas",
            };

            Process elevated = Process.Start(info);
            if (elevated == null)
            {
                return false;
            }

            elevated.WaitForExit();
            Log("提权实例退出码 = " + elevated.ExitCode);
            return true;
        }
        catch (Exception exception)
        {
            // 用户在 UAC 上点了"否",Win32Exception 1223
            Log("提权失败:" + exception.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ 解压

    /// <summary>
    /// 把附加在自己末尾的 zip 解压到 %LOCALAPPDATA%\DeepSeekHarness\Boot。
    ///
    /// 逐个条目解 —— 这样才能报进度(一整包一次性解开是拿不到进度的),
    /// 顺带也让"进度条卡住不动"这件事不存在。
    /// </summary>
    private static bool ExtractPayload(string selfPath, ProgressPlan plan, ProgressWindow window)
    {
        string target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepSeekHarness", "Boot");

        Log("extracting to " + target);

        if (Directory.Exists(target))
        {
            try
            {
                Directory.Delete(target, true);
            }
            catch
            {
                target = target + "-" + DateTime.Now.ToString("HHmmss");
                Log("旧目录删不掉,改用 " + target);
            }
        }

        Directory.CreateDirectory(target);

        using (FileStream file = new FileStream(selfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            long zipStart = FindZipStart(file);
            Log("zipStart = " + zipStart);
            if (zipStart < 0)
            {
                return false;
            }

            file.Seek(zipStart, SeekOrigin.Begin);

            // 把附加段单独复制成一个临时 zip 再解压 ——
            // 直接从"文件中间的某个偏移"建 ZipArchive 在某些实现下会报错。
            string tempZip = Path.Combine(Path.GetTempPath(), "dsh-payload-" + Guid.NewGuid().ToString("N") + ".zip");
            using (FileStream zipOut = new FileStream(tempZip, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[1024 * 256];
                int read;
                while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                {
                    zipOut.Write(buffer, 0, read);
                }
            }

            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                {
                    Log("extracting zip (" + new FileInfo(tempZip).Length + " bytes, "
                        + archive.Entries.Count + " entries) ...");

                    string targetFull = Path.GetFullPath(target);

                    for (int i = 0; i < archive.Entries.Count; i++)
                    {
                        ZipArchiveEntry entry = archive.Entries[i];

                        string destination = Path.GetFullPath(Path.Combine(target, entry.FullName));

                        // 防目录穿越:压缩包里的路径不许跑到目标目录外面去
                        if (!destination.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (String.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destination);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            entry.ExtractToFile(destination, true);
                        }

                        // 每 20 个报一次就够了,省得消息泵被刷爆。
                        // 只报"第几个/共几个",不报具体文件名 —— 微软的指南明说
                        // 进度页别显示"复制了哪些文件",那是给技术支持看的。
                        if (i % 20 == 0 || i == archive.Entries.Count - 1)
                        {
                            plan.Report((i + 1) / (double)archive.Entries.Count);
                            window.SetProgress(plan.Fraction);
                            window.SetDetail("正在展开 " + (i + 1) + " / " + archive.Entries.Count + " 个文件…");
                        }
                    }
                }

                Log("extract done");
                plan.Report(1);
            }            finally
            {
                try
                {
                    File.Delete(tempZip);
                }
                catch
                {
                }
            }
        }

        // 记下**实际**用的目录 —— 旧目录删不掉时上面换成带时间戳的那个了,
        // 主流程必须按这个找安装器,不能想当然用固定路径(那条路会报"解压后没有找到安装程序")。
        _payloadDirectory = target;

        return true;
    }

    /// <summary>
    /// 在文件末尾附近找 zip 的结束记录(EOCD),据此算出 zip 数据的起始偏移。
    /// 附加式自解压就是这么定位的:EOCD 固定签名 0x06054B50,注释最长 65535 字节。
    /// </summary>
    private static long FindZipStart(FileStream file)
    {
        const int MaxComment = 65535;
        const int EocdMinSize = 22;

        long length = file.Length;
        int window = (int)Math.Min(length, MaxComment + EocdMinSize + 1024);

        byte[] buffer = new byte[window];
        file.Seek(length - window, SeekOrigin.Begin);

        int total = 0;
        while (total < window)
        {
            int read = file.Read(buffer, total, window - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        for (int i = total - EocdMinSize; i >= 0; i--)
        {
            if (buffer[i] != 0x50 || buffer[i + 1] != 0x4B || buffer[i + 2] != 0x05 || buffer[i + 3] != 0x06)
            {
                continue;
            }

            long centralSize = BitConverter.ToUInt32(buffer, i + 12);
            long centralOffset = BitConverter.ToUInt32(buffer, i + 16);
            int commentLength = BitConverter.ToUInt16(buffer, i + 20);

            if (i + EocdMinSize + commentLength != total)
            {
                continue;
            }

            long zipStart = length - centralSize - centralOffset - EocdMinSize - commentLength;
            if (zipStart >= 0 && zipStart < length)
            {
                return zipStart;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------ 小工具

    private static bool HasSwitch(string[] args, string name)
    {
        if (args == null)
        {
            return false;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string JoinArguments(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return String.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append('"').Append(args[i].Replace("\"", "\\\"")).Append('"');
        }

        return builder.ToString();
    }

    private static string[] RemoveSwitch(string[] args, string name)
    {
        if (args == null)
        {
            return new string[0];
        }

        List<string> kept = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (!String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(args[i]);
            }
        }

        return kept.ToArray();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 删目录,删不掉就重试几次再放弃。
    ///
    /// 刚退出的那个安装器可能还留着文件句柄(尤其是它写过的东西),直接删会吃到
    /// "正在被另一个进程使用"。删不掉也不当作失败 —— %LOCALAPPDATA% 迟早会被系统清,
    /// 而且下次引导本来就会先把这个目录清一遍。
    /// </summary>
    private static void TryDeleteDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return;
                }

                Directory.Delete(directory, true);
                Log("已清理临时目录:" + directory);
                return;
            }
            catch (Exception exception)
            {
                if (attempt == 5)
                {
                    Log("临时目录没删掉(不影响使用):" + exception.Message);
                }
                else
                {
                    Thread.Sleep(400);
                }
            }
        }
    }

    /// <summary>引导阶段窗口一闪而过,所以每一步都落盘。</summary>
    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "dsh-boot.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void Fail(string message)
    {
        try
        {
            MessageBox.Show(message, "DeepSeek Harness 安装程序",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
        }
    }
}

/// <summary>
/// 引导期间那个小进度窗。
///
/// 排版照微软《Setup 体验指南》(win32 uxguide exper-setup)来,和主安装器的进度页保持一致:
///   第一行  标题
///   第二行  当前阶段(带 (2/4) 序号) + 右上角百分比
///   第三行  **一根**总进度条,从 0 走到 100 一次,不重启、不倒退
///   第四行  次要细节 —— 速度 / 已下载字节 / 当前文件名
///
/// 为什么细节行也要有:几十上百 MB 的下载和"看起来没在动"的安装,
/// 用户真正想知道的是"在动吗、多快、还剩多少"。这些信息回答的是这个问题,
/// 而不是给技术支持看的 GUID —— 所以放在淡色小字里,不喧宾夺主。
/// </summary>
internal sealed class ProgressWindow : Form
{
    private readonly Label _stage;
    private readonly ProgressBar _bar;
    private readonly Label _percent;
    private readonly Label _detail;

    public ProgressWindow()
    {
        Text = "DeepSeek Harness 安装程序";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 182);
        BackColor = Color.White;

        Label caption = new Label();
        caption.Text = "正在准备安装环境";
        caption.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        caption.Location = new Point(22, 18);
        caption.AutoSize = true;

        // 微软《Setup 体验指南》"Present helpful progress" 那条:
        // 用户只想知道"该等还是先去干别的""快完了吗",所以给一句总体耗时的说明。
        Label note = new Label();
        note.Text = "安装程序需要先补齐必需的运行库,这一步可能需要几分钟。";
        note.Font = new Font("Microsoft YaHei UI", 8.5F);
        note.ForeColor = Color.FromArgb(130, 130, 130);
        note.Location = new Point(24, 46);
        note.Size = new Size(432, 18);
        note.AutoEllipsis = true;

        _stage = new Label();
        _stage.Text = "正在准备…";
        _stage.Font = new Font("Microsoft YaHei UI", 9.5F);
        _stage.ForeColor = Color.FromArgb(60, 60, 60);
        _stage.Location = new Point(24, 76);
        _stage.Size = new Size(360, 20);
        _stage.AutoEllipsis = true;

        _percent = new Label();
        _percent.Text = "0%";
        _percent.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        _percent.ForeColor = Color.FromArgb(60, 60, 60);
        _percent.TextAlign = ContentAlignment.MiddleRight;
        _percent.Location = new Point(388, 76);
        _percent.Size = new Size(68, 20);

        _bar = new ProgressBar();
        _bar.Location = new Point(26, 104);
        _bar.Size = new Size(430, 14);
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Minimum = 0;
        _bar.Maximum = 1000;

        _detail = new Label();
        _detail.Text = String.Empty;
        _detail.Font = new Font("Microsoft YaHei UI", 8.5F);
        _detail.ForeColor = Color.FromArgb(130, 130, 130);
        _detail.Location = new Point(26, 126);
        _detail.Size = new Size(430, 34);
        _detail.AutoEllipsis = true;

        Controls.Add(caption);
        Controls.Add(note);
        Controls.Add(_stage);
        Controls.Add(_percent);
        Controls.Add(_bar);
        Controls.Add(_detail);

        // 这个窗口只负责报信,别让用户点 X 把它关了 —— 关了后面照样在跑,反而更让人困惑
        ControlBox = false;
    }

    public void SetStatus(string text)
    {
        _stage.Text = text ?? String.Empty;
        _stage.Refresh();
    }

    /// <summary>次要细节行:速度 / 字节数 / 当前文件。回答"在动吗、多快、还剩多少"。</summary>
    public void SetDetail(string text)
    {
        _detail.Text = text ?? String.Empty;
        _detail.Refresh();
    }

    /// <summary>总进度(0..1)。</summary>
    public void SetProgress(double fraction)
    {
        if (fraction < 0)
        {
            fraction = 0;
        }

        if (fraction > 1)
        {
            fraction = 1;
        }

        _lastFraction = fraction;
        ApplyBar();
        _percent.Text = ((int)Math.Round(fraction * 100)) + "%";
        _percent.Refresh();
    }

    private double _lastFraction;

    /// <summary>
    /// 把值真正画到条子上。
    ///
    /// 从 Marquee 切回 Continuous 时必须**重新赋一次 Value 再强制重画** ——
    /// 只改 Style 的话 WinForms 会留下 marquee 的残影,表现是
    /// "文字写着 100%,绿条却只到三分之一"(实测截图踩过)。
    /// </summary>
    private void ApplyBar()
    {
        _bar.Value = (int)(_lastFraction * 1000);
        _bar.Invalidate();
        _bar.Update();
    }

    /// <summary>
    /// 切"不确定进度"(跑第三方安装包时拿不到百分比,让条子来回滚表示还在动)。
    /// 取消滚动时进度值还是原来那个,不会倒退。
    /// </summary>
    public void SetMarquee(bool on)
    {
        _bar.Style = on ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;

        if (!on)
        {
            ApplyBar();
        }

        _bar.Refresh();
    }
}

/// <summary>
/// 下载测速。取 0.4 秒一次的瞬时值再做滑动平均 ——
/// 直接用瞬时值的话数字会疯跳,用户反而觉得"是不是出问题了"。
/// </summary>
internal sealed class SpeedMeter
{
    private readonly Stopwatch _watch = new Stopwatch();
    private long _lastBytes;
    private double _lastSeconds;
    private double _speed;

    public void Reset()
    {
        _watch.Reset();
        _watch.Start();
        _lastBytes = 0;
        _lastSeconds = 0;
        _speed = 0;
    }

    /// <summary>喂进当前已收字节数,返回平滑后的速度(字节/秒)。</summary>
    public double Update(long bytes)
    {
        double now = _watch.Elapsed.TotalSeconds;
        double span = now - _lastSeconds;

        if (span >= 0.4)
        {
            double instant = (bytes - _lastBytes) / span;
            if (instant < 0)
            {
                instant = 0;
            }

            _speed = _speed <= 0 ? instant : (_speed * 0.6 + instant * 0.4);
            _lastBytes = bytes;
            _lastSeconds = now;
        }

        return _speed;
    }

    public double Speed
    {
        get { return _speed; }
    }
}
