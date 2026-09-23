using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Controls;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 安装未完成页(失败或取消,回滚之后进这里)。
    ///
    /// 微软《Setup》体验指南对这类页面的要求很明确:
    ///   · 说清楚**发生了什么**;
    ///   · 给出**用户自己能动手的下一步**,而不是只丢一句"出错了";
    ///   · 不要拿一串只有技术支持才看得懂的细节淹没用户(细节进日志文件)。
    /// 所以这里分三块:失败原因、回滚了什么、以及手动下载的入口。
    /// </summary>
    public sealed partial class FailedPage : Page, IWizardPage
    {
        public FailedPage()
        {
            InitializeComponent();
            ApplyText();
            Loaded += OnLoaded;
            ActualThemeChanged += delegate
            {
                if (IsLoaded)
                {
                    OnLoaded(this, new RoutedEventArgs());
                }
            };
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            // 页脚的"关闭"就是退出程序
            Application.Current.Exit();
            return false;
        }

        private void ApplyText()
        {
            InstallReport report = InstallSession.Current.Result;
            bool cancelled = report != null && report.Cancelled;

            Scaffold.Title = cancelled
                ? Localization.T("failed.title.cancelled")
                : Localization.T("failed.title");
            Scaffold.Subtitle = cancelled
                ? Localization.T("failed.desc.cancelled")
                : Localization.T("failed.desc");
            Scaffold.SetStep(8);

            ReasonLabel.Text = Localization.T("failed.reasons");
            RollbackLabel.Text = Localization.T("failed.rollback");
            ManualLabel.Text = Localization.T("failed.manual");

            // 用户自己点"取消"时:没有东西"失败",也不必给手动下载入口 ——
            // 那不是故障,是他不想装了。只留"已经撤销了什么",让他确认状态干净。
            if (cancelled)
            {
                ReasonBox.Visibility = Visibility.Collapsed;
                ManualBox.Visibility = Visibility.Collapsed;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InstallSession session = InstallSession.Current;
            InstallReport report = session.Result;

            ReasonsHost.Children.Clear();
            RollbackHost.Children.Clear();
            LinksHost.Children.Clear();

            // ---------------------------------------------------------- 失败原因
            if (report != null)
            {
                for (int i = 0; i < report.Steps.Count; i++)
                {
                    InstallStepResult step = report.Steps[i];
                    if (step.State != InstallStepState.Failed)
                    {
                        continue;
                    }

                    ReasonsHost.Children.Add(BuildReasonRow(step.Title, step.Message));
                }
            }

            if (ReasonsHost.Children.Count == 0)
            {
                ReasonsHost.Children.Add(new TextBlock
                {
                    Text = Localization.T("failed.nothing"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
                });
            }

            // ---------------------------------------------------------- 回滚结果
            List<string> rolled = session.RolledBack;
            if (rolled != null && rolled.Count > 0)
            {
                for (int i = 0; i < rolled.Count; i++)
                {
                    RollbackHost.Children.Add(new TextBlock
                    {
                        Text = "· " + rolled[i],
                        FontSize = 11.5,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
                    });
                }
            }
            else
            {
                RollbackHost.Children.Add(new TextBlock
                {
                    Text = Localization.T("failed.norollback"),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
                });
            }

            // ---------------------------------------------------------- 手动下载
            AddLink("Node.js", "https://nodejs.org/en/download");
            AddLink(Localization.IsChinese ? "DSH 本体(npm)" : "DSH core (npm)", "https://www.npmjs.com/package/" + WellKnown.DshPackage);
            AddLink(Localization.T("failed.link.launcher"), "https://github.com/" + WellKnown.LauncherRepository + "/releases/latest");
        }

        private UIElement BuildReasonRow(string title, string message)
        {
            StackPanel row = new StackPanel { Spacing = 1 };

            row.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Theme.Brush("PrimaryTextBrush", Windows.UI.Color.FromArgb(255, 15, 15, 16)),
            });

            if (!string.IsNullOrWhiteSpace(message))
            {
                row.Children.Add(new TextBlock
                {
                    Text = message,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
                });
            }

            return row;
        }

        private void AddLink(string label, string url)
        {
            HyperlinkButton button = new HyperlinkButton
            {
                Content = label + "  " + url,
                NavigateUri = new Uri(url),
                FontSize = 11.5,
                Padding = new Thickness(0, 2, 0, 2),
            };

            LinksHost.Children.Add(button);
        }
    }
}
