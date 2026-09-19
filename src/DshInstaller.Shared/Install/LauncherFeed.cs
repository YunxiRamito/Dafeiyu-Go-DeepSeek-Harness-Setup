using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 启动器的发布清单。
    /// 安装器不再把启动器打包进自己肚子里,而是去这个清单查最新版再下。
    /// 好处:启动器发新版,安装器一个字都不用重发。
    /// </summary>
    public sealed class LauncherRelease
    {
        public string Version { get; set; }

        /// <summary>下载地址列表,按优先级排(镜像在前,官方兜底)。</summary>
        public List<string> Urls { get; set; } = new List<string>();

        /// <summary>压缩包 SHA256,可为空(空就跳过校验)。</summary>
        public string Sha256 { get; set; }

        /// <summary>压缩包里启动器所在的相对子目录(正规发布时为空)。</summary>
        public string SubDirectory { get; set; }

        public string Notes { get; set; }
    }

    /// <summary>拉取并解析启动器发布清单。</summary>
    public static class LauncherFeed
    {
        /// <summary>
        /// 清单地址。默认指向启动器仓库里的 manifest.json:
        /// - 官方走 raw.githubusercontent
        /// - 国内走 jsDelivr CDN 的 gh 通道(带 @main 这种 tag)
        /// 用户在界面上切"官方/镜像"时,这里跟着换顺序。
        /// </summary>
        public static List<string> ManifestUrls(string repository, string branch = "main", string file = "manifest.json")
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return new List<string>();
            }

            string repo = repository.Trim().Trim('/');
            string raw = "https://raw.githubusercontent.com/" + repo + "/" + branch + "/" + file;

            // 顺序按"国内可达性"排。raw.githubusercontent 在国内经常直接超时,
            // 所以加速前缀放前面。
            //
            // jsDelivr 那条要用**国内镜像** cdn.jsdmirror.com:官方 cdn.jsdelivr.net
            // 实测只有 23 KB/s,而 jsdmirror 有 1.5 MB/s(同一次测试、同一个文件)。
            List<string> urls = new List<string>();
            urls.Add("https://cdn.jsdmirror.com/gh/" + repo + "@" + branch + "/" + file);
            urls.Add("https://ghproxy.net/" + raw);
            urls.Add("https://gh-proxy.com/" + raw);
            urls.Add("https://cdn.jsdelivr.net/gh/" + repo + "@" + branch + "/" + file);
            urls.Add(raw);
            return urls;
        }

        /// <summary>
        /// 启动器发行包在 npm 上的名字。
        ///
        /// 为什么不直接把 zip 挂在 GitHub:国内直连只有几十到一百多 KB/s,
        /// 而且要 10 MB。npm 有 npmmirror 全量镜像,实测 9.8 MB/s —— 快两个数量级。
        /// (jsDelivr 试过,不行:它只对热门仓库的已缓存文件快,我们这种冷门仓库
        /// 走它还是回源 GitHub 的速度,八连接甚至 0 字节。)
        /// </summary>
        public const string NpmPackage = "@yunxiramito/dsh-launcher";

        /// <summary>
        /// 由**版本号拼出** npmmirror 的 tarball 地址。
        ///
        /// 关键点:地址是确定的,不用在清单里维护。所以以后发启动器只要推一个新版本号,
        /// 安装器一行都不用改,也不用等谁去同步 mirrors 数组。
        /// </summary>
        public static string NpmMirrorUrl(string version)
        {
            return "https://registry.npmmirror.com/" + NpmPackage + "/-/" + NpmTarballName(version);
        }

        /// <summary>npm tarball 的文件名(作用域包的文件名不带作用域)。</summary>
        private static string NpmTarballName(string version)
        {
            int slash = NpmPackage.LastIndexOf('/');
            string bare = slash >= 0 ? NpmPackage.Substring(slash + 1) : NpmPackage;
            return bare + "-" + version + ".tgz";
        }

        /// <summary>把 GitHub release 资产地址包一层国内加速前缀。</summary>
        public static List<string> MirrorizeAsset(string assetUrl, string preference)
        {
            List<string> urls = new List<string>();
            if (string.IsNullOrEmpty(assetUrl))
            {
                return urls;
            }

            // 加速前缀只对 GitHub 地址有意义。
            // 实测:把前缀套在 python.org 上,ghproxy 返 403、gh-proxy 返 404,
            // 其余全部超时 —— 白等 40 秒,还把本来能直连的地址给耽误了。
            bool isGitHub = assetUrl.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0
                || assetUrl.IndexOf("githubusercontent.com", StringComparison.OrdinalIgnoreCase) >= 0
                || assetUrl.IndexOf("github.io", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isGitHub)
            {
                urls.Add(assetUrl);
                return urls;
            }

            // 顺序按**最近一次实测**(2026-09-19 晚,虚拟机,同一张网)排:
            //   gh-proxy.com  173 KB/s  ← 最快,放第一
            //   ghproxy.net    44 KB/s
            //   ghfast.top / ghproxy.homeboyc.cn / github.akams.cn / ghp.ci / moeyy.cn
            //                           全部超时 —— 已经剔掉,留着只会让测速阶段白等
            // 另外现在**开下前会实测排序**(SpeedProbe),所以这份顺序主要是
            // "测速失败时的兜底顺序",排第一的那个必须是最靠谱的。
            string[] proxies = new string[]
            {
                "https://gh-proxy.com/",
                "https://ghproxy.net/",
                "https://ghfast.top/",
            };

            if (string.Equals(preference, MirrorSource.China, StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < proxies.Length; i++)
                {
                    urls.Add(proxies[i] + assetUrl);
                }

                urls.Add(assetUrl);
            }
            else
            {
                urls.Add(assetUrl);
                for (int i = 0; i < proxies.Length; i++)
                {
                    urls.Add(proxies[i] + assetUrl);
                }
            }

            return urls;
        }

        /// <summary>
        /// 拉清单。清单格式(放在启动器仓库根目录的 manifest.json):
        /// {
        ///   "version": "1.3.9",
        ///   "sha256": "....",
        ///   "subDirectory": "",
        ///   "assets": {
        ///     "github": "https://github.com/<owner>/<repo>/releases/download/v1.3.9/DeepSeekHarness-1.3.9.zip",
        ///     "mirrors": ["https://..."]
        ///   },
        ///   "notes": "..."
        /// }
        /// 也兼容只写 version + url 的简版。
        /// </summary>
        public static LauncherRelease Fetch(string repository, string preference, string branch = "main")
        {
            List<string> urls = ManifestUrls(repository, branch);
            if (urls.Count == 0)
            {
                return null;
            }

            if (string.Equals(preference, MirrorSource.China, StringComparison.OrdinalIgnoreCase))
            {
                // 镜像优先
                urls.Reverse();
            }

            string json = DownloadEngine.DownloadText(urls, 20000);
            if (!string.IsNullOrWhiteSpace(json))
            {
                LauncherRelease fromManifest = Parse(json, preference);
                if (fromManifest != null && fromManifest.Urls != null && fromManifest.Urls.Count > 0)
                {
                    return fromManifest;
                }
            }

            // 清单拿不到(仓库里还没写 manifest.json,或者 CDN 全被墙)就退到 GitHub API。
            // API 的响应结构不一样,单独解析。
            InstallLogger.Write("启动器清单拉取失败,改试 GitHub API: " + repository);

            List<string> apiUrls = ApiUrls(repository);
            string apiJson = DownloadEngine.DownloadText(apiUrls, 20000);
            if (!string.IsNullOrWhiteSpace(apiJson))
            {
                LauncherRelease fromApi = ParseGitHubRelease(apiJson, preference);
                if (fromApi != null && fromApi.Urls != null && fromApi.Urls.Count > 0)
                {
                    return fromApi;
                }
            }

            InstallLogger.Write("启动器清单与 API 都拿不到: " + repository);
            return null;
        }

        /// <summary>
        /// GitHub API 的候选地址。同样套一层国内加速前缀。
        /// API 直连在国内常常不稳,所以前缀放前面。
        /// </summary>
        public static List<string> ApiUrls(string repository)
        {
            List<string> urls = new List<string>();
            if (string.IsNullOrWhiteSpace(repository))
            {
                return urls;
            }

            string repo = repository.Trim().Trim('/');
            string api = "https://api.github.com/repos/" + repo + "/releases/latest";

            urls.Add("https://ghproxy.net/" + api);
            urls.Add(api);
            return urls;
        }

        /// <summary>
        /// 解析 GitHub API 的 releases/latest 响应。
        /// 结构跟仓库里的 manifest.json 完全不同:版本在 tag_name,资产在 assets[].browser_download_url。
        /// </summary>
        public static LauncherRelease ParseGitHubRelease(string json, string preference)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    LauncherRelease release = new LauncherRelease();

                    JsonElement tag;
                    if (root.TryGetProperty("tag_name", out tag) && tag.ValueKind == JsonValueKind.String)
                    {
                        release.Version = tag.GetString().TrimStart('v', 'V');
                    }

                    JsonElement body;
                    if (root.TryGetProperty("body", out body) && body.ValueKind == JsonValueKind.String)
                    {
                        release.Notes = body.GetString();
                    }

                    JsonElement assets;
                    if (root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement asset in assets.EnumerateArray())
                        {
                            JsonElement url;
                            if (!asset.TryGetProperty("browser_download_url", out url)
                                || url.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            string assetUrl = url.GetString();
                            if (string.IsNullOrWhiteSpace(assetUrl)
                                || !assetUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            release.Urls.AddRange(MirrorizeAsset(assetUrl, preference));
                        }
                    }

                    if (release.Urls.Count == 0)
                    {
                        return null;
                    }

                    if (string.IsNullOrWhiteSpace(release.Version))
                    {
                        release.Version = GuessVersion(release.Urls[0]);
                    }

                    return release;
                }
            }
            catch (Exception exception)
            {
                InstallLogger.Write("解析 GitHub API 响应失败: " + exception.Message);
                return null;
            }
        }
        public static LauncherRelease Parse(string json, string preference)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    LauncherRelease release = new LauncherRelease();

                    JsonElement version;
                    if (root.TryGetProperty("version", out version))
                    {
                        release.Version = version.GetString();
                    }

                    JsonElement sha;
                    if (root.TryGetProperty("sha256", out sha) && sha.ValueKind == JsonValueKind.String)
                    {
                        release.Sha256 = sha.GetString();
                    }

                    JsonElement sub;
                    if (root.TryGetProperty("subDirectory", out sub) && sub.ValueKind == JsonValueKind.String)
                    {
                        release.SubDirectory = sub.GetString();
                    }

                    JsonElement notes;
                    if (root.TryGetProperty("notes", out notes) && notes.ValueKind == JsonValueKind.String)
                    {
                        release.Notes = notes.GetString();
                    }

                    // 地址:assets.github / assets.mirrors[] 或者简版 url / urls[]
                    JsonElement assets;
                    if (root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Object)
                    {
                        // **镜像排在最前面。**
                        //
                        // 清单里的 mirrors 是人工挑过的国内镜像(实测 1.5 MB/s),
                        // 而 github 那条要套 ghproxy 前缀(几十到一百多 KB/s,抖得厉害)。
                        // 顺序反了的话,配合"只测前三个节点"就等于**从来没测过镜像**
                        // —— 表现是"明明配了 jsdmirror,安装器还是走 GitHub"(实测踩过)。
                        //
                        // mirrors 是加速线路专属的:官方线路下一条都不用 ——
                        // 用户选"官方"通常是在墙外,直连 GitHub 比任何镜像都快。
                        bool useMirrors = MirrorSource.IsChina(preference);

                        JsonElement mirrors;
                        if (useMirrors
                            && assets.TryGetProperty("mirrors", out mirrors)
                            && mirrors.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement item in mirrors.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    string value = item.GetString();
                                    if (!release.Urls.Contains(value))
                                    {
                                        release.Urls.Add(value);
                                    }
                                }
                            }
                        }

                        JsonElement github;
                        if (assets.TryGetProperty("github", out github) && github.ValueKind == JsonValueKind.String)
                        {
                            release.Urls.AddRange(MirrorizeAsset(github.GetString(), preference));
                        }
                        else if (!useMirrors)
                        {
                            // 没给 github 字段、又是官方线路:没什么可用的了
                            InstallLogger.Write("清单里既没有 github 字段,mirrors 在官方线路下也不采用");
                        }
                    }
                    else
                    {
                        JsonElement url;
                        if (root.TryGetProperty("url", out url) && url.ValueKind == JsonValueKind.String)
                        {
                            release.Urls.AddRange(MirrorizeAsset(url.GetString(), preference));
                        }

                        JsonElement urls;
                        if (root.TryGetProperty("urls", out urls) && urls.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement item in urls.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    string value = item.GetString();
                                    if (!release.Urls.Contains(value))
                                    {
                                        release.Urls.Add(value);
                                    }
                                }
                            }
                        }
                    }

                    if (release.Urls.Count == 0)
                    {
                        InstallLogger.Write("启动器清单里没有可用地址");
                        return null;
                    }

                    return release;
                }
            }
            catch (Exception exception)
            {
                InstallLogger.Write("解析启动器清单失败: " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// 从 GitHub release 资产文件名里猜版本号,失败返回 null。
        /// 主要给"没有 manifest 时直接扒 latest 页面"这种兜底用。
        /// </summary>
        public static string GuessVersion(string fileName)
        {
            Match match = Regex.Match(fileName, "(\\d+\\.\\d+\\.\\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}