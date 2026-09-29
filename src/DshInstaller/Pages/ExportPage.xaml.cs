using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DshInstaller.Shared.Backup;
using DshInstaller.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 卸载前导出数据。
    ///
    /// 为什么把导出放在卸载流程里:用户点卸载的那一刻,才是他真正可能想留下点什么的时候。
    /// 装个新版本、换台机器,先把配置和技能带走 —— 等卸完再想起来就晚了
    /// (目录已经没了)。
    ///
    /// 默认勾选跟安装器导入页同一口径:配置 + 技能 + 插件,会话与缓存不勾。
    /// </summary>
    public sealed partial class ExportPage : Page, IWizardPage
    {
        private readonly List<BackupGroup> _groups = new List<BackupGroup>();

        private readonly Dictionary<string, CheckBox> _boxes =
            new Dictionary<string, CheckBox>();

        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

        private string _targetDirectory;

        private bool _busy;

        public ExportPage()
        {
            InitializeComponent();
            ApplyText();
            LoadGroups();
        }

        public bool CanGoNext
        {
            get { return !_busy; }
        }

        public bool OnNext()
        {
            return true;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.IsChinese ? "导出数据" : "Export your data";
            Scaffold.Subtitle = Localization.IsChinese
                ? "卸载之前，可以把配置、技能、插件打包成一个 .dym 文件带走。"
                : "Before uninstalling, you can pack settings, skills and plug-ins into a .dym file.";

            // 卸载流程只有三步:确定 → 导出(可选) → 选择内容。这一页算第二步。
            Scaffold.SetSteps(3, 1);

            GroupsTitle.Text = Localization.IsChinese ? "要带走哪些" : "What to keep";
            GroupsDesc.Text = Localization.IsChinese
                ? "默认勾上配置、技能和插件；会话与缓存体积大，需要就自己勾。"
                : "Settings, skills and plug-ins are ticked by default.";

            TargetTitle.Text = Localization.IsChinese ? "存到哪儿" : "Where to save it";
            FolderButton.Content = Localization.IsChinese ? "选择文件夹" : "Choose folder";
            ExportButton.Content = Localization.IsChinese ? "开始导出" : "Export now";

            // 默认放桌面:导出完用户能一眼看到,不用去翻目录
            string desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);
            _targetDirectory = String.IsNullOrWhiteSpace(desktop)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : desktop;
            UpdateTargetText();
        }

        private void LoadGroups()
        {
            string dshRoot = InstallSession.Current.DshRoot;
            List<BackupGroup> groups = UserDataBackup.Describe(dshRoot);

            if (groups.Count == 0)
            {
                EmptyText.Visibility = Visibility.Visible;
                EmptyText.Text = Localization.IsChinese
                    ? "没找到可导出的数据（配置 / 技能 / 插件都还不存在）。直接点下一步继续卸载就行。"
                    : "Nothing to export. Just continue with the uninstall.";
                ExportButton.IsEnabled = false;
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
        }

        private void UpdateTargetText()
        {
            TargetText.Text = String.IsNullOrWhiteSpace(_targetDirectory)
                ? (Localization.IsChinese ? "还没选。" : "Nothing selected.")
                : _targetDirectory;
        }

        private void FolderButton_Click(object sender, RoutedEventArgs args)
        {
            if (_busy)
            {
                return;
            }

            string picked = FolderDialog.Pick(
                Localization.IsChinese ? "导出到哪个文件夹" : "Choose a folder",
                _targetDirectory);

            if (!String.IsNullOrWhiteSpace(picked))
            {
                _targetDirectory = picked;
                UpdateTargetText();
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs args)
        {
            if (_busy || String.IsNullOrWhiteSpace(_targetDirectory))
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
                    ? "一个都没勾,那就没东西可导出了。"
                    : "Nothing is ticked, so there is nothing to export.";
                return;
            }

            string archive = Path.Combine(
                _targetDirectory,
                "Dafeiyu-Go-备份-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + DymArchive.Extension);

            _busy = true;
            ExportButton.IsEnabled = false;
            FolderButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ExportProgress.Value = 0;
            ExportDetail.Text = Localization.IsChinese ? "准备中…" : "Preparing…";
            StatusText.Text = String.Empty;

            string dshRoot = InstallSession.Current.DshRoot;

            bool ok = await Task.Run(delegate
            {
                return UserDataBackup.Export(
                    dshRoot,
                    chosen,
                    archive,
                    delegate(string text, double percent)
                    {
                        _dispatcher.TryEnqueue(delegate
                        {
                            if (text != null)
                            {
                                ExportDetail.Text = text;
                            }

                            ExportProgress.Value = Math.Max(0, Math.Min(100, percent));
                        });
                    },
                    null,
                    CancellationToken.None);
            });

            _busy = false;
            ExportButton.IsEnabled = true;
            FolderButton.IsEnabled = true;

            if (ok)
            {
                ExportProgress.Value = 100;
                ExportDetail.Text = archive;
                StatusText.Text = Localization.IsChinese
                    ? "导出好了。这个是 7z 格式，用 7-Zip / WinRAR 也能打开。点下一步继续卸载。"
                    : "Exported. It is a real 7z archive. Continue to uninstall.";
                InstallSession.Current.ExportedBackupPath = archive;
            }
            else
            {
                ExportDetail.Text = Localization.IsChinese ? "导出失败。" : "Export failed.";
                StatusText.Text = Localization.IsChinese
                    ? "导出没成功。可以换个文件夹再试一次,或者直接点下一步继续卸载。"
                    : "Export failed. Try another folder, or continue the uninstall.";
            }
        }
    }
}
