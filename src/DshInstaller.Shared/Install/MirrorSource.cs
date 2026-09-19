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
        public const string Official = "official";
        public const string China = "china";

        /// <summary>按偏好给出候选地址:选中的排前面,另一条兜底。</summary>
        public static List<string> Order(string preference, string officialUrl, string chinaUrl)
        {
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
        /// Node 的所有可用下载源,按偏好排序。
        /// 实测(2026-09-19,国内网络):npmmirror 262ms / ustc 324ms / nju 353ms / aliyun 475ms / 官方 699ms,
        /// 清华源不可用所以没放。
        /// </summary>
        public static List<string> NodeUrls(string version, string fileName, string preference)
        {
            string official = NodeOfficial(version, fileName);
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
                result.Add(official);
            }
            else
            {
                result.Add(official);
                result.Add(npmmirror);
                result.Add(ustc);
                result.Add(nju);
                result.Add(aliyun);
            }

            return result;
        }

        /// <summary>查 Node 最新 LTS 时用的候选清单地址。</summary>
        public static List<string> NodeIndexUrls(string preference)
        {
            List<string> result = new List<string>();
            result.Add("https://registry.npmmirror.com/-/binary/node/index.json");
            result.Add("https://nodejs.org/dist/index.json");
            result.Add("https://mirrors.ustc.edu.cn/node/index.json");
            result.Add("https://mirror.nju.edu.cn/nodejs-release/index.json");

            if (!string.Equals(preference, China, StringComparison.OrdinalIgnoreCase))
            {
                // 官方优先时把它挪到最前
                result.Remove("https://nodejs.org/dist/index.json");
                result.Insert(0, "https://nodejs.org/dist/index.json");
            }

            return result;
        }
        public static string NodeMirror(string version, string fileName)
        {
            return "https://npmmirror.com/mirrors/node/" + version + "/" + fileName;
        }

        /// <summary>查最新的 Node 22 LTS 版本号。</summary>
        public static string ResolveLatestNodeVersion(string preference, int major)
        {
            List<string> urls = NodeIndexUrls(preference);

            string json = DownloadEngine.DownloadText(urls);
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
        /// 用户定的规矩:选加速线路就**优先国内镜像**(jsDelivr 的国内镜像 / 华为云),
        /// GitHub 只当兜底;选官方线路就**全部走官方源**,一个镜像都不塞 ——
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
        /// MinGit 的候选地址,按线路给。
        ///
        /// 虚拟机实测(2026-09-19):同一个 46 MB 包 ——
        ///   华为云 10,527 KB/s | npmmirror 8,426 KB/s | github.com **17 KB/s**
        /// 差了六百倍,所以国内线路必须先试镜像。
        /// </summary>
        public static List<string> MinGitUrls(string preference, string version)
        {
            string file = "MinGit-" + version + "-64-bit.zip";
            string folder = "v" + version + ".windows.1";
            List<string> result = new List<string>();

            if (IsChina(preference))
            {
                result.Add("https://mirrors.huaweicloud.com/git-for-windows/" + folder + "/" + file);
                result.Add("https://registry.npmmirror.com/-/binary/git-for-windows/" + folder + "/" + file);
            }

            // 官方兜底。加速线路上再过一遍 ghproxy 那层前缀 —— 国内直连 github 常常被拒。
            result.AddRange(LauncherFeed.MirrorizeAsset(MinGitOfficial(version), preference));
            return result;
        }

        /// <summary>Python embed 的候选地址(实测 华为云 14,961 KB/s / npmmirror 7,600 KB/s / python.org 45 KB/s)。</summary>
        public static List<string> PythonUrls(string preference, string version)
        {
            string file = "python-" + version + "-embed-amd64.zip";
            List<string> result = new List<string>();

            if (IsChina(preference))
            {
                result.Add("https://mirrors.huaweicloud.com/python/" + version + "/" + file);
                result.Add("https://registry.npmmirror.com/-/binary/python/" + version + "/" + file);
            }

            result.Add(PythonOfficial(version));
            return result;
        }

        /// <summary>
        /// pnpm 的候选地址。
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
            List<string> result = new List<string>();

            if (IsChina(preference))
            {
                result.Add(PnpmTarballUrl(version));
            }

            result.AddRange(LauncherFeed.MirrorizeAsset(PnpmOfficial(version), preference));
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
            return string.Equals(preference, China, StringComparison.OrdinalIgnoreCase)
                ? "https://registry.npmmirror.com"
                : "https://registry.npmjs.org";
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
