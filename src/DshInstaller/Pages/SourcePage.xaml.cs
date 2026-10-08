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
        private SelectableCard _backendCard;

        /// <summary>控件还没铺好之前,事件里别落盘 —— 初始化本身会触发一轮 SelectionChanged。</summary>
        private bool _proxyReady;

        private bool _loadingProxy;
        private MainWindow _availabilityWindow;

        public SourcePage()
        {
            InitializeComponent();
            BuildCards();
            BuildProxy();
            ApplyText();
            Loaded += OnLoaded;
            Unloaded += delegate
            {
                if (_availabilityWindow != null) _availabilityWindow.BackendStateChanged -= UpdateBackendAvailability;
                _availabilityWindow = null;
            };
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            InstallSession.Current.SourcePreference =
                _backendCard != null && _backendCard.IsSelected ? MirrorSource.Backend : _officialCard != null && _officialCard.IsSelected
                    ? MirrorSource.Official
                    : MirrorSource.China;
            BackendDownloadSource.SelectedPreference = InstallSession.Current.SourcePreference;

            // 代理已经边改边存了,这里再落一次兜底(用户可能只改了一半就点了下一步)
            SaveProxy();
            return true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadProxyIntoUi();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsLoaded) return;
                _availabilityWindow = App.MainWindowInstance;
                if (_availabilityWindow != null) _availabilityWindow.BackendStateChanged += UpdateBackendAvailability;
                UpdateBackendAvailability();
            });
        }

        private void BuildCards()
        {
            _chinaCard = new SelectableCard();
            _chinaCard.Selected += delegate { Select(_chinaCard); };
            _officialCard = new SelectableCard();
            _officialCard.Selected += delegate { Select(_officialCard); };
            CardsHost.Children.Add(_chinaCard);
            _backendCard = new SelectableCard();
            _backendCard.IsEnabled = false;
            _backendCard.Opacity = 0.5;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_backendCard, "BackendSourceCard");
            _backendCard.Selected += delegate { Select(_backendCard); };
            CardsHost.Children.Add(_backendCard);
            CardsHost.Children.Add(_officialCard);
        }

        private void Select(SelectableCard card)
        {
            if (!card.IsEnabled) return;
            _chinaCard.SetSelectedQuietly(
                ReferenceEquals(card, _chinaCard));
            _officialCard.SetSelectedQuietly(
                ReferenceEquals(card, _officialCard));
            _backendCard.SetSelectedQuietly(ReferenceEquals(card, _backendCard));
            InstallSession.Current.SourcePreference =
                ReferenceEquals(card, _backendCard) ? MirrorSource.Backend : ReferenceEquals(card, _officialCard)
                    ? MirrorSource.Official
                    : MirrorSource.China;
            BackendDownloadSource.SelectedPreference = InstallSession.Current.SourcePreference;
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
                ? "大陆CDN下载（推荐）"
                : "Mainland CDN (recommended)";
            _chinaCard.Description = chinese
                ? "使用国内CDN镜像加速下载"
                : "Accelerate downloads with mainland CDN mirrors";
            _chinaCard.CardPath = chinese
                ? "适合中国大陆网络"
                : "For networks in mainland China";

            _officialCard.CardTitle = chinese
                ? "官方下载"
                : "Official sources";
            _officialCard.Description = chinese
                ? "适合海外用户，从Github/npm等官方源下载"
                : "For overseas users: download from official sources such as GitHub and npm";
            _officialCard.CardPath = chinese
                ? "适合能够稳定访问官方源的网络"
                : "For stable access to official sources";
            _backendCard.CardTitle = chinese ? "大肥鱼国内加速（备用）" : "Dafeiyu mainland acceleration (backup)";
            _backendCard.Description = chinese ? "使用软件自己的后端服务器下载；速度可能稍慢，但适配大部分国内用户连接" : "Download through the application's backend; speeds may be slower, but connectivity suits most users in mainland China";
            _backendCard.CardPath = chinese ? "大肥鱼后端服务器" : "Dafeiyu backend server";

            Footnote.Text = chinese
                ? "下载源只影响下载速度，不影响最终安装内容。"
                : "The source affects download speed only, not the installed content.";

            bool official =
                InstallSession.Current.SourcePreference
                    == MirrorSource.Official;
            bool backend = BackendDownloadSource.IsSelected(InstallSession.Current.SourcePreference);
            _chinaCard.SetSelectedQuietly(!official && !backend);
            _officialCard.SetSelectedQuietly(official);
            _backendCard.SetSelectedQuietly(backend);
            BackendDownloadSource.SelectedPreference = InstallSession.Current.SourcePreference;
        }

        private void UpdateBackendAvailability()
        {
            bool online = _availabilityWindow?.BackendState == BackendConnectionState.Online;
            _backendCard.IsEnabled = online;
            _backendCard.Opacity = online ? 1 : 0.5;
        }
    }
}
