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

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = true };
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

                // 1) 先问一句"多大、支不支持 Range"。不满足就老老实实走单连接。
                long total = ProbeLength(urls[0]);
                if (total < MinimumSize)
                {
                    if (total > 0)
                    {
                        Notice(notice, SharedText.T("正在启动单线程下载", "Starting single-threaded download"));
                    }

                    return false;
                }

                Notice(notice, SharedText.T("正在启动多线程下载", "Starting multi-threaded download"));

                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                long chunk = total / SegmentCount;
                long[] received = new long[SegmentCount];
                string[] parts = new string[SegmentCount];

                for (int i = 0; i < SegmentCount; i++)
                {
                    long start = i * chunk;
                    long end = (i == SegmentCount - 1) ? total - 1 : (start + chunk - 1);

                    string part = targetPath + ".seg" + i.ToString();
                    parts[i] = part;

                    int index = i;
                    long from = start;
                    long to = end;

                    Task.Run(delegate
                    {
                        for (int attempt = 0; attempt < SegmentAttempts; attempt++)
                        {
                            string url = urls[attempt % urls.Count];
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

                while (true)
                {
                    if (cancellation != null && cancellation())
                    {
                        return false;
                    }

                    long done = 0;
                    for (int i = 0; i < SegmentCount; i++)
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

                    // 全都没动静了(每段都试完还失败)就放弃,让调用方回落
                    bool anyProgress = done > 0;
                    if (elapsed > 90 && !anyProgress)
                    {
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
                    for (int i = 0; i < SegmentCount; i++)
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

                for (int i = 0; i < SegmentCount; i++)
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

        /// <summary>要一段 bytes=0-0,从 Content-Range 里读出总长度;不支持 Range 就返回 -1。</summary>
        private static long ProbeLength(string url)
        {
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Range = new RangeHeaderValue(0, 0);

                    using (HttpResponseMessage response = Client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                        .GetAwaiter().GetResult())
                    {
                        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                        {
                            // 200 说明它忽略了 Range —— 那就没法分段
                            return -1;
                        }

                        ContentRangeHeaderValue range = response.Content.Headers.ContentRange;
                        if (range == null || !range.Length.HasValue)
                        {
                            return -1;
                        }

                        return range.Length.Value;
                    }
                }
            }
            catch
            {
                return -1;
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
                        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent
                            && response.StatusCode != System.Net.HttpStatusCode.OK)
                        {
                            return false;
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
