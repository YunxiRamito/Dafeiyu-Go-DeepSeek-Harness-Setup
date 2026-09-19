using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DshInstaller.Controls;
using DshInstaller.Shared;
using DshInstaller.Shared.Detection;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 环境检测页。逐条亮起来,让用户看到"在查什么",查完给个结论。
    /// 检测本身在后台线程跑,结果回到 UI 线程填。
    /// </summary>
    public sealed partial class DetectPage : Page, IWizardPage
    {
        private readonly List<ComponentRow> _rows = new List<ComponentRow>();
        private readonly DispatcherQueue _dispatcher;
        private ProbeReport _report;
        private bool _finished;

        public DetectPage()
        {
            InitializeComponent();
            _dispatcher = DispatcherQueue.GetForCurrentThread();
            ApplyText();
            Loaded += OnLoaded;
        }

        public bool CanGoNext
        {
            get { return _finished; }
        }

        public bool OnNext()
        {
            // 系统版本不够就拦住,不让往下一步走
            if (_report != null && !_report.IsWindowsSupported)
            {
                return false;
            }

            InstallSession.Current.Report = _report;

            // 环境齐全 -> 跳过组件页;有缺失 -> 去组件页
            MainWindow window = FindWindow();
            if (window != null && _report != null && _report.AllRequiredReady)
            {
                window.SetNextOverride(WizardPage.DshLocation);
            }

            return true;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.T("detect.title");
            Scaffold.Subtitle = Localization.T("detect.desc");
            Scaffold.SetStep(1);
            ProgressText.Text = Localization.T("detect.running");
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildRows();
            _ = RunDetectionAsync();
        }

        /// <summary>先把要查的行铺出来,状态都是"检查中"。</summary>
        private void BuildRows()
        {
            RowsHost.Children.Clear();
            RowsHost.RowDefinitions.Clear();
            RowsHost.ColumnDefinitions.Clear();
            _rows.Clear();

            string[] names = Localization.IsChinese
                ? new[] { "Windows 版本", "Node.js", "npm", "DeepSeek Harness 本体", "Windows App Runtime", ".NET 8 桌面运行时", "Git", "pnpm", "Python" }
                : new[] { "Windows version", "Node.js", "npm", "DeepSeek Harness core", "Windows App Runtime", ".NET 8 Desktop Runtime", "Git", "pnpm", "Python" };

            // 两列:左列前 5 项,右列剩下 4 项,一屏看完不用滚
            const int leftCount = 5;
            RowsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RowsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int line = 0; line < leftCount; line++)
            {
                RowsHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int index = 0; index < names.Length; index++)
            {
                ComponentRow row = new ComponentRow { Name = names[index] };
                row.SetCompact(true);
                row.SetState(RowState.Pending, Localization.T("state.pending"));
                _rows.Add(row);

                int column = index < leftCount ? 0 : 1;
                int line = index < leftCount ? index : index - leftCount;
                Grid.SetColumn(row, column);
                Grid.SetRow(row, line);
                RowsHost.Children.Add(row);
            }
        }

        private async Task RunDetectionAsync()
        {
            ProbeReport report = await Task.Run(delegate()
            {
                try
                {
                    return EnvironmentProbe.Run(null);
                }
                catch
                {
                    return null;
                }
            });

            if (report == null)
            {
                _finished = true;
                ProgressText.Text = Localization.IsChinese ? "检测失败" : "Detection failed";
                return;
            }

            _report = report;

            // 按组件 Id 找行,逐个亮
            string[] order = { "windows", "node", "npm", "dsh", "winappruntime", "dotnet8", "git", "pnpm", "python" };
            for (int index = 0; index < order.Length; index++)
            {
                ComponentRow row = index < _rows.Count ? _rows[index] : null;
                if (row == null)
                {
                    continue;
                }

                SetRowBusy(row);
                await Task.Delay(110);

                if (order[index] == "windows")
                {
                    // Windows 不在组件列表里,单独表达
                    bool ok = report.IsWindowsSupported;
                    row.Detail = report.WindowsName;
                    row.SetState(
                        ok ? RowState.Ready : RowState.Error,
                        ok
                            ? (Localization.IsChinese ? "支持" : "Supported")
                            : (Localization.IsChinese ? "过低" : "Not supported"),
                        "build " + report.WindowsBuild);
                    continue;
                }

                ComponentStatus status = report[order[index]];
                row.ApplyStatus(status);
            }

            CheckProgress.Value = 100;
            // 满值黑条看着像一条分割线,检查完就收起来
            CheckProgress.Visibility = Visibility.Collapsed;
            ProgressText.Text = Localization.IsChinese ? "检测完成" : "Check complete";

            ShowVerdict(report);

            _finished = true;
            MainWindow window = FindWindow();
            if (window != null)
            {
                window.SetNextOverride(null);
            }
        }

        private void SetRowBusy(ComponentRow row)
        {
            try
            {
                _dispatcher.TryEnqueue(delegate()
                {
                    row.SetState(RowState.Busy, Localization.T("state.checking"));
                });
            }
            catch
            {
            }
        }

        private void ShowVerdict(ProbeReport report)
        {
            VerdictBox.Visibility = Visibility.Visible;

            if (!report.IsWindowsSupported)
            {
                VerdictBox.Background = Brush("DangerBrush", Windows.UI.Color.FromArgb(255, 214, 69, 69));
                VerdictBox.Opacity = 0.14;
                VerdictIcon.Glyph = "\uE7BA";
                VerdictIcon.Foreground = Brush("DangerBrush", Windows.UI.Color.FromArgb(255, 214, 69, 69));
                VerdictText.Text = Localization.T("detect.winbad");
                return;
            }

            if (report.AllRequiredReady)
            {
                VerdictBox.Background = Brush("AccentSoftBrush", Windows.UI.Color.FromArgb(26, 77, 107, 254));
                VerdictIcon.Glyph = "\uE73E";
                VerdictIcon.Foreground = Brush("SuccessBrush", Windows.UI.Color.FromArgb(255, 46, 158, 91));
                VerdictText.Text = Localization.T("detect.allgood");
                return;
            }

            VerdictBox.Background = Brush("AccentSoftBrush", Windows.UI.Color.FromArgb(26, 77, 107, 254));
            VerdictIcon.Glyph = "\uE7BA";
            VerdictIcon.Foreground = Brush("WarningBrush", Windows.UI.Color.FromArgb(255, 217, 138, 0));

            int pending = report.PendingRequired().Count;
            VerdictText.Text = Localization.IsChinese
                ? "有 " + pending + " 个必需组件缺失，可在下一步中安装"
                : pending + " required component(s) missing; the next step can install them";
        }

        private MainWindow FindWindow()
        {
            try
            {
                return App.MainWindowInstance;
            }
            catch
            {
                return null;
            }
        }

        private static Brush Brush(string key, Windows.UI.Color fallback)
        {
            try
            {
                object value = Application.Current.Resources[key];
                Brush brush = value as Brush;
                if (brush != null)
                {
                    return brush;
                }
            }
            catch
            {
            }

            return new SolidColorBrush(fallback);
        }
    }
}
