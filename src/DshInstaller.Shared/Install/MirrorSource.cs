using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 下载源。默认官方,GitHub 那类会给一条镜像优先。
    /// 用户能在界面上切换"官方 / 国内镜像"。
    /// </summary>
    public static class MirrorSource
    {
        public const string Backend = "Backend";
        public static List<string> BackendUrls(string official, string preference)
        {
            var urls = new List<string>();
            if (BackendDownloadSource.IsSelected(preference)) urls.Add(BackendDownloadSource.Wrap(official));
            urls.Add(official); return urls;
        }
        public const string Official = "official";
        public const string China = "china";

        /// <summary>按偏好给出候选地址:选中的排前面,另一条兜底。</summary>
        public static List<string> Order(string preference, string officialUrl, string chinaUrl)
        {
            if (BackendDownloadSource.IsSelected(preference)) return BackendUrls(officialUrl, preference);
            List<string> result = new List<string>();
            if (string.Equals(preference, China, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(chinaUrl)) { result.Add(chinaUrl); }
                if (!string.IsNullOrEmpty(officialUrl)) { result.Add(officialUrl); }
            }
            else
            {
                if (!string.IsNullOrEmpty(officialUrl)) { result.Add(officialUrl); }
                if (!string.IsNullOrEmpty(chinaUrl)) { result.Add(chinaUrl); }
            }

            return result;
        }

        // ------------------------------------------------------------ Node.js

        public static string NodeOfficial(string version, string fileName)
        {
            return "https://nodejs.org/dist/" + version + "/" + fileName;
        }

        /// <summary>
        /// Node 在所选线路内的候选下载源。
        /// 实测(2026-09-19,国内网络):npmmirror 262ms / ustc 324ms / nju 353ms / aliyun 475ms / 官方 699ms,
        /// 清华源不可用所以没放。
        /// </summary>
        public static List<string> NodeUrls(string version, string fileName, string preference)
        {
            string official = NodeOfficial(version, fileName);
            if (BackendDownloadSource.IsSelected(preference)) return BackendUrls(official, preference);
            string npmmirror = "https://npmmirror.com/mirrors/node/" + version + "/" + fileName;
            string ustc = "https://mirrors.ustc.edu.cn/node/" + version + "/" + fileName;
            string nju = "https://mirror.nju.edu.cn/nodejs-release/" + version + "/" + fileName;
            string aliyun = "https://mirrors.aliyun.com/nodejs-release/" + version + "/" + fileName;

            List<string> result = new List<string>();
            if (string.Equals(preference, China, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(npmmirror);
                result.Add(ustc);
                result.Add(nju);
                result.Add(aliyun);
            }
            else
            {
                result.Add(official);
            }

            return result;
        }

        /// <summary>查 Node 最新 LTS 时用的候选清单地址。</summary>
        public static List<string> NodeIndexUrls(string preference)
        {
            if (BackendDownloadSource.IsSelected(preference)) return BackendUrls("https://nodejs.org/dist/index.json", preference);
            List<string> result = new List<string>();
            if (string.Equals(preference, China, StringComparison.OrdinalIgnoreCase))
            {
                result.Add("https://registry.npmmirror.com/-/binary/node/index.json");
                result.Add("https://mirrors.ustc.edu.cn/node/index.json");
                result.Add("https://mirror.nju.edu.cn/nodejs-release/index.json");
            }
            else
            {
                result.Add("https://nodejs.org/dist/index.json");
            }

            return result;
        }
        public static string NodeMirror(string version, string fileName)
        {
            return "https://npmmirror.com/mirrors/node/" + version + "/" + fileName;
        }

        /// <summary>查最新的 Node 22 LTS 版本号。</summary>
        public static string ResolveLatestNodeVersion(string preference, int major,
            Action<DownloadProgress> progress = null, Func<bool> cancellation = null)
        {
            List<string> urls = NodeIndexUrls(preference);

            string json = DownloadEngine.DownloadText(urls, progress: progress, cancellation: cancellation);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            // 找第一个匹配 major 且带 lts 的版本
            MatchCollection matches = Regex.Matches(
                json,
                "\\{\\s*\"version\"\\s*:\\s*\"v" + major + "\\.(\\d+)\\.(\\d+)\"[^}]*?\"lts\"\\s*:\\s*\"([^\"]+)\"");
            if (matches.Count > 0)
            {
                return "v" + major + "." + matches[0].Groups[1].Value + "." + matches[0].Groups[2].Value;
            }

            // 退一步:同大版本里最新的
            matches = Regex.Matches(json, "\"version\"\\s*:\\s*\"v" + major + "\\.(\\d+)\\.(\\d+)\"");
            if (matches.Count > 0)
            {
                return "v" + major + "." + matches[0].Groups[1].Value + "." + matches[0].Groups[2].Value;
            }

            return null;
        }

        // ------------------------------------------------------------ Git / Python / pnpm

        /// <summary>
        /// 是不是"加速线路"。
        ///
        /// 用户定的规矩:选加速线路就**全部走国内镜像**(jsDelivr 的国内镜像 / 华为云),
        /// 不跨到官方源;选官方线路就**全部走官方源**,一个镜像都不塞 ——
        /// 那种情况通常是人在墙外,直连比什么都快。
        /// </summary>
        public static bool IsChina(string preference)
        {
            return string.Equals(preference, China, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>MinGit 便携版(GitHub 会重定向到 objects 存储)。</summary>
        public static string MinGitOfficial(string version)
        {
            return "https://github.com/git-for-windows/git/releases/download/v" + version + ".windows.1/MinGit-" + version + "-64-bit.zip";
        }

        public static string PythonOfficial(string version)
        {
            return "https://www.python.org/ftp/python/" + version + "/python-" + version + "-embed-amd64.zip";
        }

        public static string PnpmOfficial(string version)
        {
            return "https://github.com/pnpm/pnpm/releases/download/v" + version + "/pnpm-win-x64.exe";
        }

        /// <summary>
        /// MinGit 在所选线路内的候选地址。
        ///
        /// 虚拟机实测(2026-09-19):同一个 46 MB 包 ——
        ///   华为云 10,527 KB/s | npmmirror 8,426 KB/s | github.com **17 KB/s**
        /// 差了六百倍,所以国内线路必须先试镜像。
        /// </summary>
        public static List<string> MinGitUrls(string preference, string version)
        {
            if (BackendDownloadSource.IsSelected(preference)) return BackendUrls(MinGitOfficial(version), preference);
            string file = "MinGit-" + version + "-64-bit.zip";
            string folder = "v" + version + ".windows.1";
            List<string> result = new List<string>();

            if (IsChina(preference))
            {
                result.Add("https://mirrors.huaweicloud.com/git-for-windows/" + folder + "/" + file);
                result.Add("https://registry.npmmirror.com/-/binary/git-for-windows/" + folder + "/" + file);
            }
            else
            {
                result.Add(MinGitOfficial(version));
            }
            return result;
        }

        /// <summary>Python embed 的候选地址(实测 华为云 14,961 KB/s / npmmirror 7,600 KB/s / python.org 45 KB/s)。</summary>
        public static List<string> PythonUrls(string preference, string version)
        {
            if (BackendDownloadSource.IsSelected(preference)) return BackendUrls(PythonOfficial(version), preference);
            string file = "python-" + version + "-embed-amd64.zip";
            List<string> result = new List<string>();

            if (IsChina(preference))
            {
                result.Add("https://mirrors.huaweicloud.com/python/" + version + "/" + file);
                result.Add("https://registry.npmmirror.com/-/binary/python/" + version + "/" + file);
            }
            else
            {
                result.Add(PythonOfficial(version));
            }
            return result;
        }

        /// <summary>
        /// pnpm 在所选线路内的候选地址。
        ///
        /// pnpm 是唯一**没有现成镜像**的:华为云的 /pnpm/ 是个前端页面(不是目录),
        /// npmmirror 的二进制目录里也没有 pnpm。但它的平台二进制本身发布在 npm 上 ——
        /// `@pnpm/win-x64` 这个包的 tgz 里就是 `package/pnpm.exe`,npmmirror 有镜像,
        /// 实测 9.8 MB/s。所以国内线路下我们下一个 tgz 再解出 exe(见 RunPnpmAsync)。
        ///
        /// 返回值第一项如果是 .tgz,调用方要按 tgz 处理。
        /// </summary>
        public static List<string> PnpmUrls(string preference, string version)
        {
            if (BackendDownloadSource.IsSelected(preference)) return BackendUrls(PnpmOfficial(version), preference);
            List<string> result = new List<string>();

            if (IsChina(preference))
            {
                result.Add(PnpmTarballUrl(version));
            }
            else
            {
                result.Add(PnpmOfficial(version));
            }
            return result;
        }

        /// <summary>npmmirror 上 @pnpm/win-x64 的 tarball(tgz 里含 package/pnpm.exe)。</summary>
        public static string PnpmTarballUrl(string version)
        {
            return "https://registry.npmmirror.com/@pnpm/win-x64/-/win-x64-" + version + ".tgz";
        }

        // ------------------------------------------------------------ npm 源

        public static string NpmRegistry(string preference)
        {
            if (BackendDownloadSource.IsSelected(preference)) return BackendDownloadSource.BaseUrl + "/api/npm/";
            return string.Equals(preference, China, StringComparison.OrdinalIgnoreCase)
                ? "https://registry.npmmirror.com"
                : "https://registry.npmjs.org";
        }

        /// <summary>
        /// GitHub 资源共用的镜像池。启动器更新、GitHub 插件等所有 GitHub 文件
        /// 都从这里取候选，避免每个模块各写一套代理顺序。
        /// 大陆线路只返回镜像；官方线路只返回原始地址。
        /// </summary>
        public static List<string> GitHubProxyUrls(
            string githubUrl,
            string preference)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(githubUrl))
            {
                return result;
            }
            if (BackendDownloadSource.IsSelected(preference)) return BackendUrls(githubUrl, preference);

            if (!IsChina(preference))
            {
                result.Add(githubUrl);
                return result;
            }

            // 虚拟机实测(2026-09-27,三轮):
            //   gh-proxy.com   通,而且**唯一回 206** —— 只有它能多线程分段下载
            //   ghproxy.net    通,但只回 200,不接受 Range
            //   ghfast.top     **每次都超时**,直接删掉,别再让用户等它
            // 所以顺序固定为 gh-proxy.com → ghproxy.net。
            string[] proxies = new string[]
            {
                "https://gh-proxy.com/",
                "https://ghproxy.net/"
            };
            for (int index = 0; index < proxies.Length; index++)
            {
                result.Add(proxies[index] + githubUrl);
            }

            return result;
        }

        // ------------------------------------------------------------ 源的"人话"名字

        /// <summary>
        /// 这条地址是不是"国内镜像"(相对官方源而言)。给进度显示挑词用。
        /// </summary>
        public static bool IsMirrorUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string[] marks = new string[]
            {
                "gh-proxy.com",
                "ghproxy.net",
                "jsdmirror.com",
                "jsdelivr.net",
                "npmmirror.com",
                "huaweicloud.com",
                "mirrors.ustc.edu.cn",
                "mirror.nju.edu.cn",
                "mirrors.aliyun.com",
            };

            for (int i = 0; i < marks.Length; i++)
            {
                if (url.IndexOf(marks[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>取地址的主机名;取不到就把整条地址原样返回(短一些)。</summary>
        public static string HostOf(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            try
            {
                Uri parsed;
                if (Uri.TryCreate(url, UriKind.Absolute, out parsed))
                {
                    return parsed.Host;
                }
            }
            catch
            {
            }

            return url.Length > 40 ? url.Substring(0, 40) + "…" : url;
        }

        /// <summary>
        /// 给用户看的"当前在用哪条源"。域名对用户不友好,所以配一个序号 + 中文说明,
        /// 形如「镜像 1（gh-proxy.com）」。<paramref name="ordinal"/> 从 0 起。
        /// </summary>
        public static string DescribeSource(string url, int ordinal)
        {
            if (BackendDownloadSource.IsBackendUrl(url)) return SharedText.T("大肥鱼国内加速", "Dafeiyu mainland acceleration");
            string host = HostOf(url);
            if (IsMirrorUrl(url))
            {
                return SharedText.T(
                    "镜像 " + (ordinal + 1) + "（" + host + "）",
                    "Mirror " + (ordinal + 1) + " (" + host + ")");
            }

            return SharedText.T("官方源（" + host + "）", "Official (" + host + ")");
        }

        /// <summary>在候选清单里找这条地址的序号(找不到返回 0)。</summary>
        public static int IndexOf(IList<string> urls, string url)
        {
            if (urls == null)
            {
                return 0;
            }

            for (int i = 0; i < urls.Count; i++)
            {
                if (string.Equals(urls[i], url, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>候选清单里这条地址的"人话"名字。</summary>
        public static string DescribeSource(IList<string> urls, string url)
        {
            return DescribeSource(url, IndexOf(urls, url));
        }

        // ------------------------------------------------------------ 系统组件(运行库)

        /// <summary>
        /// .NET 桌面运行时的候选下载地址,按可信度排序。
        ///
        /// 为什么不只在界面上给个官网链接:运行库是**必装项**,要的是"点一下自己就装好",
        /// 让用户自己跑去网页上下载再双击,那不叫安装程序。
        ///
        /// 顺序:release-metadata 解析出来的**具体版本直链**(最准)→ aka.ms 稳定通道
        /// (永远指向该大版本最新补丁)→ 固定版本的构建服务器直链(兜底)。
        /// </summary>
        public static List<string> DotNetDesktopRuntimeUrls(string preference, int major)
        {
            List<string> result = new List<string>();

            string resolved = ResolveLatestDotNetDesktopUrl(major);
            if (!string.IsNullOrEmpty(resolved))
            {
                result.Add(resolved);
            }

            // aka.ms 稳定通道:官方文档里给"总是最新补丁"用的短链
            result.Add("https://aka.ms/dotnet/" + major + ".0/windowsdesktop-runtime-win-x64.exe");

            // 兜底:写死一个已知存在的补丁版本(前面两条都失败时才轮到它)
            result.Add("https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.11/windowsdesktop-runtime-8.0.11-win-x64.exe");
            result.Add("https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.0/windowsdesktop-runtime-8.0.0-win-x64.exe");

            return Dedupe(result);
        }

        /// <summary>
        /// 从 .NET 官方 release-metadata 里抠出"最新 8.0.x 桌面运行时 win-x64"的直链。
        ///
        /// 为什么值得多走这一趟:固定版本号会过期,写死的地址早晚 404;
        /// 而这里拿到的永远是这个大版本下最新的补丁版(顺带把安全更新带上)。
        /// </summary>
        public static string ResolveLatestDotNetDesktopUrl(int major)
        {
            string[] indexUrls = new string[]
            {
                "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/" + major + ".0/releases.json",
                "https://builds.dotnet.microsoft.com/dotnet/release-metadata/" + major + ".0/releases.json",
            };

            for (int i = 0; i < indexUrls.Length; i++)
            {
                string json = DownloadEngine.DownloadText(new List<string> { indexUrls[i] });
                string url = ParseDotNetDesktopUrl(json);
                if (!string.IsNullOrEmpty(url))
                {
                    return url;
                }
            }

            return null;
        }

        /// <summary>
        /// 解析 release-metadata:取第一个(最新的)release 里 windowsdesktop 段的 win-x64 文件地址。
        /// 用真正的 JSON 解析而不是正则 —— 这份文档几十万字符,拿正则去啃很容易挑错行。
        /// </summary>
        public static string ParseDotNetDesktopUrl(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (System.Text.Json.JsonDocument document =
                    System.Text.Json.JsonDocument.Parse(json))
                {
                    System.Text.Json.JsonElement releases;
                    if (!document.RootElement.TryGetProperty("releases", out releases)
                        || releases.ValueKind != System.Text.Json.JsonValueKind.Array)
                    {
                        return null;
                    }

                    // releases 是按新到旧排的,第一个就是最新补丁
                    for (int r = 0; r < releases.GetArrayLength(); r++)
                    {
                        System.Text.Json.JsonElement release = releases[r];
                        System.Text.Json.JsonElement desktop;
                        if (!release.TryGetProperty("windowsdesktop", out desktop))
                        {
                            continue;
                        }

                        System.Text.Json.JsonElement files;
                        if (!desktop.TryGetProperty("files", out files)
                            || files.ValueKind != System.Text.Json.JsonValueKind.Array)
                        {
                            continue;
                        }

                        for (int f = 0; f < files.GetArrayLength(); f++)
                        {
                            System.Text.Json.JsonElement file = files[f];

                            System.Text.Json.JsonElement rid;
                            if (!file.TryGetProperty("rid", out rid)
                                || !string.Equals(rid.GetString(), "win-x64", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            System.Text.Json.JsonElement url;
                            if (file.TryGetProperty("url", out url))
                            {
                                string value = url.GetString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    return value;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // 格式变了或者断流截断了,交给上层走兜底地址
            }

            return null;
        }

        /// <summary>
        /// Windows App Runtime 的候选下载地址。
        /// aka.ms 的 windowsappsdk 短链是官方给的分发方式:latest 永远指向 1.8 线最新,
        /// 版本号那条是精确锁定(万一 latest 被人推坏还能退回来)。
        /// </summary>
        public static List<string> WindowsAppRuntimeUrls(string sdkVersion)
        {
            List<string> result = new List<string>();
            result.Add("https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe");
            if (!string.IsNullOrEmpty(sdkVersion))
            {
                result.Add("https://aka.ms/windowsappsdk/1.8/" + sdkVersion + "/windowsappruntimeinstall-x64.exe");
            }

            return Dedupe(result);
        }

        /// <summary>旧调用点保留:单个 Windows App Runtime 地址。</summary>
        public static string WindowsAppRuntimeUrl(string sdkVersion)
        {
            return "https://aka.ms/windowsappsdk/1.8/" + sdkVersion + "/windowsappruntimeinstall-x64.exe";
        }

        private static List<string> Dedupe(List<string> values)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                bool seen = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if (string.Equals(result[j], value, StringComparison.OrdinalIgnoreCase))
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                {
                    result.Add(value);
                }
            }

            return result;
        }
    }
}
