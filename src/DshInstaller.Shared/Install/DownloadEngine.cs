using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DshInstaller.Shared.Install
{
    /// <summary>下载进度。</summary>
    public sealed class DownloadProgress
    {
        public long ReceivedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double BytesPerSecond { get; set; }

        public double Fraction
        {
            get
            {
                if (TotalBytes <= 0)
                {
                    return -1;
                }

                double value = (double)ReceivedBytes / TotalBytes;
                return value > 1 ? 1 : value;
            }
        }

        public TimeSpan? Remaining
        {
            get
            {
                if (TotalBytes <= 0 || BytesPerSecond <= 0 || ReceivedBytes >= TotalBytes)
                {
                    return null;
                }

                double seconds = (TotalBytes - ReceivedBytes) / BytesPerSecond;
                return TimeSpan.FromSeconds(seconds);
            }
        }

        public string SpeedText
        {
            get { return FormatBytes((long)BytesPerSecond) + "/s"; }
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = new string[] { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + units[unit];
        }
    }

    /// <summary>
    /// 下载引擎:多源轮换重试、断点续传、停滞检测、速度统计。
    ///
    /// 国内网络下"某个源某次不通"是常态,所以策略是**多准备源 + 失败就换源重试**,
    /// 而不是指望某一个源稳定。已经下到一半的数据靠 `.part` 保留,换源也能接着下。
    /// </summary>
    public sealed class DownloadEngine
    {
        /// <summary>每个下载任务最多尝试几次(每次换一个源)。</summary>
        public const int MaxAttempts = 5;

        /// <summary>连接 + 收到响应头的超时。死源要尽快放弃,不能让用户干等。</summary>
        private const int ResponseTimeoutMs = 8000;

        /// <summary>连续多久没有新数据就判定"停滞",断开重连(带续传)。</summary>
        private const int StallTimeoutMs = 10000;

        /// <summary>换源之间歇一下;太快换源遇到限流反而更糟。</summary>
        private const int RetryDelayMs = 1200;

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseProxy = true,
            };

            HttpClient client = new HttpClient(handler);

            // 这里给的是"整个请求"的上限,真正管"卡住不动"的是下面的停滞检测。
            client.Timeout = TimeSpan.FromMinutes(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DSH-Installer/" + WellKnown.InstallerVersion);
            return client;
        }

        /// <summary>
        /// 从多个候选地址里挑一个能用的下下来。
        ///
        /// 重试策略:
        ///   · 总共尝试 <see cref="MaxAttempts"/> 次,**每次都换一个源**(源不够就轮着来);
        ///   · 单次超过 8 秒没拿到响应头就算这个源不行;
        ///   · 已经开始下载但连续 10 秒没有新数据,判定停滞,断开后**带 Range 续传**重连;
        ///   · 续传靠 <c>目标.part</c>,它跨尝试保留,所以换源也能接着下。
        /// </summary>
        /// <param name="urls">按优先级排好的地址(镜像在前,官方兜底)。</param>
        /// <param name="targetPath">落盘路径。</param>
        /// <param name="progress">进度回调,可能在后台线程触发。</param>
        /// <param name="cancellation">取消信号。</param>
        /// <param name="onSourceFailed">某个源失败时回调(用于界面提示)。</param>
        /// <param name="onNotice">给用户看的提示(第几次尝试、换到哪个源)。</param>
        public static string Download(
            IList<string> urls,
            string targetPath,
            Action<DownloadProgress> progress,
            Func<bool> cancellation,
            Func<string, bool> onSourceFailed = null,
            Action<string> onNotice = null)
        {
            if (urls == null || urls.Count == 0)
            {
                throw new ArgumentException("没有下载地址", "urls");
            }

            // 开下之前先花几秒实测一遍,把最快的源排到前面。
            // 引擎原来的规矩是"谁排前面用谁,只有失败才换",于是抽到慢源就一直慢到底 ——
            // 那几个 GitHub 加速前缀在晚高峰能差一个数量级(实测 10 KB/s vs 2 MB/s)。
            // 排序后如果全都测不出来,顺序保持原样,行为跟以前一致。
            // **不测速。**
            //
            // 分流之后候选本身就是人工排好的「国内镜像 → 官方兜底」,再花十几秒测一遍
            // 只是让用户干等(测速阶段界面是不动的)。真遇到慢的源,下载过程中会自动换
            // —— 见下面"连续 5 秒低于 60 KB/s"那条,那才是真正管用的判据。
            List<string> ordered = new List<string>(urls);

            // 大文件先试 8 连接分段 —— 那几家加速前缀普遍按**连接**限速,
            // 单连接 173 KB/s 换成 8 条常见能到三倍以上。
            //
            // 它是旁路:只在"支持 Range 且够大"时接活,任何一步不顺(探测、分段、合并、校验)
            // 都返回 false,这里就原样落到下面那套单连接逻辑。
            // 所以它的失败模式是"退化成以前那样",而不是"装不上"。
            if (SegmentedDownloader.TryDownload(ordered, targetPath, progress, cancellation, onNotice))
            {
                return targetPath;
            }

            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string partialPath = targetPath + ".part";
            List<string> failures = new List<string>();

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (cancellation != null && cancellation())
                {
                    throw new OperationCanceledException();
                }

                // 每次换一个源:源不够就取模轮着来
                string url = ordered[(attempt - 1) % ordered.Count];

                if (attempt > 1)
                {
                    Notice(onNotice, SharedText.T(
                        "第 " + attempt + " 次尝试,切换下载源重试…",
                        "Attempt " + attempt + ", trying another source…"));

                    try
                    {
                        Thread.Sleep(RetryDelayMs);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    DownloadOne(url, partialPath, progress, cancellation);

                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }

                    File.Move(partialPath, targetPath);
                    InstallLogger.Write("下载完成(第 " + attempt + " 次尝试):" + url + " -> " + targetPath);
                    return url;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add("[" + attempt + "] " + url + " : " + exception.Message);
                    InstallLogger.Write(
                        "下载失败(第 " + attempt + "/" + MaxAttempts + " 次):" + url + " : " + exception.Message);

                    Notice(onNotice, SharedText.T(
                        "本次下载失败,准备切换下载源(" + attempt + "/" + MaxAttempts + ")",
                        "This source failed; switching (" + attempt + "/" + MaxAttempts + ")"));

                    if (onSourceFailed != null)
                    {
                        try
                        {
                            onSourceFailed(url);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            throw new InvalidOperationException(
                SharedText.T(
                    "已尝试 " + MaxAttempts + " 个下载源,均未成功:",
                    "Downloading failed after trying " + MaxAttempts + " sources:")
                + "\r\n" + string.Join("\r\n", failures.ToArray()));
        }

        private static void Notice(Action<string> onNotice, string message)
        {
            if (onNotice == null)
            {
                return;
            }

            try
            {
                onNotice(message);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 低于这个速度、并持续 <see cref="SlowSeconds"/> 秒,就认定"这个源不行了",换下一个。
        ///
        /// 为什么用**下载中的实测速度**而不是开下前测速:测速测的是"这几秒里它多快",
        /// 公共代理前几秒快后几秒掉到几十 KB 是常态;真正要判的是"它现在还在不在好好干活"。
        /// 换源靠 .part 续传,已经下到的部分不白费。
        /// </summary>
        private const long SlowBytesPerSecond = 60 * 1024;

        /// <summary>慢多久算该换源了。</summary>
        private const double SlowSeconds = 5;

        private static void DownloadOne(
            string url,
            string partialPath,
            Action<DownloadProgress> progress,
            Func<bool> cancellation)
        {
            long existing = 0;
            if (File.Exists(partialPath))
            {
                existing = new FileInfo(partialPath).Length;
            }

            // 这个 CTS 管两件事:响应头超时、以及停滞时把底层连接掐掉。
            // 收到响应头之后要把超时撤掉,否则它会连带把正在读的正文一起掐断。
            using (CancellationTokenSource timeout = new CancellationTokenSource())
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (existing > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
                }

                timeout.CancelAfter(ResponseTimeoutMs);

                HttpResponseMessage response;
                try
                {
                    response = Client.Send(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                }
                catch (Exception exception) when (exception is OperationCanceledException || exception is TaskCanceledException)
                {
                    throw new TimeoutException(SharedText.T(
                        "连接超时(服务器在 " + (ResponseTimeoutMs / 1000) + " 秒内未响应)",
                        "Connection timed out (no response within " + (ResponseTimeoutMs / 1000) + "s)"));
                }

                using (response)
                {
                    // 响应头到了,撤掉超时;后面靠"停滞检测"管正文
                    try
                    {
                        timeout.CancelAfter(System.Threading.Timeout.Infinite);
                    }
                    catch
                    {
                    }

                    bool resumed = response.StatusCode == HttpStatusCode.PartialContent;
                    if (!resumed)
                    {
                        existing = 0;
                    }

                    if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new InvalidOperationException(
                            "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase);
                    }

                    long total = existing;
                    if (response.Content.Headers.ContentLength.HasValue)
                    {
                        total = resumed
                            ? existing + response.Content.Headers.ContentLength.Value
                            : response.Content.Headers.ContentLength.Value;
                    }

                    DownloadProgress state = new DownloadProgress
                    {
                        ReceivedBytes = existing,
                        TotalBytes = total,
                    };

                    Stopwatch clock = Stopwatch.StartNew();
                    long windowBytes = 0;
                    double lastReportSeconds = 0;
                    double slowSinceSeconds = -1;

                    using (Stream remote = response.Content.ReadAsStream())
                    using (FileStream local = new FileStream(
                        partialPath,
                        resumed ? FileMode.Append : FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 128,
                        FileOptions.SequentialScan))
                    {
                        byte[] buffer = new byte[1024 * 128];
                        while (true)
                        {
                            if (cancellation != null && cancellation())
                            {
                                throw new OperationCanceledException();
                            }

                            int read = ReadWithStallDetection(remote, buffer, timeout);
                            if (read <= 0)
                            {
                                break;
                            }

                            local.Write(buffer, 0, read);
                            state.ReceivedBytes += read;
                            windowBytes += read;

                            double elapsed = clock.Elapsed.TotalSeconds;
                            if (elapsed - lastReportSeconds >= 0.25)
                            {
                                double windowSeconds = elapsed - lastReportSeconds;
                                state.BytesPerSecond = windowBytes / (windowSeconds <= 0 ? 0.25 : windowSeconds);
                                windowBytes = 0;
                                lastReportSeconds = elapsed;

                                // 太慢就换源:连续 5 秒都不到 60 KB/s,再耗着就是白等。
                                // 抛出异常会被上面的尝试循环接住 —— 换下一个源、带着 Range 续传接着下。
                                if (state.BytesPerSecond < SlowBytesPerSecond)
                                {
                                    if (slowSinceSeconds < 0)
                                    {
                                        slowSinceSeconds = elapsed;
                                    }
                                    else if (elapsed - slowSinceSeconds >= SlowSeconds)
                                    {
                                        throw new TimeoutException(SharedText.T(
                                            "这个源太慢(" + (int)(state.BytesPerSecond / 1024) + " KB/s),换一个",
                                            "Source too slow (" + (int)(state.BytesPerSecond / 1024) + " KB/s); switching"));
                                    }
                                }
                                else
                                {
                                    // 缓过来了就当没慢过 —— 网络抖动一下不该被换掉
                                    slowSinceSeconds = -1;
                                }

                                if (progress != null)
                                {
                                    progress(state);
                                }
                            }
                        }

                        local.Flush(true);
                    }

                    if (progress != null)
                    {
                        double totalSeconds = clock.Elapsed.TotalSeconds;
                        if (totalSeconds > 0 && state.ReceivedBytes >= existing)
                        {
                            state.BytesPerSecond = (state.ReceivedBytes - existing) / totalSeconds;
                        }

                        progress(state);
                    }
                }
            }
        }

        /// <summary>
        /// 读一次,超过 <see cref="StallTimeoutMs"/> 没有数据就判定停滞。
        ///
        /// 为什么需要它:HttpClient 的超时是"整个请求"级别的(这里给到 30 分钟),
        /// 服务器收下连接却不再发数据时 <c>Read</c> 会一直阻塞 ——
        /// 实测在虚拟机上就这么卡住过:界面停在某个百分比不动,既不报错也不结束。
        /// </summary>
        private static int ReadWithStallDetection(
            Stream remote,
            byte[] buffer,
            CancellationTokenSource cancelOnStall)
        {
            Task<int> readTask = remote.ReadAsync(buffer, 0, buffer.Length);

            if (!readTask.Wait(StallTimeoutMs))
            {
                // 把底层连接掐掉,让那个还挂着的 ReadAsync 尽快结束
                try
                {
                    cancelOnStall.Cancel();
                }
                catch
                {
                }

                throw new TimeoutException(SharedText.T(
                    "下载停滞(连续 " + (StallTimeoutMs / 1000) + " 秒无数据)",
                    "Download stalled (no data for " + (StallTimeoutMs / 1000) + "s)"));
            }

            try
            {
                return readTask.Result;
            }
            catch (AggregateException aggregate)
            {
                Exception inner = aggregate.InnerException;
                if (inner != null)
                {
                    throw inner;
                }

                throw;
            }
        }

        /// <summary>
        /// 下载文本(拿版本清单、校验和之类)。
        /// 同样换源重试 —— 清单地址在国内比资产更容易被打掉。
        /// </summary>
        public static string DownloadText(IList<string> urls, int timeoutMs = 20000)
        {
            if (urls == null || urls.Count == 0)
            {
                return null;
            }

            List<string> failures = new List<string>();

            for (int attempt = 1; attempt <= Math.Min(MaxAttempts, urls.Count * 2); attempt++)
            {
                string url = urls[(attempt - 1) % urls.Count];
                try
                {
                    using (CancellationTokenSource timeout = new CancellationTokenSource(timeoutMs))
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (HttpResponseMessage response = Client.Send(
                        request, HttpCompletionOption.ResponseContentRead, timeout.Token))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                return text;
                            }
                        }
                        else
                        {
                            failures.Add("[" + attempt + "] " + url + " : HTTP " + (int)response.StatusCode);
                            continue;
                        }
                    }
                }
                catch (Exception exception)
                {
                    failures.Add("[" + attempt + "] " + url + " : " + exception.Message);
                }
            }

            InstallLogger.Write("取文本失败(试了 " + failures.Count + " 次):\r\n" + string.Join("\r\n", failures.ToArray()));
            return null;
        }

        /// <summary>校验文件 SHA256。</summary>
        public static string ComputeSha256(string path)
        {
            try
            {
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    StringBuilder builder = new StringBuilder(hash.Length * 2);
                    for (int index = 0; index < hash.Length; index++)
                    {
                        builder.Append(hash[index].ToString("x2"));
                    }

                    return builder.ToString();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
