using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Shared;

namespace DshInstaller.Pages
{
    public sealed partial class DshLocationPage : Page, IWizardPage
    {
        private bool _initializing = true;
        private string _newRoot;
        private string _choice;
        private DshInstallation Selected => (InstallationBox.SelectedItem as ComboBoxItem)?.Tag as DshInstallation;

        public DshLocationPage()
        {
            InitializeComponent();
            ApplyText();
            Loaded += OnLoaded;
        }

        public bool CanGoNext => !String.IsNullOrWhiteSpace(PathBox.Text)
            && (_choice == "fresh" || (_choice == "reuse" && Selected?.Kind == DshInstallationKind.Npm)
                || (_choice == "migrate" && Selected?.Kind == DshInstallationKind.OfficialDesktop
                    && !String.IsNullOrWhiteSpace(MigrationHomeBox.Text)));

        public bool OnNext()
        {
            if (!CanGoNext) return false;
            var session = InstallSession.Current;
            string path;
            try
            {
                if (!Path.IsPathFullyQualified(PathBox.Text.Trim())) throw new IOException("请选择完整的安装路径。");
                path = Path.GetFullPath(PathBox.Text.Trim());
                var detected = DshLocator.InspectInstallation(path);
                if (_choice == "reuse" && detected?.Kind != DshInstallationKind.Npm)
                    throw new IOException("所选 DSH 已不存在，请重新选择。");
                if (_choice != "reuse" && detected != null)
                    throw new IOException("全新安装和迁移需要独立目录，请选择其他安装位置。");
                string migrationHome = _choice == "migrate" ? MigrationHomeBox.Text.Trim() : null;
                if (migrationHome != null && (!Path.IsPathFullyQualified(migrationHome) || !Directory.Exists(migrationHome)))
                    throw new IOException("请指定有效的官方版 DSH 数据目录。");
                var options = InstallerPlan.Build();
                options.DshRoot = path;
                if (!session.LauncherChosen) options.LauncherRoot = Path.Combine(path, WellKnown.LauncherFolder);
                options.OfficialMigrationInstallRoot = _choice == "migrate" ? Selected.Root : null;
                options.OfficialMigrationHome = migrationHome;
                string validation = options.Validate();
                if (validation != null) throw new IOException(validation);
                session.OfficialMigrationInstallRoot = options.OfficialMigrationInstallRoot;
                session.OfficialMigrationHome = migrationHome;
            }
            catch (Exception exception)
            {
                ValidationMessage.Message = exception.Message;
                ValidationMessage.IsOpen = true;
                return false;
            }
            session.DshRoot = path;
            session.DshRootChosen = true;
            session.DshChoice = _choice;
            session.SelectedDshRoot = Selected?.Root;
            session.UseExistingDsh = _choice == "reuse";
            if (!session.LauncherChosen) session.LauncherRoot = Path.Combine(path, WellKnown.LauncherFolder);
            return true;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.T("dsh.title");
            Scaffold.Subtitle = Localization.T("dsh.desc");
            Scaffold.SetStep(3);
            PathLabel.Text = Localization.IsChinese ? "安装到" : "Install to";
            BrowseButton.Content = Localization.T("btn.browse");
            HintText.Text = Localization.IsChinese ? "DSH、插件和 Skill 将使用所选安装位置。" : "DSH, plug-ins and skills will use the selected installation.";
            ExistingTitle.Text = Localization.IsChinese ? "发现已有 DSH" : "Existing DSH installations found";
            InstallationBox.PlaceholderText = Localization.IsChinese ? "请选择要使用或迁移的 DSH" : "Choose a DSH installation";
            ReuseButton.Content = Localization.IsChinese ? "使用所选 DSH，跳过下载" : "Use the selected DSH and skip the download";
            MigrateButton.Content = Localization.IsChinese ? "迁移官方版对话、插件和 Skill" : "Migrate official conversations, plug-ins and skills";
            FreshButton.Content = Localization.IsChinese ? "全新安装 DSH" : "Install a fresh DSH";
            MigrationHomeBox.Header = Localization.IsChinese ? "官方版数据目录" : "Official DSH data folder";
            MigrationOptions.Header = Localization.IsChinese ? "更改迁移来源" : "Change migration source";
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _initializing = true;
            var session = InstallSession.Current;
            var candidates = session.Report?.DshInstallations;
            InstallationBox.Items.Clear();
            if (candidates != null)
                foreach (var candidate in candidates)
                    InstallationBox.Items.Add(new ComboBoxItem
                    {
                        Content = new TextBlock { Text = (candidate.Kind == DshInstallationKind.OfficialDesktop ? "官方版" : "DSH")
                            + (String.IsNullOrEmpty(candidate.Version) ? "" : " " + candidate.Version) + "\n" + candidate.Root, TextWrapping = TextWrapping.Wrap },
                        Tag = candidate
                    });
            bool hasOfficial = candidates?.Any(candidate => candidate.Kind == DshInstallationKind.OfficialDesktop) == true;
            ExistingBox.Visibility = InstallationBox.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ExistingDescription.Text = hasOfficial
                ? (Localization.IsChinese ? "暂不适配直接使用官方版，您可以选择迁移官方版对话、插件和 Skill，或全新安装 DSH。官方版及原数据会保留。"
                    : "Direct use of the official app is not supported yet. Migrate its conversations, plug-ins and skills, or install a fresh DSH. The official app and original data are retained.")
                : (Localization.IsChinese ? "请选择要使用的 DSH，也可以另外全新安装。" : "Choose the DSH to use, or install a fresh copy.");
            MigrateButton.Visibility = hasOfficial ? Visibility.Visible : Visibility.Collapsed;
            ReuseButton.Visibility = candidates?.Any(candidate => candidate.Kind == DshInstallationKind.Npm) == true ? Visibility.Visible : Visibility.Collapsed;
            InstallationBox.SelectedItem = InstallationBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                String.Equals(((DshInstallation)item.Tag).Root, session.SelectedDshRoot, StringComparison.OrdinalIgnoreCase));
            if (InstallationBox.SelectedIndex < 0 && InstallationBox.Items.Count == 1) InstallationBox.SelectedIndex = 0;
            _choice = session.DshChoice;
            if (_choice == null && !hasOfficial)
                _choice = InstallationBox.Items.Count == 0 ? "fresh" : InstallationBox.Items.Count == 1 ? "reuse" : null;
            ReuseButton.IsChecked = _choice == "reuse";
            MigrateButton.IsChecked = _choice == "migrate";
            FreshButton.IsChecked = _choice == "fresh";
            _newRoot = session.DshRootChosen && !session.UseExistingDsh ? session.DshRoot : FindFreshRoot(session.DefaultRoot);
            MigrationHomeBox.Text = session.OfficialMigrationHome ?? ResolveOfficialHome();
            _initializing = false;
            UpdateChoice();
        }

