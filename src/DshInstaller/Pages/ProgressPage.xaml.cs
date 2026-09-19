using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Controls;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 安装进度页。
    ///
    /// 它只是"显示层":真正的安装在 <see cref="InstallRunner"/> 里顺序执行,
    /// 这里负责把界面上的选择翻译成安装计划、把事件 marshal 回 UI 线程、把日志写进抽屉。
    /// </summary>
    public sealed partial class ProgressPage : Page, IWizardPage, IWizardPageFooterAction, IWizardPageCloseGuard
    {
        private readonly List<ComponentRow> _rows = new List<ComponentRow>();
        private readonly DispatcherQueue _dispatcher;
        private readonly List<string> _log = new List<string>();

        private InstallRunner _runner;
        private CancellationTokenSource _cancellation;
        private int _totalSteps;
        private string _planPath;
        private InstallOptions _options;
        private InstallContext _context;
        private bool _cancelled;
        private bool _finished;
        private bool _exitAfterRollback;
        private double _lastPercent;

        /// <summary>日志抽屉滚到底,像终端一样。不滚的话新行全在视口外面。</summary>
        private void ScrollLogToEnd()
        {
            try
            {
                LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null, true);
            }
            catch
            {
            }
        }

        public ProgressPage()
        {
            InitializeComponent();
            _dispatcher = DispatcherQueue.GetForCurrentThread();
            ApplyText();
            Loaded += OnLoaded;
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            return true;
        }

        private void ApplyText()
        {
            bool uninstalling = InstallSession.Current.Mode == SessionMode.Uninstall;
            Scaffold.Title = uninstalling
                ? Localization.T("uninstall.progress.title")
                : Localization.T("progress.title");
            Scaffold.Subtitle = uninstalling
                ? Localization.T("uninstall.progress.subtitle")
                : Localization.T("progress.subtitle");
            if (uninstalling)
            {
                // 卸载流程的步骤条只有三格
                Scaffold.SetSteps(3, 2);
            }
            else
            {
                Scaffold.SetStep(6);
            }

            LogExpander.Header = Localization.T("progress.showlog");
            CurrentTaskText.Text = Localization.T("progress.preparing");
            CurrentDetailText.Text = string.Empty;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_runner != null)
            {
                return;
            }

            // 提权实例走这条路:选项是命令行里那个 plan 文件带的,不是内存里拼的
            InstallOptions options = Program.PendingPlan ?? InstallerPlan.Build();
            _options = options;
            _planPath = DevOptions.PlanPath;

            // 卸载与安装共用这套进度显示,只是计划不一样
            bool uninstalling = InstallSession.Current.Mode == SessionMode.Uninstall;
            List<InstallStep> plan = uninstalling
                ? UninstallSteps.BuildPlan(EnsureUninstallOptions())
                : BuiltInSteps.BuildPlan(options);

            _cancellation = new CancellationTokenSource();
            InstallContext context = new InstallContext(
                options,
                _cancellation.Token,
                AppendLog,
                delegate(string detail, double percent) { });

            _context = context;
            _runner = new InstallRunner(plan, context);

            // 把外部命令的执行也送进日志抽屉 —— "执行了什么"用户看得见才放心
            ProcessRunner.CommandObserver = delegate(string command)
            {
                AppendLog(Localization.T("log.exec") + " " + command);
            };
            _runner.StepStateChanged += OnStepStateChanged;
            _runner.StepDetail += OnStepDetailEvent;

            BuildRows(plan);
            _ = RunAsync();
        }

        /// <summary>把界面上的选择翻译成后端认识的选项。</summary>
        private InstallOptions BuildOptions()
        {
            InstallSession session = InstallSession.Current;
            ProbeReport report = session.Report;

            InstallOptions options = new InstallOptions
            {
                AllUsers = session.Scope == InstallScope.AllUsers,
                ComponentsRoot = session.ComponentsRoot,
                DshRoot = session.DshRoot,
                LauncherRoot = session.LauncherRoot,
                UseExistingDsh = session.UseExistingDsh,
                SourcePreference = session.SourcePreference,
                InstallGit = session.InstallGit,
                InstallPnpm = session.InstallPnpm,
                InstallPython = session.InstallPython,
                CreateDesktopShortcut = session.CreateDesktopShortcut,
                EnableAutostart = session.EnableAutostart,
                LaunchAfterwards = session.LaunchAfterwards,
                DryRun = DevOptions.DryRun,
                TempRoot = Path.Combine(Path.GetTempPath(), "DSH-Installer"),
            };

            // 缺 Node 才装。检测结果没有(例如直接跳页过来)时按"要装"处理。
            ComponentStatus node = report == null ? null : report["node"];
            options.InstallNode = node == null || !node.IsSatisfied;

            // 已经确认"用现有的"就不重装本体
            options.InstallDsh = !session.UseExistingDsh;
            options.InstallLauncher = true;

            // 目录没填时给个兜底,免得 Validate 直接失败
            if (string.IsNullOrWhiteSpace(options.ComponentsRoot))
            {
                options.ComponentsRoot = Path.Combine(session.DefaultRoot, "components");
            }

            if (string.IsNullOrWhiteSpace(options.DshRoot))
            {
                // 和 InstallerPlan 保持一致:直接用安装根目录,不再套一层 "DSH"
                options.DshRoot = session.DefaultRoot;
            }

            if (string.IsNullOrWhiteSpace(options.LauncherRoot))
            {
                options.LauncherRoot = Path.Combine(options.DshRoot, WellKnown.LauncherFolder);
            }

            return options;
        }

        /// <summary>卸载选项没准备好时(例如直接跳页过来)从状态文件兜底。</summary>
        private UninstallOptions EnsureUninstallOptions()
        {
            InstallSession session = InstallSession.Current;
            if (session.UninstallOptions == null)
            {
                InstallerState state = ConfigStore.Load();
                session.UninstallOptions = UninstallOptions.FromState(
                    state, session.Scope == InstallScope.AllUsers);
                session.UninstallOptions.UserDataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
            }

            // 必须把演练标记传下去。不然 --dry-run 时卸载**真的会删东西** ——
            // 预览界面变成了真卸载,这个坑很致命。
            session.UninstallOptions.DryRun = DevOptions.DryRun;

            return session.UninstallOptions;
        }

        private void BuildRows(List<InstallStep> plan)
        {
            RowsHost.Children.Clear();
            _rows.Clear();
            _totalSteps = plan.Count;

            for (int index = 0; index < plan.Count; index++)
            {
                ComponentRow row = new ComponentRow { Name = plan[index].Title };
                row.SetSingleLine();
                row.SetState(RowState.Pending, Localization.T("state.pending"));
                _rows.Add(row);
                RowsHost.Children.Add(row);
            }
        }

        // ---------------------------------------------------------------- 跑

        private async Task RunAsync()
        {
            AppendLog(DevOptions.DryRun
                ? Localization.T("progress.dryrun")
                : Localization.T("progress.begin"));

            InstallReport report;
            try
            {
                report = await Task.Run(delegate { return _runner.RunAsync(); });
            }
            catch (Exception exception)
            {
                AppendLog(Localization.T("progress.crashed") + " " + exception.Message);
                report = new InstallReport
                {
                    Succeeded = false,
                    Error = exception.Message,
                };
            }

            InstallSession.Current.Result = report;

            // 失败或取消都要回滚 —— 把这次装出来的东西统统撤掉,别留半拉子状态。
            // (撤销范围由各步骤登记,只撤"本次新建"的东西,用户原有的目录不碰。)
            if (!report.Succeeded || report.Cancelled || _cancelled)
            {
                await RunRollbackAsync();
            }

            _finished = true;
            RefreshFooter();

            if (!string.IsNullOrEmpty(_planPath))
            {
                InstallerPlan.Cleanup(_planPath);
            }

            SetCurrent(
                report.Succeeded ? Localization.T("progress.alldone") : Localization.T("progress.stopped"),
                report.Succeeded ? Localization.T("progress.cancontinue") : (report.Error ?? string.Empty));

            if (report.Succeeded)
            {
                SetPercent(100);
            }

            AppendLog(report.Succeeded
                ? Localization.T("progress.finished")
                : Localization.T("progress.finishedbad"));

            // 无人值守:写结果文件后直接退出,不留在完成页等人点
            if (DevOptions.Silent)
            {
                InstallerPlan.WriteReport(DevOptions.ReportPath, _options, report);
                await Task.Delay(600);
                Microsoft.UI.Xaml.Application.Current.Exit();
                return;
            }

            // 用户自己中断的(点取消 / 叉号 / Alt+F4):回滚已经做完,直接退出程序,
            // 不给他看失败页 —— 那不是故障,是他不想装了。
            if (_exitAfterRollback)
            {
                AppendLog(Localization.T("progress.exiting"));
                await Task.Delay(500);
                Application.Current.Exit();
                return;
            }

            // 自动失败进"安装未完成"页(红叉,说明原因并给手动下载入口);成功进完成页
            await Task.Delay(report.Succeeded ? 900 : 1200);

            MainWindow window = App.MainWindowInstance;
            if (window != null)
            {
                window.Navigate(report.Succeeded ? WizardPage.Done : WizardPage.Failed, null);
            }
        }

        // ---------------------------------------------------------------- 事件

        private void OnStepStateChanged(int index, InstallStepState state, string message)
        {
            _dispatcher.TryEnqueue(delegate
            {
                if (index < 0 || index >= _rows.Count)
                {
                    return;
                }

                ComponentRow row = _rows[index];
                switch (state)
                {
                    case InstallStepState.Running:
                        row.SetState(RowState.Busy, Localization.T("progress.steprunning"));
                        // 指南要求当前步骤写成"动词开头的短句 + 省略号"
                        SetCurrent(row.Name + "…", string.Empty);
                        break;

                    case InstallStepState.Done:
                        row.SetState(RowState.Ready, Localization.T("progress.stepdone"));
                        break;

                    case InstallStepState.Skipped:
                        row.SetState(RowState.Optional, Localization.T("progress.stepskip"), message);
                        break;

                    case InstallStepState.Failed:
                        row.SetState(RowState.Error, Localization.T("progress.stepfail"), message);
                        break;

                    default:
                        row.SetState(RowState.Pending, Localization.T("state.pending"));
                        break;
                }

                // 只在"完成"时把进度推到这一步的末尾。
                // 进入 Running 时**不能**推到 (index+1) —— 紧接着这一步自己上报的细节会算出更小的值,
                // 进度条就会往回跳一下(实测:切到下一步时条子突然退回,像抽搐)。
                if (state == InstallStepState.Done && _totalSteps > 0)
                {
                    SetPercent((index + 1.0) / _totalSteps * 100);
                }
                else if (state == InstallStepState.Running && _totalSteps > 0)
                {
                    SetPercent(index / (double)_totalSteps * 100);
                }
            });
        }

        private void OnStepDetailEvent(int index, string detail, double percent)
        {
            _dispatcher.TryEnqueue(delegate
            {
                if (index >= 0 && index < _rows.Count)
                {
                    SetCurrent(_rows[index].Name, detail);
                }
                else
                {
                    CurrentDetailText.Text = detail ?? string.Empty;
                }

                if (percent >= 0 && _totalSteps > 0)
                {
                    // 当前那一步的右侧也显示自己的百分比 ——
                    // 下载几十上百 MB 时,总进度条走得慢,光看它不知道卡在哪。
                    if (index >= 0 && index < _rows.Count)
                    {
                        // 只改文字,别调 SetState —— 那会重建整行,进度圈的动画会被重启
                        _rows[index].SetStatusText(((int)Math.Round(percent)) + "%");
                    }

                    // 单步进度折算成总进度:(序号 + 本步完成度) / 总步数
                    SetPercent((index + percent / 100.0) / _totalSteps * 100);
                }
            });
        }

        // ---------------------------------------------------------------- 页脚"取消"

        // 取消键放在向导页脚右下角(和"下一步"同一个位置),而不是页面内容里 ——
        // 微软《Setup》指南要求安装期间取消键必须可用,而且位置要符合向导的整体布局。

        public string FooterActionText
        {
            get
            {
                if (_finished)
                {
                    return string.Empty;
                }

                return Localization.T("btn.cancel");
            }
        }

        public bool FooterActionEnabled
        {
            get { return !_cancelled; }
        }

        // ---------------------------------------------------------------- 关闭拦截

        // 安装进行中不许直接关窗口。用户点叉号 / Alt+F4 时会走到这里:
        // 拦下 -> 禁用关闭按钮 -> 取消 + 回滚 -> 回滚完直接退出。
        public bool CanClose
        {
            get { return _finished; }
        }

        public void OnCloseRequested()
        {
            AppendLog(Localization.T("progress.closing"));
            _exitAfterRollback = true;
            RequestCancel();

            // 回滚可能要几秒。万一根本没在跑(例如已经结束),直接退,别让窗口卡着。
            if (_finished)
            {
                Application.Current.Exit();
            }
        }

        public void OnFooterAction()
        {
            // 用户点"取消"= 他不想装了:同样回滚完就退出,不给失败页。
            _exitAfterRollback = true;
            RequestCancel();
        }

        /// <summary>
        /// 请求取消。只发信号,当前步骤自己在下一个检查点退出;
        /// 退出后会走回滚,把已经装好的东西撤回去。
        /// </summary>
        private void RequestCancel()
        {
            if (_cancellation == null || _cancellation.IsCancellationRequested)
            {
                return;
            }

            _cancelled = true;
            AppendLog(Localization.T("progress.cancelling"));
            SetCurrent(Localization.T("progress.cancelling"), string.Empty);

            try
            {
                _cancellation.Cancel();
            }
            catch
            {
            }

            RefreshFooter();
        }

        private void RefreshFooter()
        {
            MainWindow window = App.MainWindowInstance;
            if (window != null)
            {
                window.RefreshChrome();
            }
        }

        /// <summary>
        /// 回滚:把这次安装**自己创建**的东西撤掉。
        ///
        /// 只撤自己造的 —— 安装前就存在的目录绝不碰,否则会误删用户原有的东西。
        /// 需要撤销什么由各步骤在跑的时候登记(见 InstallContext 里的那些 Note 方法)。
        /// </summary>
        private async Task RunRollbackAsync()
        {
            InstallContext context = _context;
            if (context == null)
            {
                return;
            }

            List<string> created = context.CreatedDirectories;
            bool createdDsh = ContainsPath(created, _options.DshRoot);
            bool createdLauncher = ContainsPath(created, _options.LauncherRoot);
            bool createdComponents = ContainsPath(created, _options.ComponentsRoot);

            // 回滚**一定要留痕**,而且是无条件的。
            //
            // 用户反馈过"按取消回滚了,但文件夹和 PATH 都还在" —— 光看现象分不清是
            // "回滚压根没跑"还是"跑了但按设计跳过了"。所以这里先把**这次安装自己造了什么**
            // 原样打出来:目录清单、PATH 条目、快捷方式、自启。
            //
            // 顺带把那个设计前提讲明白:回滚只撤"本次创建的东西"。重复装到同一个目录时,
            // 从第二次起那些目录本来就存在,按设计不会去动它们(动错了会删掉用户的文件)。
            AppendLog(Localization.IsChinese
                ? "回滚检查 · 本次创建目录:"
                    + (created.Count == 0 ? "无" : string.Join("、", created.ToArray()))
                    + " · PATH 条目:"
                    + (context.AddedPathEntries.Count == 0 ? "无" : string.Join("、", context.AddedPathEntries.ToArray()))
                    + " · 快捷方式:" + (context.CreatedShortcut ? "有" : "无")
                    + " · 开机自启:" + (context.RegisteredAutostart ? "有" : "无")
                : "Rollback check · created directories: "
                    + (created.Count == 0 ? "none" : string.Join(", ", created.ToArray()))
                    + " · PATH entries: "
                    + (context.AddedPathEntries.Count == 0 ? "none" : string.Join(", ", context.AddedPathEntries.ToArray()))
                    + " · shortcut: " + (context.CreatedShortcut ? "yes" : "no")
                    + " · autostart: " + (context.RegisteredAutostart ? "yes" : "no"));

            if (!createdDsh && !createdLauncher && !createdComponents
                && !context.CreatedShortcut && !context.RegisteredAutostart
                && context.AddedPathEntries.Count == 0)
            {
                AppendLog(Localization.T("rollback.nothing"));
                SetCurrent(Localization.T("rollback.nothing"), string.Empty);
                return;
            }

            AppendLog(Localization.T("rollback.begin"));
            SetCurrent(Localization.T("rollback.begin"), string.Empty);

            UninstallOptions rollback = new UninstallOptions
            {
                DshRoot = createdDsh ? _options.DshRoot : null,
                LauncherRoot = createdLauncher ? _options.LauncherRoot : null,
                ComponentsRoot = createdComponents ? _options.ComponentsRoot : null,
                AllUsers = _options.AllUsers,
                CleanPath = true,
                PathEntries = context.AddedPathEntries,
                RemoveShortcuts = context.CreatedShortcut,
                RemoveAutostart = context.RegisteredAutostart,
                RemoveUserData = false,
                UserDataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"),
                DryRun = _options.DryRun,
            };

            InstallOptions rollbackOptions = new InstallOptions
            {
                DryRun = _options.DryRun,
                TempRoot = _options.TempRoot,

                // 这三个路径**必须填**。
                //
                // 卸载器实际按上面 UninstallOptions 里的路径干活,这里看着像是冗余 ——
                // 但执行器开工第一件事就是 Options.Validate(),空着它会判"目录为空"、
                // 直接返回 **一步都不跑**,日志里却照样打一句"回滚完成"。
                // 表现就是"取消回滚了,文件夹和 PATH 一个都没少"(实测踩过,查了很久)。
                DshRoot = _options.DshRoot ?? string.Empty,
                LauncherRoot = _options.LauncherRoot ?? string.Empty,
                ComponentsRoot = _options.ComponentsRoot ?? string.Empty,
            };

            InstallContext rollbackContext = new InstallContext(
                rollbackOptions, _cancellation.Token, AppendLog,
                delegate(string detail, double percent) { });

            InstallRunner runner = new InstallRunner(UninstallSteps.BuildPlan(rollback), rollbackContext);

            int total = runner.StepCount;
            runner.StepStateChanged += delegate(int i, InstallStepState state, string message)
            {
                _dispatcher.TryEnqueue(delegate
                {
                    if (state == InstallStepState.Running && i < RowsHost.Children.Count)
                    {
                        RowsHost.Children.Clear();
                    }
                });
            };

            runner.StepDetail += delegate(int i, string detail, double percent)
            {
                _dispatcher.TryEnqueue(delegate
                {
                    SetCurrent(Localization.T("rollback.running"), detail ?? string.Empty);
                    if (percent >= 0 && total > 0)
                    {
                        SetPercent((i + percent / 100.0) / total * 100);
                    }
                });
            };

            InstallReport rollbackReport = null;
            try
            {
                rollbackReport = await Task.Run(delegate { return runner.RunAsync(); });
            }
            catch (Exception exception)
            {
                AppendLog(Localization.T("rollback.failed") + " " + exception.Message);
            }

            // 记下撤销了什么,失败页要显示给用户看("现在电脑是什么状态")
            List<string> undone = InstallSession.Current.RolledBack;
            if (undone == null)
            {
                undone = new List<string>();
                InstallSession.Current.RolledBack = undone;
            }

            if (rollbackReport != null)
            {
                for (int i = 0; i < rollbackReport.Steps.Count; i++)
                {
                    InstallStepResult step = rollbackReport.Steps[i];
                    if (step.State == InstallStepState.Done)
                    {
                        undone.Add(step.Title);
                    }
                }
            }

            AppendLog(Localization.T("rollback.done"));
        }

        /// <summary>路径列表里有没有这一项(忽略尾部反斜杠与大小写)。</summary>
        private static bool ContainsPath(List<string> paths, string path)
        {
            if (paths == null || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string wanted = path.TrimEnd('\\');
            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i].TrimEnd('\\'), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void SetCurrent(string task, string detail)
        {
            _dispatcher.TryEnqueue(delegate
            {
                CurrentTaskText.Text = task ?? string.Empty;
                CurrentDetailText.Text = detail ?? string.Empty;
            });
        }

        private void SetPercent(double value)
        {
            _dispatcher.TryEnqueue(delegate
            {
                double clamped = value < 0 ? 0 : (value > 100 ? 100 : value);

                // 兜底:进度只增不减。任何一步算出比当前更小的值都不采纳 ——
                // 用户看到的进度条往回退,会以为安装出了问题。
                if (clamped < _lastPercent)
                {
                    clamped = _lastPercent;
                }

                _lastPercent = clamped;
                TotalBar.Value = clamped;
                PercentText.Text = ((int)Math.Round(clamped)) + "%";
            });
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            // 同时落一份到文件。界面上的日志抽屉关了窗口就没了,
            // 出问题时要能事后查(尤其无人值守模式)。
            // 演练模式不落盘 —— 开发时在本机翻页预览不该留任何东西。
            if (!DevOptions.DryRun)
            {
                try
                {
                    DshInstaller.Shared.InstallLogger.Write(message);
                }
                catch
                {
                }
            }

            _dispatcher.TryEnqueue(delegate
            {
                _log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
                while (_log.Count > 400)
                {
                    _log.RemoveAt(0);
                }

                LogText.Text = string.Join(Environment.NewLine, _log.ToArray());

                // 布局还没算完时 ScrollableHeight 是旧值,放到下一个 UI 时机再滚
                _dispatcher.TryEnqueue(ScrollLogToEnd);
            });
        }
    }
}