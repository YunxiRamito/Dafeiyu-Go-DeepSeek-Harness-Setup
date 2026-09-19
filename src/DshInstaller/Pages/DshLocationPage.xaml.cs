using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Shared;

namespace DshInstaller.Pages
{
    /// <summary>DSH 本体装在哪。已经装过就给"直接用现有的"。</summary>
    public sealed partial class DshLocationPage : Page, IWizardPage
    {
        private string _existingRoot;

        public DshLocationPage()
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

            InstallSession.Current.DshRoot = path;
            InstallSession.Current.DshRootChosen = true;

            // 启动器默认放在 DSH 目录下的子目录。
            // 判据是"用户有没有自己定过",而不是"现在是不是空的" ——
            // 用后者的话,来回翻页时会被反复覆盖。
            if (!InstallSession.Current.LauncherChosen)
            {
                InstallSession.Current.LauncherRoot = Path.Combine(path, WellKnown.LauncherFolder);
            }

            return true;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.T("dsh.title");
            Scaffold.Subtitle = Localization.T("dsh.desc");
            Scaffold.SetStep(2);

            PathLabel.Text = Localization.IsChinese ? "安装到" : "Install to";
            BrowseButton.Content = Localization.T("btn.browse");
            HintText.Text = Localization.IsChinese ? "DSH 本体将会安装在这个目录。" : "The DSH core will be installed to this directory.";

            ExistingTitle.Text = Localization.IsChinese ? "本机已安装 DSH" : "DSH is already installed on this machine";
            UseExistingBox.Content = Localization.IsChinese ? "使用现有安装，跳过下载" : "Use the existing installation and skip the download";
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ProbeReport report = InstallSession.Current.Report;
            ComponentStatus dsh = report != null ? report["dsh"] : null;

            if (dsh != null && dsh.IsSatisfied && !string.IsNullOrEmpty(dsh.Path))
            {
                _existingRoot = dsh.Path;
                ExistingBox.Visibility = Visibility.Visible;
                ExistingPath.Text = dsh.Path + (string.IsNullOrEmpty(dsh.DetectedVersion) ? string.Empty : "   v" + dsh.DetectedVersion);
                UseExistingBox.IsChecked = true;
                PathBox.Text = dsh.Path;
            }
            else
            {
                PathBox.Text = string.IsNullOrEmpty(InstallSession.Current.DshRoot)
                    ? InstallSession.Current.DefaultRoot
                    : InstallSession.Current.DshRoot;
            }

            UpdateDiskHint();
            PathBox.TextChanged += delegate
            {
                UpdateDiskHint();
                // 路径是这里才填上的,按钮的可用状态得跟着重算
                // (否则"下一步"会一直是灰的 —— 那个坑真踩过)
                MainWindow window = App.MainWindowInstance;
                if (window != null)
                {
                    window.RefreshChrome();
                }
            };

            MainWindow current = App.MainWindowInstance;
            if (current != null)
            {
                current.RefreshChrome();
            }
        }

        private void UpdateDiskHint()
        {
            try
            {
                string path = PathBox.Text;
                if (string.IsNullOrWhiteSpace(path))
                {
                    DiskHint.Text = string.Empty;
                    return;
                }

                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root))
                {
                    DiskHint.Text = string.Empty;
                    return;
                }

                DriveInfo drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    DiskHint.Text = string.Empty;
                    return;
                }

                double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                string note = Localization.IsChinese
                    ? string.Format("{0} 可用 {1:N1} GB · DSH 本体大约需要 300 MB", root, freeGb)
                    : string.Format("{0} has {1:N1} GB free · DSH needs about 300 MB", root, freeGb);

                // "仅为本用户安装"却挑了个要管理员权限的目录 —— 当场说清楚,别等点安装才弹 UAC
                string elevation = PathAccess.DescribeIfNeedsElevation(path);
                if (!string.IsNullOrEmpty(elevation))
                {
                    note = note + "\n" + elevation;
                }

                DiskHint.Text = note;
            }
            catch
            {
                DiskHint.Text = string.Empty;
            }
        }

        private void OnUseExistingChanged(object sender, RoutedEventArgs e)
        {
            if (UseExistingBox.IsChecked == true && !string.IsNullOrEmpty(_existingRoot))
            {
                PathBox.Text = _existingRoot;
            }
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            string picked = FolderDialog.Pick(
                Localization.IsChinese ? "选择 DSH 本体的安装目录" : "Choose the DSH installation directory",
                PathBox.Text);

            if (!string.IsNullOrEmpty(picked))
            {
                PathBox.Text = picked;
            }
        }
    }
}
