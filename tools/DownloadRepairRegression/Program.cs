using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

internal static class Program
{
    private static int _checks;
    private static readonly Assembly Shared = typeof(DownloadEngine).Assembly;
    private static readonly string FixtureRoot = Path.GetFullPath(
        Environment.GetEnvironmentVariable("DAFEIYU_INSTALLER_TEMP_ROOT") ?? Path.GetTempPath());
    private static readonly string Root = Path.Combine(FixtureRoot, "download-repair-" + Guid.NewGuid().ToString("N"));

    private static int Main()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAFEIYU_INSTALLER_TEMP_ROOT")))
        {
            Console.Error.WriteLine("DAFEIYU_INSTALLER_TEMP_ROOT must point to the required G: temporary directory.");
            return 2;
        }
        if (!string.Equals(Path.GetPathRoot(FixtureRoot), "G:\\", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Regression fixtures must be rooted on G:, got " + FixtureRoot);
            return 2;
        }
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
            SegmentedDownload();
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
        string aliasTarget = Path.Combine(Root, "subst-volume");
        Directory.CreateDirectory(aliasTarget);
        string? drive = null;
        try { drive = CreateSubstAlias(aliasTarget); }
        catch (IOException error)
        {
            Console.WriteLine("SKIP physical cross-volume deployment: G:-backed SUBST alias unavailable (" + error.Message.Trim() + ")");
            SameVolumeDeployment();
            return;
        }
        string source = Path.Combine(drive + Path.DirectorySeparatorChar, "node-source");
        string target = Path.Combine(Root, "node");
        try
        {
            string physicalSource = Path.Combine(aliasTarget, "node-source");
            Check(!string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase),
                "cross-volume branch exercised through a G:-backed SUBST alias");
            Check(IsWithin(Root, physicalSource) && IsWithin(Root, target), "cross-volume fixture remains physically under configured G: temp root");
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
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, true);
            RemoveSubstAlias(drive, aliasTarget);
            if (Directory.Exists(aliasTarget)) Directory.Delete(aliasTarget, true);
        }
    }

    private static void SameVolumeDeployment()
    {
        string source = Path.Combine(Root, "same-volume-source");
        string target = Path.Combine(Root, "same-volume-node");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "node.exe"), "node-fixture");
        Move(source, target);
        Check(File.ReadAllText(Path.Combine(target, "node.exe")) == "node-fixture", "same-volume fallback node deployed");
        Check(!Directory.Exists(source), "same-volume fallback source moved");
        Directory.CreateDirectory(source);
        try { Move(source, Path.Combine(Root, "same-volume-canceled"), new CancellationToken(true)); throw new Exception("Missing cancellation"); }
        catch (OperationCanceledException) { Check(Directory.Exists(source), "canceled deployment preserves G: source"); }
        Check(!Directory.Exists(Path.Combine(Root, "same-volume-canceled")), "canceled deployment exposes no G: target");
        try { Move(source, target); throw new Exception("Missing target guard"); }
        catch (IOException) { Check(File.ReadAllText(Path.Combine(target, "node.exe")) == "node-fixture", "existing G: target preserved"); }
        try { Move("relative", Path.Combine(Root, "relative")); throw new Exception("Missing absolute guard"); }
        catch (ArgumentException) { Check(true, "relative deployment path rejected"); }
    }

    private static string CreateSubstAlias(string target)
    {
        var occupied = new HashSet<char>(DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])));
        foreach (char letter in "DEFGHIJKLMNOPQRSTUVWXYZ")
        {
            if (Directory.Exists(letter + ":\\")) occupied.Add(letter);
        }
        for (char letter = 'Z'; letter >= 'D'; letter--)
        {
            if (occupied.Contains(letter)) continue;
            string alias = letter + ":";
            if (!DefineDosDevice(0x00000008, alias, target))
                throw new IOException("Unable to create process-local drive alias " + alias + ": " + Marshal.GetLastWin32Error());
            if (Directory.Exists(alias + "\\")) return alias;
            DefineDosDevice(0x0000000E, alias, target);
        }
        throw new IOException("No free drive letter is available for the cross-volume fixture.");
    }

    private static void RemoveSubstAlias(string alias, string target)
    {
        if (string.IsNullOrEmpty(alias)) return;
        if (!DefineDosDevice(0x0000000E, alias, target))
            throw new IOException("Unable to remove process-local drive alias " + alias + ": " + Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DefineDosDevice(uint flags, string deviceName, string targetPath);

    private static bool IsWithin(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void SegmentedDownload()
    {
        using var server = new FixtureServer();
        var downloader = Shared.GetType("DshInstaller.Shared.Install.SegmentedDownloader")!;
        var tryDownload = downloader.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method => method.Name == "TryDownload" && method.GetParameters().Length == 5);
        byte[] expected = Enumerable.Range(0, 8 * 1024 * 1024).Select(index => (byte)(index * 31)).ToArray();
        server.Bodies["/range"] = expected;
        string target = Path.Combine(Root, "segmented.bin");
        var notices = new List<string>();
        bool downloaded = (bool)tryDownload.Invoke(null, new object?[]
        {
            new[] { server.Base + "range" }, target, null, (Func<bool>)(() => false), (Action<string>)notices.Add,
        })!;
        Check(downloaded, "segmented download completes with Range responses");
        Check(File.ReadAllBytes(target).SequenceEqual(expected), "segmented output preserves byte ordering and content");
        Check(server.Ranges.Count >= 9, "segmented download probes and requests all ranges");
        string[] successfulResidue = Directory.EnumerateFiles(Root)
            .Where(path => path.StartsWith(target + ".", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (successfulResidue.Length != 0) Console.WriteLine("segmented residue: " + string.Join(", ", successfulResidue));
        Check(successfulResidue.Length == 0, "successful segmented download removes segment and merge files");

        server.Bodies["/slow-range"] = expected;
        var clock = Stopwatch.StartNew();
        string canceledTarget = Path.Combine(Root, "segmented-canceled.bin");
        bool canceledResult = (bool)tryDownload.Invoke(null, new object?[]
        {
            new[] { server.Base + "slow-range" }, canceledTarget,
            null, (Func<bool>)(() => clock.ElapsedMilliseconds >= 150), (Action<string>)notices.Add,
        })!;
        Check(!canceledResult && clock.Elapsed < TimeSpan.FromSeconds(3), "segmented cancellation stops workers promptly");
        Check(!File.Exists(canceledTarget), "canceled segmented download exposes no target");
        string[] canceledResidue = Directory.EnumerateFiles(Root)
            .Where(path => path.StartsWith(canceledTarget + ".", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (canceledResidue.Length != 0) Console.WriteLine("canceled segmented residue: " + string.Join(", ", canceledResidue));
        Check(canceledResidue.Length == 0, "canceled segmented download removes all partial files");
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
        internal readonly ConcurrentQueue<string> Requests = new();
        internal readonly ConcurrentQueue<string> Ranges = new();
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
                    string? rangeHeader = null;
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    {
                        if (!line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) continue;
                        rangeHeader = line.Substring("Range:".Length).Trim();
                        Ranges.Enqueue(path + " " + rangeHeader);
                    }
                    Requests.Enqueue(path);
                    if (path == "/slow") await Task.Delay(2000, _stop.Token);
                    string body = path == "/good" ? "verified-package" : path == "/text" ? "metadata-fixture" : "invalid-package";
                    byte[] bytes = Bodies.TryGetValue(path, out var supplied) ? supplied : Encoding.UTF8.GetBytes(body);
                    int start = 0;
                    int end = bytes.Length - 1;
                    bool partial = (path == "/range" || path == "/slow-range")
                        && TryParseRange(rangeHeader, bytes.Length, out start, out end);
                    byte[] responseBytes = partial ? bytes[start..(end + 1)] : bytes;
                    string status = partial ? "206 Partial Content" : "200 OK";
                    string range = partial ? "Content-Range: bytes " + start.ToString(CultureInfo.InvariantCulture)
                        + "-" + end.ToString(CultureInfo.InvariantCulture) + "/" + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" : string.Empty;
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\n" + range
                        + "Content-Length: " + responseBytes.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header);
                    if (path == "/slow-body") await Task.Delay(2000, _stop.Token);
                    if (path == "/slow-range")
                    {
                        for (int offset = 0; offset < responseBytes.Length; offset += 64 * 1024)
                        {
                            int count = Math.Min(64 * 1024, responseBytes.Length - offset);
                            await stream.WriteAsync(responseBytes.AsMemory(offset, count), _stop.Token);
                            await Task.Delay(25, _stop.Token);
                        }
                    }
                    else
                    {
                        await stream.WriteAsync(responseBytes);
                    }
                }
                catch (Exception) when (_stop.IsCancellationRequested) { }
                catch (IOException) { }
            }
        }

        private static bool TryParseRange(string? value, int totalLength, out int start, out int end)
        {
            start = 0;
            end = totalLength - 1;
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
            string[] bounds = value.Substring("bytes=".Length).Split('-', 2);
            if (bounds.Length != 2
                || !int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out start)
                || !int.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out end)) return false;
            return start >= 0 && end >= start && end < totalLength;
        }

        public void Dispose() { _stop.Cancel(); _listener.Stop(); _stop.Dispose(); }
    }
}
