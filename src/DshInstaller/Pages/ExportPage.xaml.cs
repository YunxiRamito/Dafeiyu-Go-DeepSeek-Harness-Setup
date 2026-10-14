using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DshInstaller.Controls;
using DshInstaller.Shared.Backup;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DshInstaller.Pages
{
    public sealed partial class ExportPage : Page, IWizardPage, IWizardPageCloseGuard
    {
        private readonly List<BackupGroup> _groups = new List<BackupGroup>();
        private readonly Dictionary<string, CheckBox> _boxes = new Dictionary<string, CheckBox>();
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
        private readonly Dictionary<FrameworkElement, Storyboard> _visibilityAnimations = new Dictionary<FrameworkElement, Storyboard>();
        private CancellationTokenSource _cancellation;
        private bool _busy, _initializing = true, _sourceReady, _successful, _sourceConfigFailed, _restoring;
        private string _sourceHome, _dshRoot;
        private readonly string _fileStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);

        public ExportPage()
        {
            InitializeComponent();
            ApplyText();
            SetAutomationIds();
            var session = InstallSession.Current;
            OfficialMode.IsChecked = session.ExportToOfficialDsh;
            DymMode.IsChecked = !session.ExportToOfficialDsh;
            TargetDirectoryBox.Text = session.ExportTargetDirectory ?? string.Empty;
            _successful = session.ExportSucceeded && ValidOutput(session.ExportedBackupPath);
            session.ExportSucceeded = _successful;
            SkipExportBox.IsChecked = session.ExportSkipped;
            _initializing = false;
            UpdateMode();
            _restoring = true;
            LoadSources();
            _restoring = false;
            if (_successful) ShowResult(session.ExportedBackupPath);
            Loaded += delegate { App.MainWindowInstance?.RefreshChrome(); };
        }

        private static string Text(string chinese, string english) => Localization.IsChinese ? chinese : english;
        public bool CanGoNext => !_busy && ((_successful && ValidOutput(InstallSession.Current.ExportedBackupPath)) || SkipExportBox.IsChecked == true);
        public bool CanClose => !_busy;

        public bool OnNext()
        {
            if (!CanGoNext)
            {
                ShowStatus(Text("请先完成导出，或明确选择跳过导出。", "Finish exporting or explicitly choose to skip the export."), InfoBarSeverity.Warning);
                return false;
            }
            var session = InstallSession.Current;
            session.ExportSkipped = !_successful && SkipExportBox.IsChecked == true;
            session.Mode = SessionMode.Uninstall;
            // Returning to removal options always starts with retaining data.
            if (session.UninstallOptions != null)
            {
                session.UninstallOptions.KeepUserData = true;
                session.UninstallOptions.RemoveUserData = false;
            }
            return true;
        }

        public void OnCloseRequested() => CancelExport();

        private void ApplyText()
        {
            Scaffold.Title = Text("卸载前导出数据", "Export data before uninstalling");
            Scaffold.Subtitle = Text("选择迁移到官方 DSH，或保留 DYM 备份。", "Migrate to official DSH, or keep a DYM backup.");
            Scaffold.SetSteps(3, 1);
            ModeTitle.Text = Text("导出方式", "Export format");
            OfficialMode.Content = Text("导出到官方 DSH", "Export to official DSH");
            DymMode.Content = Text("导出到 DYM 文件", "Export to a DYM file");
            SourceTitle.Text = Text("数据来源 · DSH_HOME", "Data source · DSH_HOME");
            SourceChoices.PlaceholderText = Text("请选择数据来源", "Choose a data source");
            SourceHomeBox.PlaceholderText = Text("数据目录的完整路径", "Full path to the data home");
            GroupsTitle.Text = Text("选择要导出的数据", "Choose data to export");
            GroupsDesc.Text = Text("默认包含会话与附件；备份可能含登录凭据，请妥善保管。", "Sessions and attachments are included by default. Backups may contain credentials; keep them private.");
            TargetTitle.Text = Text("目标目录", "Destination directory");
            TargetDirectoryBox.PlaceholderText = Text("请输入或选择卸载目录之外的位置", "Enter or choose a directory outside the installation");
            CancelButtonText.Text = Text("取消导出", "Cancel export");
            SkipExportText.Text = Text("跳过导出，继续选择卸载内容", "Skip exporting and continue to removal options");
            ContinueHint.Text = Text("未成功导出时，只有明确选择跳过才能继续。下一步默认保留用户数据。", "Without a successful export, continuing requires an explicit skip. User data is kept by default in the next step.");
            SetToolLabel(SourceFolderButton, Text("选择数据目录", "Choose data home"));
            SetToolLabel(ReadSourceButton, Text("读取所选数据目录", "Read selected data home"));
            SetToolLabel(FolderButton, Text("选择导出目录", "Choose destination directory"));
            SetToolLabel(RevealButton, Text("在资源管理器中定位文件", "Show file in Explorer"));
        }

        private static void SetToolLabel(Button button, string label)
        {
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, label);
        }

        private void SetAutomationIds()
        {
            foreach (FrameworkElement element in new FrameworkElement[] { Body, OfficialMode, DymMode, SourceChoices, SourceHomeBox,
                SourceFolderButton, ReadSourceButton, GroupsHost, TargetDirectoryBox, FolderButton, OutputPreview, ExportButton,
                CancelExportButton, ExportProgress, ExportDetail, StatusMessage, ResultPath, RevealButton, SkipExportBox })
                AutomationProperties.SetAutomationId(element, "Export" + element.Name);
        }

        private void LoadSources()
        {
            try
            {
                _dshRoot = InstallSession.Current.ExportDshRoot;
                RootText.Text = Text("安装目录：", "Installation directory: ") + (_dshRoot ?? Text("未记录", "Not recorded"));
                var candidates = InstallSession.Current.FindExportHomes();
                foreach (var candidate in candidates)
                    SourceChoices.Items.Add(new ComboBoxItem
                    {
                        Tag = candidate.Path,
                        Content = new TextBlock { Text = candidate.Path + " (" + candidate.Origin + ")", TextWrapping = TextWrapping.Wrap }
                    });
                var previous = SourceChoices.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                    string.Equals(item.Tag as string, InstallSession.Current.ExportSourceHome, StringComparison.OrdinalIgnoreCase));
                if (previous != null) SourceChoices.SelectedItem = previous;
                else if (candidates.Count == 1) SourceChoices.SelectedIndex = 0;
                SourceHint.Text = candidates.Count > 1
                    ? Text("发现多个数据目录，请确认来源；不会自动合并不同 home。", "Multiple data homes were found. Choose the source; distinct homes are not merged automatically.")
                    : Text("可编辑数据路径，选择文件夹，或重新读取来源。", "Edit the data path, choose a folder, or read the source again.");
                if (candidates.Count == 0)
                    EmptyText.Text = Text("未找到数据目录。请选择来源；这不代表不存在用户数据。", "No data home was found. Choose a source; user data may still exist elsewhere.");
            }
            catch (Exception exception)
            {
                _sourceConfigFailed = true;
                SourceHint.Text = Text("来源配置读取失败，请手动指定正确的数据目录。", "Source configuration could not be read. Choose the correct data home manually.");
                ShowStatus(exception.Message, InfoBarSeverity.Error);
            }
            EmptyText.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshControls();
        }

        private void OnSourceChanged(object sender, SelectionChangedEventArgs args)
        {
            if (_initializing || _busy) return;
            if (SourceChoices.SelectedItem is ComboBoxItem item)
            {
                SourceHomeBox.Text = item.Tag as string;
                LoadGroups();
            }
        }

        private void OnSourcePathChanged(object sender, TextChangedEventArgs args)
        {
            if (_initializing || _busy) return;
            _sourceReady = false;
            _groups.Clear();
            _boxes.Clear();
            GroupsHost.Children.Clear();
            EmptyText.Text = Text("请读取并确认所选数据目录。", "Read and confirm the selected data home.");
            EmptyText.Visibility = Visibility.Visible;
            if (!_restoring) InvalidateExport();
            RefreshControls();
        }

        private void OnSourceLostFocus(object sender, RoutedEventArgs args)
        {
            if (!_initializing && !_busy && !_sourceReady && !string.IsNullOrWhiteSpace(SourceHomeBox.Text)) LoadGroups();
        }

        private void OnReadSource(object sender, RoutedEventArgs args) { if (!_busy) LoadGroups(); }

        private void OnSourceBrowse(object sender, RoutedEventArgs args)
        {
            if (_busy) return;
            try
            {
                string picked = FolderDialog.Pick(Text("选择 DSH_HOME 数据目录", "Choose the DSH_HOME data directory"), SourceHomeBox.Text);
                if (!string.IsNullOrWhiteSpace(picked)) { SourceHomeBox.Text = picked; LoadGroups(); }
            }
            catch (Exception exception) { ShowStatus(exception.Message, InfoBarSeverity.Error); }
        }

        private void LoadGroups()
        {
            if (!_restoring) InvalidateExport();
            _sourceReady = false;
            _groups.Clear();
            _boxes.Clear();
            GroupsHost.Children.Clear();
            try
            {
                string home = InstallSession.NormalizeExportPath(SourceHomeBox.Text);
                if (!Directory.Exists(home)) throw new DirectoryNotFoundException(Text("所选数据目录不存在：", "The selected data home does not exist: ") + home);
                _sourceHome = home;
                string root = _dshRoot ?? home;
                bool official = OfficialMode.IsChecked == true;
                if (official && !string.Equals(OfficialDshExport.ResolveHome(root, home), home, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Text("所选目录没有官方 DSH 数据，请确认实际 DSH_HOME；不会自动改用其他来源。", "The selected directory contains no official DSH data. Confirm DSH_HOME; another source will not be substituted automatically."));
                _groups.AddRange(official ? OfficialDshExport.Describe(root, home) : UserDataBackup.Describe(root, home));
                // Attachments are durable session data; older backup templates omit this prefix.
                if (!official && Directory.Exists(Path.Combine(home, "attachments")))
                {
                    BackupGroup sessions = _groups.FirstOrDefault(group => group.Id == UserDataBackup.GroupSessions);
                    if (sessions == null)
                    {
                        sessions = new BackupGroup { Id = UserDataBackup.GroupSessions, Exists = true,
                            SourcePath = Path.Combine(home, "attachments"), ArchivePrefix = ".dsh/attachments" };
                        _groups.Add(sessions);
                    }
                    sessions.ArchivePrefixes = (sessions.ArchivePrefixes ?? new[] { sessions.ArchivePrefix })
                        .Concat(new[] { ".dsh/attachments" }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }
                foreach (BackupGroup group in _groups)
                {
                    if (!official && group.Id == UserDataBackup.GroupSessions)
                    {
                        group.NameZh = "会话、附件与缓存";
                        group.NameEn = "Sessions, attachments and cache";
                    }
                    var box = new CheckBox
                    {
                        Content = new TextBlock { Text = group.Display(Localization.IsChinese), TextWrapping = TextWrapping.Wrap },
                        IsChecked = _restoring && InstallSession.Current.ExportSelectedGroupIds != null
                            ? InstallSession.Current.ExportSelectedGroupIds.Contains(group.Id)
                            : group.SelectedByDefault || group.Id == UserDataBackup.GroupSessions
                    };
                    box.Checked += OnGroupChanged;
                    box.Unchecked += OnGroupChanged;
                    AutomationProperties.SetAutomationId(box, "ExportGroup_" + group.Id);
                    _boxes[group.Id] = box;
                    GroupsHost.Children.Add(box);
                }
                _sourceReady = true;
                InstallSession.Current.ExportSourceHome = home;
                EmptyText.Text = Text("此来源未找到可导出分组。请检查数据目录；尚未生成备份。", "No exportable groups were found in this source. Check the data home; no backup has been created.");
                if (!_sourceConfigFailed) StatusMessage.IsOpen = false;
            }
            catch (Exception exception) { ShowStatus(exception.Message, InfoBarSeverity.Error); }
            EmptyText.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshControls();
        }

        private void OnGroupChanged(object sender, RoutedEventArgs args) { if (!_busy && !_restoring) InvalidateExport(); }

        private void OnModeChanged(object sender, RoutedEventArgs args)
        {
            if (_initializing || _busy) return;
            InstallSession.Current.ExportToOfficialDsh = OfficialMode.IsChecked == true;
            InvalidateExport();
            UpdateMode();
            AnimateMode();
            if (_sourceReady) LoadGroups();
        }

        private void UpdateMode()
        {
            bool official = OfficialMode.IsChecked == true;
            ModeDescription.Text = official
                ? Text("生成独立自解压迁移 EXE，保存到你选择的目录。", "Create a standalone self-extracting migration EXE in your chosen directory.")
                : Text("生成 .dym 数据备份文件，保存到你选择的目录。", "Create a .dym data backup in your chosen directory.");
            ExportButtonText.Text = official ? Text("生成迁移 EXE", "Create migration EXE") : Text("导出 DYM", "Export DYM");
            UpdateOutputPreview();
        }

        private void AnimateMode()
        {
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation { From = 0.35, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, EnableDependentAnimation = true };
            Storyboard.SetTarget(animation, ModeDescription);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        private string OutputName => (OfficialMode.IsChecked == true ? "Dafeiyu-Go-Official-DSH-Migration-" : "Dafeiyu-Go-Backup-")
            + _fileStamp + (OfficialMode.IsChecked == true ? ".exe" : DymArchive.Extension);

        private void UpdateOutputPreview() => OutputPreview.Text = Text("输出文件：", "Output file: ") + OutputName;

        private void OnTargetChanged(object sender, TextChangedEventArgs args)
        {
            if (_initializing || _busy) return;
            InstallSession.Current.ExportTargetDirectory = TargetDirectoryBox.Text;
            InvalidateExport();
            UpdateOutputPreview();
        }

        private void InvalidateExport()
        {
            _successful = false;
            InstallSession.Current.ExportSucceeded = false;
            InstallSession.Current.ExportSkipped = false;
            InstallSession.Current.ExportedBackupPath = null;
            SkipExportBox.IsChecked = false;
            SetVisible(ResultPanel, false);
            App.MainWindowInstance?.RefreshChrome();
        }

        private void OnSkipChanged(object sender, RoutedEventArgs args)
        {
            if (_initializing) return;
            InstallSession.Current.ExportSkipped = SkipExportBox.IsChecked == true;
            App.MainWindowInstance?.RefreshChrome();
        }

        private void FolderButton_Click(object sender, RoutedEventArgs args)
        {
            if (_busy) return;
            try
            {
                string picked = FolderDialog.Pick(Text("选择导出目标目录", "Choose the export destination"), TargetDirectoryBox.Text);
                if (!string.IsNullOrWhiteSpace(picked)) TargetDirectoryBox.Text = picked;
            }
            catch (Exception exception) { ShowStatus(exception.Message, InfoBarSeverity.Error); }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs args)
        {
            if (_busy) return;
            CancellationTokenSource cancellation = null;
            try
            {
                if (!_sourceReady) LoadGroups();
                if (!_sourceReady) return;
                var chosen = _groups.Where(group => _boxes.TryGetValue(group.Id, out CheckBox box) && box.IsChecked == true).ToList();
                if (chosen.Count == 0) throw new InvalidOperationException(Text("请至少选择一组数据。", "Select at least one data group."));
                string directory = InstallSession.Current.ValidateExportTarget(TargetDirectoryBox.Text, _sourceHome);
                string output = Path.Combine(directory, OutputName);
                if (File.Exists(output)) throw new IOException(Text("输出文件已存在，请更换目标目录：", "The output file already exists. Choose another destination: ") + output);
                bool official = OfficialMode.IsChecked == true;
                string root = _dshRoot ?? _sourceHome;
                string home = _sourceHome;
                InvalidateExport();
                InstallSession.Current.ExportedBackupPath = null;
                cancellation = _cancellation = new CancellationTokenSource();
                _busy = true;
                InstallSession.Current.ExportInProgress = true;
                RefreshControls();
                SetVisible(ProgressPanel, true);
                ExportProgress.Value = 0;
                ExportDetail.Text = Text("正在准备导出…", "Preparing the export…");
                StatusMessage.IsOpen = false;
                string lastLog = null;
                Action<string> log = line => { if (!string.IsNullOrWhiteSpace(line)) lastLog = line; };
                Action<string, double> progress = (detail, percent) => _dispatcher.TryEnqueue(() =>
                {
                    if (!ReferenceEquals(_cancellation, cancellation)) return;
                    if (detail != null) ExportDetail.Text = detail;
                    if (!double.IsNaN(percent) && !double.IsInfinity(percent)) ExportProgress.Value = Math.Clamp(percent, 0, 100);
                });
                string generated = output, summary = null;
                var warnings = new List<string>();
                bool ok;
                bool canceled;
                string smoke = InstallSession.ExportUiSmokeRoot == null ? null : Environment.GetEnvironmentVariable("DAFEIYU_EXPORT_UI_SMOKE_BEHAVIOR");
                if (smoke == "busy-failure" || smoke == "failure")
                {
                    await Task.Delay(smoke == "busy-failure" ? 12000 : 150, cancellation.Token);
                    throw new IOException(Text("UI smoke 模拟导出失败（未运行导出核心）。", "UI smoke simulated export failure (export core was not run)."));
                }
                if (official)
                {
                    var result = await Task.Run(() => OfficialDshExport.Export(root, home, chosen, output, progress, log, cancellation.Token));
                    ok = result.Ok;
                    canceled = result.Canceled;
                    generated = result.OutputPath;
                    summary = result.Summary;
                    if (result.Warnings != null) warnings.AddRange(result.Warnings);
                    if (!ok && !canceled) throw new IOException(result.Error ?? lastLog ?? Text("迁移 EXE 未能生成。", "The migration EXE could not be created."));
                }
                else
                {
                    ok = await Task.Run(() => UserDataBackup.Export(root, chosen, output, progress, log, cancellation.Token, home));
                    canceled = cancellation.IsCancellationRequested && !ok;
                    if (!ok && !canceled) throw new IOException(lastLog ?? Text("DYM 导出失败。请检查目标权限、数据文件和可用空间。", "DYM export failed. Check destination permissions, source files and free space."));
                }
                if (canceled) throw new OperationCanceledException(cancellation.Token);
                if (!ok || !ValidOutput(generated)) throw new IOException(Text("导出未生成有效文件，尚未完成备份。", "No valid output file was created; the backup is incomplete."));
                _successful = true;
                var session = InstallSession.Current;
                session.ExportSucceeded = true;
                session.ExportSelectedGroupIds = chosen.Select(group => group.Id).ToList();
                session.ExportedBackupPath = generated;
                session.ExportTargetDirectory = directory;
                ExportProgress.Value = 100;
                ExportDetail.Text = Text("已生成文件。", "File created.");
                ShowResult(generated);
                string message = Text("所选数据已导出。可定位文件后继续选择卸载内容。", "The selected data has been exported. Locate the file, then continue to removal options.");
                if (!string.IsNullOrWhiteSpace(summary)) message += Environment.NewLine + summary;
                if (warnings.Count > 0) message += Environment.NewLine + Text("请检查以下导出提示：", "Review these export warnings:") + Environment.NewLine + string.Join(Environment.NewLine, warnings);
                ShowStatus(message, warnings.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            }
            catch (OperationCanceledException)
            {
                ShowStatus(Text("导出已取消，未确认备份成功。请重试，或明确选择跳过导出。", "Export canceled; no successful backup was confirmed. Retry or explicitly skip exporting."), InfoBarSeverity.Warning);
                ExportDetail.Text = Text("已取消导出。", "Export canceled.");
            }
            catch (Exception exception)
            {
                ShowStatus(Text("导出失败：", "Export failed: ") + exception.Message, InfoBarSeverity.Error);
                ExportDetail.Text = Text("未完成备份。", "Backup incomplete.");
            }
            finally
            {
                if (cancellation != null)
                {
                    _cancellation = null;
                    cancellation.Dispose();
                    _busy = false;
                    InstallSession.Current.ExportInProgress = false;
                }
                RefreshControls();
            }
        }

        private static bool ValidOutput(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private void ShowResult(string path)
        {
            ResultPath.Text = path;
            SetVisible(ResultPanel, true);
        }

        private void SetVisible(FrameworkElement element, bool visible)
        {
            if (_visibilityAnimations.TryGetValue(element, out Storyboard previous))
            {
                previous.Stop();
                _visibilityAnimations.Remove(element);
            }
            if (visible && element.Visibility == Visibility.Visible) return;
            if (!visible && element.Visibility == Visibility.Collapsed) return;
            element.Visibility = Visibility.Visible;
            var storyboard = new Storyboard();
            var animation = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true };
            animation.KeyFrames.Add(new SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = visible ? 0 : 1 });
            animation.KeyFrames.Add(new SplineDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), Value = visible ? 1 : 0,
                KeySpline = new KeySpline { ControlPoint1 = new Windows.Foundation.Point(0.16, 1), ControlPoint2 = new Windows.Foundation.Point(0.3, 1) } });
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
            _visibilityAnimations[element] = storyboard;
            storyboard.Completed += delegate
            {
                if (_visibilityAnimations.TryGetValue(element, out Storyboard current) && ReferenceEquals(current, storyboard))
                {
                    element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    _visibilityAnimations.Remove(element);
                    storyboard.Stop();
                    element.Opacity = 1;
                }
            };
            storyboard.Begin();
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusMessage.Message = message;
            StatusMessage.Severity = severity;
            StatusMessage.IsOpen = true;
            AutomationProperties.SetName(StatusMessage, message);
        }

        private void RefreshControls()
        {
            OfficialMode.IsEnabled = DymMode.IsEnabled = SourceChoices.IsEnabled = SourceHomeBox.IsEnabled
                = SourceFolderButton.IsEnabled = ReadSourceButton.IsEnabled = TargetDirectoryBox.IsEnabled
                = FolderButton.IsEnabled = SkipExportBox.IsEnabled = RevealButton.IsEnabled = !_busy;
            GroupsHost.IsHitTestVisible = !_busy;
            foreach (CheckBox box in _boxes.Values) box.IsEnabled = !_busy;
            ExportButton.IsEnabled = !_busy && _sourceReady && _groups.Count > 0;
            SetVisible(CancelExportButton, _busy);
            CancelExportButton.IsEnabled = _busy && _cancellation?.IsCancellationRequested == false;
            App.MainWindowInstance?.RefreshChrome();
        }

        private void OnCancelExport(object sender, RoutedEventArgs args) => CancelExport();

        private void CancelExport()
        {
            if (!_busy || _cancellation == null) return;
            _cancellation.Cancel();
            ExportDetail.Text = Text("正在取消导出，请等待清理完成…", "Canceling the export; waiting for cleanup…");
            RefreshControls();
        }

        private void OnReveal(object sender, RoutedEventArgs args)
        {
            if (_busy) return;
            try
            {
                string path = ResultPath.Text;
                if (!ValidOutput(path)) throw new FileNotFoundException(Text("导出文件已被移动或删除。", "The exported file has been moved or deleted."), path);
                var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                start.ArgumentList.Add("/select," + Path.GetFullPath(path));
                Process.Start(start)?.Dispose();
            }
            catch (Exception exception) { ShowStatus(exception.Message, InfoBarSeverity.Error); }
        }
    }
}
