using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Controls;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 卸载第二步:逐项选择删什么、留什么。
    ///
    /// 关键规则:**只有本安装程序装过的东西才提供卸载**。
    /// 用户自己装的 Node / Git 之类,即使就躺在同一个组件目录里,也不给勾 ——
    /// 复选框直接禁用并写明原因,免得一键把人家自己装的东西删了。
    /// 判断依据是安装时留下的状态文件:它记着"我们到底装过什么"。
    /// </summary>
    public sealed partial class UninstallItemsPage : Page, IWizardPage
    {
        private readonly InstallerState _state;
        private readonly List<CheckBox> _boxes = new List<CheckBox>();
        private readonly List<string> _boxIds = new List<string>();

        public UninstallItemsPage()
        {
            InitializeComponent();
            _state = ConfigStore.Load();
            ApplyText();
            BuildItems();
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            InstallSession session = InstallSession.Current;
            UninstallOptions options = session.UninstallOptions ?? new UninstallOptions();

            options.RemoveDshCore = IsChecked("dsh");
            options.RemoveLauncher = IsChecked("launcher");
            options.RemoveComponents = IsChecked("components");
            options.RemoveShortcuts = IsChecked("shortcut");
            options.RemoveAutostart = IsChecked("autostart");
            options.CleanPath = IsChecked("path");
            options.RemoveUserData = UserDataBox.IsChecked == true;
            options.DryRun = DevOptions.DryRun;

            // 把**记录里的真实路径**灌进选项里 —— 这一步以前漏了。
            //
            // 界面上老老实实显示着"DSH 本体 C:\...\DeepSeek Harness",但那是拿 _state
            // 渲染用的;options 里那三个路径一直是 null。于是卸载步骤拿到空路径,
            // 逐个"目录未记录,跳过"过去 —— 看着像卸载完了,其实一个字节没删(实测:
            // 整个安装目录原样留着)。而且那几条跳过只走 Report 不落盘,日志里干干净净。
            if (_state != null)
            {
                options.DshRoot = _state.DshRoot;
                options.LauncherRoot = _state.LauncherRoot;
                options.ComponentsRoot = _state.ComponentsRoot;

                if (options.PathEntries == null || options.PathEntries.Count == 0)
                {
                    options.PathEntries = _state.PathEntries ?? new List<string>();
                }
            }

            if (string.IsNullOrWhiteSpace(options.UserDataRoot))
            {
                options.UserDataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
            }

            // 没勾的项把路径清掉,免得步骤拿到路径又去删一遍
            if (!options.RemoveDshCore)
            {
                options.DshRoot = null;
            }

            if (!options.RemoveLauncher)
            {
                options.LauncherRoot = null;
            }

            if (!options.RemoveComponents)
            {
                options.ComponentsRoot = null;
            }

            if (!options.CleanPath)
            {
                options.PathEntries = new List<string>();
            }

            session.UninstallOptions = options;
            session.Mode = SessionMode.Uninstall;
            return true;
        }

        private bool IsChecked(string id)
        {
            for (int i = 0; i < _boxIds.Count; i++)
            {
                if (_boxIds[i] == id)
                {
                    return _boxes[i].IsChecked == true;
                }
            }

            return false;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.T("uninstall.items.title");
            Scaffold.Subtitle = Localization.T("uninstall.items.desc");
            Scaffold.SetSteps(3, 1);

            OptionsLabel.Text = Localization.T("uninstall.options");
            UserDataBox.Content = Localization.T("uninstall.userdata");
            UserDataHint.Text = Localization.T("uninstall.userdata.hint");
        }

        private void BuildItems()
        {
            ItemsHost.Children.Clear();
            _boxes.Clear();
            _boxIds.Clear();

            bool hasRecord = _state != null;

            AddItem("launcher",
                Localization.T("uninstall.item.launcher"),
                hasRecord ? _state.LauncherRoot : null,
                hasRecord && !string.IsNullOrWhiteSpace(_state.LauncherRoot),
                Localization.T("uninstall.item.launcher.desc"));

            AddItem("dsh",
                Localization.T("uninstall.item.dsh"),
                hasRecord ? _state.DshRoot : null,
                hasRecord && !string.IsNullOrWhiteSpace(_state.DshRoot),
                Localization.T("uninstall.item.dsh.desc"));

            AddItem("components",
                Localization.T("uninstall.item.components"),
                hasRecord ? _state.ComponentsRoot : null,
                hasRecord && !string.IsNullOrWhiteSpace(_state.ComponentsRoot),
                Localization.T("uninstall.item.components.desc"));

            AddItem("path",
                Localization.T("uninstall.item.path"),
                hasRecord && _state.PathEntries != null && _state.PathEntries.Count > 0
                    ? string.Join("; ", _state.PathEntries.ToArray())
                    : null,
                hasRecord && _state.PathEntries != null && _state.PathEntries.Count > 0,
                Localization.T("uninstall.item.path.desc"));

            AddItem("shortcut",
                Localization.T("uninstall.item.shortcut"),
                null,
                hasRecord && _state.DesktopShortcut,
                Localization.T("uninstall.item.shortcut.desc"));

            AddItem("autostart",
                Localization.T("uninstall.item.autostart"),
                null,
                hasRecord && _state.Autostart,
                Localization.T("uninstall.item.autostart.desc"));

            // 全部都不是我们的:给一句总说明,别让用户对着六个灰框发懵
            if (!hasRecord)
            {
                TextBlock note = new TextBlock
                {
                    Text = Localization.T("uninstall.noRecord"),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 2),
                    Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
                };
                ItemsHost.Children.Add(note);
            }
        }

        private void AddItem(string id, string label, string path, bool ours, string description)
        {
            StackPanel row = new StackPanel { Spacing = 2, Margin = new Thickness(0, 5, 0, 5) };

            CheckBox box = new CheckBox
            {
                Content = label,
                IsChecked = ours,

                // 不是本安装程序装的 -> 不给勾,并说明原因
                IsEnabled = ours,
            };

            _boxes.Add(box);
            _boxIds.Add(id);
            row.Children.Add(box);

            if (!string.IsNullOrWhiteSpace(path))
            {
                row.Children.Add(new TextBlock
                {
                    Text = path,
                    FontSize = 11,
                    Margin = new Thickness(30, 0, 0, 0),
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
                });
            }

            string hint = ours ? description : Localization.T("uninstall.notOurs");

            if (!string.IsNullOrWhiteSpace(hint))
            {
                row.Children.Add(new TextBlock
                {
                    Text = hint,
                    FontSize = 10.5,
                    Margin = new Thickness(30, 0, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
                });
            }

            ItemsHost.Children.Add(row);
        }
    }
}
