using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 下载源测速:开下之前先花几秒实测一遍,把最快的排到前面。
    ///
    /// 为什么需要它:国内那几个 GitHub 加速前缀(ghproxy / gh-proxy / ghfast …)
    /// 速度差得出一个数量级 —— 晚高峰能差到 10 KB/s vs 2 MB/s。
    /// 而下载引擎原来的规矩是"谁排前面用谁,只有失败才换",于是抽到一个慢源就一直慢到底,
    /// 10 MB 的启动器要跑十几分钟(用户实测反馈)。
    ///
    /// 探测方式:对每个候选发一次普通 GET,最多读 <c>millisecondsPerUrl</c> 毫秒,
    /// 用"这段时间读到的字节数 / 实际耗时"当速度。排在最前的源在真实下载时会被再次请求,
    /// 多花的那点流量相对于"避免慢源"是划算的。
    /// </summary>
    internal static class SpeedProbe
    {
        /// <summary>单个源最多读多久。4 秒足够看出快慢了,再长就是白等。</summary>
        private const int DefaultProbeMs = 4000;

        /// <summary>单个源最多读这么多字节就不再往下读(别为了测速把整包都拉下来)。</summary>
        private const long MaxProbeBytes = 4L * 1024 * 1024;

        /// <summary>测得比这还快就直接用它,不再浪费时间测其余源。</summary>
        private const double FastEnoughBytesPerSecond = 1024.0 * 1024;

        /// <summary>
        /// 最多测几个节点。
        ///
        /// 分流之后候选本来就短(加速线路是国内镜像打头 + 官方兜底),
        /// 测三个已经足够挑出"哪个能跑、哪个快";再多测纯属让用户干等。
        /// 官方线路只有一条候选,压根不会被测 —— 走的是上面的单条直接返回。
        /// </summary>
        private const int MaxProbeNodes = 3;

        private static readonly HttpClient Client = CreateClient();

        /// <summary>
        /// 按**域名**缓存的测速结果。
        ///
        /// 为什么按域名而不是按完整 URL:同一个 GitHub 加速前缀会被反复用到 ——
        /// 启动器、Git、pnpm、Python 全走它。完整 URL 各不相同,按域名缓存才命中得上。
        /// 一次运行里测一遍就够了,没必要每下一个包就重测 4 秒(用户点名要的)。
        /// </summary>
        private static readonly Dictionary<string, double> CachedSpeeds =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = true };
            HttpClient client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DSH-Installer");
            return client;
        }

        /// <summary>
        /// 按实测速度把候选地址从快到慢排。
        /// 测不出来的(0)排最后,并且**保持原有相对顺序** —— 原来的顺序是人工按可达性排过的。
        /// </summary>
        public static List<string> RankBySpeed(IList<string> urls, Action<string> notice)
        {
            return RankBySpeed(urls, DefaultProbeMs, notice);
        }

        public static List<string> RankBySpeed(IList<string> urls, int millisecondsPerUrl, Action<string> notice)
        {
            List<string> ordered = new List<string>();

            if (urls == null || urls.Count == 0)
            {
                return ordered;
            }

            if (urls.Count == 1)
            {
                ordered.Add(urls[0]);
                return ordered;
            }

            // **只测前三个节点就够了**。
            //
            // 分流之后候选本身就变短了:加速线路是「国内镜像 → 官方兜底」,
            // 官方线路干脆只有一条(那种情况上面已经直接返回,压根不测)。
            // 再往后的候选是"轮换时才轮到"的备胎,不必在测速阶段一个个等 4 秒。
            int probeCount = urls.Count > MaxProbeNodes ? MaxProbeNodes : urls.Count;
            int count = probeCount;
            string[] candidates = new string[count];
            double[] speeds = new double[count];

            Notice(notice, SharedText.T("正在启动测速通道…", "Starting speed test…"));

            int probed = 0;
            int fastIndex = -1;

            for (int i = 0; i < count; i++)
            {
                candidates[i] = urls[i];

                string host = Host(urls[i]);
                double known;
                if (CachedSpeeds.TryGetValue(host, out known))
                {
                    // 这个域名这次运行里已经测过,直接用上次的结果(不再多嘴报一句)
                    speeds[i] = known;
                    continue;
                }

                // 每个源报一声,让用户看到"在动" —— 测速这一段本来就是纯等待,
                // 不给点反馈的话很容易被当成卡死。用"节点 N"而不是裸露域名,
                // 域名对用户没意义(用户点名要的)。
                Notice(notice, SharedText.T("测试节点", "Testing node ") + (probed + 1) + " …");
                speeds[i] = Measure(urls[i], millisecondsPerUrl);
                CachedSpeeds[host] = speeds[i];
                probed = i + 1;

                // 够快就别再测了:1 MB/s 以上再挑也快不到哪去,
                // 而每多测一个源就要多等 4 秒 —— 早一秒开下更划算。
                if (speeds[i] >= FastEnoughBytesPerSecond)
                {
                    fastIndex = i;
                    break;
                }
            }

            if (fastIndex >= 0)
            {
                // 这个源够快,直接排最前;没来得及测的那些按原顺序跟在后面。
                ordered.Add(candidates[fastIndex]);
                for (int i = 0; i < count; i++)
                {
                    if (i != fastIndex)
                    {
                        ordered.Add(candidates[i]);
                    }
                }

                AppendTail(ordered, urls, probeCount);

                Notice(notice, SharedText.T("测速完成。", "Speed test complete."));

                return ordered;
            }

            // 选择排序:候选一般就三五个,不值得上 LINQ;
            // 而且"不稳定排序"会把同速的源顺序打乱,那种顺序是人工排过的,别动。
            for (int i = 0; i < count; i++)
            {
                int best = i;
                for (int j = i + 1; j < count; j++)
                {
                    if (speeds[j] > speeds[best])
                    {
                        best = j;
                    }
                }

                if (best != i)
                {
                    string swapUrl = candidates[i];
                    candidates[i] = candidates[best];
                    candidates[best] = swapUrl;

                    double swapSpeed = speeds[i];
                    speeds[i] = speeds[best];
                    speeds[best] = swapSpeed;
                }
            }

            for (int i = 0; i < count; i++)
            {
                ordered.Add(candidates[i]);
            }

            AppendTail(ordered, urls, probeCount);

            // 全测完了也只报一句"测速完成" —— 挑中哪个源、快多少,
            // 那是日志里该有的东西,不该糊在用户脸上(用户点名要去掉)。
            Notice(notice, SharedText.T("测速完成。", "Speed test complete."));

            return ordered;
        }

        /// <summary>
        /// 把没测的那些(第 MaxProbeNodes 条往后)按原顺序接到末尾当备胎。
        /// 顺序是人工按可达性排过的,别打乱。
        /// </summary>
        private static void AppendTail(List<string> ordered, IList<string> urls, int probed)
        {
            for (int i = probed; i < urls.Count; i++)
            {
                if (!ordered.Contains(urls[i]))
                {
                    ordered.Add(urls[i]);
                }
            }
        }

        /// <summary>实测一个源:最多读 milliseconds 毫秒,返回字节/秒(0 = 失败或读不到东西)。</summary>
        private static double Measure(string url, int milliseconds)
        {
            try
            {
                // 给探测单独一个硬超时。
                //
                // 这一条是踩出来的:HttpClient 上那个 30 秒的 Timeout 是给**正常下载**用的,
                // 探测要是有源卡在"连上了但不发数据",每个源就干等半分钟,
                // 四五个源两分钟 —— 界面上就是"卡死在测速"(用户实测反馈)。
                // 所以:整个探测最多 milliseconds + 1.5 秒,到点就判它不可用。
                using (System.Threading.CancellationTokenSource budget =
                    new System.Threading.CancellationTokenSource())
                {
                    budget.CancelAfter(milliseconds + 1500);
                    System.Threading.CancellationToken token = budget.Token;

                    Stopwatch clock = Stopwatch.StartNew();

                    using (HttpResponseMessage response = Client
                        .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
                        .GetAwaiter().GetResult())
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            return 0;
                        }

                        using (Stream stream = response.Content.ReadAsStreamAsync(token).GetAwaiter().GetResult())
                        {
                            byte[] buffer = new byte[64 * 1024];
                            long total = 0;

                            while (clock.ElapsedMilliseconds < milliseconds && !token.IsCancellationRequested)
                            {
                                System.Threading.Tasks.Task<int> read =
                                    stream.ReadAsync(buffer, 0, buffer.Length, token);

                                // 单次读也别无限等
                                if (!read.Wait(milliseconds + 1000))
                                {
                                    break;
                                }

                                int got = read.Result;
                                if (got <= 0)
                                {
                                    break;
                                }

                                total += got;
                                if (total >= MaxProbeBytes)
                                {
                                    break;
                                }
                            }

                            double seconds = clock.Elapsed.TotalSeconds;
                            if (seconds < 0.05)
                            {
                                return 0;
                            }

                            return total / seconds;
                        }
                    }
                }
            }
            catch
            {
                return 0;
            }
        }

        private static string Host(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch
            {
                return url;
            }
        }

        private static string Format(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024 * 1024)
            {
                return (bytesPerSecond / 1024 / 1024).ToString("0.0") + " MB";
            }

            if (bytesPerSecond >= 1024)
            {
                return (bytesPerSecond / 1024).ToString("0") + " KB";
            }

            return ((long)bytesPerSecond) + " B";
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
