using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace DshInstaller.Shared
{
    /// <summary>
    /// 安装器的代理设置。语义照着启动器的 ProxySupport 来:
    ///
    ///   None   —— 明确直连(把请求的 Proxy 置空);
    ///   System —— 跟随 Windows 的 Internet 选项(.NET 默认行为);
    ///   Custom —— 用用户填的协议 + 地址 + 端口(http / https / socks5)。
    ///
    /// 两条硬规矩:
    ///   1. **回环地址一律绕开代理** —— 否则探本机 8787 端口也会被丢进代理;
    ///   2. 代理只作用在"出网请求"上,国内 CDN 与官方源的分流仍然由 MirrorSource 决定,
    ///      两者互不干涉(选官方源 + 自定义代理 = 走代理直连官方)。
    /// </summary>
    public sealed class ProxySettings
    {
        /// <summary>None / System / Custom。</summary>
        public string Mode { get; set; } = "None";

        /// <summary>Http / Https / Socks5。</summary>
        public string Protocol { get; set; } = "Http";

        public string Host { get; set; } = string.Empty;

        public int Port { get; set; }

        public ProxySettings Clone()
        {
            return new ProxySettings
            {
                Mode = Mode,
                Protocol = Protocol,
                Host = Host,
                Port = Port,
            };
        }
    }

    /// <summary>安装器所有出网请求统一从这里取代理。</summary>
    public static class ProxySupport
    {
        private static readonly object Gate = new object();
        private static readonly string[] LocalBypass =
        {
            "localhost",
            "127.0.0.1",
            "::1",
        };

        private static ProxySettings _current = new ProxySettings();

        /// <summary>
        /// 代理设置的版本号,每次改动 +1。
        ///
        /// HttpClient 是按静态字段缓存的,代理改了不重建就等于没改。
        /// 与其到处记得调"刷新一下",不如让需要的人自己去比这个号
        /// (下载引擎就是这么做的)—— 少一处"忘了调"的坑。
        /// </summary>
        public static int Generation
        {
            get
            {
                lock (Gate)
                {
                    return _generation;
                }
            }
        }

        private static int _generation;

        /// <summary>当前生效的代理设置(改完记得调 DownloadEngine.RefreshClient)。</summary>
        public static ProxySettings Current
        {
            get
            {
                lock (Gate)
                {
                    return _current;
                }
            }

            set
            {
                lock (Gate)
                {
                    _current = value == null ? new ProxySettings() : value.Clone();
                    _generation++;
                }
            }
        }

        public static string Mode
        {
            get { return Normalize(Current.Mode); }
        }

        public static bool IsDirect
        {
            get { return Mode == "None"; }
        }

        /// <summary>从安装器自己的配置文件读代理设置。</summary>
        public static ProxySettings Load()
        {
            InstallerState state = null;
            try
            {
                state = ConfigStore.LoadRaw();
            }
            catch
            {
            }

            ProxySettings settings = new ProxySettings();
            if (state != null)
            {
                settings.Mode = Normalize(state.ProxyMode);
                settings.Protocol = NormalizeProtocol(state.ProxyProtocol);
                settings.Host = state.ProxyHost ?? string.Empty;
                settings.Port = state.ProxyPort;
            }

            Current = settings;
            return settings;
        }

        /// <summary>把代理设置落盘(写进 installer-state.json,和安装状态同一份配置)。</summary>
        public static void Save(ProxySettings settings)
        {
            Current = settings;

            try
            {
                InstallerState state = ConfigStore.LoadRaw() ?? new InstallerState();
                state.ProxyMode = Current.Mode;
                state.ProxyProtocol = Current.Protocol;
                state.ProxyHost = Current.Host;
                state.ProxyPort = Current.Port;
                ConfigStore.Save(state);
            }
            catch
            {
            }
        }

        /// <summary>拼出代理地址。返回 false = 当前不该用代理。</summary>
        public static bool TryBuildProxyUri(out Uri uri)
        {
            return TryBuildProxyUri(Current, out uri);
        }

        public static bool TryBuildProxyUri(ProxySettings settings, out Uri uri)
        {
            uri = null;
            if (settings == null || Normalize(settings.Mode) != "Custom")
            {
                return false;
            }

            string host = (settings.Host ?? string.Empty).Trim();
            if (host.Length == 0 || settings.Port < 1 || settings.Port > 65535)
            {
                return false;
            }

            string scheme = "http";
            string protocol = NormalizeProtocol(settings.Protocol);
            if (protocol == "Https")
            {
                scheme = "https";
            }
            else if (protocol == "Socks5")
            {
                scheme = "socks5";
            }

            string hostPart = host.IndexOf(':') >= 0 && !host.StartsWith("[", StringComparison.Ordinal)
                ? "[" + host + "]"
                : host;

            return Uri.TryCreate(
                scheme + "://" + hostPart + ":" + settings.Port,
                UriKind.Absolute,
                out uri);
        }

        /// <summary>造一个 .NET 代理对象(回环绕过已打开)。</summary>
        public static IWebProxy CreateProxy()
        {
            Uri uri;
            if (!TryBuildProxyUri(out uri))
            {
                return null;
            }

            return new WebProxy(uri)
            {
                BypassProxyOnLocal = true,
                BypassList = LocalBypass,
            };
        }

        /// <summary>把三种模式作用到 HttpClientHandler 上。</summary>
        public static void Apply(HttpClientHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            switch (Mode)
            {
                case "System":
                    handler.UseProxy = true;
                    handler.Proxy = null;
                    break;

                case "Custom":
                    IWebProxy proxy = CreateProxy();
                    handler.UseProxy = proxy != null;
                    handler.Proxy = proxy;
                    break;

                default:
                    handler.UseProxy = false;
                    handler.Proxy = null;
                    break;
            }
        }

        /// <summary>
        /// 给子进程(npm / pnpm / git / node)准备代理环境变量。
        /// 直连时显式清掉,免得继承到系统里那份、把用户"不使用代理"的选择推翻。
        /// </summary>
        public static IDictionary<string, string> ProcessEnvironment(IDictionary<string, string> target = null)
        {
            Dictionary<string, string> environment = new Dictionary<string, string>(
                target ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);

            ApplyTo(environment);
            return environment;
        }

        /// <summary>
        /// 把当前代理设置写进一组环境变量(就地改,调用方给的就是子进程要用的那份)。
        ///
        /// 所有外部命令都走 ProcessRunner,由它统一调用这里 —— 这样 npm / pnpm / git
        /// 这些自己发请求的工具也认这份代理,而不是只有我们自己的下载引擎认。
        /// </summary>
        public static void ApplyTo(IDictionary<string, string> environment)
        {
            if (environment == null)
            {
                return;
            }

            string[] names = { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" };

            // 先按不区分大小写删干净:环境变量名在 Windows 上不区分大小写,
            // 但字典是区分的;不清掉旧键就会出现两份,子进程取哪份看运气。
            List<string> keys = new List<string>();
            foreach (string key in environment.Keys)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(key, names[i], StringComparison.OrdinalIgnoreCase))
                    {
                        keys.Add(key);
                        break;
                    }
                }
            }

            for (int i = 0; i < keys.Count; i++)
            {
                environment.Remove(keys[i]);
            }

            string noProxy = "localhost,127.0.0.1,::1";
            string mode = Mode;

            if (mode == "None")
            {
                environment["NO_PROXY"] = noProxy;
                return;
            }

            Uri uri = null;
            if (mode == "System")
            {
                uri = ResolveSystemProxy();
            }
            else
            {
                TryBuildProxyUri(out uri);
            }

            if (uri != null)
            {
                environment["HTTP_PROXY"] = uri.AbsoluteUri;
                environment["HTTPS_PROXY"] = uri.AbsoluteUri;
                environment["ALL_PROXY"] = uri.AbsoluteUri;
            }

            environment["NO_PROXY"] = noProxy;
        }

        /// <summary>给用户看的一句话说明。</summary>
        public static string Describe()
        {
            return Describe(Current);
        }

        public static string Describe(ProxySettings settings)
        {
            switch (Normalize(settings == null ? null : settings.Mode))
            {
                case "System":
                    return SharedText.T("跟随系统代理", "Use the system proxy");

                case "Custom":
                    Uri uri;
                    return TryBuildProxyUri(settings, out uri)
                        ? SharedText.T("自定义代理 " + uri.AbsoluteUri, "Custom proxy " + uri.AbsoluteUri)
                        : SharedText.T("自定义代理(没填全,按直连处理)", "Custom proxy (incomplete, using a direct connection)");

                default:
                    return SharedText.T("不使用代理", "Do not use a proxy");
            }
        }

        /// <summary>系统代理(没有就返回 null)。</summary>
        public static Uri ResolveSystemProxy()
        {
            try
            {
                IWebProxy proxy = WebRequest.GetSystemWebProxy();
                if (proxy == null)
                {
                    return null;
                }

                Uri probe = new Uri("https://registry.npmjs.org/");
                Uri resolved = proxy.GetProxy(probe);
                if (resolved == null
                    || resolved == probe
                    || string.Equals(resolved.Host, probe.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return resolved;
            }
            catch
            {
                return null;
            }
        }

        private static string Normalize(string value)
        {
            if (string.Equals(value, "System", StringComparison.OrdinalIgnoreCase))
            {
                return "System";
            }

            if (string.Equals(value, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                return "Custom";
            }

            return "None";
        }

        private static string NormalizeProtocol(string value)
        {
            if (string.Equals(value, "Https", StringComparison.OrdinalIgnoreCase))
            {
                return "Https";
            }

            if (string.Equals(value, "Socks5", StringComparison.OrdinalIgnoreCase))
            {
                return "Socks5";
            }

            return "Http";
        }
    }
}
