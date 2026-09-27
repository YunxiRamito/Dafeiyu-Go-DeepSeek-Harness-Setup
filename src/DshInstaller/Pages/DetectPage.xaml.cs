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
            ActualThemeChanged += delegate
            {
                if (_report != null)
                {
                    ShowVerdict(_report);
                }
            };
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
            Scaffold.SetStep(2);
            ProgressText.Text = Localization.T("detect.running");
            HealthLabel.Text = Localization.T("detect.health");
            HealthHint.Text = Localization.T("detect.health.hint");
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
            // 体检跟着检测一起在后台跑:磁盘、端口、代理都是本机查询,不额外等网。
            // 放在同一个 Task.Run 里是为了让报告和组件状态同源 —— 端口那条要读"DSH 装没装"。
            PreflightReport preflight = null;
            ProbeReport report = await Task.Run(delegate()
            {
                try
                {
                    ProbeReport probed = EnvironmentProbe.Run(null);
                    preflight = probed == null ? null : Preflight.Run(probed);
                    return probed;
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
            ShowHealth(preflight);
            InstallSession.Current.Preflight = preflight;

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
                VerdictBox.Background = Brush("DangerSoftBrush", Windows.UI.Color.FromArgb(32, 214, 69, 69));
                VerdictBox.Opacity = 1;
                VerdictIcon.Glyph = "\uE7BA";
                VerdictIcon.Foreground = Brush("DangerBrush", Windows.UI.Color.FromArgb(255, 214, 69, 69));
                VerdictText.Text = Localization.T("detect.winbad");
                return;
            }

            if (report.AllRequiredReady)
            {
                VerdictBox.Background = Brush("SuccessSoftBrush", Windows.UI.Color.FromArgb(24, 46, 158, 91));
                VerdictIcon.Glyph = "\uE73E";
                VerdictIcon.Foreground = Brush("SuccessBrush", Windows.UI.Color.FromArgb(255, 46, 158, 91));
                VerdictText.Text = Localization.T("detect.allgood");
                return;
            }

            VerdictBox.Background = Brush("WarningSoftBrush", Windows.UI.Color.FromArgb(30, 217, 138, 0));
            VerdictIcon.Glyph = "\uE7BA";
            VerdictIcon.Foreground = Brush("WarningBrush", Windows.UI.Color.FromArgb(255, 217, 138, 0));

            int pending = report.PendingRequired().Count;
            VerdictText.Text = Localization.IsChinese
                ? "有 " + pending + " 个必需组件缺失，可在下一步中安装"
                : pending + " required component(s) missing; the next step can install them";
        }

        /// <summary>
        /// 铺装前体检。有风险的那几项刷浅黄并带一句"怎么办";
        /// 没风险的也照常列出来 —— 用户要看到"这些我都替你查过了"。
        /// </summary>
        private void ShowHealth(PreflightReport preflight)
        {
            if (preflight == null || preflight.Items.Count == 0)
            {
                HealthBox.Visibility = Visibility.Collapsed;
                return;
            }

            HealthBox.Visibility = Visibility.Visible;
            HealthHost.Children.Clear();

            for (int index = 0; index < preflight.Items.Count; index++)
            {
                PreflightItem item = preflight.Items[index];
                if (item == null)
                {
                    continue;
                }

                HealthHost.Children.Add(BuildHealthRow(item));
            }

            if (!preflight.HasRisk)
            {
                TextBlock allClear = new TextBlock
                {
                    Text = Localization.T("detect.health.ok"),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                };

                Theme.Bind(
                    allClear,
                    TextBlock.ForegroundProperty,
                    "SuccessBrush",
                    Windows.UI.Color.FromArgb(255, 46, 158, 91));

                HealthHost.Children.Add(allClear);
            }
        }

        private UIElement BuildHealthRow(PreflightItem item)
        {
            StackPanel content = new StackPanel { Spacing = 2 };

            Grid line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock label = new TextBlock
            {
                Text = item.Label,
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            Theme.Bind(
                label,
                TextBlock.ForegroundProperty,
                "SecondaryTextBrush",
                Windows.UI.Color.FromArgb(255, 92, 99, 110));
            Grid.SetColumn(label, 0);
            line.Children.Add(label);

            TextBlock value = new TextBlock
            {
                Text = item.Value ?? string.Empty,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
            };
            Theme.Bind(
                value,
                TextBlock.ForegroundProperty,
                "PrimaryTextBrush",
                Windows.UI.Color.FromArgb(255, 26, 29, 35));
            Grid.SetColumn(value, 1);
            line.Children.Add(value);

            content.Children.Add(line);

            if (!string.IsNullOrWhiteSpace(item.Advice))
            {
                TextBlock advice = new TextBlock
                {
                    Text = item.Advice,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(118, 0, 0, 0),
                };

                Theme.Bind(
                    advice,
                    TextBlock.ForegroundProperty,
                    item.Risk ? "WarningBrush" : "TertiaryTextBrush",
                    item.Risk
                        ? Windows.UI.Color.FromArgb(255, 217, 138, 0)
                        : Windows.UI.Color.FromArgb(255, 138, 144, 153));

                content.Children.Add(advice);
            }

            if (!item.Risk)
            {
                return content;
            }

            Border box = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Child = content,
            };

            Theme.Bind(
                box,
                Border.BackgroundProperty,
                "WarningSoftBrush",
                Windows.UI.Color.FromArgb(30, 217, 138, 0));

            return box;
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
