using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DshInstaller.Shared.Detection;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 内置的安装步骤。
    ///
    /// 每个工厂方法返回一个 <see cref="InstallStep"/>,执行器按顺序跑。
    /// 标题用 <see cref="SharedText.T"/> 出中英双语,所以界面层不用关心步骤名。
    /// </summary>
    public static class BuiltInSteps
    {
        public const string IdNode = "node";
        public const string IdDsh = "dsh";
        public const string IdLauncher = "launcher";
        public const string IdGit = "git";
        public const string IdPnpm = "pnpm";
        public const string IdPython = "python";
        public const string IdShortcut = "shortcut";
        public const string IdAutostart = "autostart";
        public const string IdVerify = "verify";
        public const string IdPath = "path";
        public const string IdDotNetRuntime = "runtime-dotnet";
        public const string IdWinAppRuntime = "runtime-winapprt";
        public const string IdUninstaller = "uninstaller";
        public const string IdLaunch = "launch";

        // 便携组件的默认版本。Node 会去查最新 LTS,这三个先用固定版本(改这里即可升级)。
        private const string DefaultGitVersion = "2.50.1";
        private const string DefaultPnpmVersion = "10.15.1";
        private const string DefaultPythonVersion = "3.12.8";

        // ---------------------------------------------------------------- 必装运行库
        //
        // 为什么是"必选步骤"而不是"打包进安装器":
        //   把 .NET 桌面运行时和 Windows App Runtime 一起打进安装包,包里就得多出 100 MB 左右,
        //   每个用户都得先下完这一大坨才能开始装 —— 而且大多数人机器上早就有了。
        //   所以改成:检测 → 缺了才下(几十 MB)→ 静默安装。安装包的体积因此回到十几 MB。
        //
        // 注意:运行库是**机器级**的,装它要管理员。要不要提权在向导提交那一刻就已经决定了
        //   (见 InstallSession.NeedsElevation),这里的步骤只负责"装"。

        public static InstallStep DotNetRuntime(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdDotNetRuntime,
                Title = SharedText.T(
                    "安装 .NET " + WellKnown.DotNetDesktopMajor + " 桌面运行时",
                    "Install the .NET " + WellKnown.DotNetDesktopMajor + " Desktop Runtime"),
                Required = true,
                Run = RunDotNetRuntimeAsync,
            };
        }

        private static async Task RunDotNetRuntimeAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;

            string present = RuntimeProbe.DotNetDesktopVersion;
            if (!string.IsNullOrEmpty(present))
            {
                context.Log(".NET 桌面运行时已存在:" + present);
                context.Report(SharedText.T("已就绪,跳过", "Already available, skipping"), 100);
                return;
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不下载也不安装", "Dry run, nothing downloaded or installed"), 100);
                return;
            }

            List<string> urls = MirrorSource.DotNetDesktopRuntimeUrls(
                o.SourcePreference, WellKnown.DotNetDesktopMajor);

            string installer = Path.Combine(o.TempRoot, WellKnown.DotNetRuntimeInstallerFile);
            context.Report(
                SharedText.T("准备中 · 下载 .NET " + WellKnown.DotNetDesktopMajor + " 桌面运行时",
                             "Preparing · downloading the .NET " + WellKnown.DotNetDesktopMajor + " Desktop Runtime"),
                3);

            await DownloadRuntimeAsync(context, token, urls, installer, 5, 70).ConfigureAwait(false);

            await InstallRuntimeAsync(context, token, installer, "/install /quiet /norestart",
                SharedText.T(".NET 桌面运行时", "the .NET Desktop Runtime"), 75, 96).ConfigureAwait(false);

            string after = RuntimeProbe.DotNetDesktopVersion;
            TryDelete(installer);

            if (string.IsNullOrEmpty(after))
            {
                throw new InvalidOperationException(SharedText.T(
                    "安装程序已结束,但仍未检测到 .NET 桌面运行时。请手动安装后重试。",
                    "The installer finished but the .NET Desktop Runtime is still not detected. Install it manually and try again."));
            }

            context.Log(".NET 桌面运行时已就位:" + after);
            context.Report(SharedText.T("完成", "Done") + " · " + after, 100);
        }

        public static InstallStep WinAppRuntime(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdWinAppRuntime,
                Title = SharedText.T("安装 Windows App Runtime 1.8", "Install Windows App Runtime 1.8"),
                Required = true,
                Run = RunWinAppRuntimeAsync,
            };
        }

        private static async Task RunWinAppRuntimeAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;

            string present = RuntimeProbe.WindowsAppRuntimeVersion;
            if (!string.IsNullOrEmpty(present))
            {
                context.Log("Windows App Runtime 已存在:" + present);
                context.Report(SharedText.T("已就绪,跳过", "Already available, skipping"), 100);
                return;
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不下载也不安装", "Dry run, nothing downloaded or installed"), 100);
                return;
            }

            List<string> urls = MirrorSource.WindowsAppRuntimeUrls(WellKnown.WindowsAppRuntimeVersion);

            string installer = Path.Combine(o.TempRoot, WellKnown.WindowsAppRuntimeInstallerFile);
            context.Report(SharedText.T("准备中 · 下载 Windows App Runtime",
                                        "Preparing · downloading Windows App Runtime"), 3);

            await DownloadRuntimeAsync(context, token, urls, installer, 5, 70).ConfigureAwait(false);

            // 官方给的静默开关就是 --quiet;它内部会去部署几个 MSIX 框架包,所以同样要管理员。
            await InstallRuntimeAsync(context, token, installer, "--quiet",
                SharedText.T("Windows App Runtime", "Windows App Runtime"), 75, 96).ConfigureAwait(false);

            string after = RuntimeProbe.WindowsAppRuntimeVersion;
            TryDelete(installer);

            if (string.IsNullOrEmpty(after))
            {
                throw new InvalidOperationException(SharedText.T(
                    "安装程序已结束,但仍未检测到 Windows App Runtime 1.8。请手动安装后重试。",
                    "The installer finished but Windows App Runtime 1.8 is still not detected. Install it manually and try again."));
            }

            context.Log("Windows App Runtime 已就位:" + after);
            context.Report(SharedText.T("完成", "Done") + " · " + after, 100);
        }

        /// <summary>下载运行库安装包(带进度)。运行库都在几百 MB 以下,断点续传交给下载引擎。</summary>
        private static async Task DownloadRuntimeAsync(
            InstallContext context,
            CancellationToken token,
            List<string> urls,
            string target,
            double fromPercent,
            double toPercent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));

            await Task.Run(delegate
            {
                DownloadEngine.Download(
                    urls, target,
                    delegate(DownloadProgress progress)
                    {
                        context.Report(DescribeDownload(progress),
                            fromPercent + progress.Fraction * (toPercent - fromPercent));
                    },
                    delegate { return token.IsCancellationRequested; },
                    delegate(string failedUrl)
                    {
                        context.Log("运行库下载源失败:" + failedUrl);
                        return true;
                    },
                    delegate(string notice)
                    {
                        context.Report(SharedText.T("下载中 · ", "Downloading · ") + notice);
                    });
            }, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// 跑运行库安装程序并核对退出码。
        ///
        /// 退出码口径(微软那套):0 成功、3010/1641 成功但要重启、1638 已经有别的版本了。
        /// 这三种都当成功 —— 真正的判据是**装完之后重新探测能不能看到**,
        /// 所以这里不做"退出码不认就直接判失败"的死板处理。
        /// </summary>
        private static async Task InstallRuntimeAsync(
            InstallContext context,
            CancellationToken token,
            string installer,
            string arguments,
            string displayName,
            double fromPercent,
            double toPercent)
        {
            if (!File.Exists(installer))
            {
                throw new InvalidOperationException(
                    SharedText.T("安装包未下载成功:", "The installer package was not downloaded: ") + installer);
            }

            context.Report(SharedText.T("安装中 · ", "Installing · ") + displayName, fromPercent);

            ProcessRunner.Result result = await Task.Run(
                delegate
                {
                    return ProcessRunner.Run(installer, arguments, null, 20 * 60 * 1000,
                        delegate(string line) { context.Log("  " + line); });
                }, token).ConfigureAwait(false);

            context.Log(displayName + " 安装退出码 = " + result.ExitCode);

            if (result.TimedOut)
            {
                throw new InvalidOperationException(
                    SharedText.T("安装超时(20 分钟):", "Installation timed out after 20 minutes: ") + displayName);
            }

            if (result.ExitCode != 0 && result.ExitCode != 3010
                && result.ExitCode != 1641 && result.ExitCode != 1638)
            {
                throw new InvalidOperationException(
                    SharedText.T("安装失败(退出码 ", "Installation failed (exit code ")
                    + result.ExitCode + "): " + displayName
                    + (string.IsNullOrWhiteSpace(result.Combined)
                        ? string.Empty
                        : " —— " + Tail(result.Combined, 6)));
            }

            context.Report(SharedText.T("完成", "Done") + " · " + displayName, toPercent);
        }

        // ---------------------------------------------------------------- Node

        public static InstallStep Node(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdNode,
                Title = SharedText.T("安装 Node.js", "Install Node.js"),
                Required = true,
                Run = RunNodeAsync,
            };
        }

        private static async Task RunNodeAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;
            string nodeDir = Path.Combine(o.ComponentsRoot, "node");
            string nodeExe = Path.Combine(nodeDir, "node.exe");

            if (File.Exists(nodeExe))
            {
                // 光有 node.exe 不算数 —— DSH 那一步真正要用的是 npm.cmd。
                // 上一次中断很可能只留下半个 node 目录(解压没完就断了),
                // 那时候这里一"跳过",后面就报"未找到 npm(Node 步骤应已先完成)",
                // 而日志上 Node 那步还显示成功,极难查(实测踩过)。
                if (File.Exists(Path.Combine(nodeDir, "npm.cmd")))
                {
                    context.Report(SharedText.T("已存在,跳过", "Already present, skipping"), 100);
                    context.Log("Node 已存在于 " + nodeExe + " ,跳过下载");
                    return;
                }

                context.Log("node.exe 在,但缺 npm.cmd,重新解压一份完整的");
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不实际下载", "Dry run, not downloading"), 100);
                return;
            }

            context.Report(SharedText.T("准备中 · 正在查询最新的 LTS 版本", "Preparing · resolving the latest LTS version"), 3);
            string version = await Task.Run(delegate
            {
                try
                {
                    return MirrorSource.ResolveLatestNodeVersion(o.SourcePreference, WellKnown.PreferredNodeMajor);
                }
                catch
                {
                    return null;
                }
            }, token).ConfigureAwait(false);

            if (string.IsNullOrEmpty(version))
            {
                version = "v22.13.0";
                context.Log("查不到最新版本,回退到 " + version);
            }

            string fileName = "node-" + version + "-win-x64.zip";
            // Node 有 5 个实测可用的源,交给引擎轮换
            List<string> urls = MirrorSource.NodeUrls(version, fileName, o.SourcePreference);

            string archive = Path.Combine(o.TempRoot, fileName);
            context.Report(SharedText.T("准备中 · 下载 Node " + version, "Preparing · downloading Node " + version), 5);

            await Task.Run(delegate
            {
                DownloadEngine.Download(
                    urls,
                    archive,
                    delegate(DownloadProgress progress)
                    {
                        context.Report(
                            DescribeDownload(progress),
                            5 + progress.Fraction * 65);
                    },
                    delegate { return token.IsCancellationRequested; },
                    delegate(string failedUrl)
                    {
                        context.Log("下载源失败:" + failedUrl);
                        return true;
                    },
                    delegate(string notice)
                    {
                        // 换源重试要让用户看见,否则他会以为卡住了
                        context.Report(SharedText.T("下载中 · ", "Downloading · ") + notice);
                    });
            }, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();
            context.Report(SharedText.T("安装中 · 正在解压", "Installing · extracting"), 75);

            string extractRoot = Path.Combine(o.TempRoot, "node-extract");
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, true);
            }

            ExtractionResult nodeExtract = await Task.Run(
                delegate { return ArchiveExtractor.ExtractDetailed(archive, extractRoot); }, token).ConfigureAwait(false);
            context.Log("解压 Node: " + nodeExtract.Describe());

            // 官方包解出来是一层 node-vX-win-x64,把它挪成 <组件目录>\node
            string inner = FindSingleDirectory(extractRoot);
            if (inner == null)
            {
                throw new InvalidOperationException(
                    SharedText.T("解压后未找到 node 目录", "The extracted archive has no node directory"));
            }

            bool componentsExisted = Directory.Exists(o.ComponentsRoot);
            Directory.CreateDirectory(o.ComponentsRoot);
            if (!componentsExisted)
            {
                context.NoteCreatedDirectory(o.ComponentsRoot);
            }

            if (Directory.Exists(nodeDir))
            {
                Directory.Delete(nodeDir, true);
            }

            Directory.Move(inner, nodeDir);
            TryDelete(extractRoot);
            TryDelete(archive);

            if (!File.Exists(nodeExe))
            {
                throw new InvalidOperationException(
                    SharedText.T("解压后没有 node.exe", "node.exe is missing after extraction"));
            }

            context.Log("Node 已就位:" + nodeExe);
            context.Report(SharedText.T("完成", "Done"), 100);
        }

        // ---------------------------------------------------------------- DSH 本体

        public static InstallStep Dsh(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdDsh,
                Title = SharedText.T("安装 DeepSeek Harness 本体", "Install the DeepSeek Harness core"),
                Required = true,
                Run = RunDshAsync,
            };
        }

        private static async Task RunDshAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;
            string marker = Path.Combine(o.DshRoot, WellKnown.DshMarker);

            if (o.UseExistingDsh && File.Exists(marker))
            {
                context.Report(SharedText.T("使用现有安装,跳过下载", "Using the existing installation"), 100);
                context.Log("本机已有 DSH,跳过安装:" + o.DshRoot);
                return;
            }

            if (File.Exists(marker))
            {
                context.Report(SharedText.T("已安装,跳过", "Already installed, skipping"), 100);
                context.Log("DSH 本体已存在:" + marker);
                return;
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不实际安装", "Dry run, not installing"), 100);
                return;
            }

            string npmCmd = FindNpm(o.ComponentsRoot);
            if (npmCmd == null)
            {
                throw new InvalidOperationException(
                    SharedText.T("未找到 npm(Node 步骤应已先完成)", "npm was not found (the Node step should run first)"));
            }

            bool dshExisted = Directory.Exists(o.DshRoot);
            Directory.CreateDirectory(o.DshRoot);
            if (!dshExisted)
            {
                context.NoteCreatedDirectory(o.DshRoot);
            }

            context.Report(SharedText.T("安装中 · 正在获取本体依赖（耗时较长）", "Installing · fetching the core dependencies (this may take a while)"), 20);

            string registry = MirrorSource.NpmRegistry(o.SourcePreference);
            // 包名必须整体加引号:版本范围里的 ^ 在 cmd.exe 里是转义字符,
            // 不加引号会被 cmd 吃掉,变成 "@deepseek-ai/dsh@0.1.5-rc.1" 之类的错版本 → npm 退出码 1。
            string arguments =
                "install \"" + WellKnown.DshPackage + "@" + WellKnown.DshPackageVersion + "\""
                + " --prefix \"" + o.DshRoot + "\""
                + " --registry " + registry
                + " --no-audit --no-fund --loglevel=error";

            context.Log("npm " + arguments);
            context.Log("node 目录:" + Path.Combine(o.ComponentsRoot, "node"));

            // npm 走一个临时 .cmd 包装:
            //   1. 显式 set PATH 带上便携版 node 目录 —— 否则 npm 的生命周期脚本
            //      (koffi 的 cnoke.cjs 就调 node)会报 "'node' is not recognized"(实测踩过);
            //   2. 顺便避开长命令行在 cmd 里的引号地狱。
            string nodeDir = Path.Combine(o.ComponentsRoot, "node");
            string wrapper = Path.Combine(o.TempRoot, "npm-install.cmd");
            Directory.CreateDirectory(o.TempRoot);

            System.Text.StringBuilder script = new System.Text.StringBuilder();

            // 头一行必须先切到 UTF-8 代码页,再写别的东西。原因有两个:
            //   1. .cmd 是 cmd.exe 按**当前代码页**逐行解码的。中文路径写在里面,
            //      在 GBK 系统上会被读成乱码 → npm 拿着一个不存在的目录去装,
            //      直接退出码 1(实测踩过,而且报错本身还是乱码,特别难查);
            //   2. 顺带让 npm 的输出也变成 UTF-8,和 ProcessRunner 那边的解码对上,
            //      日志里就不会再是一串方块。
            //
            // 注意 File.WriteAllText 这里**不能**用 Encoding.Default ——
            // 在 .NET Core/.NET 8 里它已经变成 UTF-8 了(不是 ANSI),正是它把路径写坏的。
            script.AppendLine("@echo off");
            script.AppendLine("chcp 65001 >nul");
            script.AppendLine("set \"PATH=" + nodeDir + ";%PATH%\"");
            script.AppendLine("\"" + npmCmd + "\" " + arguments);
            File.WriteAllText(wrapper, script.ToString(), new System.Text.UTF8Encoding(false));

            context.Log("node 目录:" + nodeDir);
            context.Log("包装脚本:" + wrapper);

            // npm 不给百分比,但 node_modules 的体积是个可靠的代理:
            // 实测装完约 223 MB,所以按"已获取多少 MB"报进度,比干转圈强得多。
            string modulesDir = Path.Combine(o.DshRoot, "node_modules");
            ProcessRunner.Result result;
            using (System.Threading.CancellationTokenSource pollCancel =
                System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task poller = Task.Run(async delegate
                {
                    while (!pollCancel.IsCancellationRequested)
                    {
                        try
                        {
                            double mb = DirectorySizeMb(modulesDir);
                            double percent = 20 + Math.Min(0.95, mb / ExpectedDshSizeMb) * 75;

                            // npm 前几十秒在解析依赖树,一个字节都不写 ——
                            // 这段时间一直显示 0.0 MB 会让人以为卡死了,所以换成"正在初始化"。
                            string detail;
                            if (mb < 0.5)
                            {
                                detail = SharedText.T(
                                    "安装中 · 正在初始化 npm，可能需要一些时间…",
                                    "Installing · initializing npm, this may take a while…");
                            }
                            else
                            {
                                detail = SharedText.T("安装中 · 正在部署 DSH 核心 ", "Installing · deploying the DSH core ")
                                    + mb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                                    + " / " + ExpectedDshSizeMb.ToString("0")
                                    + " MB";
                            }

                            context.Report(detail, percent);
                        }
                        catch
                        {
                        }

                        try
                        {
                            await Task.Delay(1200, pollCancel.Token).ConfigureAwait(false);
                        }
                        catch
                        {
                            break;
                        }
                    }
                }, token);

                ProcessRunner.Result runResult;
                try
                {
                    runResult = await Task.Run(delegate
                    {
                        return ProcessRunner.Run(
                            "cmd.exe",
                            "/c \"" + wrapper + "\"",
                            o.DshRoot,
                            1800000,
                            delegate(string line)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    context.Log("  " + line.Trim());
                                }
                            },
                            nodeDir);
                    }, token).ConfigureAwait(false);
                }
                finally
                {
                    pollCancel.Cancel();
                }

                result = runResult;
                try
                {
                    await poller.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (!result.Ok)
            {
                throw new InvalidOperationException(
                    SharedText.T("npm 安装失败(退出码 " + result.ExitCode + ")", "npm failed (exit code " + result.ExitCode + ")")
                    + Environment.NewLine + Tail(result.Combined, 12));
            }

            if (!File.Exists(marker))
            {
                throw new InvalidOperationException(
                    SharedText.T("npm 已结束,但未找到 DSH 本体", "npm finished but the DSH core was not found")
                    + ":" + marker);
            }

            context.Log("DSH 本体已安装:" + o.DshRoot);
            context.Report(SharedText.T("完成", "Done"), 100);
        }

        // ---------------------------------------------------------------- 启动器

        public static InstallStep Launcher(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdLauncher,
                Title = SharedText.T("部署启动器", "Deploy the launcher"),
                Required = true,
                Run = RunLauncherAsync,
            };
        }

        private static async Task RunLauncherAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;
            string launcherExe = Path.Combine(o.LauncherRoot, WellKnown.LauncherExe);

            if (File.Exists(launcherExe))
            {
                context.Report(SharedText.T("已存在,跳过", "Already present, skipping"), 100);
                context.Log("启动器已存在:" + launcherExe);
                return;
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不实际部署", "Dry run, not deploying"), 100);
                return;
            }

            context.Report(SharedText.T("准备中 · 正在查询启动器版本", "Preparing · resolving the launcher release"), 5);

            LauncherRelease release = await Task.Run(delegate
            {
                try
                {
                    return LauncherFeed.Fetch(
                        WellKnown.LauncherRepository,
                        o.SourcePreference,
                        WellKnown.LauncherBranch);
                }
                catch (Exception exception)
                {
                    context.Log("清单拉取失败:" + exception.Message);
                    return null;
                }
            }, token).ConfigureAwait(false);

            if (release == null || release.Urls == null || release.Urls.Count == 0)
            {
                throw new InvalidOperationException(
                    SharedText.T("未获取到启动器下载地址", "No launcher download URL is available"));
            }

            context.Log("启动器版本 " + release.Version + ",候选源 " + release.Urls.Count + " 个");

            // 加速线路下把 npmmirror 那条**排最前** —— 它是所有候选里最快的
            // (实测 9.8 MB/s,而 GitHub 系只有几十到一百多 KB/s)。
            // 地址由版本号拼出来,所以以后启动器发新版,这里不用动。
            List<string> urls = new List<string>(release.Urls);
            if (MirrorSource.IsChina(o.SourcePreference) && !string.IsNullOrEmpty(release.Version))
            {
                string npm = LauncherFeed.NpmMirrorUrl(release.Version);
                if (!urls.Contains(npm))
                {
                    urls.Insert(0, npm);
                }

                // 万一正好赶上"刚发版、npmmirror 还没同步"这个空窗期:
                // 先问一句,没同步就催它一下、等一会儿(有上限)。
                // 主要防线其实是发版流程(同步好之前不更新清单),这里只是兜底 ——
                // 毕竟用户不该为"发布节奏"买单。
                await EnsureNpmMirrorReady(context, release.Version, token).ConfigureAwait(false);
            }

            // 扩展名故意写成 .bin:候选里既有 npm 的 .tgz 也有 GitHub 的 .zip,
            // 到底拿到哪种得看内容(下面 MaterializeLauncherArchive),不能靠后缀猜。
            string downloaded = Path.Combine(o.TempRoot, "launcher-" + release.Version + ".bin");
            context.Report(SharedText.T("准备中 · 下载启动器 " + release.Version, "Preparing · downloading the launcher " + release.Version), 10);

            await Task.Run(delegate
            {
                DownloadEngine.Download(
                    urls,
                    downloaded,
                    delegate(DownloadProgress progress)
                    {
                        context.Report(
                            DescribeDownload(progress),
                            10 + progress.Fraction * 65);
                    },
                    delegate { return token.IsCancellationRequested; },
                    delegate(string failedUrl)
                    {
                        context.Log("下载源失败:" + failedUrl);
                        return true;
                    },
                    delegate(string notice)
                    {
                        // 换源重试要让用户看见,否则他会以为卡住了
                        context.Report(SharedText.T("下载中 · ", "Downloading · ") + notice);
                    });
            }, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();

            // 拿到的是 npm 的 tgz 还是 GitHub 的 zip?按内容判断,不靠 URL 猜 ——
            // 引擎在候选之间轮换,轮到哪里都可能。
            string archive = MaterializeLauncherArchive(context, downloaded, release.Version);

            if (!string.IsNullOrEmpty(release.Sha256))
            {
                string actual = HashFile(archive);
                if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        SharedText.T("启动器包校验失败", "Launcher package checksum mismatch")
                        + ": " + actual + " != " + release.Sha256);
                }

                context.Log("校验通过:" + actual.Substring(0, 16) + "…");
            }

            context.Report(SharedText.T("安装中 · 正在解压", "Installing · extracting"), 80);

            bool launcherExisted = Directory.Exists(o.LauncherRoot);
            Directory.CreateDirectory(o.LauncherRoot);
            if (!launcherExisted)
            {
                context.NoteCreatedDirectory(o.LauncherRoot);
            }

            ExtractionResult launcherExtract = await Task.Run(
                delegate { return ArchiveExtractor.ExtractDetailed(archive, o.LauncherRoot); }, token).ConfigureAwait(false);
            context.Log("解压启动器: " + launcherExtract.Describe());

            TryDelete(archive);

            if (!File.Exists(launcherExe))
            {
                throw new InvalidOperationException(
                    SharedText.T("解压后未找到启动器主程序", "The launcher executable is missing after extraction")
                    + ":" + launcherExe);
            }

            context.Log("启动器已部署:" + o.LauncherRoot);

            // 顺手写一份 launcher.json,告诉启动器"DSH 在哪、node 在哪"。
            //
            // 为什么非写不可:启动器找 node 的第一顺位是环境变量 **DSH_NODE**、
            // 第二顺位就是同目录这份 launcher.json,最后才轮到 PATH。
            // 而 PATH 是安装过程中改的 —— **已经在跑的进程不会重新读环境变量**,
            // 装完立刻点桌面快捷方式(资源管理器那条链还是旧环境),
            // 启动器就会弹"没有找到 Node.js,说明安装没走完"(实测踩过,而且冤枉)。
            // 落一份文件不受这个影响,重启之后也照样管用。
            try
            {
                string dshRoot = (o.DshRoot ?? string.Empty).Replace("\\", "\\\\");
                string nodeExe = Path.Combine(o.ComponentsRoot, "node", "node.exe").Replace("\\", "\\\\");

                string configPath = Path.Combine(o.LauncherRoot, "launcher.json");
                string json = "{\r\n  \"dshRoot\": \"" + dshRoot + "\",\r\n  \"nodePath\": \""
                    + nodeExe + "\"\r\n}\r\n";

                File.WriteAllText(configPath, json, new System.Text.UTF8Encoding(false));
                context.Log("已写入 " + configPath);
            }
            catch (Exception exception)
            {
                context.Log("写 launcher.json 失败(启动器仍可按 PATH 找 node):" + exception.Message);
            }

            context.Report(SharedText.T("完成", "Done"), 100);
        }

        // ---------------------------------------------------------------- 可选:Git

        public static InstallStep Git(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdGit,
                Title = SharedText.T("安装 Git(便携版)", "Install Git (portable)"),
                Required = false,
                Run = RunGitAsync,
            };
        }

        private static async Task RunGitAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;
            string gitDir = Path.Combine(o.ComponentsRoot, "git");
            string gitExe = Path.Combine(gitDir, "cmd", "git.exe");

            if (File.Exists(gitExe) || FindOnPath("git.exe") != null)
            {
                context.Report(SharedText.T("已就绪,跳过", "Already available, skipping"), 100);
                return;
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不实际下载", "Dry run, not downloading"), 100);
                return;
            }

            string fileName = "MinGit-" + DefaultGitVersion + "-64-bit.zip";
            // 按线路给候选:加速线路先试华为云 / npmmirror(实测 10 MB/s),
            // 官方线路就走 github(国内直连常常"积极拒绝",所以加速线路上还留了 ghproxy 兜底)。
            List<string> urls = MirrorSource.MinGitUrls(o.SourcePreference, DefaultGitVersion);

            string archive = Path.Combine(o.TempRoot, fileName);
            context.Report(SharedText.T("准备中 · 下载 MinGit", "Preparing · downloading MinGit"), 5);

            await Task.Run(delegate
            {
                DownloadEngine.Download(
                    urls, archive,
                    delegate(DownloadProgress progress)
                    {
                        context.Report(
                            DescribeDownload(progress),
                            5 + progress.Fraction * 75);
                    },
                    delegate { return token.IsCancellationRequested; },
                    null,
                    delegate(string notice)
                    {
                        context.Report(SharedText.T("下载中 · ", "Downloading · ") + notice);
                    });
            }, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();
            context.Report(SharedText.T("安装中 · 正在解压", "Installing · extracting"), 85);

            Directory.CreateDirectory(gitDir);
            ExtractionResult gitExtract = await Task.Run(
                delegate { return ArchiveExtractor.ExtractDetailed(archive, gitDir); }, token).ConfigureAwait(false);
            context.Log("解压 Git: " + gitExtract.Describe());
            TryDelete(archive);

            context.Log("Git 已解压到 " + gitDir);
            context.Report(SharedText.T("完成", "Done"), 100);
        }

        // ---------------------------------------------------------------- 可选:pnpm

        public static InstallStep Pnpm(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdPnpm,
                Title = SharedText.T("安装 pnpm(便携版)", "Install pnpm (portable)"),
                Required = false,
                Run = RunPnpmAsync,
            };
        }

        private static async Task RunPnpmAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;
            string pnpmDir = Path.Combine(o.ComponentsRoot, "pnpm");
            string pnpmExe = Path.Combine(pnpmDir, "pnpm.exe");

            if (File.Exists(pnpmExe) || FindOnPath("pnpm.cmd") != null || FindOnPath("pnpm.exe") != null)
            {
                context.Report(SharedText.T("已就绪,跳过", "Already available, skipping"), 100);
                return;
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不实际下载", "Dry run, not downloading"), 100);
                return;
            }

            Directory.CreateDirectory(pnpmDir);

            // pnpm 是唯一没现成镜像的组件:华为云 / npmmirror 的二进制目录里都没有它。
            // 但它的平台二进制发布在 npm 上(@pnpm/win-x64),npmmirror 有镜像且实测 9.8 MB/s,
            // 而 GitHub 那条只有 48 KB/s。所以加速线路下第一条候选是个 **tgz**。
            //
            // 那个 tgz 下下来之后要解出里面的 package/pnpm.exe —— 而候选列表里还混着
            // GitHub 的直链 exe,引擎轮换到哪条都可能。所以下完**按内容判断**:
            // gzip 头(1F 8B)就是 tgz,否则拿到的已经是 exe。
            bool china = MirrorSource.IsChina(o.SourcePreference);
            string tgzPath = Path.Combine(o.TempRoot, "pnpm-win-x64-" + DefaultPnpmVersion + ".tgz");
            string downloadTarget = china ? tgzPath : pnpmExe;
            List<string> urls = MirrorSource.PnpmUrls(o.SourcePreference, DefaultPnpmVersion);

            context.Report(SharedText.T("准备中 · 下载 pnpm", "Preparing · downloading pnpm"), 10);

            await Task.Run(delegate
            {
                DownloadEngine.Download(
                    urls, downloadTarget,
                    delegate(DownloadProgress progress)
                    {
                        context.Report(
                            DescribeDownload(progress),
                            10 + progress.Fraction * 85);
                    },
                    delegate { return token.IsCancellationRequested; },
                    null,
                    delegate(string notice)
                    {
                        context.Report(SharedText.T("下载中 · ", "Downloading · ") + notice);
                    });
            }, token).ConfigureAwait(false);

            if (china)
            {
                MaterializePnpm(context, downloadTarget, pnpmExe);
            }

            context.Log("pnpm 已就位:" + pnpmExe);
            context.Report(SharedText.T("完成", "Done"), 100);
        }

        /// <summary>
        /// 等 npmmirror 把这一版同步好。
        ///
        /// 为什么要等:npm 上是发出去了,但 npmmirror 要过一会儿才镜像到。
        /// 而安装器**只认 npmmirror 这条路**(其他候选都是 GitHub 系,几十 KB/s),
        /// 正好卡在空窗里的用户就会被慢一路。
        ///
        /// 等不到也不报错 —— 照常走后面的候选,慢总比装不上好。
        /// </summary>
        private static async Task EnsureNpmMirrorReady(
            InstallContext context, string version, CancellationToken token)
        {
            string url = LauncherFeed.NpmMirrorUrl(version);

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                bool ready = await Task.Run(delegate { return UrlIsReady(url); }, token)
                    .ConfigureAwait(false);

                if (ready)
                {
                    return;
                }

                // 催 npmmirror 按需同步(它确实有这个接口)
                RequestNpmSync();

                context.Log("npmmirror 还没这一版(第 " + attempt + "/4 次),催同步后等 5 秒");
                context.Report(SharedText.T(
                    "准备中 · 正在等待镜像同步", "Preparing · waiting for the mirror to catch up"), 8);

                try
                {
                    await Task.Delay(5000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            context.Log("npmmirror 迟迟没同步,改用后面的下载源(GitHub 系,会慢一些)");
        }

        /// <summary>探一下这个地址现在能不能下(HEAD 就够)。</summary>
        private static bool UrlIsReady(string url)
        {
            try
            {
                using (System.Net.Http.HttpClient client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(8);

                    using (System.Net.Http.HttpRequestMessage request =
                        new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url))
                    using (System.Net.Http.HttpResponseMessage response = client.Send(request))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>催 npmmirror 同步这个包(失败就算了,不影响主流程)。</summary>
        private static void RequestNpmSync()
        {
            try
            {
                using (System.Net.Http.HttpClient client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(8);

                    string api = "https://registry.npmmirror.com/-/package/"
                        + LauncherFeed.NpmPackage + "/syncs";

                    using (client.PutAsync(api, null).GetAwaiter().GetResult())
                    {
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 把下下来的东西变成一份**启动器 zip**。
        ///
        /// 两种可能:npmmirror 的 tgz(gzip 头 1F 8B,里面就是我们那个 zip),
        /// 或者引擎轮换后直接从 GitHub 拿到 zip。按内容判断。
        ///
        /// 顺带说明:清单里的 sha256 是**对 zip** 算的,所以从 tgz 解出来之后
        /// 再交给上面的校验,口径一致 —— 不用为 npm 那条路单独维护一份哈希。
        /// </summary>
        private static string MaterializeLauncherArchive(InstallContext context, string downloaded, string version)
        {
            bool isGzip = false;
            try
            {
                using (FileStream probe = File.OpenRead(downloaded))
                {
                    isGzip = probe.ReadByte() == 0x1F && probe.ReadByte() == 0x8B;
                }
            }
            catch
            {
            }

            if (!isGzip)
            {
                return downloaded;
            }

            string zip = Path.Combine(Path.GetDirectoryName(downloaded), "launcher-" + version + ".zip");
            context.Log("下到的是 npm 的 tgz,解出里面的 zip");

            using (FileStream file = File.OpenRead(downloaded))
            using (System.IO.Compression.GZipStream gzip = new System.IO.Compression.GZipStream(
                file, System.IO.Compression.CompressionMode.Decompress))
            using (System.Formats.Tar.TarReader reader = new System.Formats.Tar.TarReader(gzip))
            {
                System.Formats.Tar.TarEntry entry;
                while ((entry = reader.GetNextEntry()) != null)
                {
                    if (!entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    entry.ExtractToFile(zip, true);
                    TryDelete(downloaded);
                    return zip;
                }
            }

            throw new InvalidOperationException(
                SharedText.T("npm 包里没有找到启动器 zip", "The npm package did not contain a launcher zip"));
        }

        /// <summary>
        /// 把下下来的东西变成 <c>pnpm.exe</c>。
        ///
        /// 两种可能:拿到的是 npmmirror 的 tgz(gzip 头 1F 8B,里面是 package/pnpm.exe),
        /// 或者引擎轮换后拿到的已经是 GitHub 的 exe。按内容判断,不按 URL 猜。
        /// </summary>
        private static void MaterializePnpm(InstallContext context, string downloaded, string pnpmExe)
        {
            bool isGzip = false;
            try
            {
                using (FileStream probe = File.OpenRead(downloaded))
                {
                    isGzip = probe.ReadByte() == 0x1F && probe.ReadByte() == 0x8B;
                }
            }
            catch
            {
            }

            if (!isGzip)
            {
                context.Log("拿到的是 pnpm.exe 本体,直接就位");
                File.Copy(downloaded, pnpmExe, true);
                TryDelete(downloaded);
                return;
            }

            context.Log("从 tgz 里取 pnpm.exe");

            using (FileStream file = File.OpenRead(downloaded))
            using (System.IO.Compression.GZipStream gzip = new System.IO.Compression.GZipStream(
                file, System.IO.Compression.CompressionMode.Decompress))
            using (System.Formats.Tar.TarReader reader = new System.Formats.Tar.TarReader(gzip))
            {
                System.Formats.Tar.TarEntry entry;
                while ((entry = reader.GetNextEntry()) != null)
                {
                    if (!entry.Name.EndsWith("pnpm.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    entry.ExtractToFile(pnpmExe, true);
                    TryDelete(downloaded);
                    return;
                }
            }

            throw new InvalidOperationException(
                SharedText.T("pnpm 包里没有 pnpm.exe", "The pnpm archive did not contain pnpm.exe"));
        }

        // ---------------------------------------------------------------- 可选:Python

        public static InstallStep Python(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdPython,
                Title = SharedText.T("安装 Python(便携版)", "Install Python (portable)"),
                Required = false,
                Run = RunPythonAsync,
            };
        }

        private static async Task RunPythonAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;
            string pythonDir = Path.Combine(o.ComponentsRoot, "python");
            string pythonExe = Path.Combine(pythonDir, "python.exe");

            if (File.Exists(pythonExe))
            {
                context.Report(SharedText.T("已就绪,跳过", "Already available, skipping"), 100);
                return;
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不实际下载", "Dry run, not downloading"), 100);
                return;
            }

            string fileName = "python-" + DefaultPythonVersion + "-embed-amd64.zip";
            // 加速线路先试华为云(实测 15 MB/s)与 npmmirror;官方线路走 python.org。
            List<string> urls = MirrorSource.PythonUrls(o.SourcePreference, DefaultPythonVersion);

            string archive = Path.Combine(o.TempRoot, fileName);
            context.Report(SharedText.T("准备中 · 下载 Python", "Preparing · downloading Python"), 5);

            await Task.Run(delegate
            {
                DownloadEngine.Download(
                    urls, archive,
                    delegate(DownloadProgress progress)
                    {
                        context.Report(
                            DescribeDownload(progress),
                            5 + progress.Fraction * 75);
                    },
                    delegate { return token.IsCancellationRequested; },
                    null,
                    delegate(string notice)
                    {
                        context.Report(SharedText.T("下载中 · ", "Downloading · ") + notice);
                    });
            }, token).ConfigureAwait(false);

            token.ThrowIfCancellationRequested();
            context.Report(SharedText.T("安装中 · 正在解压", "Installing · extracting"), 85);

            Directory.CreateDirectory(pythonDir);
            ExtractionResult pyExtract = await Task.Run(
                delegate { return ArchiveExtractor.ExtractDetailed(archive, pythonDir); }, token).ConfigureAwait(false);
            context.Log("解压 Python: " + pyExtract.Describe());
            TryDelete(archive);

            context.Log("Python 已解压到 " + pythonDir);
            context.Report(SharedText.T("完成", "Done"), 100);
        }

        // ---------------------------------------------------------------- 卸载入口

        /// <summary>
        /// 安装器自己的长期落脚点 —— 卸载器以后靠它把安装器再拉起来。
        ///
        /// 注意它在**安装目录里面**(&lt;启动器目录&gt;\.installer),不是 %LOCALAPPDATA%。
        /// 以前图省事放在 `%LOCALAPPDATA%\DeepSeekHarness\Boot`,可那正是引导程序解压用的
        /// 临时目录 —— 用户看到"临时目录里堆了几十 MB"自然会想清掉(实测反馈),
        /// 而清了它独立卸载器就找不到安装器了。所以分开:临时的归临时,长期的归安装目录。
        /// </summary>
        public static string InstallerHomeFor(string dshRoot)
        {
            if (string.IsNullOrWhiteSpace(dshRoot))
            {
                return null;
            }

            return Path.Combine(dshRoot, ".installer");
        }

        public static InstallStep Uninstaller(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdUninstaller,
                Title = SharedText.T("部署卸载程序", "Deploy the uninstaller"),
                Required = true,
                Run = RunUninstallerAsync,
            };
        }

        /// <summary>
        /// 把"以后怎么卸载"这件事安排明白。
        ///
        /// 以前没有这一步:安装器跑在引导程序解压出来的临时目录里,装完就被清掉,
        /// 用户回头想卸载时机器上**根本没有安装器可跑**(实测踩过)。
        /// 所以现在要:
        ///   1. 把安装器自己留在 %LOCALAPPDATA%\DeepSeekHarness\Boot,不清理;
        ///   2. 往启动器目录放一个独立的 DSH-Uninstall.exe;
        ///   3. 在"应用和功能"里登记一条,UninstallString 指向那个独立卸载程序。
        /// </summary>
        private static Task RunUninstallerAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不部署卸载程序", "Dry run, uninstaller not deployed"), 100);
                return Task.CompletedTask;
            }

            string home = InstallerHomeFor(o.DshRoot);
            string current = AppContext.BaseDirectory.TrimEnd('\\');

            // 1) 把安装器自己保留下来。已经在那个目录里就不用复制了。
            if (!string.Equals(current, home.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                context.Report(SharedText.T("部署中 · 保留安装程序", "Deploying · keeping the installer"), 20);
                int copied = CopyDirectory(current, home);
                context.Log("安装程序已保留到 " + home + "(" + copied + " 个文件)");
                current = home;
            }
            else
            {
                context.Log("安装程序本来就在 " + home + ",无需复制");
            }

            string installerExe = Path.Combine(home, "DSH-Installer.exe");
            if (!File.Exists(installerExe))
            {
                context.Log("警告:保留目录里没有 DSH-Installer.exe,以后只能用安装包卸载");
            }

            // 2) 独立卸载程序:放在 **DSH 根目录**下,用户翻目录时一眼就能找到
            //    (启动器是子目录,卸载器放那儿反而不好找)。
            string uninstallerSource = Path.Combine(home, WellKnown.UninstallerExe);
            string uninstallerTarget = Path.Combine(o.DshRoot, WellKnown.UninstallerExe);

            if (File.Exists(uninstallerSource))
            {
                context.Report(SharedText.T("部署中 · 放置卸载程序", "Deploying · placing the uninstaller"), 60);
                try
                {
                    Directory.CreateDirectory(o.DshRoot);
                    File.Copy(uninstallerSource, uninstallerTarget, true);
                    context.Log("卸载程序已放到 " + uninstallerTarget);
                }
                catch (Exception exception)
                {
                    // 拷不过去不影响主流程:注册表那条照样能用 home 里的那份
                    context.Log("把卸载程序复制到 DSH 目录失败:" + exception.Message);
                }
            }
            else
            {
                context.Log("警告:包里没有 " + WellKnown.UninstallerExe + ",跳过部署");
            }

            // 3) 登记到"应用和功能"
            context.Report(SharedText.T("部署中 · 登记卸载项", "Deploying · registering the uninstall entry"), 85);
            try
            {
                string uninstallString = File.Exists(uninstallerSource)
                    ? uninstallerSource
                    : (File.Exists(uninstallerTarget) ? uninstallerTarget : null);

                if (string.IsNullOrEmpty(uninstallString))
                {
                    context.Log("没有可用的卸载程序,不登记卸载项");
                }
                else
                {
                    Microsoft.Win32.RegistryKey root = o.AllUsers
                        ? Microsoft.Win32.Registry.LocalMachine
                        : Microsoft.Win32.Registry.CurrentUser;

                    using (Microsoft.Win32.RegistryKey key =
                        root.CreateSubKey(WellKnown.RegistryUninstallKey))
                    {
                        if (key != null)
                        {
                            key.SetValue("DisplayName", WellKnown.ProductName);
                            key.SetValue("DisplayVersion", WellKnown.InstallerVersion);
                            key.SetValue("Publisher", "Deepseek / KitamaruRamito");
                            key.SetValue("InstallLocation", o.LauncherRoot ?? string.Empty);
                            key.SetValue("UninstallString", "\"" + uninstallString + "\"");
                            key.SetValue("QuietUninstallString", "\"" + uninstallString + "\" --silent");
                            key.SetValue("NoModify", 1);
                            key.SetValue("NoRepair", 1);

                            string displayIcon = Path.Combine(
                                o.LauncherRoot ?? string.Empty, WellKnown.LauncherExe);
                            if (File.Exists(displayIcon))
                            {
                                key.SetValue("DisplayIcon", displayIcon);
                            }

                            // 把**装到哪儿了**一并写进注册表。
                            //
                            // 为什么不能只靠 %LOCALAPPDATA% 那份 installer-state.json:
                            //   · 取消安装回滚时会把状态文件清掉;
                            //   · 装到一半失败时它压根没生成;
                            //   · 换个目录重装时,注册表这条会被重写,而状态文件是另一份。
                            // 结果就是用户想卸载时"没记录可读",卸载向导一项都不让勾(实测)。
                            // 注册表这条卸载项每次装都会重写,拿它当权威最省事。
                            key.SetValue("DshRoot", o.DshRoot ?? string.Empty);
                            key.SetValue("LauncherRoot", o.LauncherRoot ?? string.Empty);
                            key.SetValue("ComponentsRoot", o.ComponentsRoot ?? string.Empty);
                            key.SetValue("InstallerHome", home ?? string.Empty);

                            if (context.AddedPathEntries != null && context.AddedPathEntries.Count > 0)
                            {
                                key.SetValue("PathEntries", context.AddedPathEntries.ToArray());
                            }

                            context.Log("已登记卸载项(" + (o.AllUsers ? "HKLM" : "HKCU") + ")");
                        }
                    }

                    // 卸载程序靠这两条才能找到"装到哪儿了",所以要落到状态文件里
                    InstallerState state = ConfigStore.Load() ?? new InstallerState();
                    state.InstallerHome = home;
                    state.UninstallerPath = uninstallString;
                    ConfigStore.Save(state);
                }
            }
            catch (Exception exception)
            {
                context.Log("登记卸载项失败:" + exception.Message);
            }

            context.Report(SharedText.T("完成", "Done"), 100);
            return Task.CompletedTask;
        }

        /// <summary>把整个目录复制过去(覆盖同名文件)。返回复制的文件数。</summary>
        private static int CopyDirectory(string source, string target)
        {
            int count = 0;
            if (!Directory.Exists(source))
            {
                return 0;
            }

            Directory.CreateDirectory(target);

            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string relative = files[i].Substring(source.Length).TrimStart('\\');
                string destination = Path.Combine(target, relative);

                string directory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                try
                {
                    File.Copy(files[i], destination, true);
                    count++;
                }
                catch
                {
                    // 单个文件失败(被占用之类)不致命,继续复制其余的
                }
            }

            return count;
        }

        // ---------------------------------------------------------------- 启动

        /// <summary>
        /// 装完把启动器拉起来。失败**不算安装失败** —— 启动是锦上添花,
        /// 用户点桌面快捷方式照样能起来,不该为它回滚一整套装好的东西。
        /// </summary>
        public static InstallStep LaunchLauncher(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdLaunch,
                Title = SharedText.T("启动 DeepSeek Harness", "Start DeepSeek Harness"),
                Required = false,
                Run = RunLaunchAsync,
            };
        }

        private static Task RunLaunchAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不启动", "Dry run, not starting"), 100);
                return Task.CompletedTask;
            }

            string exe = Path.Combine(o.LauncherRoot ?? string.Empty, WellKnown.LauncherExe);
            if (!File.Exists(exe))
            {
                context.Log("没找到启动器,跳过启动:" + exe);
                context.Report(SharedText.T("跳过(没有启动器)", "Skipped (no launcher)"), 100);
                return Task.CompletedTask;
            }

            context.Report(SharedText.T("正在启动…", "Starting…"), 50);

            try
            {
                // UseShellExecute = true:启动器是 requireAdministrator,
                // 需要走 shell 才能正确触发它的清单(该提权就提权)。
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = o.LauncherRoot,
                    UseShellExecute = true,
                };

                System.Diagnostics.Process.Start(info);
                context.Log("已启动:" + exe);
                context.Report(SharedText.T("完成", "Done"), 100);
            }
            catch (Exception exception)
            {
                // 起不来也不影响安装结果,用户点快捷方式一样能起来
                context.Log("启动失败(不影响安装):" + exception.Message);
                context.Report(SharedText.T("启动失败,可手动打开", "Could not start; open it manually"), 100);
            }

            return Task.CompletedTask;
        }


        public static InstallStep Shortcut(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdShortcut,
                Title = SharedText.T("创建快捷方式", "Create shortcuts"),
                Required = false,
                Run = RunShortcutAsync,
            };
        }

        private static Task RunShortcutAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;

            if (!o.CreateDesktopShortcut && !o.CreateStartMenuShortcut)
            {
                context.Report(SharedText.T("未勾选,跳过", "Not selected, skipping"), 100);
                return Task.CompletedTask;
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不实际创建", "Dry run, not creating"), 100);
                return Task.CompletedTask;
            }

            string launcherExe = Path.Combine(o.LauncherRoot, WellKnown.LauncherExe);
            string link = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                WellKnown.ProductName + ".lnk");

            if (o.CreateDesktopShortcut)
            {
                bool ok = ShellLink.Create(new ShellLink.ShortcutSpec
                {
                    Path = link,
                    Target = launcherExe,
                    Arguments = WellKnown.SilentArgument,
                    WorkingDirectory = o.LauncherRoot,
                    Description = WellKnown.ProductName,
                    IconLocation = launcherExe,
                });

                if (!ok)
                {
                    throw new InvalidOperationException(
                        SharedText.T("桌面快捷方式创建失败", "Failed to create the desktop shortcut"));
                }

                context.CreatedShortcut = true;
                context.Log("已创建桌面快捷方式:" + link);
            }

            // 开始菜单:让用户能在"所有应用"里搜到它。
            // 机器范围放公共菜单(%PROGRAMDATA%),否则放当前用户的菜单 ——
            // 装给所有用户却把快捷方式塞进某个人的菜单里,别人是看不到的。
            if (o.CreateStartMenuShortcut)
            {
                try
                {
                    string programs = o.AllUsers
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                            WellKnown.ProductName)
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                            WellKnown.ProductName);

                    Directory.CreateDirectory(programs);

                    string startLink = Path.Combine(programs, WellKnown.ProductName + ".lnk");
                    bool startOk = ShellLink.Create(new ShellLink.ShortcutSpec
                    {
                        Path = startLink,
                        Target = launcherExe,
                        Arguments = WellKnown.SilentArgument,
                        WorkingDirectory = o.LauncherRoot,
                        Description = WellKnown.ProductName,
                        IconLocation = launcherExe,
                    });

                    if (startOk)
                    {
                        context.CreatedShortcut = true;
                        context.Log("已创建开始菜单快捷方式:" + startLink);
                    }
                    else
                    {
                        context.Log("开始菜单快捷方式创建失败(不影响其余部分)");
                    }
                }
                catch (Exception exception)
                {
                    context.Log("写开始菜单失败(不影响其余部分):" + exception.Message);
                }
            }

            context.Report(SharedText.T("完成", "Done"), 100);
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------- 开机自启

        public static InstallStep Autostart(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdAutostart,
                Title = SharedText.T("配置开机自启动", "Configure startup on boot"),
                Required = false,
                Run = RunAutostartAsync,
            };
        }

        private static Task RunAutostartAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;

            if (!o.EnableAutostart)
            {
                context.Report(SharedText.T("未勾选,跳过", "Not selected, skipping"), 100);
                return Task.CompletedTask;
            }

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不实际注册", "Dry run, not registering"), 100);
                return Task.CompletedTask;
            }

            string launcherExe = Path.Combine(o.LauncherRoot, WellKnown.LauncherExe);
            if (!File.Exists(launcherExe))
            {
                throw new InvalidOperationException(
                    SharedText.T("未找到启动器,无法配置开机自启", "The launcher is missing, cannot configure startup"));
            }

            // 为什么要用计划任务而不是 Run 键/启动文件夹:
            // 启动器的清单是 requireAdministrator,而 Windows 在登录阶段不弹 UAC,
            // 那种方式拉起来的进程会被静默拦掉,自启等于没写。
            // 计划任务用 RunLevel=Highest + LogonType=Interactive,由任务计划服务带管理员令牌启动。
            string script =
                "$ErrorActionPreference='Stop';" +
                "$exe = " + Quote(launcherExe) + ";" +
                "$arg = " + Quote(WellKnown.SilentArgument) + ";" +
                "$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name;" +
                "$action = New-ScheduledTaskAction -Execute $exe -Argument $arg;" +
                "$trigger = New-ScheduledTaskTrigger -AtLogon -User $user;" +
                "$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest;" +
                "$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable;" +
                "Register-ScheduledTask -TaskName " + Quote(WellKnown.AutostartTaskName)
                + " -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null;" +
                "Write-Output 'OK'";

            context.Report(SharedText.T("配置中 · 正在注册计划任务", "Configuring · registering the scheduled task"), 50);

            string output = PowerShellScript.Run(script, 60000);
            if (string.IsNullOrEmpty(output) || output.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    SharedText.T("计划任务注册失败,可能是权限不足", "Registering the scheduled task failed; administrator rights may be required")
                    + (string.IsNullOrEmpty(output) ? string.Empty : " -> " + output.Trim()));
            }

            context.RegisteredAutostart = true;
            context.Log("已注册开机自启计划任务:" + WellKnown.AutostartTaskName);
            context.Report(SharedText.T("完成", "Done"), 100);
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------- PATH

        public static InstallStep PathStep(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdPath,
                Title = SharedText.T("写入环境变量 PATH", "Update the PATH environment variable"),
                Required = false,
                Run = RunPathAsync,
            };
        }

        private static Task RunPathAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,不改环境变量", "Dry run, PATH left unchanged"), 100);
                return Task.CompletedTask;
            }

            // 便携组件默认不在 PATH 里,外面命令行敲 node / git / pnpm 会找不到,
            // DSH 的部分插件也会因此失败。这里把它们加进去。
            // Python 故意不加:embeddable 包里的 python.exe 会盖掉用户自己装的 Python。
            List<string> wanted = new List<string>();
            if (o.InstallNode)
            {
                wanted.Add(Path.Combine(o.ComponentsRoot, "node"));
            }

            if (o.InstallGit)
            {
                wanted.Add(Path.Combine(o.ComponentsRoot, "git", "cmd"));
            }

            if (o.InstallPnpm)
            {
                wanted.Add(Path.Combine(o.ComponentsRoot, "pnpm"));
            }

            if (wanted.Count == 0)
            {
                context.Report(SharedText.T("没有需要写 PATH 的组件", "No component needs a PATH entry"), 100);
                return Task.CompletedTask;
            }

            // 注意:Load() 在状态文件还不存在时返回 **null**,不能直接解引用(这里踩过 NRE)
            InstallerState state = ConfigStore.Load() ?? new InstallerState();
            if (state.PathEntries == null)
            {
                state.PathEntries = new List<string>();
            }

            int okCount = 0;
            for (int i = 0; i < wanted.Count; i++)
            {
                string directory = wanted[i];

                // 注意:**不要求目录当下存在**。
                // 可选组件按用户要求排在 PATH 之后,所以跑这一步时
                // <组件目录>\git\cmd 和 <组件目录>\pnpm 还可能没解出来。
                // 目录晚一点出现没关系(装完就在了),但要是这会儿跳过,
                // 用户就会遇到"勾了 Git 却在命令行里敲不出来"(实测踩过)。
                if (!Directory.Exists(directory))
                {
                    context.Log("目录还没出现,先照样写进 PATH:" + directory);
                }

                PathChangeResult result = o.AllUsers
                    ? PathEditor.AddToMachinePath(directory)
                    : PathEditor.AddToUserPath(directory);

                context.Log(result.Message);

                if (result.Ok)
                {
                    okCount++;
                    context.NotePathEntry(directory);

                    // 记下来给卸载器用(要还原 PATH 只能按这份清单)
                    if (!result.NoChange
                        && !state.PathEntries.Any(delegate(string x)
                        {
                            return string.Equals(
                                x == null ? null : x.TrimEnd('\\'),
                                directory.TrimEnd('\\'),
                                StringComparison.OrdinalIgnoreCase);
                        }))
                    {
                        state.PathEntries.Add(directory);
                    }
                }
                else
                {
                    // PATH 写不进去不算致命(可能只是太长),记一条日志继续
                    context.Log("  ↑ 这一条没写进去,不影响安装");
                }
            }

            try
            {
                ConfigStore.Save(state);
            }
            catch (Exception exception)
            {
                context.Log("状态文件写入失败:" + exception.Message);
            }

            context.Report(
                SharedText.T("已处理 " + okCount + " 项", "Processed " + okCount + " entries"),
                100);

            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------- 收尾校验

        public static InstallStep Verify(InstallOptions options)
        {
            return new InstallStep
            {
                Id = IdVerify,
                Title = SharedText.T("收尾检查", "Final checks"),
                Required = true,
                IsVerifier = true,
                Run = RunVerifyAsync,
            };
        }

        private static Task RunVerifyAsync(InstallContext context, CancellationToken token)
        {
            InstallOptions o = context.Options;

            if (o.DryRun)
            {
                context.Report(SharedText.T("演练模式,跳过检查", "Dry run, skipping checks"), 100);
                return Task.CompletedTask;
            }

            List<string> missing = new List<string>();

            if (o.InstallDsh && !File.Exists(Path.Combine(o.DshRoot, WellKnown.DshMarker)))
            {
                missing.Add(SharedText.T("DSH 本体", "DSH core") + " -> " + o.DshRoot);
            }

            if (o.InstallLauncher && !File.Exists(Path.Combine(o.LauncherRoot, WellKnown.LauncherExe)))
            {
                missing.Add(SharedText.T("启动器", "Launcher") + " -> " + o.LauncherRoot);
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    SharedText.T("以下项目未安装成功:", "The following were not installed: ")
                    + string.Join("; ", missing.ToArray()));
            }

            context.Log("校验通过");

            // 记下这次装到了哪里。卸载器完全依赖这份状态才知道该删什么 ——
            // 不能靠"按默认路径重新猜",用户可能把组件装到别处。
            try
            {
                InstallerState state = ConfigStore.Load() ?? new InstallerState();
                state.DshRoot = o.DshRoot;
                state.LauncherRoot = o.LauncherRoot;
                state.ComponentsRoot = o.ComponentsRoot;
                state.NodeRoot = Path.Combine(o.ComponentsRoot, "node");
                state.Scope = o.AllUsers ? "machine" : "user";
                state.Autostart = o.EnableAutostart;
                state.DesktopShortcut = o.CreateDesktopShortcut;
                state.InstallGit = o.InstallGit;
                state.InstallPnpm = o.InstallPnpm;
                state.InstallPython = o.InstallPython;
                state.InstalledAt = DateTime.Now.ToString("o");
                state.InstallerVersion = WellKnown.InstallerVersion;

                // PATH 那一步如果失败或没跑,这里兜个底,免得卸载时漏清
                if (context.AddedPathEntries.Count > 0)
                {
                    if (state.PathEntries == null)
                    {
                        state.PathEntries = new List<string>();
                    }

                    for (int i = 0; i < context.AddedPathEntries.Count; i++)
                    {
                        string entry = context.AddedPathEntries[i];
                        bool has = false;
                        for (int j = 0; j < state.PathEntries.Count; j++)
                        {
                            if (string.Equals(state.PathEntries[j], entry, StringComparison.OrdinalIgnoreCase))
                            {
                                has = true;
                                break;
                            }
                        }

                        if (!has)
                        {
                            state.PathEntries.Add(entry);
                        }
                    }
                }

                ConfigStore.Save(state);
                context.Log("已记录安装状态,供以后卸载使用");
            }
            catch (Exception exception)
            {
                context.Log("记录安装状态失败:" + exception.Message);
            }

            context.Report(SharedText.T("完成", "Done"), 100);
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>
        /// 按选项挑出这次要跑的步骤,顺序就是执行顺序。
        ///
        /// 排序原则:**必选在前,可选在后**。
        ///   - 运行库是硬前置 —— 少一个,启动器连窗口都出不来,所以排在最前面;
        ///   - 可选组件(Git / pnpm / Python)一律排在所有必选动作之后 ——
        ///     它们是"锦上添花",失败了也不该挡在"能用的 DSH"前面;
        ///   - 收尾校验永远最后,不然它检查的状态还没形成。
        /// </summary>
        public static List<InstallStep> BuildPlan(InstallOptions options)
        {
            List<InstallStep> plan = new List<InstallStep>();

            // ---- 必选:运行库 → Node → DSH 本体 → 启动器 → 卸载入口 → 快捷方式 / PATH / 自启
            if (options.InstallDotNetRuntime)
            {
                plan.Add(DotNetRuntime(options));
            }

            if (options.InstallWindowsAppRuntime)
            {
                plan.Add(WinAppRuntime(options));
            }

            if (options.InstallNode)
            {
                plan.Add(Node(options));
            }

            if (options.InstallDsh)
            {
                plan.Add(Dsh(options));
            }

            if (options.InstallLauncher)
            {
                plan.Add(Launcher(options));
            }

            if (options.DeployUninstaller)
            {
                plan.Add(Uninstaller(options));
            }

            // ---- 可选组件:必须排在 PATH **之前**。
            //   PATH 那一步要往环境变量里写 <组件目录>\git\cmd 和 <组件目录>\pnpm,
            //   所以这两个目录得先被解出来。(顺序排反过两次,别再动了。)
            if (options.InstallGit)
            {
                plan.Add(Git(options));
            }

            if (options.InstallPnpm)
            {
                plan.Add(Pnpm(options));
            }

            if (options.InstallPython)
            {
                plan.Add(Python(options));
            }

            // 有东西要写才排这一步。没东西写还列一行"没有需要写 PATH 的组件",
            // 用户只会以为漏了(实测反馈:这几项看着像写死的)。
            if (options.InstallNode || options.InstallGit || options.InstallPnpm)
            {
                plan.Add(PathStep(options));
            }
            // 没勾就不排这一步 —— 排进去只会写一行"未勾选,跳过",看着像漏了什么
            if (options.EnableAutostart)
            {
                plan.Add(Autostart(options));
            }

            // 快捷方式放在最后、紧挨着校验:它指向的是启动器,
            // 等所有东西都就位了再建,免得快捷方式先出来、目标却还没铺好。
            // 快捷方式:两份都没勾就别排这一步(同上,别让用户以为漏了)
            if (options.CreateDesktopShortcut || options.CreateStartMenuShortcut)
            {
                plan.Add(Shortcut(options));
            }
            plan.Add(Verify(options));

            // 「装完立即启动」**故意不做成一步**:
            // 启动与否由完成页那个复选框决定,而那一步在完成页之前就定好了 ——
            // 做成计划里的步骤,用户勾了没勾都照启(实测反馈)。见 DonePage.OnNext()。

            return plan;
        }

        /// <summary>
        /// 下载中的细节行,格式刻意做成"阶段 · 速度 · 已下/总量",
        /// 因为下载几十上百 MB 时总进度条走得慢,用户需要知道"在动、多快、还剩多少"。
        /// </summary>
        private static string DescribeDownload(DownloadProgress progress)
        {
            string speed = string.IsNullOrEmpty(progress.SpeedText) ? "--" : progress.SpeedText;
            return SharedText.T("下载中", "Downloading")
                + " · " + speed
                + " · " + DownloadProgress.FormatBytes(progress.ReceivedBytes)
                + " / " + DownloadProgress.FormatBytes(progress.TotalBytes);
        }
        /// <summary>DSH 本体装完大约这个体积(MB),用来把 npm 的体积增长折算成百分比。</summary>
        private const double ExpectedDshSizeMb = 230;

        /// <summary>递归算目录体积(MB)。算不出来返回 0,不抛。</summary>
        private static double DirectorySizeMb(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return 0;
                }

                long total = 0;
                string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        total += new FileInfo(files[i]).Length;
                    }
                    catch
                    {
                    }
                }

                return total / 1048576.0;
            }
            catch
            {
                return 0;
            }
        }
        private static string FindNpm(string componentsRoot)
        {
            string[] candidates = new string[]
            {
                Path.Combine(componentsRoot, "node", "npm.cmd"),
                Path.Combine(componentsRoot, "node", "npm"),
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            // 兜底:系统里可能有 Node
            return FindOnPath("npm.cmd");
        }

        private static string FindOnPath(string fileName)
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("Path") ?? string.Empty;
                string[] parts = path.Split(';');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(parts[i]))
                    {
                        continue;
                    }

                    try
                    {
                        string candidate = Path.Combine(parts[i].Trim(), fileName);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string FindSingleDirectory(string root)
        {
            if (!Directory.Exists(root))
            {
                return null;
            }

            string[] dirs = Directory.GetDirectories(root);
            if (dirs.Length == 1 && Directory.GetFiles(root).Length == 0)
            {
                return dirs[0];
            }

            return root;
        }

        private static string HashFile(string path)
        {
            try
            {
                using (System.Security.Cryptography.SHA256 sha =
                    System.Security.Cryptography.SHA256.Create())
                {
                    using (FileStream stream = File.OpenRead(path))
                    {
                        byte[] hash = sha.ComputeHash(stream);
                        System.Text.StringBuilder builder = new System.Text.StringBuilder(hash.Length * 2);
                        for (int i = 0; i < hash.Length; i++)
                        {
                            builder.Append(hash[i].ToString("x2"));
                        }

                        return builder.ToString();
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Tail(string text, int lines)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string[] all = text.Replace("\r\n", "\n").Split('\n');
            int take = Math.Min(lines, all.Length);
            return string.Join("\n", all.Skip(all.Length - take).ToArray());
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "''";
            }

            return "'" + value.Replace("'", "''") + "'";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }
}