        private static string ResolveOfficialHome()
        {
            string configured = Environment.GetEnvironmentVariable("DSH_HOME");
            return String.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh")
                : Environment.ExpandEnvironmentVariables(configured);
        }

        private static string FindFreshRoot(string preferred)
        {
            string candidate = preferred;
            int index = 0;
            while (Directory.Exists(candidate) || File.Exists(candidate))
                candidate = preferred + "-Dafeiyu-Go" + (++index == 1 ? "" : "-" + index);
            return candidate;
        }

        private void OnInstallationChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            if (Selected?.Kind == DshInstallationKind.OfficialDesktop && _choice == "reuse") _choice = null;
            if (Selected?.Kind == DshInstallationKind.Npm && _choice == "migrate") _choice = null;
            ReuseButton.IsChecked = _choice == "reuse";
            MigrateButton.IsChecked = _choice == "migrate";
            UpdateChoice();
        }

        private void OnChoiceChanged(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            if (_choice != "reuse") _newRoot = PathBox.Text;
            _choice = (sender as RadioButton)?.Tag as string;
            UpdateChoice();
        }

        private void UpdateChoice()
        {
            ReuseButton.IsEnabled = Selected?.Kind == DshInstallationKind.Npm;
            MigrateButton.IsEnabled = Selected?.Kind == DshInstallationKind.OfficialDesktop;
            MigrationOptions.Visibility = _choice == "migrate" ? Visibility.Visible : Visibility.Collapsed;
            PathBox.IsReadOnly = _choice == "reuse";
            BrowseButton.IsEnabled = _choice != "reuse";
            PathBox.Text = _choice == "reuse" && Selected != null ? Selected.Root : _newRoot;
            UpdateDiskHint();
            App.MainWindowInstance?.RefreshChrome();
        }

        private void OnPathChanged(object sender, TextChangedEventArgs e)
        {
            if (_initializing) return;
            if (Object.ReferenceEquals(sender, PathBox) && _choice != "reuse") _newRoot = PathBox.Text;
            ValidationMessage.IsOpen = false;
            UpdateDiskHint();
            App.MainWindowInstance?.RefreshChrome();
        }

        private void UpdateDiskHint()
        {
            try
            {
                string root = Path.GetPathRoot(PathBox.Text);
                if (String.IsNullOrWhiteSpace(root)) { DiskHint.Text = String.Empty; return; }
                var drive = new DriveInfo(root);
                if (!drive.IsReady) { DiskHint.Text = String.Empty; return; }
                double freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
                DiskHint.Text = Localization.IsChinese ? String.Format("{0} 可用 {1:N1} GB · DSH 本体大约需要 300 MB", root, freeGb)
                    : String.Format("{0} has {1:N1} GB free · DSH needs about 300 MB", root, freeGb);
                string elevation = PathAccess.DescribeIfNeedsElevation(PathBox.Text);
                if (!String.IsNullOrEmpty(elevation)) DiskHint.Text += "\n" + elevation;
            }
            catch { DiskHint.Text = String.Empty; }
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            string picked = FolderDialog.Pick(Localization.IsChinese ? "选择 DSH 安装目录" : "Choose DSH installation folder", PathBox.Text);
            if (!String.IsNullOrEmpty(picked)) PathBox.Text = picked;
        }
    }
}
