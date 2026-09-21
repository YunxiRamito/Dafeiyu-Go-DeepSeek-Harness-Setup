using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DshInstaller.Controls;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 组件页。上面选根目录和下载源,下面必选 / 可选**左右并排**。
    ///
    /// 为什么并排而不是上下摞:上下摞的时候必选卡片自己带滚动条,
    /// 把"可选"整块顶到窗口外面,用户根本不知道下面还有东西(实测反馈)。
    /// 并排之后每列三四个条目,Auto 高度就撑得下,一屏看全。
    /// 顺序上"必选在前"仍然成立 —— 左列先读到。
    /// </summary>
    public sealed partial class ComponentsPage : Page, IWizardPage
    {
        private readonly Dictionary<string, CheckBox> _optionalBoxes =
            new Dictionary<string, CheckBox>();

        public ComponentsPage()
        {
            InitializeComponent();
            Theme.Bind(
                ElevationHint,
                TextBlock.ForegroundProperty,
                "WarningTextBrush",
                Windows.UI.Color.FromArgb(255, 196, 118, 0));
            ApplyText();
            Loaded += OnLoaded;
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            InstallSession session = InstallSession.Current;
            session.ComponentsRoot = RootBox.Text;
            session.ComponentsChosen = true;

            CheckBox git;
            if (_optionalBoxes.TryGetValue("git", out git))
            {
                session.InstallGit = git.IsChecked == true;
            }

            CheckBox pnpm;
            if (_optionalBoxes.TryGetValue("pnpm", out pnpm))
            {
                session.InstallPnpm = pnpm.IsChecked == true;
            }

            CheckBox python;
            if (_optionalBoxes.TryGetValue("python", out python))
            {
                session.InstallPython = python.IsChecked == true;
            }

            return true;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.T("components.title");
            Scaffold.Subtitle = Localization.T("components.desc");
            Scaffold.SetStep(3);

            RootLabel.Text = Localization.IsChinese ? "组件目录" : "Component directory";
            SourceLabel.Text = Localization.IsChinese ? "下载源" : "Download source";
            BrowseButton.Content = Localization.T("btn.browse");
            RequiredLabel.Text = Localization.IsChinese ? "必需" : "Required";
            OptionalLabel.Text = Localization.IsChinese ? "可选" : "Optional (selected items will be installed)";

            SourceBox.Items.Clear();
            SourceBox.Items.Add(Localization.IsChinese ? "国内镜像（推荐）" : "China mirror (recommended)");
            SourceBox.Items.Add(Localization.IsChinese ? "官方源" : "Official source");
            SourceBox.SelectedIndex = InstallSession.Current.SourcePreference == MirrorSource.China ? 0 : 1;

            // 默认放在 DSH 目录下的 components 子目录里。
            // DSH 位置页现在排在前面,所以这里拿得到它的选择 —— 用户改了 DSH 装哪儿,
            // 组件默认也跟着走,不会出现"两个根目录八竿子打不着"的情况。
            InstallSession session = InstallSession.Current;
            if (string.IsNullOrEmpty(session.ComponentsRoot) || !session.ComponentsChosen)
            {
                session.ComponentsRoot = Path.Combine(
                    string.IsNullOrEmpty(session.DshRoot) ? session.DefaultRoot : session.DshRoot,
                    "components");
            }

            RootBox.Text = session.ComponentsRoot;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildRows();

            RootBox.TextChanged += delegate { UpdateElevationHint(); };
            UpdateElevationHint();
        }

        /// <summary>
        /// 范围选了"仅为本用户安装",可目录又需要管理员权限时给一句提醒;不需要就藏起来。
        /// (别等用户点了"安装"才弹 UAC —— 那会儿他会觉得程序在乱要权限。)
        /// </summary>
        private void UpdateElevationHint()
        {
            string note = PathAccess.DescribeIfNeedsElevation(RootBox.Text);

            if (string.IsNullOrEmpty(note))
            {
                ElevationHint.Text = string.Empty;
                ElevationHint.Visibility = Visibility.Collapsed;
                return;
            }

            ElevationHint.Text = note;
            ElevationHint.Visibility = Visibility.Visible;
        }

        private void BuildRows()
        {
            RequiredHost.Children.Clear();
            OptionalHost.Children.Clear();
            _optionalBoxes.Clear();

            ProbeReport report = InstallSession.Current.Report;

            AddRequired(report, "winappruntime", "Windows App Runtime 1.8");
            AddRequired(report, "dotnet8", ".NET 8 桌面运行时");
            AddRequired(report, "node", "Node.js 22 LTS");
            AddRequired(report, "dsh", "DeepSeek Harness 本体");

            AddOptional(report, "git", "Git",
                Localization.IsChinese ? "用于从 Github 安装 DSH 插件" : "Required to install DSH plug-ins from GitHub.");
            AddOptional(report, "pnpm", "pnpm",
                Localization.IsChinese ? "安装插件更方便" : "Makes installing plug-ins more convenient.");
            AddOptional(report, "python", "Python",
                Localization.IsChinese ? "用于使用 Python SDK" : "Required only when using the Python SDK.");
        }

        private void AddRequired(ProbeReport report, string id, string fallbackName)
        {
            ComponentStatus status = report != null ? report[id] : null;
            bool satisfied = status != null && status.IsSatisfied;

            ComponentRow row = new ComponentRow
            {
                Name = status != null ? status.DisplayName : fallbackName,
            };
            row.SetCompact(true);

            if (satisfied)
            {
                row.SetState(RowState.Ready, Localization.IsChinese ? "已就绪" : "Ready", status.DetectedVersion);
            }
            else
            {
                row.SetState(RowState.Missing, Localization.IsChinese ? "将安装" : "Will be installed",
                    status != null ? status.DetectedVersion : null);
            }

            RequiredHost.Children.Add(row);
        }

        private void AddOptional(ProbeReport report, string id, string name, string why)
        {
            ComponentStatus status = report != null ? report[id] : null;
            bool installed = status != null && status.IsSatisfied;

            CheckBox box = new CheckBox
            {
                Content = name,
                IsChecked = !installed,
                IsEnabled = !installed,
                MinWidth = 0,
            };

            TextBlock whyText = new TextBlock
            {
                Text = installed
                    ? (Localization.IsChinese ? "已安装" : "Already installed")
                    : why,
                FontSize = 10.5,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(30, 0, 0, 4),
            };
            Theme.Bind(
                whyText,
                TextBlock.ForegroundProperty,
                "TertiaryTextBrush",
                Windows.UI.Color.FromArgb(255, 138, 144, 153));

            _optionalBoxes[id] = box;

            OptionalHost.Children.Add(box);
            OptionalHost.Children.Add(whyText);
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            string picked = FolderDialog.Pick(
                Localization.IsChinese ? "选择组件安装目录" : "Choose the component directory",
                RootBox.Text);

            if (!string.IsNullOrEmpty(picked))
            {
                RootBox.Text = picked;
            }
        }

        private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
        {
            InstallSession.Current.SourcePreference =
                SourceBox.SelectedIndex == 1 ? MirrorSource.Official : MirrorSource.China;
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
