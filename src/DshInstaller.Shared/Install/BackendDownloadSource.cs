using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Text.Json;

namespace DshInstaller.Shared.Install
{
    public static class BackendDownloadSource
    {
        public const string BaseUrl = "https://202.189.21.218:8787";
        private const string Pin = "2c72728755267c607124b2ac86e431743c34af5c6069aa22bbcc401e0f626f0b";
        private static readonly object CertificateGate = new object();
        public static string SelectedPreference { get; set; } = "china";
        public static string Resolve(string url) => IsSelected(SelectedPreference) ? Wrap(url) : url;
        public static List<string> Candidates(IList<string> urls)
        {
            var result = new List<string>();
            foreach (string url in urls)
            {
                string preferred = Resolve(url);
                if (!result.Contains(preferred)) result.Add(preferred);
                // Keep the explicit direct fallback; wrapping it again erased the second route.
                if (!IsBackendUrl(url) && !result.Contains(url)) result.Add(url);
            }
            return result;
        }
        public static void EnsureReady(string url, Action<DownloadProgress> progress, Func<bool> cancellation)
        {
            if (!IsBackendUrl(url) || !url.StartsWith(BaseUrl + "/api/download?", StringComparison.Ordinal)) return;
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            ProxySupport.Apply(handler);
            Apply(handler);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            WaitUntilReady(http, url.Replace("/api/download?", "/api/download/status?"), progress, cancellation,
                TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2));
        }
        internal static void WaitUntilReady(HttpClient http, string statusUrl, Action<DownloadProgress> progress,
            Func<bool> cancellation, TimeSpan totalTimeout, TimeSpan stallTimeout, TimeSpan pollInterval)
        {
            var clock = Stopwatch.StartNew();
            TimeSpan lastActivity = TimeSpan.Zero;
            TimeSpan lastReport = TimeSpan.Zero;
            long lastReceived = 0;
            progress?.Invoke(new DownloadProgress { TotalBytes = -1, IsCachePreparing = true, SourceLabel = "大肥鱼国内加速" });
            while (true)
            {
                if (cancellation != null && cancellation()) throw new OperationCanceledException();
                using var cancel = new CancellationTokenSource();
                using var monitor = new Timer(_ => { if (cancellation != null && cancellation()) cancel.Cancel(); }, null, 0, 100);
                HttpResponseMessage polled;
                try { polled = http.GetAsync(statusUrl, cancel.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) when (cancellation == null || !cancellation())
                { throw new TimeoutException("后端服务器缓存状态响应超时。"); }
                using var response = polled;
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                if (json.RootElement.GetProperty("ready").GetBoolean()) return;
                long received = Math.Max(0, json.RootElement.GetProperty("receivedBytes").GetInt64());
                long total = json.RootElement.TryGetProperty("totalBytes", out var totalValue) && totalValue.ValueKind == JsonValueKind.Number
                    && totalValue.TryGetInt64(out var knownTotal)
                    ? knownTotal : -1;
                TimeSpan elapsed = clock.Elapsed;
                double seconds = (elapsed - lastReport).TotalSeconds;
                progress?.Invoke(new DownloadProgress { TotalBytes = total, ReceivedBytes = received, IsCachePreparing = true,
                    BytesPerSecond = seconds > 0 ? Math.Max(0, received - lastReceived) / seconds : 0, SourceLabel = "大肥鱼国内加速" });
                if (received != lastReceived) lastActivity = elapsed;
                lastReceived = received;
                lastReport = elapsed;
                if (elapsed > totalTimeout || elapsed - lastActivity > stallTimeout)
                    throw new TimeoutException("后端服务器缓存停滞，正在切换下载源。");
                var next = elapsed + pollInterval;
                while (clock.Elapsed < next)
                { if (cancellation != null && cancellation()) throw new OperationCanceledException(); Thread.Sleep(100); }
            }
        }
        public static bool IsSelected(string preference) => string.Equals(preference, "Backend", StringComparison.OrdinalIgnoreCase);
        public static bool IsBackendUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && (uri.Host == "202.189.21.218" || uri.Host == "api.ramirinko.top") && uri.Port == 8787 && string.IsNullOrEmpty(uri.UserInfo) && (uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/health/", StringComparison.Ordinal));
        public static string Wrap(string url)
        {
            foreach (var prefix in new[] { "https://ghproxy.net/", "https://gh-proxy.com/", "https://ghfast.top/" })
                if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) url = url.Substring(prefix.Length);
            if (url.StartsWith("https://registry.npmmirror.com/", StringComparison.OrdinalIgnoreCase) && !url.Contains("/-/binary/", StringComparison.Ordinal))
                url = "https://registry.npmjs.org/" + url.Substring("https://registry.npmmirror.com/".Length);
            if (IsBackendUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return url;
            return BaseUrl + "/api/download?url=" + Uri.EscapeDataString(uri.AbsoluteUri);
        }
        public static void Apply(HttpClientHandler handler)
        {
            handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors)
                => IsBackendUrl(request.RequestUri.AbsoluteUri) ? Validate(certificate) : errors == SslPolicyErrors.None;
        }
        private static bool Validate(X509Certificate2 certificate)
        {
            try { return certificate != null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()), Convert.FromHexString(Pin)); }
            catch { return false; }
        }
        public static string CertificateFile()
        {
            lock (CertificateGate)
            {
                string trusted = null;
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                {
                    handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                    { if (!Validate(certificate)) return false;
                        trusted = certificate.ExportCertificatePem(); return true; };
                    using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) })
                    using (var response = http.GetAsync(BaseUrl + "/api/download/ca", HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult())
                    { response.EnsureSuccessStatusCode(); trusted = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); }
                }
                if (trusted == null) throw new InvalidOperationException("后端服务器证书校验失败。");
                var certificates = new X509Certificate2Collection(); certificates.ImportFromPem(trusted);
                if (certificates.Count == 0 || !System.Linq.Enumerable.Any(System.Linq.Enumerable.Cast<X509Certificate2>(certificates),
                    cert => System.Linq.Enumerable.Any(System.Linq.Enumerable.OfType<X509BasicConstraintsExtension>(cert.Extensions), extension => extension.CertificateAuthority)))
                    throw new InvalidOperationException("后端服务器 CA 无效。");
                string path = Path.Combine(Path.GetTempPath(), "Dafeiyu-Go", "backend-ca.pem");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, trusted);
                return path;
            }
        }
    }
}
