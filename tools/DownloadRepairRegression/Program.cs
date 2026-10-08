using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

internal static class Program
{
    private static int _checks;
    private static readonly Assembly Shared = typeof(DownloadEngine).Assembly;
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "sandbox-" + Guid.NewGuid().ToString("N"));

    private static int Main()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("DAFEIYU_INSTALLER_SETTINGS_DIRECTORY", Path.Combine(Root, "settings"));
        ProxySupport.Current = new ProxySettings { Mode = "None" };
        try
        {
            Candidates();
            CacheStatus();
            ProgressText();
            HttpFallback();
            WrappedArchiveFallback();
            CrossVolume();
            Console.WriteLine($"PASS {_checks} installation download repair checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { BackendDownloadSource.SelectedPreference = "china"; Directory.Delete(Root, true); }
    }

    private static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL " + name); _checks++; Console.WriteLine("PASS " + name); }

    private static void Candidates()
    {
        const string node = "https://nodejs.org/dist/index.json";
        BackendDownloadSource.SelectedPreference = "backend";
        var urls = BackendDownloadSource.Candidates(MirrorSource.BackendUrls(node, "backend"));
        Check(urls.Count == 2, "backend route deduplicated");
        Check(urls[0] == BackendDownloadSource.Wrap(node), "backend preferred");
        Check(urls[1] == node, "direct fallback preserved");
        var npm = BackendDownloadSource.Candidates(new[] { "https://registry.npmmirror.com/pkg/-/pkg.tgz", "https://registry.npmjs.org/pkg/-/pkg.tgz" });
        Check(npm.Count == 3, "npm backend plus two original routes");
        Check(npm.Count(BackendDownloadSource.IsBackendUrl) == 1, "equivalent npm backend routes deduplicated");
        BackendDownloadSource.SelectedPreference = "china";
        urls = BackendDownloadSource.Candidates(new[] { node, node });
        Check(urls.Count == 1 && urls[0] == node, "normal mode keeps original route");
    }

    private static void Wait(HttpClient client, List<DownloadProgress> output, Func<bool>? canceled = null,
        int stallMs = 1000, int pollMs = 10)
    {
        var method = typeof(BackendDownloadSource).GetMethod("WaitUntilReady", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { method.Invoke(null, new object?[] { client, "http://fixture/status", (Action<DownloadProgress>)output.Add,
            canceled, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(stallMs), TimeSpan.FromMilliseconds(pollMs) }); }
        catch (TargetInvocationException e) { throw e.InnerException!; }
    }

    private static void CacheStatus()
    {
        var states = new List<DownloadProgress>();
        using (var http = new HttpClient(new SequenceHandler(
            "{\"ready\":false,\"receivedBytes\":128,\"totalBytes\":1024}",
            "{\"ready\":false,\"receivedBytes\":512,\"totalBytes\":1024}",
            "{\"ready\":true,\"receivedBytes\":1024,\"totalBytes\":1024}")))
            Wait(http, states);
        Check(states.Count == 3, "initial and actual cache samples reported");
        Check(states.All(p => p.IsCachePreparing), "cache phase distinguished");
        Check(states[0].Fraction == -1, "unknown initial size has no percentage");
        Check(states[1].ReceivedBytes == 128 && states[1].TotalBytes == 1024, "server bytes and total reflected");
        Check(states[2].ReceivedBytes == 512 && states[2].BytesPerSecond > 0, "cache speed uses growth");
        Check(states.All(p => p.SourceLabel == "大肥鱼国内加速"), "source visible while caching");
        using (var http = new HttpClient(new SequenceHandler("{\"ready\":false,\"receivedBytes\":0,\"totalBytes\":null}")))
        {
            try { Wait(http, new(), stallMs: 120); throw new Exception("Missing stall timeout"); }
            catch (TimeoutException) { Check(true, "no cache bytes times out and permits fallback"); }
        }
        using (var http = new HttpClient(new SequenceHandler("{}")))
        {
            try { Wait(http, new(), () => true); throw new Exception("Missing cancel"); }
            catch (OperationCanceledException) { Check(true, "cache canceled before request"); }
        }
        using (var http = new HttpClient(new SequenceHandler((string?)null)) { Timeout = TimeSpan.FromMilliseconds(80) })
        {
            try { Wait(http, new()); throw new Exception("Missing status timeout"); }
            catch (TimeoutException) { Check(true, "status timeout is retryable, not user cancellation"); }
        }
        using (var http = new HttpClient(new SequenceHandler((string?)null)))
        {
            var clock = Stopwatch.StartNew();
            try { Wait(http, new(), () => clock.ElapsedMilliseconds > 100); throw new Exception("Missing in-flight cancel"); }
            catch (OperationCanceledException) { Check(clock.Elapsed < TimeSpan.FromSeconds(1), "in-flight cache request canceled promptly"); }
        }
    }

    private static void ProgressText()
    {
        var describe = typeof(BuiltInSteps).GetMethod("DescribeDownload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var percent = typeof(BuiltInSteps).GetMethod("DownloadPercent", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cache = new DownloadProgress { ReceivedBytes = 2048, TotalBytes = -1, IsCachePreparing = true, SourceLabel = "fixture" };
        string text = (string)describe.Invoke(null, new object[] { cache })!;
        Check(text.StartsWith("缓存中"), "UI says caching during server preparation");
        Check(text.Contains("2.0 KB"), "UI shows actual cached bytes");
        Check(!text.Contains("-1") && !text.Contains(" / "), "unknown total not displayed as negative bytes");
        Check((double)percent.Invoke(null, new object[] { cache, 5d, 70d })! == -1, "cache has no fake installation percentage");
        cache.TotalBytes = 4096;
        text = (string)describe.Invoke(null, new object[] { cache })!;
        Check(text.Contains("4.0 KB"), "known cache total shown");
        Check((double)percent.Invoke(null, new object[] { cache, 5d, 70d })! == -1, "cache percentage not confused with local transfer");
        cache.IsCachePreparing = false;
        Check((double)percent.Invoke(null, new object[] { cache, 5d, 70d })! == 37.5, "local transfer percentage reflects local bytes");
    }

    private static void Move(string source, string target, CancellationToken token = default)
    {
        var method = Shared.GetType("DshInstaller.Shared.Install.PortableDirectoryDeployment")!.GetMethod("MoveToTarget", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { method.Invoke(null, new object[] { source, target, token }); }
        catch (TargetInvocationException e) { throw e.InnerException!; }
    }

    private static void CrossVolume()
    {
        string source = Path.Combine(Path.GetTempPath(), "dafeiyu-node-test-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(Root, "node");
        try
        {
            Check(!string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase), "test uses distinct C and G volumes");
            Directory.CreateDirectory(Path.Combine(source, "node_modules", "npm"));
            Directory.CreateDirectory(Path.Combine(source, "empty"));
            File.WriteAllText(Path.Combine(source, "node.exe"), "node-fixture");
            File.WriteAllText(Path.Combine(source, "npm.cmd"), "npm-fixture");
            File.WriteAllText(Path.Combine(source, "node_modules", "npm", "index.js"), "nested-fixture");
            Move(source, target);
            Check(File.ReadAllText(Path.Combine(target, "node.exe")) == "node-fixture", "cross-volume node copied");
            Check(File.ReadAllText(Path.Combine(target, "npm.cmd")) == "npm-fixture", "cross-volume npm copied");
            Check(File.ReadAllText(Path.Combine(target, "node_modules", "npm", "index.js")) == "nested-fixture", "nested module copied");
            Check(Directory.Exists(Path.Combine(target, "empty")), "empty directory copied");
            Check(!Directory.Exists(source), "source cleaned only after target ready");
            Check(!Directory.EnumerateDirectories(Root, ".node-stage-*").Any(), "no deployment staging residue");
            Directory.CreateDirectory(source);
            try { Move(source, Path.Combine(Root, "canceled"), new CancellationToken(true)); throw new Exception("Missing cancellation"); }
            catch (OperationCanceledException) { Check(Directory.Exists(source), "canceled deployment preserves source"); }
            Check(!Directory.Exists(Path.Combine(Root, "canceled")), "canceled deployment exposes no target");
            try { Move(source, target); throw new Exception("Missing target guard"); }
            catch (IOException) { Check(File.ReadAllText(Path.Combine(target, "npm.cmd")) == "npm-fixture", "existing target preserved"); }
            try { Move("relative", Path.Combine(Root, "relative")); throw new Exception("Missing absolute guard"); }
            catch (ArgumentException) { Check(true, "relative deployment path rejected"); }
        }
        finally { if (Directory.Exists(source)) Directory.Delete(source, true); }
    }

    private static void HttpFallback()
    {
        using var server = new FixtureServer();
        string target = Path.Combine(Root, "launcher.bin");
        var notices = new List<string>();
        var progress = new List<DownloadProgress>();
        string selected = DownloadEngine.Download(new[] { server.Base + "bad", server.Base + "good" }, target,
            p => progress.Add(new DownloadProgress { ReceivedBytes = p.ReceivedBytes, TotalBytes = p.TotalBytes }),
            () => false, onNotice: notices.Add, allowSegmented: false, validateCompleted: path =>
            { if (File.ReadAllText(path) != "verified-package") throw new InvalidDataException("fixture checksum mismatch"); });
        Check(selected.EndsWith("good"), "checksum mismatch advances to alternate URL");
        Check(File.ReadAllText(target) == "verified-package", "only verified bytes accepted");
        Check(server.Requests.Any(p => p.StartsWith("/bad")) && server.Requests.Any(p => p.StartsWith("/good")), "both independent routes tried");
        Check(!server.Ranges.Any(), "rejected archive not reused as partial transfer");
        Check(notices.Count >= 1, "retry notice visible");
        Check(progress.Any(p => p.TotalBytes < 0), "connection phase emits progress state");
        Check(progress.Last().ReceivedBytes == Encoding.UTF8.GetByteCount("verified-package"), "final byte count correct");
        Check(DownloadEngine.DownloadText(new[] { server.Base + "text" }) == "metadata-fixture", "metadata text fetched independently");
        var clock = Stopwatch.StartNew();
        try { DownloadEngine.Download(new[] { server.Base + "slow" }, target, null,
            () => clock.ElapsedMilliseconds > 150, allowSegmented: false); throw new Exception("Missing header cancellation"); }
        catch (OperationCanceledException) { Check(clock.Elapsed < TimeSpan.FromSeconds(1), "waiting response headers is cancelable"); }
        clock.Restart();
        try { DownloadEngine.DownloadText(new[] { server.Base + "slow" }, cancellation: () => clock.ElapsedMilliseconds > 150);
            throw new Exception("Missing metadata cancellation"); }
        catch (OperationCanceledException) { Check(clock.Elapsed < TimeSpan.FromSeconds(1), "waiting metadata request is cancelable"); }
        clock.Restart();
        notices.Clear();
        try { DownloadEngine.Download(new[] { server.Base + "slow-body" }, Path.Combine(Root, "canceled-body"), null,
            () => clock.ElapsedMilliseconds > 150, onNotice: notices.Add, allowSegmented: false);
            throw new Exception("Missing body cancellation"); }
        catch (OperationCanceledException) { Check(clock.Elapsed < TimeSpan.FromSeconds(1), "waiting response body is cancelable"); }
        Check(notices.Count == 0, "user cancellation not reported as download-source failure");
    }

    private static void WrappedArchiveFallback()
    {
        using var server = new FixtureServer();
        byte[] valid = Encoding.ASCII.GetBytes("PK-fixture-official-release");
        using (var memory = new MemoryStream())
        {
            using (var gzip = new GZipStream(memory, CompressionMode.Compress, true))
            using (var tar = new TarWriter(gzip, leaveOpen: true))
            using (var entry = new MemoryStream(Encoding.ASCII.GetBytes("PK-fixture-stale-npm-release")))
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "package/DeepSeekHarness.zip") { DataStream = entry });
            server.Bodies["/npm-bad"] = memory.ToArray();
        }
        server.Bodies["/zip-good"] = valid;
        string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(valid));
        var materialize = typeof(BuiltInSteps).GetMethod("MaterializeLauncherArchive", BindingFlags.Static | BindingFlags.NonPublic)!;
        var context = new InstallContext(new InstallOptions { TempRoot = Path.Combine(Root, "temp") }, CancellationToken.None);
        string? verified = null;
        string selected = DownloadEngine.Download(new[] { server.Base + "npm-bad", server.Base + "zip-good" },
            Path.Combine(Root, "wrapped.bin"), null, () => false, allowSegmented: false, validateCompleted: path =>
            {
                string archive = (string)materialize.Invoke(null, new object[] { context, path, "fixture" })!;
                string actual = DownloadEngine.ComputeSha256(archive);
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    Check(Path.GetExtension(archive) == ".zip", "npm wrapper extracted before ZIP checksum validation");
                    Check(File.ReadAllText(archive).Contains("stale-npm"), "stale npm inner archive detected");
                    File.Delete(archive);
                    throw new InvalidDataException("stale npm fixture checksum mismatch");
                }
                verified = archive;
            });
        Check(selected.EndsWith("zip-good"), "stale npm wrapper falls back to independent release ZIP");
        Check(verified != null && File.ReadAllBytes(verified).SequenceEqual(valid), "inner archive checksum never bypassed");
        Check(!server.Ranges.Any(), "stale wrapped archive not resumed into release ZIP");
    }

    private sealed class SequenceHandler(params string?[] replies) : HttpMessageHandler
    {
        private int _index;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? text = replies[Math.Min(Interlocked.Increment(ref _index) - 1, replies.Length - 1)];
            if (text == null) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text!) };
        }
    }

    private sealed class FixtureServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        internal readonly List<string> Requests = new();
        internal readonly List<string> Ranges = new();
        internal readonly Dictionary<string, byte[]> Bodies = new();
        internal string Base { get; }
        internal FixtureServer()
        {
            _listener.Start();
            Base = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/";
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    try { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = Serve(client); }
                    catch (OperationCanceledException) { break; }
                }
            });
        }
        private async Task Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    string path = (await reader.ReadLineAsync())!.Split(' ')[1];
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                        if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) Ranges.Add(line);
                    Requests.Add(path);
                    if (path == "/slow") await Task.Delay(2000, _stop.Token);
                    string body = path == "/good" ? "verified-package" : path == "/text" ? "metadata-fixture" : "invalid-package";
                    byte[] bytes = Bodies.TryGetValue(path, out var supplied) ? supplied : Encoding.UTF8.GetBytes(body);
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header);
                    if (path == "/slow-body") await Task.Delay(2000, _stop.Token);
                    await stream.WriteAsync(bytes);
                }
                catch (Exception) when (_stop.IsCancellationRequested) { }
                catch (IOException) { }
            }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); _stop.Dispose(); }
    }
}
