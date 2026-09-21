using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DshInstaller.Controls;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 欢迎页。保留鲸鱼标作为 DeepSeek Harness 兼容标识，产品名称显示为大肥鱼Go。
    /// </summary>
    public sealed partial class WelcomePage : Page, IWizardPage
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

        private void BuildBrand()
        {
            MarkHost.Children.Clear();
            MarkHost.Children.Add(DshBrand.Mark(56));
            WordmarkText.Text = Localization.IsChinese ? "大肥鱼Go" : "Dafeiyu-Go";
            WordmarkEnglish.Text = Localization.IsChinese
                ? "Dafeiyu-Go"
                : "DeepSeek Harness Installer & Launcher";
        }

        private void ApplyText()
        {
            DescText.Text = Localization.T("welcome.desc");
            TermsText.Text = Localization.T("welcome.terms");

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
