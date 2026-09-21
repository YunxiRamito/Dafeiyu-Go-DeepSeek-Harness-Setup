using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DshInstaller.Pages
{
    /// <summary>完成页。列一下落在哪了,再问一句要不要现在启动。</summary>
    public sealed partial class DonePage : Page, IWizardPage
    {
        public DonePage()
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
            InstallSession.Current.LaunchAfterwards = LaunchBox.IsChecked == true;

            // 先记一笔再动手 —— 万一启动那步出岔子,日志里至少能看出"走没走到这儿"。
            DshInstaller.Shared.InstallLogger.Write(
                "完成页:点完成,立即启动=" + InstallSession.Current.LaunchAfterwards
                + ",模式=" + InstallSession.Current.Mode);

            // 启动器**只在这一刻启动** —— 按这个复选框的勾选状态。
            //
            // 以前是安装流程最后加一步"启动启动器",但那一步在**完成页之前**就定好了,
            // 完成页这个复选框根本管不到它(勾了没勾都照启),纯装饰。
            // 现在改成点"完成"时按勾选决定,勾了就启、没勾就不启。
            if (InstallSession.Current.LaunchAfterwards
                && InstallSession.Current.Mode == SessionMode.Install)
            {
                LaunchLauncher();
            }

            return true;
        }

        /// <summary>把启动器拉起来。起不来只记日志 —— 用户点快捷方式一样能开。</summary>
        private void LaunchLauncher()
        {
            try
            {
                string root = InstallSession.Current.LauncherRoot;
                if (string.IsNullOrEmpty(root))
                {
                    return;
                }

                string exe = System.IO.Path.Combine(root, DshInstaller.Shared.WellKnown.LauncherExe);
                if (!System.IO.File.Exists(exe))
                {
                    DshInstaller.Shared.InstallLogger.Write("完成页:没找到启动器 " + exe);
                    return;
                }

                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = root,
                    UseShellExecute = true,
                };

                System.Diagnostics.Process.Start(info);
                DshInstaller.Shared.InstallLogger.Write("完成页:已启动 " + exe);
            }
            catch (Exception exception)
            {
                DshInstaller.Shared.InstallLogger.Write("完成页:启动失败 " + exception.Message);
            }
        }

        private void ApplyText()
        {
            bool uninstalling = InstallSession.Current.Mode == SessionMode.Uninstall;

            if (uninstalling)
            {
                TitleText.Text = Localization.T("uninstall.done.title");
                SubtitleText.Text = Localization.T("uninstall.done.desc");
                LaunchBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                TitleText.Text = Localization.T("done.title");
                SubtitleText.Text = Localization.IsChinese
                    ? "大肥鱼Go现已可使用：请查看任务栏图标。"
                    : "Dafeiyu-Go is now available. Check your system tray.";
            }

            LaunchBox.Content = Localization.IsChinese ? "现在启动大肥鱼Go" : "Launch Dafeiyu-Go now";
            FootnoteText.Text = Localization.IsChinese ? "一键安装程序与启动器由 DeepSeek V4.1 Flash 与 雨沫云汐 制作。" : "The installer and launcher are made by DeepSeek V4.1 Flash and Yumo Yunxi.";
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InstallSession session = InstallSession.Current;
            Shared.Install.InstallReport result = session.Result;

            SummaryHost.Children.Clear();

            // 完成页只说"完成"和"未完全完成"两种 —— 真正失败/取消的走失败页(红叉)。
            bool ok = result == null || result.Succeeded;

            // 三态:
            //   全部成功           -> 对勾,完事
            //   只有可选组件没装上 -> 黄色感叹号:软件能用,只是提醒一下
            //   必需件失败 / 被取消 -> 走失败页(红色叉叉),不会进到这里
            List<Shared.Install.InstallStepResult> optionalFailed = new List<Shared.Install.InstallStepResult>();
            if (result != null)
            {
                for (int i = 0; i < result.Steps.Count; i++)
                {
                    Shared.Install.InstallStepResult step = result.Steps[i];
                    if (step.State == Shared.Install.InstallStepState.Failed)
                    {
                        optionalFailed.Add(step);
                    }
                }
            }

            if (ok && optionalFailed.Count > 0)
            {
                // 黄色感叹号。用固定的注意色,深色模式下也认得出来。
                Windows.UI.Color warn = Windows.UI.Color.FromArgb(255, 247, 169, 40);
                BadgeOuter.Fill = new SolidColorBrush(WithAlpha(warn, 46));
                BadgeInner.Fill = new SolidColorBrush(warn);
                BadgeIcon.Glyph = "\uE7BA";
                BadgeIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 26, 8));

                if (InstallSession.Current.Mode == SessionMode.Uninstall)
                {
                    // 卸载也有"没删干净"的情况 —— 别把安装页那套话搬过来。
                    // 以前这里无脑用 done.partial.*,于是卸载跑到"安装未完全完成 / 未安装（可选）"
                    // 上面去了,用户看着当然懵(实测:XDD)。
                    TitleText.Text = Localization.IsChinese
                        ? "卸载未完全完成"
                        : "Uninstall did not complete fully";

                    SubtitleText.Text = Localization.IsChinese
                        ? "下列项目未能删除(可能被占用)。可以手动清理,或重启后再卸载一次。"
                        : "The following items could not be removed (they may be in use). Remove them manually, or reboot and uninstall again.";

                    PartialLabel.Text = Localization.IsChinese ? "未能删除" : "Not removed";
                }
                else
                {
                    TitleText.Text = Localization.T("done.partial.title");
                    SubtitleText.Text = Localization.T("done.partial.desc");
                    PartialLabel.Text = Localization.T("done.partial.label");
                }

                PartialBox.Visibility = Visibility.Visible;
                for (int i = 0; i < optionalFailed.Count; i++)
                {
                    TextBlock partialLine = new TextBlock
                    {
                        Text = "· " + optionalFailed[i].Title
                            + (string.IsNullOrWhiteSpace(optionalFailed[i].Message)
                                ? string.Empty
                                : "  (" + FirstLine(optionalFailed[i].Message) + ")"),
                        FontSize = 11.5,
                        TextWrapping = TextWrapping.Wrap,
                    };
                    Theme.Bind(
                        partialLine,
                        TextBlock.ForegroundProperty,
                        "TertiaryTextBrush",
                        Windows.UI.Color.FromArgb(255, 138, 144, 153));
                    PartialHost.Children.Add(partialLine);
                }
            }

            if (result != null)
            {
                int done = result.CountOf(Shared.Install.InstallStepState.Done);
                int skipped = result.CountOf(Shared.Install.InstallStepState.Skipped);
                int failed = result.CountOf(Shared.Install.InstallStepState.Failed);

                AddRow("\uE73E", Localization.IsChinese ? "完成" : "Completed",
                    done + (Localization.IsChinese ? " 项" : string.Empty)
                    + (skipped > 0
                        ? Localization.T("done.skippedcount") + skipped + (Localization.IsChinese ? " 项" : string.Empty)
                        : string.Empty)
                    + (failed > 0
                        ? Localization.T("done.failedcount") + failed + (Localization.IsChinese ? " 项" : string.Empty)
                        : string.Empty));

                // 失败的那一步要指名道姓,并把原因写出来
                for (int i = 0; i < result.Steps.Count; i++)
                {
                    Shared.Install.InstallStepResult step = result.Steps[i];
                    if (step.State == Shared.Install.InstallStepState.Failed)
                    {
                        AddRow("\uEA39", step.Title, step.Message);
                    }
                }
            }

            AddRow("\uE8A7", Localization.IsChinese ? "DSH 本体" : "DSH core", session.DshRoot);
            AddRow("\uE7E8", Localization.IsChinese ? "启动器" : "Launcher", session.LauncherRoot);

            if (session.CreateDesktopShortcut)
            {
                AddRow("\uE8FC", Localization.IsChinese ? "桌面快捷方式" : "Desktop shortcut",
                    Localization.IsChinese ? "已创建" : "Created");
            }

            if (session.EnableAutostart)
            {
                AddRow("\uE945", Localization.IsChinese ? "开机自启" : "Start with Windows",
                    Localization.IsChinese ? "已开启" : "Enabled");
            }
        }

        /// <summary>取同样的颜色、换一个透明度。</summary>
        private static Windows.UI.Color WithAlpha(Windows.UI.Color color, byte alpha)
        {
            return Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        /// <summary>取多行说明的第一行,放进列表里当简短原因。</summary>
        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    string line = lines[i].Trim();
                    return line.Length > 90 ? line.Substring(0, 90) + "…" : line;
                }
            }

            return string.Empty;
        }

        private void AddRow(string glyph, string label, string value)
        {
            StackPanel row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
            };

            FontIcon icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Theme.Bind(
                icon,
                FontIcon.ForegroundProperty,
                "AccentBrush",
                Windows.UI.Color.FromArgb(255, 77, 107, 254));
            row.Children.Add(icon);

            TextBlock labelText = new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                Width = 96,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Theme.Bind(
                labelText,
                TextBlock.ForegroundProperty,
                "SecondaryTextBrush",
                Windows.UI.Color.FromArgb(255, 92, 99, 110));
            row.Children.Add(labelText);

            TextBlock valueText = new TextBlock
            {
                Text = value ?? string.Empty,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Theme.Bind(
                valueText,
                TextBlock.ForegroundProperty,
                "PrimaryTextBrush",
                Windows.UI.Color.FromArgb(255, 26, 29, 35));
            row.Children.Add(valueText);

            SummaryHost.Children.Add(row);
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
