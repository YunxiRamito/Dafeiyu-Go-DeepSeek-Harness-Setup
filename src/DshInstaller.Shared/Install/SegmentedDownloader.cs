using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 多连接分段下载。
    ///
    /// 为什么需要它:国内这些 GitHub 加速前缀**普遍按连接限速** ——
    /// 实测同一个源,单连接 173 KB/s,而这个数字跟"源本身有多快"没关系,
    /// 是它给每条连接划的配额。开 8 条各拉八分之一,常见能到 3~4 倍。
    ///
    /// 设计上刻意做成"可有可无的旁路":
    ///   · 只处理**支持 Range 且足够大**的文件,其余一律返回 false;
    ///   · 任何一步失败(探测、分段、合并、校验)都返回 false,
    ///     调用方原样落回原来那套单连接逻辑 —— 不会因为这条路有问题就下不了。
    /// 这样它的失败模式是"退化成以前那样",而不是"装不上"。
    /// </summary>
    internal static class SegmentedDownloader
    {
        /// <summary>开几条连接。</summary>
        private const int SegmentCount = 8;

        /// <summary>小于这个大小就别折腾了,分段的开销比省下的时间还多。</summary>
        private const long MinimumSize = 4L * 1024 * 1024;

        /// <summary>单段最多重试几次(每次换一个源)。</summary>
        private const int SegmentAttempts = 3;

        /// <summary>
        /// 探测"这个源支不支持 Range"的时限。
        ///
        /// 必须有 —— 探的是公共服务,慢起来能挂几分钟。以前这里裸调 HttpClient,
        /// 继承的是 30 分钟超时,而且探测期间**不发任何提示**,界面就停在上一句
        /// "测速完成。"上,看着跟卡死一样(实测:虚拟机就卡在这儿)。
        /// </summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(12);

        /// <summary>
        /// 往后找候选时用的探测时限。比 <see cref="ProbeTimeout"/> 短 ——
        /// 这是"多试几条"的场景,单条探太久会把省下来的时间又搭进去。
        /// </summary>
        private static readonly TimeSpan LookaheadProbeTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 字节数多久没变化就算停滞:交给单连接那条路,它自带换源和停滞检测。
        /// 口径和用户要求的一致 —— **20 秒没有新字节就换源**。
        /// </summary>
        private const double StallSeconds = 20;

        /// <summary>八条连接**加起来**都低于这个速度,就算这个源没带宽(用户定的阈值:60 KB/s)。</summary>
        private const long SlowThresholdBytesPerSecond = 60 * 1024;

        /// <summary>低于上面那个速度持续多久就换源。</summary>
        private const double SlowThresholdSeconds = 5;

        private static readonly object ClientGate = new object();
        private static HttpClient _client;
        private static int _clientGeneration = -1;

        /// <summary>
        /// 分段下载也有自己的一份 handler(它要的是"能 Range 的裸连接"),
        /// 但代理设置必须和主引擎一致 —— 否则会出现"主包走了代理、分片却没走"
        /// 这种一半通一半不通的怪现象。改动代理同样自动重建。
        /// </summary>
        private static HttpClient Client
        {
            get
            {
                lock (ClientGate)
                {
                    if (_client == null || _clientGeneration != ProxySupport.Generation)
                    {
                        _client = CreateClient();
                        _clientGeneration = ProxySupport.Generation;
                    }

                    return _client;
                }
            }
        }

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = true };
            ProxySupport.Apply(handler);
            BackendDownloadSource.Apply(handler);

            HttpClient client = new HttpClient(handler);

            // 单段一次可能要拉几百 KB 到几 MB,给宽一点;
            // 真正的"卡住"交给每段自己的读超时。
            client.Timeout = TimeSpan.FromMinutes(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DSH-Installer");
            return client;
        }

        public static bool TryDownload(
            IList<string> urls,
            string targetPath,
            Action<DownloadProgress> progress,
            Func<bool> cancellation,
            Action<string> notice)
        {
            return TryDownload(
                urls,
                targetPath,
                progress,
                cancellation,
                notice,
                MinimumSize,
                SegmentCount);
        }

        public static bool TryDownload(
            IList<string> urls,
            string targetPath,
            Action<DownloadProgress> progress,
            Func<bool> cancellation,
            Action<string> notice,
            long minimumSize,
            int segmentCount)
        {
            try
            {
                if (urls == null || urls.Count == 0 || string.IsNullOrEmpty(targetPath))
                {
                    return false;
                }

                if (cancellation != null && cancellation())
                {
                    return false;
                }

                // 1) 挑一个"够大、而且真给分段"的候选。
                //
                // 以前这里只看 urls[0] —— 第一条正好是**坏的**时候,整趟就傻乎乎退回单连接。
                // 实测(2026-09-27,虚拟机)就是这样:部署启动器那一步的候选里,
                // 第一条 npmmirror 返 422,后面站着能分段的 gh-proxy.com,可多线程压根没起来,
                // 全程单连接 59 KB/s,10 MB 的包慢得肉眼可见(用户直接看出来了)。
                //
                // 现在的规矩分三种情况,别搞混:
                //   · 第一条就支持 Range        -> 直接分段(和以前一样);
                //   · 第一条活着但**不接受 Range** -> 还是走单连接。
                //     国内 CDN(华为云 / npmmirror)单连接本来就有 10 MB/s,
                //     硬换成 8 条代理连接反而更慢 —— 别把快的东西换慢;
                //   · 第一条是坏的(404/422/超时) -> 才值得往后找:找到第一条支持 Range 的用。
                Notice(notice, SharedText.T("正在连接下载源…", "Connecting to the download source…"));

                if (segmentCount < 2)
                {
                    segmentCount = 2;
                }
                segmentCount = Math.Min(8, segmentCount);
                BackendDownloadSource.EnsureReady(urls[0], progress, cancellation);

                string chosen = null;
                long total = 0;

                long firstLength;
                RangeSupport firstSupport = ProbeRange(urls[0], ProbeTimeout, out firstLength);
                if (firstSupport == RangeSupport.Yes && firstLength >= minimumSize)
                {
                    chosen = urls[0];
                    total = firstLength;
                }
                else if (firstSupport == RangeSupport.No)
                {
                    if (firstLength > 0)
                    {
                        Notice(notice, SharedText.T("正在启动单线程下载", "Starting single-threaded download"));
                    }

                    InstallLogger.Write("不走分段:首选源不接受 Range(" + urls[0] + ")");
                    return false;
                }
                else
                {
                    InstallLogger.Write("首选源不可用,继续找支持 Range 的候选:" + urls[0]);

                    for (int index = 1; index < urls.Count; index++)
                    {
                        long length;
                        RangeSupport support = ProbeRange(urls[index], LookaheadProbeTimeout, out length);

                        if (support == RangeSupport.Yes && length >= minimumSize)
                        {
                            chosen = urls[index];
                            total = length;
                            InstallLogger.Write("分段下载改用它:" + urls[index]);
                            break;
                        }

                        if (support == RangeSupport.No)
                        {
                            // 这条活着、能下,只是不给分段 —— 单连接就用它,别再往后挑了
                            if (length > 0)
                            {
                                Notice(notice, SharedText.T("正在启动单线程下载", "Starting single-threaded download"));
                            }

                            InstallLogger.Write("不走分段:候选不接受 Range(" + urls[index] + ")");
                            return false;
                        }
                    }
                }

                if (chosen == null || total < minimumSize)
                {
                    Notice(notice, SharedText.T("正在启动单线程下载", "Starting single-threaded download"));
                    InstallLogger.Write("不走分段:没有可用的分段候选");
                    return false;
                }

                // 选中的排到最前,其余留着当兜底(某一段失败会轮着换源重试)
                List<string> sources = new List<string>();
                sources.Add(chosen);
                for (int index = 0; index < urls.Count; index++)
                {
                    if (!string.Equals(urls[index], chosen, StringComparison.OrdinalIgnoreCase))
                    {
                        sources.Add(urls[index]);
                    }
                }

                Notice(notice, SharedText.T("正在启动多线程下载", "Starting multi-threaded download"));

                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                long chunk = total / segmentCount;
                long[] received = new long[segmentCount];
                string[] parts = new string[segmentCount];

                for (int i = 0; i < segmentCount; i++)
                {
                    long start = i * chunk;
                    long end = (i == segmentCount - 1) ? total - 1 : (start + chunk - 1);

                    string part = targetPath + ".seg" + i.ToString();
                    parts[i] = part;

                    int index = i;
                    long from = start;
                    long to = end;

                    Task.Run(delegate
                    {
                        for (int attempt = 0; attempt < SegmentAttempts; attempt++)
                        {
                            string url = sources[attempt % sources.Count];
                            if (Fetch(url, from, to, part, received, index, cancellation))
                            {
                                return;
                            }

                            if (cancellation != null && cancellation())
                            {
                                return;
                            }

                            Thread.Sleep(600);
                        }
                    });
                }

                // 2) 边等边报进度
                Stopwatch clock = Stopwatch.StartNew();
                double lastSeconds = 0;
                long lastTotal = 0;

                // 分段这条路是绕开引擎直连候选的,所以源名字得自己算一份 ——
                // 不然多线程下载时界面上看不到"正在用的镜像"。
                string sourceLabel = MirrorSource.DescribeSource(sources, sources[0]);

                // 停滞看门狗用的两个数:上次变化时的字节数与时刻
                long lastStallBytes = -1;
                double lastStallSeconds = 0;
                double stalledSeconds = 0;

                // 慢速看门狗:同样的两个数,阈值换成速度(0 起步,算的是窗口速率)
                long lastSlowBytes = 0;
                double lastSlowSeconds = 0;
                double slowSeconds = 0;

                while (true)
                {
                    if (cancellation != null && cancellation())
                    {
                        return false;
                    }

                    long done = 0;
                    for (int i = 0; i < segmentCount; i++)
                    {
                        done += Interlocked.Read(ref received[i]);
                    }

                    if (done >= total)
                    {
                        break;
                    }

                    double elapsed = clock.Elapsed.TotalSeconds;
                    if (elapsed - lastSeconds >= 0.5)
                    {
                        double speed = (done - lastTotal) / (elapsed - lastSeconds);
                        lastSeconds = elapsed;
                        lastTotal = done;

                        if (progress != null)
                        {
                            DownloadProgress state = new DownloadProgress
                            {
                                ReceivedBytes = done,
                                TotalBytes = total,
                                BytesPerSecond = speed,
                                SourceLabel = sourceLabel,
                            };

                            try
                            {
                                progress(state);
                            }
                            catch
                            {
                            }
                        }
                    }

                    // 停滞看门狗:**字节数**连续 30 秒没变化就放弃。
                    //
                    // 为什么不能只看 elapsed + anyProgress:那样只要开头来过几 KB,
                    // 后面彻底卡死也会一直等到半小时上限 —— 界面停在某个百分比上,
                    // 用户只能干等(实测:几个公共加速前缀慢起来就是这样)。
                    // 放弃之后走单连接那条路,它自带 10 秒停滞检测和换源重试。
                    if (done > lastStallBytes)
                    {
                        lastStallBytes = done;
                        lastStallSeconds = elapsed;
                        stalledSeconds = 0;
                    }
                    else
                    {
                        stalledSeconds = elapsed - lastStallSeconds;
                    }

                    if (stalledSeconds >= StallSeconds)
                    {
                        InstallLogger.Write("分段下载停滞 " + (int)stalledSeconds + " 秒,放弃并回落单连接");
                        return false;
                    }

                    // 慢也当没戏:八条连接**加起来**都不到 60 KB/s,说明这个源本身没带宽,
                    // 再挂八条也一样 —— 早点回落单连接去换源(用户定的阈值:5 秒)。
                    //
                    // 这里必须算**速率**,不能像上面停滞那样判"字节数变没变":
                    // 八条连接慢慢爬的时候 done 每轮都在涨,只是慢得可笑。
                    double slowWindow = elapsed - lastSlowSeconds;
                    if (slowWindow >= 1.0)
                    {
                        double windowSpeed = (done - lastSlowBytes) / slowWindow;
                        lastSlowBytes = done;
                        lastSlowSeconds = elapsed;

                        slowSeconds = windowSpeed < SlowThresholdBytesPerSecond
                            ? slowSeconds + slowWindow
                            : 0;
                    }

                    if (slowSeconds >= SlowThresholdSeconds)
                    {
                        InstallLogger.Write(
                            "分段下载持续低于 " + (SlowThresholdBytesPerSecond / 1024)
                            + " KB/s 达 " + (int)slowSeconds + " 秒,换源");

                        return false;
                    }

                    if (elapsed > 30 * 60)
                    {
                        return false;
                    }

                    Thread.Sleep(200);
                }

                // 3) 合并
                using (FileStream output = new FileStream(targetPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, 256 * 1024))
                {
                    for (int i = 0; i < segmentCount; i++)
                    {
                        if (!File.Exists(parts[i]))
                        {
                            return false;
                        }

                        using (FileStream input = File.OpenRead(parts[i]))
                        {
                            input.CopyTo(output, 256 * 1024);
                        }
                    }
                }

                // 4) 校验大小
                long size = new FileInfo(targetPath).Length;
                if (size != total)
                {
                    return false;
                }

                for (int i = 0; i < segmentCount; i++)
                {
                    try
                    {
                        File.Delete(parts[i]);
                    }
                    catch
                    {
                    }
                }

                if (progress != null)
                {
                    DownloadProgress final = new DownloadProgress
                    {
                        ReceivedBytes = total,
                        TotalBytes = total,
                        SourceLabel = sourceLabel,
                    };

                    try
                    {
                        progress(final);
                    }
                    catch
                    {
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>探测一个候选:支不支持分段。</summary>
        private enum RangeSupport
        {
            /// <summary>支持:回 206 且长度可用。</summary>
            Yes,

            /// <summary>服务器活着但不接受 Range(回 200)—— 只能单连接。</summary>
            No,

            /// <summary>没探出来:连不上 / 404·422 / 超时 / 长度不对。</summary>
            Unknown,
        }

        /// <summary>
        /// 探一个候选:要一段 bytes=0-0,从 Content-Range 里读总长度。
        ///
        /// 三种结果要分开,不能混成一个 -1 —— 混在一起就没法区分
        /// "这条慢但能用"(该走单连接)和"这条是坏的"(该往后找别的候选),
        /// 而这两件事的处理方式完全相反。以前就是混着来的(实测吃过亏)。
        /// </summary>
        private static RangeSupport ProbeRange(string url, TimeSpan timeout, out long length)
        {
            length = 0;

            try
            {
                // 限时:探测失败就当"这条不行",原样回落单连接 ——
                // 比在这里挂半小时强得多
                using (CancellationTokenSource limited = new CancellationTokenSource(timeout))
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Range = new RangeHeaderValue(0, 0);

                    using (HttpResponseMessage response = Client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limited.Token)
                        .GetAwaiter().GetResult())
                    {
                        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                        {
                            // 200 = 它把 Range 忽略了:这条能用,但不能分段。
                            // 其余(404/422/5xx)= 这条此刻是坏的。
                            return response.IsSuccessStatusCode ? RangeSupport.No : RangeSupport.Unknown;
                        }

                        ContentRangeHeaderValue range = response.Content.Headers.ContentRange;
                        if (range == null || !range.Length.HasValue)
                        {
                            return RangeSupport.Unknown;
                        }

                        length = range.Length.Value;
                        return RangeSupport.Yes;
                    }
                }
            }
            catch
            {
                return RangeSupport.Unknown;
            }
        }

        /// <summary>拉一段。返回 true 表示这一整段都写进了 part 文件。</summary>
        private static bool Fetch(
            string url, long start, long end, string partPath,
            long[] received, int index,
            Func<bool> cancellation)
        {
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Range = new RangeHeaderValue(start, end);

                    using (HttpResponseMessage response = Client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                        .GetAwaiter().GetResult())
                    {
                        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                        {
                            return false;
                        }
                        if (BackendDownloadSource.IsBackendUrl(url))
                        {
                            var returnedRange = response.Content.Headers.ContentRange;
                            if (returnedRange?.From != start || returnedRange.To != end || response.Content.Headers.ContentLength != end - start + 1
                                || response.Content.Headers.ContentEncoding.Count != 0) return false;
                        }

                        using (Stream remote = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                        using (FileStream local = new FileStream(partPath, FileMode.Create, FileAccess.Write,
                            FileShare.None, 128 * 1024))
                        {
                            byte[] buffer = new byte[128 * 1024];
                            long written = 0;
                            long expected = end - start + 1;

                            while (written < expected)
                            {
                                if (cancellation != null && cancellation())
                                {
                                    return false;
                                }

                                int read = remote.Read(buffer, 0, (int)Math.Min(buffer.Length, expected - written));
                                if (read <= 0)
                                {
                                    break;
                                }

                                local.Write(buffer, 0, read);
                                written += read;
                                Interlocked.Add(ref received[index], read);
                            }

                            if (written < expected)
                            {
                                // 这一段没拉完 —— 把计数退回去,重试时重新拉
                                Interlocked.Add(ref received[index], -written);
                                return false;
                            }
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Format(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
            {
                return (bytes / 1024.0 / 1024 / 1024).ToString("0.0") + " GB";
            }

            return (bytes / 1024.0 / 1024).ToString("0.0") + " MB";
        }

        private static void Notice(Action<string> notice, string message)
        {
            if (notice == null)
            {
                return;
            }

            try
            {
                notice(message);
            }
            catch
            {
            }
        }
    }
}
