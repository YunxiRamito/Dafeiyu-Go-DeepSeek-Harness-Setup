using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Shared;

namespace DshInstaller.Pages
{
    /// <summary>启动器装在哪,外加三个勾选项。</summary>
    public sealed partial class LauncherLocationPage : Page, IWizardPage
    {
        public LauncherLocationPage()
        {
            InitializeComponent();
            ApplyText();
            Loaded += OnLoaded;
        }

        public bool CanGoNext
        {
            get { return !string.IsNullOrWhiteSpace(PathBox.Text); }
        }

        public bool OnNext()
        {
            string path = PathBox.Text == null ? string.Empty : PathBox.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            InstallSession session = InstallSession.Current;
            session.LauncherRoot = path;
            session.LauncherChosen = true;
            session.CreateDesktopShortcut = DesktopBox.IsChecked == true;
            session.CreateStartMenuShortcut = StartMenuBox.IsChecked == true;
            session.EnableAutostart = AutostartBox.IsChecked == true;
            session.AutostartChosen = true;
            return true;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.T("launcher.title");
            Scaffold.Subtitle = Localization.T("launcher.desc");
            Scaffold.SetStep(4);

            PathLabel.Text = Localization.IsChinese ? "安装到" : "Install to";
            BrowseButton.Content = Localization.T("btn.browse");
            // 这里的目录名跟着 WellKnown.LauncherFolder 走,别再写死 ——
            // 写死过一次,改了常量之后提示文字还在说旧名字(实测反馈)。
            HintText.Text = Localization.IsChinese
                ? "默认将存放于 DSH 目录下的 “" + WellKnown.LauncherFolder + "” 文件夹。"
                : "By default, files are placed in the “" + WellKnown.LauncherFolder + "” folder under the DSH directory.";

            ExtrasLabel.Text = Localization.IsChinese ? "附加选项" : "Additional options";
            DesktopBox.Content = Localization.IsChinese ? "创建桌面快捷方式" : "Create a desktop shortcut";
            StartMenuBox.Content = Localization.IsChinese ? "添加到开始菜单" : "Add to the Start menu";
            AutostartBox.Content = Localization.IsChinese ? "开机静默启动" : "Start silently with Windows";
            // 「安装完成后立即启动」不在这儿了 —— 它改到完成页去勾,
            // 因为真正决定启不启的是点"完成"那一刻(见 DonePage.OnNext)。
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InstallSession session = InstallSession.Current;

            PathBox.Text = string.IsNullOrEmpty(session.LauncherRoot)
                ? Path.Combine(session.DshRoot ?? session.DefaultRoot, WellKnown.LauncherFolder)
                : session.LauncherRoot;

            DesktopBox.IsChecked = session.CreateDesktopShortcut;
            StartMenuBox.IsChecked = session.CreateStartMenuShortcut;
            AutostartBox.IsChecked = session.EnableAutostart;

            CheckWarnings();

            MainWindow current = App.MainWindowInstance;
            if (current != null)
            {
                current.RefreshChrome();
            }
        }

        private void OnPathChanged(object sender, TextChangedEventArgs e)
        {
            CheckWarnings();

            // 路径是 OnLoaded 里才填上的,按钮可用状态得跟着重算
            MainWindow window = App.MainWindowInstance;
            if (window != null)
            {
                window.RefreshChrome();
            }
        }

        /// <summary>启动器不在 DSH 根目录附近时提醒一句(它得能找到 DSH)。</summary>
        private void CheckWarnings()
        {
            try
            {
                string launcher = PathBox.Text == null ? string.Empty : PathBox.Text.Trim();
                string dsh = InstallSession.Current.DshRoot;

                if (string.IsNullOrEmpty(launcher) || string.IsNullOrEmpty(dsh))
                {
                    WarnText.Text = string.Empty;
                    return;
                }

                string expected = Path.GetFullPath(Path.Combine(dsh, WellKnown.LauncherFolder));
                string actual = Path.GetFullPath(launcher);

                string note = null;
                if (!string.Equals(expected.TrimEnd('\\'), actual.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    note = Localization.IsChinese
                        ? "提示：启动器不在 DSH 目录下时，将向上层目录查找 DSH；放置在其他位置可能无法定位，需在启动器同目录写入 launcher.json 指定 dshRoot。"
                        : "Note: outside the DSH folder the launcher searches upward for DSH; it may need a launcher.json with dshRoot.";
                }

                // "仅为本用户安装"却挑了个要管理员权限的目录 —— 当场说清楚,别等点安装才弹 UAC
                string elevation = PathAccess.DescribeIfNeedsElevation(launcher);
                if (!string.IsNullOrEmpty(elevation))
                {
                    note = string.IsNullOrEmpty(note) ? elevation : note + "\n" + elevation;
                }

                WarnText.Text = note ?? string.Empty;
            }
            catch
            {
                WarnText.Text = string.Empty;
            }
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            string picked = FolderDialog.Pick(
                Localization.IsChinese ? "选择启动器的安装目录" : "Choose the launcher directory",
                PathBox.Text);

            if (!string.IsNullOrEmpty(picked))
            {
                PathBox.Text = picked;
            }
        }
    }
}
