using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DshInstaller.Shared.Backup;
using DeepSeekHarnessLauncher.Backup;
using DshInstaller.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 装完之后的导入页。
    ///
    /// 为什么单独占一页(而不是塞进完成页):重装/换机的人最需要的就是"把原来那份配置、
    /// 技能、插件搬回来",而这件事在完成页上很容易被忽略掉。这一页只问这一件事:
    /// 有没有备份、有就现在还原、没有就跳过。
    ///
    /// 进度文案是刻意写成「正在恢复 技能 (2/12) · xxx/SKILL.md」的:
    /// 恢复几十上百个文件时,只有一个百分比会让人怀疑它是不是卡死了。
    /// </summary>
    public sealed partial class ImportPage : Page, IWizardPage, IWizardPageCloseGuard
    {
        private readonly List<BackupGroup> _groups = new List<BackupGroup>();

        private readonly Dictionary<string, CheckBox> _boxes =
            new Dictionary<string, CheckBox>();

        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

        private string _archive;

        private bool _busy;

        private bool _imported;

        private OfficialImportPlan _officialPlan;
        private string _officialSource;
        private readonly Dictionary<string, CheckBox> _officialBoxes = new Dictionary<string, CheckBox>();
        private bool _officialBusy;
        private bool _fillingProfiles;
        private CancellationTokenSource _officialCancellation;
        private bool _confirmationOpen;
        private CancellationTokenSource _discoveryCancellation;
        private DateTime _discoveryProgressUtc;

        public ImportPage()
        {
            InitializeComponent();
            BuildConflictChoices();
            ApplyText();
        }

        public bool CanGoNext
        {
            get { return !_busy && !_confirmationOpen && _discoveryCancellation == null; }
        }

        /// <summary>点"下一步"就是"去完成页" —— 导入是可选的,跳过也该顺顺当当走下去。</summary>
        public bool OnNext()
        {
            if (_busy || _confirmationOpen || _discoveryCancellation != null) return false;
            InstallSession.Current.ImportedBackup = _imported;
            return true;
        }

        public bool CanClose => !_busy && _discoveryCancellation == null;

        public void OnCloseRequested()
        {
            _officialCancellation?.Cancel();
            _discoveryCancellation?.Cancel();
            StatusText.Text = Localization.IsChinese ? "正在完成当前数据操作，请稍候。" : "Finishing the current data operation. Please wait.";
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.IsChinese ? "导入原有数据" : "Import your data";
            Scaffold.Subtitle = Localization.IsChinese
                ? "你是否之前安装过 DSH 或者 Dafeiyu-Go？若没有，请点击下一步。"
                : "Have you installed DSH or Dafeiyu-Go before? If not, click Next.";

            Scaffold.SetStep(8);

            PickTitle.Text = Localization.IsChinese ? "导入来源" : "Import source";
            PickDesc.Text = Localization.IsChinese
                ? "从旧的客户端恢复备份。"
                : "Restore a backup from an older client.";

            string dymAction = Localization.IsChinese ? "从旧的 Dafeiyu-Go 导入" : "Import from old Dafeiyu-Go";
            string dshAction = Localization.IsChinese ? "从旧的 DSH 客户端导入" : "Import from old DSH client";
            PickButton.Content = new TextBlock { Text = dymAction, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            PickDshButton.Content = new TextBlock { Text = dshAction, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PickButton, dymAction);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PickDshButton, dshAction);
            DiscoverButton.Content = Localization.IsChinese ? "自动查找" : "Find existing data";
            DiscoverDrivesButton.Content = Localization.IsChinese ? "扫描其他磁盘" : "Scan other drives";
            CancelDiscoveryButton.Content = Localization.IsChinese ? "取消查找" : "Cancel search";
            DshProfileBox.Header = Localization.IsChinese ? "来源 Profile" : "Source profile";
            ImportButton.Content = Localization.IsChinese ? "开始导入" : "Start import";
            CancelOfficialImportButton.Content = Localization.IsChinese ? "取消导入" : "Cancel import";

            ArchiveText.Text = String.Empty;
            ArchiveText.Visibility = Visibility.Collapsed;

            GroupsTitle.Text = Localization.IsChinese ? "要恢复哪些" : "What to restore";

            ConflictLabel.Text = Localization.IsChinese
                ? "碰到同名文件："
                : "When a file already exists:";

            StatusText.Text = String.Empty;
        }

        private void BuildConflictChoices()
        {
            AddConflict(
                Localization.IsChinese ? "覆盖（推荐）" : "Overwrite (recommended)",
                ConflictPolicy.Overwrite);
            AddConflict(
                Localization.IsChinese ? "跳过已有的，只补缺的" : "Skip existing",
                ConflictPolicy.Skip);
            AddConflict(
                Localization.IsChinese ? "两个都留（新的改名成 .imported）" : "Keep both",
                ConflictPolicy.Ask);

            ConflictBox.SelectedIndex = 0;
        }

        private void AddConflict(string text, ConflictPolicy policy)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = text;
            item.Tag = policy;
            ConflictBox.Items.Add(item);
        }

        private async void DiscoverButton_Click(object sender, RoutedEventArgs args) => await DiscoverAsync(false);

        private async void DiscoverDrivesButton_Click(object sender, RoutedEventArgs args) => await DiscoverAsync(true);

        private void CancelDiscoveryButton_Click(object sender, RoutedEventArgs args)
        {
            _discoveryCancellation?.Cancel();
            DiscoveryStatus.Text = Localization.IsChinese ? "正在取消查找…" : "Canceling search…";
            CancelDiscoveryButton.IsEnabled = false;
        }

        private async Task DiscoverAsync(bool allDrives)
        {
            if (_busy || _confirmationOpen || _discoveryCancellation != null) return;
            using var cancellation = new CancellationTokenSource();
            _discoveryCancellation = cancellation;
            App.MainWindowInstance?.RefreshChrome();
            DiscoveryProgressPanel.Visibility = Visibility.Visible;
            DiscoveryProgressRing.IsActive = true;
            CancelDiscoveryButton.IsEnabled = true;
            DiscoverButton.IsEnabled = false;
            DiscoverDrivesButton.IsEnabled = false;
            PickButton.IsEnabled = false;
            PickDshButton.IsEnabled = false;
            StatusText.Text = String.Empty;
            DiscoveryStatus.Text = Localization.IsChinese ? "正在查找原有数据…" : "Searching for existing data…";
            _discoveryProgressUtc = DateTime.MinValue;
            DshDataDiscoveryResult result = null;
            try
            {
                List<string> roots = DiscoveryRoots(allDrives);
                List<string> excluded = new List<string>
                {
                    TargetDshRoot(), InstallSession.Current.LauncherRoot,
                    InstallSession.Current.ComponentsRoot, AppDomain.CurrentDomain.BaseDirectory
                };
                result = await Task.Run(() => DshDataDiscoveryService.Discover(roots, excluded, cancellation.Token,
                    (path, count) =>
                    {
                        if ((DateTime.UtcNow - _discoveryProgressUtc).TotalMilliseconds < 150) return;
                        _discoveryProgressUtc = DateTime.UtcNow;
                        _dispatcher.TryEnqueue(() =>
                        {
                            if (_discoveryCancellation != cancellation || cancellation.IsCancellationRequested) return;
                            DiscoveryStatus.Text = Localization.IsChinese
                                ? "已扫描 " + count + " 个文件夹 · " + path
                                : "Searched " + count + " folders · " + path;
                        });
                    }, maximumDepth: allDrives ? 24 : 4, maximumDirectories: allDrives ? 100000 : 4096,
                    maximumFileSystemEntries: allDrives ? 1000000 : 100000));
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = Localization.IsChinese ? "已取消查找。" : "Search canceled.";
            }
            catch (Exception exception) { StatusText.Text = exception.Message; }
            finally
            {
                _discoveryCancellation = null;
                DiscoveryProgressRing.IsActive = false;
                DiscoveryProgressPanel.Visibility = Visibility.Collapsed;
                DiscoverButton.IsEnabled = true;
                DiscoverDrivesButton.IsEnabled = true;
                PickButton.IsEnabled = true;
                PickDshButton.IsEnabled = true;
                App.MainWindowInstance?.RefreshChrome();
            }
            if (result == null) return;
            if (result.Entries.Count == 0)
            {
                StatusText.Text = Localization.IsChinese
                    ? (result.Truncated ? "查找未覆盖所有位置，未找到数据。可以扫描其他磁盘，或手动选择来源。" : "未找到原有数据。可以扫描其他磁盘，或手动选择来源。")
                    : (result.Truncated ? "The search did not cover every location and found no data. Scan other drives or choose a source manually." : "No existing data found. Scan other drives or choose a source manually.");
                return;
            }
            StatusText.Text = result.Truncated
                ? (Localization.IsChinese ? "已找到部分来源，查找未覆盖所有位置。" : "Some sources were found; the search did not cover every location.")
                : String.Empty;
            DshDataDiscoveryEntry selected = result.Entries.Count == 1
                ? result.Entries[0] : await SelectDiscoveredSourceAsync(result);
            if (selected == null) return;
            if (selected.Kind == DshDataDiscoveryEntry.DymKind) await SelectDymAsync(selected.Path);
            else await SelectDshAsync(selected.Path);
        }

        private List<string> DiscoveryRoots(bool allDrives)
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new List<string>
            {
                Path.Combine(user, ".dsh"), Path.Combine(user, "DeepSeek Harness"),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(user, "Downloads")
            };
            string configuredHome = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!String.IsNullOrWhiteSpace(configuredHome)) roots.Add(configuredHome);
            string target = TargetDshRoot();
            if (!String.IsNullOrWhiteSpace(target)) roots.Add(Path.GetDirectoryName(Path.GetFullPath(target)));
            if (allDrives)
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                    try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) roots.Add(drive.RootDirectory.FullName); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
            return roots;
        }

        private string TargetDshRoot() => DevOptions.DryRun && String.IsNullOrWhiteSpace(InstallSession.Current.DshRoot)
            ? DevOptions.DshRoot : InstallSession.Current.DshRoot;

        private async Task<DshDataDiscoveryEntry> SelectDiscoveredSourceAsync(DshDataDiscoveryResult result)
        {
            var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 320 };
            foreach (DshDataDiscoveryEntry entry in result.Entries)
            {
                var text = new StackPanel { Spacing = 3, Padding = new Thickness(0, 6, 0, 6) };
                text.Children.Add(new TextBlock
                {
                    Text = entry.Kind == DshDataDiscoveryEntry.DymKind
                        ? (Localization.IsChinese ? "Dafeiyu-Go 备份 · .dym" : "Dafeiyu-Go backup · .dym")
                        : (Localization.IsChinese ? "DSH 客户端数据" : "DSH client data"),
                    FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
                text.Children.Add(new TextBlock { Text = entry.Path, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                list.Items.Add(new ListViewItem { Content = text, Tag = entry });
            }
            list.SelectedIndex = 0;
            var content = new StackPanel { Spacing = 10, MinWidth = 360, MaxWidth = 520 };
            if (result.Truncated)
                content.Children.Add(new TextBlock { Text = Localization.IsChinese ? "查找未覆盖所有位置，下面是已找到的来源。" : "The search did not cover every location. Available sources are shown below.", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(list);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = Localization.IsChinese ? "选择导入来源" : "Choose import source",
                Content = content, PrimaryButtonText = Localization.IsChinese ? "选择" : "Select",
                CloseButtonText = Localization.IsChinese ? "取消" : "Cancel", DefaultButton = ContentDialogButton.Close
            };
            _confirmationOpen = true;
            App.MainWindowInstance?.RefreshChrome();
            try
            {
                return await dialog.ShowAsync() == ContentDialogResult.Primary
                    ? (list.SelectedItem as ListViewItem)?.Tag as DshDataDiscoveryEntry : null;
            }
            finally { _confirmationOpen = false; App.MainWindowInstance?.RefreshChrome(); }
        }

        private async void PickButton_Click(object sender, RoutedEventArgs args)
        {
            if (_busy || _discoveryCancellation != null || _confirmationOpen)
            {
                return;
            }

            string picked = FolderDialog.PickFile(
                Localization.IsChinese ? "选择备份文件" : "Choose a backup file",
                null);

            if (String.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            await SelectDymAsync(picked);
        }

        private async Task SelectDymAsync(string picked)
        {
            if (!picked.EndsWith(DymArchive.Extension, StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = Localization.IsChinese
                    ? "这不是 .dym 备份包。请选导出时生成的那个文件。"
                    : "That is not a .dym backup file.";
                return;
            }

            _archive = picked;
            PickTitle.Text = Localization.IsChinese ? "备份文件" : "Backup file";
            _officialPlan = null;
            _officialSource = null;
            DshProfileBox.Visibility = Visibility.Collapsed;
            DshImportSummary.Visibility = Visibility.Collapsed;
            ConflictBox.Visibility = Visibility.Visible;
            ConflictLabel.Visibility = Visibility.Visible;
            ArchiveText.Text = picked;
            ArchiveText.Visibility = Visibility.Visible;
            StatusText.Text = Localization.IsChinese ? "正在读取备份内容…" : "Reading the backup…";
            GroupsHost.Children.Clear();
            _boxes.Clear();
            _groups.Clear();
            GroupsCard.Visibility = Visibility.Collapsed;
            PickButton.IsEnabled = false;
            PickDshButton.IsEnabled = false;
            DiscoverButton.IsEnabled = false;
            DiscoverDrivesButton.IsEnabled = false;
            _busy = true;

            try
            {
                List<BackupGroup> groups = await Task.Run(() => UserDataBackup.DescribeFromArchive(picked, null));
                FillGroups(groups);
            }
            catch (Exception exception) { StatusText.Text = exception.Message; }
            finally
            {
                PickButton.IsEnabled = true;
                PickDshButton.IsEnabled = true;
                DiscoverButton.IsEnabled = true;
                DiscoverDrivesButton.IsEnabled = true;
                _busy = false;
            }
            if (_groups.Count > 0) await ShowImportConfirmationAsync(ImportDymAsync);
        }

        private async void PickDshButton_Click(object sender, RoutedEventArgs args)
        {
            if (_busy || _officialBusy || _discoveryCancellation != null || _confirmationOpen) return;
            string picked = FolderDialog.Pick(
                Localization.IsChinese ? "选择 DSH 数据文件夹" : "Choose DSH data folder", null);
            if (String.IsNullOrWhiteSpace(picked)) return;
            await SelectDshAsync(picked);
        }

        private async Task SelectDshAsync(string picked)
        {
            _busy = true;
            PickButton.IsEnabled = false;
            PickDshButton.IsEnabled = false;
            DiscoverButton.IsEnabled = false;
            DiscoverDrivesButton.IsEnabled = false;
            try
            {
                string home = await Task.Run(() => DshDataImportService.ResolveSourceHome(picked));
                _officialSource = picked;
                List<string> profiles = await Task.Run(() => DshDataImportService.ListProfiles(home));
                _fillingProfiles = true;
                DshProfileBox.Items.Clear();
                foreach (string profile in profiles)
                    DshProfileBox.Items.Add(new ComboBoxItem { Content = profile, Tag = profile });
                DshProfileBox.SelectedIndex = profiles.Count == 0 ? -1 : Math.Max(0,
                    profiles.FindIndex(profile => profile.Equals("default", StringComparison.OrdinalIgnoreCase)));
                _fillingProfiles = false;
                DshProfileBox.Visibility = profiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                ArchiveText.Text = home;
                ArchiveText.Visibility = Visibility.Visible;
                PickTitle.Text = Localization.IsChinese ? "DSH 数据文件夹" : "DSH data folder";
                ConflictBox.Visibility = Visibility.Collapsed;
                ConflictLabel.Visibility = Visibility.Collapsed;
                _archive = null;
                _officialPlan = null;
                await LoadOfficialPreviewAsync();
            }
            catch (Exception exception)
            {
                _officialSource = null;
                _officialPlan = null;
                _archive = null;
                GroupsCard.Visibility = Visibility.Collapsed;
                StatusText.Text = exception.Message;
            }
            finally
            {
                _fillingProfiles = false;
                _busy = false;
                PickButton.IsEnabled = true;
                PickDshButton.IsEnabled = true;
                DiscoverButton.IsEnabled = true;
                DiscoverDrivesButton.IsEnabled = true;
            }
            if (_officialPlan != null) await ShowImportConfirmationAsync(() => ImportOfficialAsync(true));
        }

        private async void DshProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (_fillingProfiles || _busy || String.IsNullOrWhiteSpace(_officialSource)) return;
            _busy = true;
            PickButton.IsEnabled = false;
            PickDshButton.IsEnabled = false;
            DshProfileBox.IsEnabled = false;
            try { await LoadOfficialPreviewAsync(); }
            finally
            {
                _busy = false;
                PickButton.IsEnabled = true;
                PickDshButton.IsEnabled = true;
                DshProfileBox.IsEnabled = true;
            }
        }

        private async Task LoadOfficialPreviewAsync()
        {
            _officialPlan = null;
            GroupsCard.Visibility = Visibility.Collapsed;
            try
            {
                string targetRoot = InstallSession.Current.DshRoot;
                if (DevOptions.DryRun && String.IsNullOrWhiteSpace(targetRoot)) targetRoot = DevOptions.DshRoot;
                if (String.IsNullOrWhiteSpace(targetRoot))
                    throw new InvalidOperationException(Localization.IsChinese ? "安装目标目录未设置。" : "The installation target is not configured.");
                string targetHome = Path.Combine(targetRoot, ".dsh");
                string targetProfile = "web";
                string sourceProfile = (DshProfileBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "default";
                string source = _officialSource;
                _officialPlan = await Task.Run(() => DshDataImportService.Preview(source, targetHome, sourceProfile, targetProfile, CancellationToken.None));
                _officialBoxes.Clear();
                GroupsHost.Children.Clear();
                foreach (OfficialImportGroup group in _officialPlan.Groups)
                {
                    string name = Localization.IsChinese ? group.Name : group.Id == "sessions" ? "Sessions" : group.Id == "skills" ? "Skills" : "Plug-ins and enablement list";
                    CheckBox box = new CheckBox { Content = new TextBlock { Text = name + " · " + group.Files + " " +
                        (Localization.IsChinese ? "个文件" : "files") + " · " + (group.Bytes / (1024d * 1024d)).ToString("0.##") + " MiB", TextWrapping = TextWrapping.Wrap }, IsChecked = true };
                    box.Checked += delegate { RefreshOfficialSummary(); };
                    box.Unchecked += delegate { RefreshOfficialSummary(); };
                    _officialBoxes[group.Id] = box;
                    GroupsHost.Children.Add(box);
                }
                RefreshOfficialSummary();
                DshImportSummary.Visibility = Visibility.Visible;
                GroupsCard.Visibility = Visibility.Visible;
                ImportButton.Visibility = Visibility.Visible;
                StatusText.Text = Localization.IsChinese ? "请停止来源和目标 DSH，再开始导入。" : "Stop the source and target DSH before importing.";
            }
            catch (Exception exception)
            {
                _officialPlan = null;
                StatusText.Text = exception.Message;
            }
        }

        private void RefreshOfficialSummary()
        {
            if (_officialPlan == null) return;
            var chosen = _officialPlan.Groups.Where(group => _officialBoxes.TryGetValue(group.Id, out var box) && box.IsChecked == true).ToList();
            DshImportSummary.Text = Localization.IsChinese
                ? "来源：" + _officialPlan.SourceHome + "\n目标：" + _officialPlan.TargetHome + "\n已选 " + chosen.Sum(group => group.Files) + " 个文件；同名项目 " + chosen.Sum(group => group.ExistingUnits) + " 个将保留并跳过。"
                : "Source: " + _officialPlan.SourceHome + "\nTarget: " + _officialPlan.TargetHome + "\n" + chosen.Sum(group => group.Files) + " files selected; " + chosen.Sum(group => group.ExistingUnits) + " existing items will be kept and skipped.";
        }

        private void FillGroups(List<BackupGroup> groups)
        {
            if (groups == null || groups.Count == 0)
            {
                GroupsCard.Visibility = Visibility.Collapsed;
                StatusText.Text = Localization.IsChinese
                    ? "这个包里没有能识别的数据（配置 / 技能 / 插件 / 会话）。换个文件试试。"
                    : "This backup contains nothing recognisable.";
                return;
            }

            for (int index = 0; index < groups.Count; index++)
            {
                BackupGroup group = groups[index];
                _groups.Add(group);

                CheckBox box = new CheckBox();
                box.Content = group.Display(Localization.IsChinese);
                box.IsChecked = group.SelectedByDefault;
                _boxes[group.Id] = box;
                GroupsHost.Children.Add(box);
            }

            GroupsCard.Visibility = Visibility.Visible;
            StatusText.Text = Localization.IsChinese
                ? "勾好要恢复的内容，然后点「开始导入」。也可以直接点下一步跳过。"
                : "Tick what to restore, then start the import.";

            if (ImportButton != null)
            {
                ImportButton.Visibility = Visibility.Visible;
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs args)
        {
            if (_officialPlan != null)
            {
                StartOfficialImport();
                return;
            }
            StartImport();
        }

        private async void StartImport() => await ImportDymAsync();

        private async Task ImportDymAsync()
        {
            if (_busy || String.IsNullOrWhiteSpace(_archive))
            {
                return;
            }

            List<BackupGroup> chosen = new List<BackupGroup>();
            for (int index = 0; index < _groups.Count; index++)
            {
                BackupGroup group = _groups[index];
                CheckBox box;
                if (_boxes.TryGetValue(group.Id, out box) && box.IsChecked == true)
                {
                    chosen.Add(group);
                }
            }

            if (chosen.Count == 0)
            {
                StatusText.Text = Localization.IsChinese
                    ? "一个都没勾,那就没什么可恢复的了。"
                    : "Nothing is ticked, so there is nothing to restore.";
                return;
            }
            if (DevOptions.DryRun)
            {
                StatusText.Text = Localization.IsChinese ? "演练模式仅预览数据，不执行导入。" : "Dry-run mode previews data without importing.";
                return;
            }

            ConflictPolicy policy = ConflictPolicy.Overwrite;
            ComboBoxItem selected = ConflictBox.SelectedItem as ComboBoxItem;
            if (selected != null && selected.Tag is ConflictPolicy)
            {
                policy = (ConflictPolicy)selected.Tag;
            }

            _busy = true;
            PickButton.IsEnabled = false;
            PickDshButton.IsEnabled = false;
            if (ImportButton != null)
            {
                ImportButton.IsEnabled = false;
            }

            ProgressPanel.Visibility = Visibility.Visible;
            ImportProgress.IsIndeterminate = false;
            ImportProgress.Value = 0;
            ImportDetail.Text = Localization.IsChinese ? "准备中…" : "Preparing…";
            StatusText.Text = String.Empty;

            string dshRoot = InstallSession.Current.DshRoot;

            // 「两个都留」在逻辑层是"问调用方"这一档;这里的答案是"永远两个都留",
            // 不弹一百个对话框去打断用户。
            Func<string, ConflictChoice> ask = delegate
            {
                return ConflictChoice.KeepBoth;
            };

            string error = null;
            bool ok = await Task.Run(delegate
            {
                string innerError;
                bool result = UserDataBackup.Import(
                    _archive,
                    dshRoot,
                    chosen,
                    policy,
                    ask,
                    delegate(string text, double percent)
                    {
                        _dispatcher.TryEnqueue(delegate
                        {
                            if (text != null)
                            {
                                ImportDetail.Text = text;
                            }

                            ImportProgress.Value = Math.Max(0, Math.Min(100, percent));
                        });
                    },
                    null,
                    CancellationToken.None,
                    out innerError);

                error = innerError;
                return result;
            });

            _busy = false;
            PickButton.IsEnabled = true;
            PickDshButton.IsEnabled = true;
            if (ImportButton != null)
            {
                ImportButton.IsEnabled = true;
            }

            if (ok)
            {
                _imported = true;
                ImportProgress.Value = 100;
                ImportDetail.Text = Localization.IsChinese ? "恢复完成。" : "Restore complete.";
                StatusText.Text = Localization.IsChinese
                    ? "数据已经搬回来了。点下一步结束安装（重开 DSH 后生效）。"
                    : "Your data is back. Continue to finish (restart DSH to apply).";
            }
            else
            {
                ImportDetail.Text = Localization.IsChinese ? "恢复失败。" : "Restore failed.";
                StatusText.Text = String.IsNullOrWhiteSpace(error)
                    ? (Localization.IsChinese ? "恢复失败,原因未知。" : "Restore failed.")
                    : (Localization.IsChinese ? "恢复失败：" : "Restore failed: ") + error;
            }
        }

        private async void StartOfficialImport() => await ImportOfficialAsync(false);

        private async Task ImportOfficialAsync(bool confirmed)
        {
            if (_officialBusy || _officialPlan == null) return;
            List<string> selected = _officialBoxes.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList();
            if (selected.Count == 0) return;
            if (DevOptions.DryRun)
            {
                StatusText.Text = Localization.IsChinese ? "演练模式仅预览数据，不执行导入。" : "Dry-run mode previews data without importing.";
                return;
            }
            _officialBusy = true;
            _busy = true;
            PickButton.IsEnabled = false;
            PickDshButton.IsEnabled = false;
            ImportButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ImportProgress.Value = 0;
            ImportDetail.Text = Localization.IsChinese ? "准备中…" : "Preparing…";
            try
            {
                OfficialImportPlan plan = _officialPlan;
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = Localization.IsChinese ? "导入 DSH 数据" : "Import DSH data",
                    Content = new TextBlock { Text = DshImportSummary.Text + "\n\n" + (Localization.IsChinese
                        ? "同名项目保留并跳过，来源文件夹保持不变。" : "Existing items will be kept. The source folder stays unchanged."), TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = Localization.IsChinese ? "导入" : "Import",
                    CloseButtonText = Localization.IsChinese ? "取消" : "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };
                if (!confirmed && await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                string targetRoot = InstallSession.Current.DshRoot;
                bool running = await Task.Run(() => DshInstaller.Shared.DshLocator.IsRootRunning(targetRoot));
                if (running)
                {
                    StatusText.Text = Localization.IsChinese ? "请先停止目标 DSH 服务，再导入数据。" : "Stop the target DSH service before importing.";
                    return;
                }
                using var cancellation = new CancellationTokenSource();
                _officialCancellation = cancellation;
                CancelOfficialImportButton.Visibility = Visibility.Visible;
                DshProfileBox.IsEnabled = false;
                foreach (var box in _officialBoxes.Values) box.IsEnabled = false;
                OfficialImportResult result = await Task.Run(() => DshDataImportService.Import(plan, selected,
                    (text, percent) => _dispatcher.TryEnqueue(delegate
                    {
                        ImportDetail.Text = text;
                        ImportProgress.Value = Math.Max(0, Math.Min(100, percent));
                    }), cancellation.Token));
                _imported |= result.Imported > 0;
                ImportProgress.Value = result.Ok ? 100 : ImportProgress.Value;
                ImportDetail.Text = result.Ok ? result.Summary : (result.Canceled ? (Localization.IsChinese ? "已取消，已导入的数据保留。" : "Canceled. Imported data is retained.")
                    : result.Error ?? (Localization.IsChinese ? "导入失败" : "Import failed")) + "\n" + result.Summary;
                StatusText.Text = result.Ok ? (Localization.IsChinese ? "DSH 数据已经导入，完成安装后重启服务生效。" : "DSH data imported. Restart the service after setup.")
                    : (Localization.IsChinese ? "DSH 数据导入未完成。" : "DSH data import did not complete.");
            }
            catch (Exception exception)
            {
                ImportDetail.Text = exception.Message;
                StatusText.Text = Localization.IsChinese ? "DSH 数据导入失败。" : "DSH data import failed.";
            }
            finally
            {
                _officialBusy = false;
                _busy = false;
                PickButton.IsEnabled = true;
                PickDshButton.IsEnabled = true;
                ImportButton.IsEnabled = true;
                DshProfileBox.IsEnabled = true;
                foreach (var box in _officialBoxes.Values) box.IsEnabled = true;
                _officialCancellation = null;
                CancelOfficialImportButton.Visibility = Visibility.Collapsed;
            }
        }

        private void CancelOfficialImportButton_Click(object sender, RoutedEventArgs args)
        {
            _officialCancellation?.Cancel();
            ImportDetail.Text = Localization.IsChinese ? "正在取消…" : "Canceling…";
        }

        private async Task ShowImportConfirmationAsync(Func<Task> import)
        {
            var parent = GroupsCard.Parent as Panel;
            if (parent == null) return;
            int index = parent.Children.IndexOf(GroupsCard);
            parent.Children.Remove(GroupsCard);
            GroupsCard.Visibility = Visibility.Visible;
            var groupBackground = GroupsCard.Background;
            Thickness groupBorder = GroupsCard.BorderThickness;
            Thickness groupPadding = GroupsCard.Padding;
            GroupsCard.Background = null;
            GroupsCard.BorderThickness = new Thickness(0);
            GroupsCard.Padding = new Thickness(0);
            ImportButton.Visibility = Visibility.Collapsed;
            var content = new StackPanel { Spacing = 12 };
            if (_officialPlan == null)
                content.Children.Add(new TextBlock
                {
                    Text = (Localization.IsChinese ? "来源：" : "Source: ") + ArchiveText.Text,
                    TextWrapping = TextWrapping.Wrap, FontSize = 12.5
                });
            var profileParent = DshProfileBox.Parent as Panel;
            int profileIndex = profileParent.Children.IndexOf(DshProfileBox);
            profileParent.Children.Remove(DshProfileBox);
            content.Children.Add(DshProfileBox);
            content.Children.Add(GroupsCard);
            var progressParent = ProgressPanel.Parent as Panel;
            int progressIndex = progressParent.Children.IndexOf(ProgressPanel);
            progressParent.Children.Remove(ProgressPanel);
            content.Children.Add(ProgressPanel);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = Localization.IsChinese ? "导入数据" : "Import data",
                Content = new ScrollViewer { Content = content, MaxHeight = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = Localization.IsChinese ? "导入" : "Import",
                CloseButtonText = Localization.IsChinese ? "取消" : "Cancel", DefaultButton = ContentDialogButton.Close
            };
            dialog.PrimaryButtonClick += async (sender, args) =>
            {
                var deferral = args.GetDeferral();
                args.Cancel = true;
                dialog.IsPrimaryButtonEnabled = false;
                dialog.CloseButtonText = String.Empty;
                try { await import(); dialog.Hide(); }
                finally { deferral.Complete(); }
            };
            _confirmationOpen = true;
            App.MainWindowInstance?.RefreshChrome();
            try { await dialog.ShowAsync(); }
            finally
            {
                _confirmationOpen = false;
                App.MainWindowInstance?.RefreshChrome();
                content.Children.Remove(GroupsCard);
                content.Children.Remove(DshProfileBox);
                DshProfileBox.Visibility = Visibility.Collapsed;
                profileParent.Children.Insert(profileIndex, DshProfileBox);
                content.Children.Remove(ProgressPanel);
                GroupsCard.Visibility = Visibility.Collapsed;
                GroupsCard.Background = groupBackground;
                GroupsCard.BorderThickness = groupBorder;
                GroupsCard.Padding = groupPadding;
                parent.Children.Insert(index, GroupsCard);
                progressParent.Children.Insert(progressIndex, ProgressPanel);
            }
        }
    }
}
