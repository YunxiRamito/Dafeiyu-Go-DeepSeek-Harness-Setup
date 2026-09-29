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
    /// 装完之后的导入页。
    ///
    /// 为什么单独占一页(而不是塞进完成页):重装/换机的人最需要的就是"把原来那份配置、
    /// 技能、插件搬回来",而这件事在完成页上很容易被忽略掉。这一页只问这一件事:
    /// 有没有备份、有就现在还原、没有就跳过。
    ///
    /// 进度文案是刻意写成「正在恢复 技能 (2/12) · xxx/SKILL.md」的:
    /// 恢复几十上百个文件时,只有一个百分比会让人怀疑它是不是卡死了。
    /// </summary>
    public sealed partial class ImportPage : Page, IWizardPage
    {
        private readonly List<BackupGroup> _groups = new List<BackupGroup>();

        private readonly Dictionary<string, CheckBox> _boxes =
            new Dictionary<string, CheckBox>();

        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

        private string _archive;

        private bool _busy;

        private bool _imported;

        public ImportPage()
        {
            InitializeComponent();
            BuildConflictChoices();
            ApplyText();
        }

        public bool CanGoNext
        {
            get { return !_busy; }
        }

        /// <summary>点"下一步"就是"去完成页" —— 导入是可选的,跳过也该顺顺当当走下去。</summary>
        public bool OnNext()
        {
            InstallSession.Current.ImportedBackup = _imported;
            return true;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.IsChinese ? "导入原有数据" : "Import your data";
            Scaffold.Subtitle = Localization.IsChinese
                ? "装完了。如果你有以前的备份（.dym 文件），现在可以一次性把配置、技能、插件搬回来。没有就点下一步跳过。"
                : "Installation finished. If you have a .dym backup, you can restore settings, skills and plug-ins now. Otherwise just continue.";

            Scaffold.SetStep(8);

            PickTitle.Text = Localization.IsChinese ? "备份文件" : "Backup file";
            PickDesc.Text = Localization.IsChinese
                ? "选一个 .dym 备份包（导出时生成的就是它）。"
                : "Pick a .dym backup file.";

            PickButton.Content = Localization.IsChinese ? "选择备份文件" : "Choose backup";

            ArchiveText.Text = Localization.IsChinese ? "还没选。" : "Nothing selected yet.";

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

        private void PickButton_Click(object sender, RoutedEventArgs args)
        {
            if (_busy)
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

            if (!picked.EndsWith(DymArchive.Extension, StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = Localization.IsChinese
                    ? "这不是 .dym 备份包。请选导出时生成的那个文件。"
                    : "That is not a .dym backup file.";
                return;
            }

            _archive = picked;
            ArchiveText.Text = picked;
            StatusText.Text = Localization.IsChinese ? "正在读取备份内容…" : "Reading the backup…";
            GroupsHost.Children.Clear();
            _boxes.Clear();
            _groups.Clear();
            GroupsCard.Visibility = Visibility.Collapsed;
            PickButton.IsEnabled = false;

            _ = Task.Run(delegate
            {
                List<BackupGroup> groups = UserDataBackup.DescribeFromArchive(picked, null);

                _dispatcher.TryEnqueue(delegate
                {
                    PickButton.IsEnabled = true;
                    FillGroups(groups);
                });
            });
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
            StartImport();
        }

        private async void StartImport()
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

            ConflictPolicy policy = ConflictPolicy.Overwrite;
            ComboBoxItem selected = ConflictBox.SelectedItem as ComboBoxItem;
            if (selected != null && selected.Tag is ConflictPolicy)
            {
                policy = (ConflictPolicy)selected.Tag;
            }

            _busy = true;
            PickButton.IsEnabled = false;
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
    }
}
