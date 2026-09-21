using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using DshInstaller.Shared.Install;

namespace DshInstaller
{
    /// <summary>
    /// 程序入口。
    ///
    /// 这是未打包的 WinUI3 应用,必须先初始化 Windows App Runtime 引导程序,
    /// 否则一访问 WinUI 类型就炸。失败时给一句人话提示,别让用户看裸异常。
    /// </summary>
    internal static class Program
    {
        private const uint WindowsAppSdkMajorMinor = 0x00010008; // 1.8

        [STAThread]
        private static void Main(string[] args)
        {
            DevOptions.Parse(args);

            // 提权实例:把落盘的安装选项读回来,并直接落到进度页开跑。
            // 这样用户在向导里填的东西不用重填,而且全程只有一次 UAC。
            if (DevOptions.Uninstall)
            {
                InstallSession.Current.Mode = SessionMode.Uninstall;
            }

            // 无人值守:跳过向导,直接进进度页开跑
            if (DevOptions.Silent)
            {
                // 静默模式同样要提权 —— 无人值守时没有"确认页"给用户点,
                // 不提权的话装到 Program Files 会直接失败(而且失败得莫名其妙)。
                // 判断依据和界面路径一致:装给所有用户、要注册开机自启、或者缺运行库。
                bool needAdmin = DevOptions.AllUsers || !DevOptions.NoAutostart
                    || (!DevOptions.NoRuntime && Shared.Detection.RuntimeProbe.AnyMissing);

                if (needAdmin && !Shared.ElevationHelper.IsElevated())
                {
                    string error;
                    if (Shared.ElevationHelper.StartElevatedWorker(
                        BuildElevationArguments(args), out error))
                    {
                        // 提权实例接手,本实例退出
                        return;
                    }

                    Shared.InstallLogger.Write("静默模式提权失败: " + error);
                }

                DevOptions.ForceStartPage((int)WizardPage.Progress);

                // 卸载模式不需要从命令行拼安装选项 ——
                // 卸载选项由进度页从安装状态文件里读(装到哪儿只有那份文件知道)。
                if (!DevOptions.Uninstall)
                {
                    PendingPlan = InstallerPlan.FromCommandLine();
                }
            }

            if (!string.IsNullOrEmpty(DevOptions.PlanPath))
            {
                InstallOptions plan = InstallerPlan.Load(DevOptions.PlanPath);
                if (plan != null)
                {
                    DevOptions.ForceStartPage((int)WizardPage.Progress);
                    PendingPlan = plan;
                }
            }

            // 未打包应用也可以在命令行上声明要用哪个版本,这里保持简单
            if (!TryInitializeRuntime(out string runtimeError))
            {
                ShowFatal(
                    "缺少 Windows App Runtime 1.8。\r\n\r\n"
                    + "安装程序本身也依赖它。请先安装:\r\n"
                    + "https://aka.ms/windowsappsdk/1.8/1.8.260804001/windowsappruntimeinstall-x64.exe\r\n\r\n"
                    + "详细信息:" + runtimeError);
                return;
            }

            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                LogStep("COM 包装初始化完成");
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, System.UnhandledExceptionEventArgs arguments)
                {
                    LogFatal("AppDomain 未处理异常", arguments.ExceptionObject as Exception);
                };

                Application.Start(delegate(ApplicationInitializationCallbackParams parameters)
                {
                    DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherQueueSynchronizationContext(dispatcherQueue));

                    try
                    {
                        LogStep("准备创建 App");
                        App app = new App();
                        LogStep("App 已创建");
                        _ = app;
                    }
                    catch (Exception exception)
                    {
                        LogFatal("创建 App 失败", exception);
                        throw;
                    }
                });
                LogStep("Application.Start 已返回");
            }
            catch (Exception exception)
            {
                LogFatal("启动失败", exception);
                ShowFatal("安装程序启动失败:\r\n" + Describe(exception));
            }
        }

        /// <summary>提权实例待执行的安装选项(非提权实例为 null)。</summary>
        internal static InstallOptions PendingPlan { get; private set; }

        /// <summary>把原始命令行原样拼回去,给提权实例用(它会再走一遍同样的解析)。</summary>
        private static string BuildElevationArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
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
        /// <summary>记一条进度,用来定位崩在哪一步。</summary>
        private static void LogStep(string message)
        {
            try
            {
                string directory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekHarness");
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(directory, "installer-crash.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                    new System.Text.UTF8Encoding(false));
            }
            catch
            {
            }
        }

        /// <summary>把启动期异常写到文件,别让它无声无息地消失(0xC000027B 那种)。</summary>
        private static void LogFatal(string stage, Exception exception)
        {
            try
            {
                string directory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekHarness");
                System.IO.Directory.CreateDirectory(directory);
                string path = System.IO.Path.Combine(directory, "installer-crash.log");
                System.IO.File.AppendAllText(
                    path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + "  [" + stage + "] " + Describe(exception)
                    + Environment.NewLine,
                    new System.Text.UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static string Describe(Exception exception)
        {
            if (exception == null)
            {
                return "(null)";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();

            // 连取异常信息都要兜底:这两步在极端情况下也会抛。
            //
            // 实测的坑:exception.StackTrace 要按元数据解析自定义特性,而这事需要
            // **程序集还在原地** —— 引导程序清理临时目录、或者程序被挪走之后,
            // 这里会抛 FileNotFoundException。以前没兜底,于是
            // "报错的过程本身把程序搞崩了":日志里只剩那句 FileNotFoundException,
            // 真正出了什么事一个字都看不到,APPCRASH 也是这么来的。
            try
            {
                builder.Append(exception.GetType().FullName);
            }
            catch
            {
                builder.Append("(取异常类型失败)");
            }

            try
            {
                if (!string.IsNullOrEmpty(exception.Message))
                {
                    builder.Append(": ").Append(exception.Message);
                }
            }
            catch
            {
            }

            try
            {
                builder.Append(Environment.NewLine).Append(exception.StackTrace);
            }
            catch
            {
                builder.Append(Environment.NewLine).Append("(取调用栈失败)");
            }

            try
            {
                if (exception.InnerException != null)
                {
                    builder.Append(Environment.NewLine).Append("  <- ").Append(Describe(exception.InnerException));
                }
            }
            catch
            {
            }

            return builder.ToString();
        }

        private static bool TryInitializeRuntime(out string error)
        {
            error = null;

#if SELF_CONTAINED_WINAPPSDK
            // 自包含发布:Windows App SDK 的运行时就在程序自己的目录里,
            // 不需要(也不能)再去系统里找已安装的版本。
            //
            // 这一步以前没关,自包含包一启动就 0xC000027B(STATUS_STOWED_EXCEPTION),
            // 在它自己目录里直接跑也一样崩 —— 查了半天才发现是 bootstrap 的锅(实测)。
            return true;
#else
            try
            {
                Bootstrap.Initialize(WindowsAppSdkMajorMinor);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
#endif
        }

        private static void ShowFatal(string message)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    message,
                    "大肥鱼Go安装程序",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch
            {
                Console.Error.WriteLine(message);
            }
        }
    }
}
