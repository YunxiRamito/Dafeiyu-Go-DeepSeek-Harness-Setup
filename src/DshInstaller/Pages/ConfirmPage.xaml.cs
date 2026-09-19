using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    /// <summary>确认页。把前面所有选择列成一张清单。</summary>
    public sealed partial class ConfirmPage : Page, IWizardPage
    {
        public ConfirmPage()
        {
            InitializeComponent();
            ApplyText();
            Loaded += OnLoaded;
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        /// <summary>
        /// 点"开始安装"。
        ///
        /// 需要管理员时(装给所有用户,或者要注册开机自启的计划任务),
        /// 在这里**一次性**提权:把选项落盘 → runas 起一个提权实例 → 当前实例退出。
        /// 提权实例读回选项后直接进进度页,所以全程只弹一次 UAC,向导也不用重填。
        /// </summary>
        public bool OnNext()
        {
            InstallSession session = InstallSession.Current;

            if (!session.NeedsElevation || Shared.ElevationHelper.IsElevated())
            {
                return true;
            }

            InstallOptions options = InstallerPlan.Build();
            string planPath;
            try
            {
                planPath = InstallerPlan.Save(options);
            }
            catch (System.Exception exception)
            {
                ShowElevationFailure(
                    Localization.IsChinese
                        ? "保存安装选项失败:" + exception.Message
                        : "Failed to save the installation options: " + exception.Message);
                return false;
            }

            string error;
            if (Shared.ElevationHelper.StartElevatedWorker("--plan=\"" + planPath + "\"", out error))
            {
                // 提权实例已经接手,本实例功成身退
                Microsoft.UI.Xaml.Application.Current.Exit();
                return false;
            }

            // 走到这里通常是用户在 UAC 上点了"否"
            InstallerPlan.Cleanup(planPath);
            ShowElevationFailure(
                Localization.IsChinese
                    ? "无法获取管理员权限,安装无法继续。\n\n"
                        + "如不希望提权,可返回上一步将范围改为「仅为本用户安装」"
                        + "并取消勾选「开机静默启动」。\n\n详情:" + error
                    : "Administrator rights are required to continue.\n\n"
                        + "To install without elevation, go back and choose \"Install for this user only\" "
                        + "and clear \"Start silently with Windows\".\n\nDetails: " + error);
            return false;
        }

        private void ShowElevationFailure(string message)
        {
            _ = DispatcherQueue.TryEnqueue(async delegate
            {
                try
                {
                    ContentDialog dialog = new ContentDialog
                    {
                        XamlRoot = XamlRoot,
                        Title = Localization.IsChinese ? "需要管理员权限" : "Administrator rights required",
                        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        CloseButtonText = Localization.T("btn.finish"),
                    };

                    await dialog.ShowAsync();
                }
                catch
                {
                }
            });
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.T("confirm.title");
            Scaffold.Subtitle = Localization.T("confirm.desc");
            Scaffold.SetStep(5);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InstallSession session = InstallSession.Current;

            RowsHost.Children.Clear();

            Add(Localization.IsChinese ? "安装范围" : "Scope",
                session.Scope == InstallScope.AllUsers
                    ? (Localization.IsChinese ? "所有用户（需要管理员权限）" : "All users (administrator rights required)")
                    : (Localization.IsChinese ? "仅当前用户" : "Current user only"));

            // 把"为什么还要提权"明说。只选了"仅当前用户"却照样弹 UAC,用户会一头雾水 ——
            // 原因只有两个(装给所有用户、要注册开机自启、要装系统运行库),直接摆出来
            // (实测反馈:"为自己安装还是弹 UAC,为什么")。
            Add(Localization.IsChinese ? "权限" : "Permissions", DescribeElevation(session));

            Add(Localization.IsChinese ? "组件目录" : "Component directory", session.ComponentsRoot);
            Add(Localization.IsChinese ? "下载源" : "Download source",
                session.SourcePreference == MirrorSource.China
                    ? (Localization.IsChinese ? "国内镜像" : "China mirror")
                    : (Localization.IsChinese ? "官方源" : "Official source"));

            List<string> extras = new List<string>();
            if (session.InstallGit)
            {
                extras.Add("Git");
            }

            if (session.InstallPnpm)
            {
                extras.Add("pnpm");
            }

            if (session.InstallPython)
            {
                extras.Add("Python");
            }

            Add(Localization.IsChinese ? "附带组件" : "Optional components",
                extras.Count == 0
                    ? (Localization.IsChinese ? "无" : "None")
                    : string.Join(", ", extras.ToArray()));

            Add(Localization.IsChinese ? "DSH 本体" : "DSH core", session.DshRoot);
            Add(Localization.IsChinese ? "启动器" : "Launcher", session.LauncherRoot);

            Add(Localization.IsChinese ? "桌面快捷方式" : "Desktop shortcut",
                YesNo(session.CreateDesktopShortcut));
            Add(Localization.IsChinese ? "开始菜单" : "Start menu",
                YesNo(session.CreateStartMenuShortcut));
            Add(Localization.IsChinese ? "开机自启" : "Start with Windows",
                YesNo(session.EnableAutostart));
            Add(Localization.IsChinese ? "装完立即启动" : "Launch after installation",
                YesNo(session.LaunchAfterwards));
        }

        /// <summary>
        /// 说清楚这次到底要不要管理员权限、以及为什么。
        ///
        /// "仅当前用户"≠"不用管理员":只要勾了开机自启(要注册最高权限的计划任务),
        /// 或者系统缺运行库(装运行库是机器级操作),照样得弹一次 UAC。
        /// 不写明白的话,用户会觉得安装程序在乱要权限。
        /// </summary>
        private static string DescribeElevation(InstallSession session)
        {
            bool chinese = Localization.IsChinese;

            if (session.Scope == InstallScope.AllUsers)
            {
                return chinese
                    ? "需要管理员权限（安装给所有用户）"
                    : "Administrator rights required (installing for all users)";
            }

            List<string> reasons = new List<string>();
            if (session.EnableAutostart)
            {
                reasons.Add(chinese ? "注册开机自启" : "registering autostart");
            }

            if (Shared.Detection.RuntimeProbe.AnyMissing)
            {
                reasons.Add(chinese ? "安装系统运行库" : "installing the required runtimes");
            }

            if (reasons.Count == 0)
            {
                return chinese ? "无需管理员权限" : "No administrator rights required";
            }

            return chinese
                ? "需要管理员权限（" + string.Join("、", reasons.ToArray()) + "）"
                : "Administrator rights required (" + string.Join(", ", reasons.ToArray()) + ")";
        }

        private static string YesNo(bool value)
        {
            if (Localization.IsChinese)
            {
                return value ? "是" : "否";
            }

            return value ? "Yes" : "No";
        }

        private void Add(string label, string value)
        {
            Grid row = new Grid { Padding = new Thickness(0, 9, 0, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock labelText = new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                Foreground = Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
            };
            Grid.SetColumn(labelText, 0);

            TextBlock valueText = new TextBlock
            {
                Text = value ?? string.Empty,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("PrimaryTextBrush", Windows.UI.Color.FromArgb(255, 26, 29, 35)),
            };
            Grid.SetColumn(valueText, 1);

            row.Children.Add(labelText);
            row.Children.Add(valueText);

            RowsHost.Children.Add(row);
            RowsHost.Children.Add(new Border
            {
                Height = 1,
                Background = Brush("DividerBrush", Windows.UI.Color.FromArgb(18, 0, 0, 0)),
            });
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
