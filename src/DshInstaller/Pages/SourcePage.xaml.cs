using Microsoft.UI.Xaml.Controls;
using DshInstaller.Controls;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    public sealed partial class SourcePage : Page, IWizardPage
    {
        private SelectableCard _chinaCard;
        private SelectableCard _officialCard;

        public SourcePage()
        {
            InitializeComponent();
            BuildCards();
            ApplyText();
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            InstallSession.Current.SourcePreference =
                _officialCard != null && _officialCard.IsSelected
                    ? MirrorSource.Official
                    : MirrorSource.China;
            return true;
        }

        private void BuildCards()
        {
            _chinaCard = new SelectableCard();
            _chinaCard.Selected += delegate { Select(_chinaCard); };
            _officialCard = new SelectableCard();
            _officialCard.Selected += delegate { Select(_officialCard); };
            CardsHost.Children.Add(_chinaCard);
            CardsHost.Children.Add(_officialCard);
        }

        private void Select(SelectableCard card)
        {
            _chinaCard.SetSelectedQuietly(
                ReferenceEquals(card, _chinaCard));
            _officialCard.SetSelectedQuietly(
                ReferenceEquals(card, _officialCard));
            InstallSession.Current.SourcePreference =
                ReferenceEquals(card, _officialCard)
                    ? MirrorSource.Official
                    : MirrorSource.China;
        }

        private void ApplyText()
        {
            bool chinese = Localization.IsChinese;
            Scaffold.Title = chinese
                ? "选择下载源"
                : "Choose download source";
            Scaffold.Subtitle = chinese
                ? "安装程序、Node、Git、Python、pnpm 和 DSH 本体都按这里的选择下载；中途仍可自动切换到可用源。"
                : "The installer, Node, Git, Python, pnpm and DSH core use this source. The installer can still fall back to another working source.";
            Scaffold.SetStep(0);

            _chinaCard.CardTitle = chinese
                ? "大陆 CDN 下载（推荐）"
                : "Mainland CDN (recommended)";
            _chinaCard.Description = chinese
                ? "只使用 npmmirror、华为云和国内 GitHub 加速镜像；镜像之间会自动重试。"
                : "Use only npmmirror, Huawei Cloud and mainland GitHub mirrors; retry between those mirrors.";
            _chinaCard.CardPath = chinese
                ? "适合中国大陆网络"
                : "For networks in mainland China";

            _officialCard.CardTitle = chinese
                ? "官方下载"
                : "Official sources";
            _officialCard.Description = chinese
                ? "只使用 GitHub、npm、python.org 和微软官方地址，不使用第三方镜像。"
                : "Use only GitHub, npm, python.org and Microsoft official endpoints.";
            _officialCard.CardPath = chinese
                ? "适合能够稳定访问官方源的网络"
                : "For stable access to official sources";

            Footnote.Text = chinese
                ? "下载源只影响下载速度，不影响最终安装内容。"
                : "The source affects download speed only, not the installed content.";

            bool official =
                InstallSession.Current.SourcePreference
                    == MirrorSource.Official;
            _chinaCard.SetSelectedQuietly(!official);
            _officialCard.SetSelectedQuietly(official);
        }
    }
}
