using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Controls;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 源选择页。上面选下载源(大陆 CDN / 官方),下面选代理。
    ///
    /// 两件事分开:源决定"去哪儿下",代理决定"怎么连出去"。
    /// 代理这一块是三选一:不使用 / 跟随系统 / 自定义(http·https·socks5 + 地址端口)。
    /// </summary>
    public sealed partial class SourcePage : Page, IWizardPage
    {
        private SelectableCard _chinaCard;
        private SelectableCard _officialCard;

        /// <summary>控件还没铺好之前,事件里别落盘 —— 初始化本身会触发一轮 SelectionChanged。</summary>
        private bool _proxyReady;

        private bool _loadingProxy;

        public SourcePage()
        {
            InitializeComponent();
            BuildCards();
            BuildProxy();
            ApplyText();
            Loaded += OnLoaded;
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

            // 代理已经边改边存了,这里再落一次兜底(用户可能只改了一半就点了下一步)
            SaveProxy();
            return true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadProxyIntoUi();
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

        // ---------------------------------------------------------------- 代理

        private void BuildProxy()
        {
            bool chinese = Localization.IsChinese;

            // 顺序 = 0 不使用 / 1 跟随系统 / 2 自定义,取选中项就是取这个序号
            ProxyModeHost.Items.Add(new RadioButton
            {
                Content = chinese ? "不使用代理" : "No proxy",
            });
            ProxyModeHost.Items.Add(new RadioButton
            {
                Content = chinese ? "跟随系统代理" : "Use the system proxy",
            });
            ProxyModeHost.Items.Add(new RadioButton
            {
                Content = chinese ? "自定义代理" : "Custom proxy",
            });

            ProxyProtocolBox.Items.Add("http");
            ProxyProtocolBox.Items.Add("https");
            ProxyProtocolBox.Items.Add("socks5");
            ProxyProtocolBox.SelectedIndex = 0;
            ProxyHostBox.PlaceholderText = "127.0.0.1";
            ProxyPortBox.PlaceholderText = "7890";
            ProxyPortHint.Text = chinese ? "端口" : "Port";
        }

        /// <summary>
        /// 把落盘的代理设置填回控件。
        ///
        /// **必须等控件稳定**(Loaded)再填:构造阶段填的话,后面 WinUI 自己那轮
        /// 首次布局会再触发一次 SelectionChanged,把刚读出来的设置盖回去 ——
        /// 启动器上被这个坑坑过一次(设置重启后变回"不使用代理")。
        /// </summary>
        private void LoadProxyIntoUi()
        {
            _loadingProxy = true;

            try
            {
                ProxySettings settings = ProxySupport.Load();

                ProxyModeHost.SelectedIndex = ModeIndex(settings.Mode);
                ProxyProtocolBox.SelectedIndex = ProtocolIndex(settings.Protocol);
                ProxyHostBox.Text = settings.Host ?? string.Empty;
                ProxyPortBox.Text = settings.Port > 0
                    ? settings.Port.ToString()
                    : string.Empty;
            }
            catch
            {
            }
            finally
            {
                _loadingProxy = false;
                _proxyReady = true;
            }

            UpdateProxyDetail();
        }

        private static int ModeIndex(string mode)
        {
            if (string.Equals(mode, "System", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        }

        private static int ProtocolIndex(string protocol)
        {
            if (string.Equals(protocol, "Https", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return string.Equals(protocol, "Socks5", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        }

        private static string ProtocolFromIndex(int index)
        {
            return index == 1 ? "Https" : (index == 2 ? "Socks5" : "Http");
        }

        private void OnProxyModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_proxyReady || _loadingProxy)
            {
                return;
            }

            UpdateProxyDetail();
            SaveProxy();
        }

        private void OnProxyDetailChanged(object sender, object e)
        {
            if (!_proxyReady || _loadingProxy)
            {
                return;
            }

            UpdateProxyDetail();
            SaveProxy();
        }

        private void UpdateProxyDetail()
        {
            bool custom = ProxyModeHost.SelectedIndex == 2;
            ProxyDetailHost.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            ProxyHint.Text = DescribeProxy();
        }

        private ProxySettings ReadProxyFromUi()
        {
            ProxySettings settings = new ProxySettings
            {
                Mode = ProxyModeHost.SelectedIndex == 1
                    ? "System"
                    : (ProxyModeHost.SelectedIndex == 2 ? "Custom" : "None"),
                Protocol = ProtocolFromIndex(ProxyProtocolBox.SelectedIndex),
                Host = (ProxyHostBox.Text ?? string.Empty).Trim(),
            };

            int port;
            if (int.TryParse((ProxyPortBox.Text ?? string.Empty).Trim(), out port)
                && port > 0 && port <= 65535)
            {
                settings.Port = port;
            }

            return settings;
        }

        private void SaveProxy()
        {
            try
            {
                ProxySupport.Save(ReadProxyFromUi());
            }
            catch
            {
            }
        }

        /// <summary>给用户看的一句话:现在会怎么连。</summary>
        private string DescribeProxy()
        {
            ProxySettings settings = ReadProxyFromUi();
            bool chinese = Localization.IsChinese;

            if (settings.Mode == "System")
            {
                return chinese
                    ? "使用 Windows「Internet 选项」里的代理设置。"
                    : "Use the proxy configured in Windows Internet options.";
            }

            if (settings.Mode != "Custom")
            {
                return chinese
                    ? "所有下载都直连，不经过代理。"
                    : "All downloads connect directly, without a proxy.";
            }

            Uri uri;
            if (!ProxySupport.TryBuildProxyUri(settings, out uri))
            {
                return chinese
                    ? "地址或端口还没填全，现在会按直连处理。"
                    : "Address or port is incomplete; downloads will connect directly.";
            }

            return chinese
                ? "所有下载走 " + uri.AbsoluteUri + "；本机地址不经过代理。"
                : "All downloads use " + uri.AbsoluteUri + "; local addresses bypass it.";
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

            ProxyLabel.Text = chinese ? "网络代理" : "Network proxy";

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
