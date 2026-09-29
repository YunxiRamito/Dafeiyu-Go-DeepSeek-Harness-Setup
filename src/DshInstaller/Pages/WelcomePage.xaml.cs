using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DshInstaller.Controls;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 欢迎页。保留鲸鱼标作为 DeepSeek Harness 兼容标识，产品名统一显示 Dafeiyu-Go。
    /// </summary>
    public sealed partial class WelcomePage : Page, IWizardPage, IWizardPageFooterAction
    {
        public WelcomePage()
        {
            InitializeComponent();
            BuildBrand();
            ApplyText();
            ActualThemeChanged += delegate
            {
                BuildBrand();
                ApplyText();
            };
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            return true;
        }

        /// <summary>
        /// 页脚动作:让「修复安装」贴在主按钮(「开始安装」)左边。
        ///
        /// 为什么从页面里搬出来:它以前是页面中间的一个按钮,和"开始安装"离得远,
        /// 用户根本看不出这两个是同一层的选择(装 / 修)。
        /// </summary>
        public string FooterActionText
        {
            get { return Localization.T("welcome.repair"); }
        }

        /// <summary>没有安装痕迹就不给点 —— 见 <see cref="HasInstallTrace"/>。</summary>
        public bool FooterActionEnabled
        {
            get { return HasInstallTrace(); }
        }

        public void OnFooterAction()
        {
            OnRepairClick(null, null);
        }

        /// <summary>
        /// 这台机器上到底有没有装过。
        ///
        /// 以前是"按钮能点、点下去才告诉你没有安装记录",白点一次才知道;
        /// 现在直接拿它的结果当禁用条件,按钮灰着本身就说明了状态。
        /// </summary>
        private static bool HasInstallTrace()
        {
            try
            {
                InstallSession session = InstallSession.Current;
                InstallOptions options = new InstallOptions
                {
                    ComponentsRoot = session.ComponentsRoot,
                    DshRoot = session.DshRoot,
                    LauncherRoot = session.LauncherRoot,
                };

                return InstallManifest.HasInstallTrace(options);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 「修复安装」:按安装记录核对一遍、把缺的补回来 —— 不重走向导,也不动用户数据。
        /// 没有安装记录时不给硬闯(修复无从谈起),提示一句让他老老实实装一次。
        /// </summary>
        private void OnRepairClick(object sender, RoutedEventArgs e)
        {
            InstallSession session = InstallSession.Current;

            if (!session.SetupRepair())
            {
                RepairHint.Text = Localization.T("welcome.repair.norecord");
                return;
            }

            MainWindow window = App.MainWindowInstance;
            if (window == null)
            {
                return;
            }

            window.Navigate(WizardPage.Progress, null);
        }

        private void BuildBrand()
        {
            MarkHost.Children.Clear();
            MarkHost.Children.Add(DshBrand.Mark(56));
            // 中英同名:品牌就是 Dafeiyu-Go,不再按语言换两套写法
            WordmarkText.Text = WellKnown.ProductName;
            WordmarkEnglish.Text = Localization.IsChinese
                ? "Dafeiyu-Go"
                : "DeepSeek Harness Installer & Launcher";
        }

        private void ApplyText()
        {
            DescText.Text = Localization.T("welcome.desc");
            TermsText.Text = Localization.T("welcome.terms");
            // 「修复安装」已经搬到页脚主按钮左边了,页面里这个不再显示
            // (XAML 里的元素先留着,免得动布局文件)
            RepairButton.Visibility = Visibility.Collapsed;
            RepairHint.Text = Localization.T("welcome.repair.hint");

            NotesHost.Children.Clear();
            AddNote("welcome.note1", "\uE774");   // globe
            AddNote("welcome.note2", "\uE7B8");   // package
            AddNote("welcome.note3", "\uE7FB");   // shield
        }

        private void AddNote(string key, string glyph)
        {
            // 用 Grid 而不是横向 StackPanel:横向 StackPanel 给子元素的可用宽度是无穷大,
            // TextWrapping 不会生效,英文长文案会溢出被裁。
            Grid row = new Grid { ColumnSpacing = 9 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
            });

            TextBlock note = new TextBlock
            {
                Text = Localization.T(key),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("SecondaryTextBrush", Windows.UI.Color.FromArgb(255, 92, 99, 110)),
            };
            Grid.SetColumn(note, 1);
            row.Children.Add(note);

            NotesHost.Children.Add(row);
        }
    }
}
