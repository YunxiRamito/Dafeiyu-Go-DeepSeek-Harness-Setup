using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;
using DafeiyuGo.Shared;

internal static class Program
{
    private const string Version = "0.2.0-rc.2";
    private static int _checks;
    private static string Root = Path.Combine(AppContext.BaseDirectory, "sandbox-" + Guid.NewGuid().ToString("N"));
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("DAFEIYU_INSTALLER_SETTINGS_DIRECTORY", Path.Combine(Root, "settings"));
        try
        {
            Versions();
            InstallerRepair();
            ManifestFallbackPolicy();
            BuildEnvironment();
            WorkDirectories();
            StaleWorkDirectories();
            CountedProgress();
            Manifest();
            ArchiveSafety();
            Commit();
            if (args.Length == 3) RealBundle(args[0], args[1], args[2]);
            Console.WriteLine($"PASS {_checks} DSH complete bundle checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); _checks++; }
    private static void Reject(Action action, string name)
    { try { action(); } catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or OperationCanceledException or InvalidOperationException) { Check(true, name); return; } throw new Exception("FAIL expected rejection: " + name); }
    private static void Versions()
    {
        Check(WellKnown.DshPackageVersion == "latest", "default follows official latest");
        foreach (string value in new[] { Version, "0.2.1-alpha.1", "1.2.3+build.4" }) Check(DshVersionResolver.IsExact(value), "exact version allowed " + value);
        foreach (string value in new[] { "latest", "../1.2.3", "^0.1.5", "1.2.3&bad", "" }) Check(!DshVersionResolver.IsExact(value), "nonexact rejected " + value);
        Check(DshVersionResolver.Resolve(Version, null!, () => false) == Version, "exact version needs no network resolution");
        Reject(() => DshVersionResolver.Resolve("^0.1.5", null!, () => false), "backend never silently substitutes a version range");
        string fakeNode = Path.Combine(Root, "node.exe"); File.WriteAllText(fakeNode, "fixture");
        var options = new InstallOptions { SourcePreference = "backend", ReusableComponents = new() { new ReusableComponent { Id="node", ExePath=fakeNode, Version="v26.7.0" } } };
        Check(!options.CanReuse("node"), "unsupported Node major is not reused");
        options.ReusableComponents[0].Version="v22.23.3";
        Check(options.CanReuse("node"), "backend allows existing Node22");
        options.ReusableComponents[0].Version="v24.21.0";
        Check(options.CanReuse("node"), "Node24 can be reused and runtime-probed before commit");
        options.SourcePreference="official"; options.ReusableComponents[0].Version="v26.7.0";
        Check(!options.CanReuse("node"), "all sources use the same supported Node policy");
    }
    private static Dictionary<string, object> ManifestValues() => new() { ["version"] = Version, ["platform"] = "win-x64", ["nodeMajor"] = 22,
        ["sha256"] = new string('a', 64), ["sizeBytes"] = 500, ["fileName"] = "dsh.zip" };
    private static void InstallerRepair()
    {
        string root = Path.Combine(Root, "existing-install");
        string marker = Path.Combine(root, WellKnown.DshMarker);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "partial-install fixture");
        var options = new InstallOptions { DshRoot = root, DryRun = true, UseExistingDsh = true };
        var details = new List<string>();
        var context = new InstallContext(options, CancellationToken.None, detail: (detail, _) => details.Add(detail));
        void Run() { details.Clear(); BuiltInSteps.Dsh(options).Run(context, CancellationToken.None).GetAwaiter().GetResult(); }
        Run();
        Check(details.Any(line => line.Contains("Using the existing installation") || line.Contains("使用现有安装")), "explicit reuse preserves existing DSH");
        options.Repair = true; Run();
        Check(details.Any(line => line.Contains("Dry run") || line.Contains("演练模式")), "repair reaches installation even with existing marker and reuse flag");
        options.Repair = false; options.ForceReinstall = true; Run();
        Check(details.Any(line => line.Contains("Dry run") || line.Contains("演练模式")), "force reinstall reaches installation even with existing marker");
        Check(File.ReadAllText(marker) == "partial-install fixture", "dry-run repair preserves existing files");
    }
    private static void ManifestFallbackPolicy()
    {
        var read = typeof(DshOfflineBundle).GetMethod("ReadManifestAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        string? Read(HttpMessageHandler handler, CancellationToken token = default)
        {
            using var client = new HttpClient(handler);
            return ((Task<string?>)read.Invoke(null, new object[] { client, "https://fixture.invalid/manifest", token })!).GetAwaiter().GetResult();
        }
        foreach (HttpStatusCode status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Gone })
            Check(Read(new ManifestResponse(status, "missing")) == null, "only absent artifact permits build fallback " + (int)status);
        Check(Read(new ManifestResponse(HttpStatusCode.OK, "{invalid-manifest}")) == "{invalid-manifest}", "invalid manifest still reaches strict manifest validation");
        foreach (HttpStatusCode status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway })
        {
            bool rejected = false;
            try { Read(new ManifestResponse(status, "error")); } catch (HttpRequestException) { rejected = true; }
            Check(rejected, "HTTP failure never falls back to npm " + (int)status);
        }
        Reject(() => Read(new ManifestResponse(HttpStatusCode.OK, " ")), "empty successful manifest never falls back");
        bool networkRejected = false;
        try { Read(new ManifestResponse(HttpStatusCode.OK, "", failNetwork: true)); } catch (HttpRequestException) { networkRejected = true; }
        Check(networkRejected, "network failure never falls back to npm");
        bool canceled = false;
        try { Read(new ManifestResponse(HttpStatusCode.OK, "ok"), new CancellationToken(true)); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "manifest cancellation is preserved");
    }
    private sealed class ManifestResponse(HttpStatusCode status, string body, bool failNetwork = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (failNetwork) throw new HttpRequestException("fixture transport unavailable");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
    private static void BuildEnvironment()
    {
        var create = typeof(DshOfflineBundle).GetMethod("CreateBuildEnvironment", BindingFlags.Static | BindingFlags.NonPublic)!;
        string? previousToken = Environment.GetEnvironmentVariable("NPM_TOKEN");
        string? previousProxy = Environment.GetEnvironmentVariable("HTTP_PROXY");
        ProxySettings previousSettings = ProxySupport.Current.Clone();
        try
        {
            Environment.SetEnvironmentVariable("NPM_TOKEN", "fixture-do-not-forward");
            Environment.SetEnvironmentVariable("HTTP_PROXY", "http://fixture.invalid:1234");
            ProxySupport.Current = new ProxySettings { Mode = "None" };
            var official = (IDictionary<string, string>)create.Invoke(null, new object[] { "official" })!;
            Check(official.ContainsKey("SystemRoot") && official.ContainsKey("Path"), "builder receives complete process environment including OS and PATH");
            Check(!official.ContainsKey("HTTP_PROXY"), "direct mode removes inherited proxy from build snapshot");
            Check(official["npm_config_registry"] == MirrorSource.NpmRegistry("official"), "build registry follows official source preference");
            var accelerated = (IDictionary<string, string>)create.Invoke(null, new object[] { "china" })!;
            Check(accelerated["npm_config_registry"] == MirrorSource.NpmRegistry("china"), "build registry follows accelerated source preference");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NPM_TOKEN", previousToken);
            Environment.SetEnvironmentVariable("HTTP_PROXY", previousProxy);
            ProxySupport.Current = previousSettings;
        }
    }
    private static void Manifest()
    {
        var values = ManifestValues();
        Check(DshBundleManifest.Parse(JsonSerializer.Serialize(values), Version).Version == Version, "strict bundle manifest accepted");
        foreach (var item in new (string Key, object Value, string Name)[] {
            ("version","0.1.5","different actual version rejected"), ("platform","linux-x64","Linux dependencies rejected"),
            ("nodeMajor",26,"wrong Node ABI target rejected"), ("sha256","bad","missing strong hash rejected"),
            ("sizeBytes",0,"zero archive size rejected"), ("sizeBytes",2147483648L,"oversized archive rejected"),
            ("fileName","../x.zip","manifest traversal filename rejected"), ("fileName","x.exe","nonZIP bundle rejected") })
        {
            values = ManifestValues(); values[item.Key] = item.Value;
            Reject(() => DshBundleManifest.Parse(JsonSerializer.Serialize(values), Version), item.Name);
        }
    }
    private static void WorkDirectories()
    {
        string install = Path.Combine(Root, "work-directory-install");
        Directory.CreateDirectory(install);
        string keep = Path.Combine(install, "keep.txt");
        File.WriteAllText(keep, "user-data");
        string cache = Path.Combine(install, "temp", "updatepackage");
        string first = DshBundleDeployment.CreateWorkDirectory(install, "download", Version);
        string second = DshBundleDeployment.CreateWorkDirectory(install, "download", Version);
        Check(Path.GetDirectoryName(first) == cache && Path.GetFileName(first) == "download-" + Version, "package cache stays in installation temp/updatepackage with readable name");
        Check(first != second && Directory.Exists(first) && Directory.Exists(second), "concurrent package work uses separate owned directories");
        string partial = Path.Combine(first, "archive.zip.part");
        File.WriteAllText(partial, "partial-download");
        File.SetAttributes(partial, FileAttributes.ReadOnly);
        DshBundleDeployment.CleanupWorkDirectory(first);
        Check(!Directory.Exists(first), "owned download cleanup removes read-only partial files");
        Check(Directory.Exists(second), "cleanup preserves another active package session");
        DshBundleDeployment.CleanupWorkDirectory(second);
        Check(!Directory.Exists(second) && File.ReadAllText(keep) == "user-data", "successful package cleanup preserves installation files");

        string node = Path.Combine(Root, "unusable-node.exe");
        File.WriteAllText(node, "not an executable");
        bool buildFailed = false;
        try
        {
            DshNpmBundleBuilder.Build(Version, install, node, "https://registry.npmjs.org/",
                new Dictionary<string, string>(), null!, CancellationToken.None);
        }
        catch (System.ComponentModel.Win32Exception) { buildFailed = true; }
        Check(buildFailed, "builder process startup failure reaches cleanup");
        Check(!Directory.Exists(cache) || !Directory.EnumerateDirectories(cache).Any(), "failed builder leaves no owned preparation directory");

        string zip = Zip(Files());
        var manifest = ActualManifest(zip);
        string manifestJson = JsonSerializer.Serialize(new { version=Version, platform="win-x64", nodeMajor=22,
            sha256=manifest.Sha256, sizeBytes=manifest.SizeBytes, fileName="fixture.zip" });
        bool runtimeFailed = false;
        var details = new List<string>();
        try { DshBundleDeployment.Deploy(zip, manifestJson, Version, install, node, (detail, _) => details.Add(detail), CancellationToken.None); }
        catch (System.ComponentModel.Win32Exception) { runtimeFailed = true; }
        Check(runtimeFailed, "deployment runtime startup failure is propagated");
        Check(!Directory.Exists(cache) || !Directory.EnumerateDirectories(cache).Any(), "failed deployment removes its extraction directory");
        Check(details.Count > 0 && details.All(detail => detail.Length <= 80 && !detail.Contains(install)), "deployment progress stays concise and paths remain in logs");
        Check(details.Any(detail=>detail.StartsWith("解压文件（0/")) && details.Any(detail=>detail.StartsWith("清理（0/")), "deployment preserves real extraction and cleanup file counts in short status text");
        Check(!File.Exists(Path.Combine(install, WellKnown.DshMarker)) && File.ReadAllText(keep) == "user-data", "failed offline validation never commits the package");

        using var cancel = new CancellationTokenSource();
        bool extracting = false;
        Reject(() => DshBundleDeployment.Deploy(zip, manifestJson, Version, install, node, (detail, _) =>
        {
            if (detail.Contains("解压", StringComparison.Ordinal)) { extracting = true; cancel.Cancel(); }
        }, cancel.Token), "deployment cancellation during extraction is preserved");
        Check(extracting && (!Directory.Exists(cache) || !Directory.EnumerateDirectories(cache).Any()), "canceled extraction removes owned files");
        byte[] corrupted = File.ReadAllBytes(zip); corrupted[^1] ^= 1; File.WriteAllBytes(zip, corrupted);
        Reject(() => DshBundleDeployment.Deploy(zip, manifestJson, Version, install, node, null!, CancellationToken.None), "invalid downloaded archive fails before deployment");
        Check(!Directory.Exists(cache) || !Directory.EnumerateDirectories(cache).Any(), "archive validation failure leaves no package staging files");
        Check(File.ReadAllText(keep) == "user-data", "all cleanup paths preserve unrelated installation data");

        string owned = DshBundleDeployment.CreateWorkDirectory(install, "extract", Version);
        string stage = Path.Combine(owned, "package");
        foreach (var file in Files())
        {
            string path = Path.Combine(stage, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Value);
        }
        DshBundleDeployment.Commit(stage, install, CancellationToken.None);
        DshBundleDeployment.CleanupWorkDirectory(owned);
        Check(File.Exists(Path.Combine(install, WellKnown.DshMarker)) && !Directory.Exists(owned), "owned nested staging commits successfully and is cleaned");
        Check(File.ReadAllText(keep) == "user-data", "nested staging commit preserves existing user files");
    }
    private static void StaleWorkDirectories()
    {
        string install = Path.Combine(Root, "stale-work-install");
        string stale = DshBundleDeployment.CreateWorkDirectory(install, "prepare", Version);
        string active = DshBundleDeployment.CreateWorkDirectory(install, "download", Version);
        string recovery = DshBundleDeployment.CreateWorkDirectory(install, "extract", Version);
        string owner = Path.Combine(stale, ".dsh-update-owner.json");
        File.WriteAllText(owner, JsonSerializer.Serialize(new { kind="DafeiyuGo.DshUpdate", phase="prepare", version=Version, pid=int.MaxValue, started=1L }));
        string recoveryMarker = Path.Combine(recovery, "recovery-required.txt");
        File.WriteAllText(recoveryMarker, "retain old files for manual recovery");
        string userDirectory = Path.Combine(install, "temp", "updatepackage", "user-download");
        Directory.CreateDirectory(userDirectory);
        string userFile = Path.Combine(userDirectory, "keep.txt"); File.WriteAllText(userFile, "keep-user-cache");
        string next = DshBundleDeployment.CreateWorkDirectory(install, "runtime-check", Version);
        Check(!Directory.Exists(stale), "new operation removes stale owned work from a dead process");
        Check(Directory.Exists(active), "new operation preserves active owned work");
        Check(Directory.Exists(recovery), "new operation preserves recovery-required work");
        Check(!DshBundleDeployment.CleanupWorkDirectory(recovery) && File.Exists(recoveryMarker), "explicit normal cleanup preserves recovery-required work");
        Check(!DshBundleDeployment.CleanupWorkDirectory(userDirectory) && File.ReadAllText(userFile)=="keep-user-cache", "unmarked user temporary directories are never deleted");
        foreach (string path in new[] { active, next }) DshBundleDeployment.CleanupWorkDirectory(path);
        File.Delete(recoveryMarker); // This recovery marker belongs only to this test fixture.
        DshBundleDeployment.CleanupWorkDirectory(recovery);
        Check(File.ReadAllText(userFile)=="keep-user-cache", "cleanup leaves unrelated temporary files intact");
    }
    private static void CountedProgress()
    {
        string install = Path.Combine(Root, "counted-progress");
        string work = DshBundleDeployment.CreateWorkDirectory(install, "prepare", Version);
        string nested = Path.Combine(work, "package", "node_modules", "fixture");
        Directory.CreateDirectory(nested);
        for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(nested, i + ".txt"), "fixture");
        int expected = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Count();
        var cleanup = new List<(int Done, int Total)>();
        bool cleaned = DshBundleDeployment.CleanupWorkDirectory(work, progress:(done, total) => cleanup.Add((done, total)));
        Check(cleaned && !Directory.Exists(work), "counted cleanup removes owned nested payload and directories");
        Check(cleanup.Count>=2 && cleanup[0]==(0, expected) && cleanup[^1]==(expected, expected), "cleanup reports actual starting and completed file counts");
        Check(cleanup.All(item=>item.Total==expected && item.Done>=0 && item.Done<=expected)
            && cleanup.Zip(cleanup.Skip(1)).All(pair=>pair.First.Done<=pair.Second.Done), "cleanup file counts stay monotonic with a stable single-walk total");

        string locked = DshBundleDeployment.CreateWorkDirectory(install, "download", Version);
        string lockedFile = Path.Combine(locked, "locked.zip.part"); File.WriteAllText(lockedFile, "locked-fixture");
        using (var handle = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            Check(!DshBundleDeployment.CleanupWorkDirectory(locked) && File.Exists(Path.Combine(locked, ".dsh-update-owner.json")), "failed cleanup keeps owner marker for later retry");
        Check(DshBundleDeployment.CleanupWorkDirectory(locked) && !Directory.Exists(locked), "cleanup retries safely after a locked file is released");

        string stage = Path.Combine(Root, "written-bytes"); Directory.CreateDirectory(stage);
        var trackerType = typeof(DshNpmBundleBuilder).GetNestedType("WrittenBytesTracker", BindingFlags.NonPublic)!;
        var updates = new List<long>(); var notices = new List<string>();
        using var tracker = (IDisposable)Activator.CreateInstance(trackerType, new object[] { stage, (Action<long>)updates.Add, (Action<string>)notices.Add })!;
        void Invoke(string name) => trackerType.GetMethod(name)!.Invoke(tracker, null);
        Check(updates.SequenceEqual(new long[] { 0 }), "dependency size starts at actual written bytes");
        string payload = Path.Combine(stage, "payload.bin"); File.WriteAllBytes(payload, new byte[2*1024*1024]);
        for (int i = 0; i < 15; i++) Invoke("Pulse");
        Check(updates.Count==1, "dependency polling does not refresh UI before one second");
        Thread.Sleep(1050); Invoke("Pulse");
        Check(updates.Count==2 && updates[^1]==2*1024*1024, "file notifications update actual dependency megabytes after one second");
        File.AppendAllText(payload, "12345"); Thread.Sleep(1050); Invoke("Pulse");
        Check(updates[^1]==new FileInfo(payload).Length, "repeated writes count only actual file growth");
        string renamed = Path.Combine(stage, "renamed.bin"); File.Move(payload, renamed);
        Thread.Sleep(1050); Invoke("Pulse");
        Check(updates[^1]==new FileInfo(renamed).Length, "dependency rename does not double-count bytes");
        File.WriteAllText(Path.Combine(stage, "last.txt"), "last");
        Invoke("Complete");
        Check(updates[^1]==Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Sum(path=>new FileInfo(path).Length), "one final reconciliation reports exact bytes without recurring full scans");
    }
    private static Dictionary<string, string> Files() => new() {
        ["package.json"] = "{\"name\":\"fixture-root\",\"dependencies\":{\"@deepseek-ai/dsh\":\"" + Version + "\"}}",
        ["package-lock.json"] = "{\"packages\":{\"node_modules/@deepseek-ai/dsh\":{\"version\":\"" + Version + "\"}}}",
        ["dsh-bundle.json"] = JsonSerializer.Serialize(new { version=Version, package="@deepseek-ai/dsh", platform="win-x64", nodeMajor=22, entryPoint="node_modules/@deepseek-ai/dsh/lib/bin.js" }),
        ["node_modules/@deepseek-ai/dsh/package.json"] = JsonSerializer.Serialize(new { name="@deepseek-ai/dsh", version=Version }),
        ["node_modules/@deepseek-ai/dsh/lib/bin.js"] = "console.log('fixture')"
    };
    private static string Zip(Dictionary<string, string> files, Action<ZipArchive>? extra = null)
    {
        string path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files) { var entry = zip.CreateEntry(file.Key); using var writer = new StreamWriter(entry.Open()); writer.Write(file.Value); }
        extra?.Invoke(zip);
        return path;
    }
    private static DshBundleManifest ActualManifest(string archive)
    {
        var values = ManifestValues(); values["sizeBytes"] = new FileInfo(archive).Length;
        values["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
        return DshBundleManifest.Parse(JsonSerializer.Serialize(values), Version);
    }
    private static void ArchiveSafety()
    {
        string zip = Zip(Files());
        var manifest = ActualManifest(zip);
        string stage = Path.Combine(Root, "valid");
        var progress = new List<(int Files,long Bytes,long Total,int Count)>();
        DshBundleDeployment.ExtractAndValidate(zip, stage, DshBundleDeployment.ParseManifest(JsonSerializer.Serialize(new {
            version=Version, platform="win-x64", nodeMajor=22, sha256=manifest.Sha256, sizeBytes=manifest.SizeBytes,fileName="fixture.zip" }),Version),
            CancellationToken.None, (files,bytes,total,count)=>progress.Add((files,bytes,total,count)));
        Check(progress.Count>=2 && progress[0].Bytes==0 && progress[^1].Bytes==progress[^1].Total
            && progress[^1].Files==progress[^1].Count, "extraction reports real completed bytes and file totals");
        Check(File.Exists(Path.Combine(stage, WellKnown.DshMarker)), "valid archive extracted and layout validated");
        Check(!File.Exists(Path.Combine(stage, ".dsh")), "bundle has no user profile data");
        Reject(() => DshOfflineBundle.ExtractAndValidate(zip, stage, manifest, CancellationToken.None), "existing stage is not overwritten");
        Reject(() => DshOfflineBundle.ExtractAndValidate(zip, Path.Combine(Root, "canceled"), manifest, new CancellationToken(true)), "extract cancellation honored");
        byte[] bytes = File.ReadAllBytes(zip); bytes[^1] ^= 0x01; File.WriteAllBytes(zip, bytes);
        Reject(() => DshOfflineBundle.VerifyArchive(zip, manifest), "single-byte archive alteration rejected before extraction");
        foreach (string name in new[] { "../escape", "/absolute", "C:/absolute", "node_modules/../escape", "node_modules/a:ads", "node_modules/CON", ".dsh/config", "credentials.json", "node_modules/file. ", "node_modules//bad" })
        {
            var files = Files(); files[name] = "evil"; string bad = Zip(files);
            string target = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Reject(() => DshOfflineBundle.ExtractAndValidate(bad, target, ActualManifest(bad), CancellationToken.None), "unsafe ZIP rejected " + name);
            Check(!Directory.Exists(target), "unsafe ZIP rejected before writes " + name);
        }
        string duplicate = Zip(Files(), z => z.CreateEntry("PACKAGE.JSON"));
        Reject(() => DshOfflineBundle.ExtractAndValidate(duplicate, Path.Combine(Root, "duplicate"), ActualManifest(duplicate), CancellationToken.None), "Windows case-fold duplicate rejected");
        string link = Zip(Files(), z => { var e = z.CreateEntry("node_modules/link"); e.ExternalAttributes = unchecked((int)0xA1FF0000); });
        Reject(() => DshOfflineBundle.ExtractAndValidate(link, Path.Combine(Root, "link"), ActualManifest(link), CancellationToken.None), "ZIP symlink rejected");
        var wrong = Files(); wrong["node_modules/@deepseek-ai/dsh/package.json"] = "{\"name\":\"@deepseek-ai/dsh\",\"version\":\"0.1.5\"}";
        string wrongZip = Zip(wrong);
        Reject(() => DshOfflineBundle.ExtractAndValidate(wrongZip, Path.Combine(Root, "wrong"), ActualManifest(wrongZip), CancellationToken.None), "actual package version mismatch rejected");
    }
    private static void Commit()
    {
        string work = Path.Combine(Root, "commit-work"), stage = Path.Combine(work, "stage"), target = Path.Combine(Root, "target");
        Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
        foreach (var file in Files()) { string path = Path.Combine(stage, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, file.Value); }
        Directory.CreateDirectory(Path.Combine(target, ".dsh")); File.WriteAllText(Path.Combine(target, ".dsh", "keep.txt"), "user-data");
        Directory.CreateDirectory(Path.Combine(target, "components")); File.WriteAllText(Path.Combine(target, "components", "keep.txt"), "node-fixture");
        string previousPackage = "{\"name\":\"user-project\",\"scripts\":{\"hello\":\"keep-me\"},\"dependencies\":{\"docx\":\"^9.8.0\",\"pptxgenjs\":\"^4.0.1\",\"node\":\"^24.21.0\",\"@deepseek-ai/dsh\":\"previous\"}}";
        File.WriteAllText(Path.Combine(target, "package.json"), previousPackage);
        string previousLock = "{\"packages\":{\"\":{\"dependencies\":{\"docx\":\"^9.8.0\",\"node\":\"^24.21.0\"}},\"node_modules/docx\":{\"version\":\"9.8.0\"},\"node_modules/node\":{\"version\":\"24.21.0\"}}}";
        File.WriteAllText(Path.Combine(target,"package-lock.json"),previousLock);
        foreach(string package in new[]{"docx","pptxgenjs","node","@user/plugin"}) {
            string path=Path.Combine(target,"node_modules",package);Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"keep"),package);
        }
        Directory.CreateDirectory(Path.Combine(target,"node_modules",".bin"));File.WriteAllText(Path.Combine(target,"node_modules",".bin","custom.cmd"),"keep-bin");
        Directory.CreateDirectory(Path.Combine(stage,"node_modules","node"));File.WriteAllText(Path.Combine(stage,"node_modules","node","replace"),"must-not-replace");
        DshOfflineBundle.Commit(stage, target, CancellationToken.None);
        Check(File.ReadAllText(Path.Combine(target, ".dsh", "keep.txt")) == "user-data", "commit preserves existing user profiles");
        Check(File.ReadAllText(Path.Combine(target, "components", "keep.txt")) == "node-fixture", "commit preserves installed components");
        Check(File.Exists(Path.Combine(target, WellKnown.DshMarker)), "complete package committed without npm");
        Check(File.ReadAllText(Path.Combine(work, "previous", "package.json")) == previousPackage, "old package retained for transaction recovery");
        using(var merged=JsonDocument.Parse(File.ReadAllText(Path.Combine(target,"package.json")))) {
            Check(merged.RootElement.GetProperty("dependencies").GetProperty("@deepseek-ai/dsh").GetString()==Version,"root DSH version updated");
            Check(merged.RootElement.GetProperty("dependencies").GetProperty("docx").GetString()=="^9.8.0" && merged.RootElement.GetProperty("scripts").GetProperty("hello").GetString()=="keep-me","user root dependency and script preserved");
        }
        using(var merged=JsonDocument.Parse(File.ReadAllText(Path.Combine(target,"package-lock.json")))) {
            Check(merged.RootElement.GetProperty("packages").GetProperty("node_modules/docx").GetProperty("version").GetString()=="9.8.0","user lock dependency preserved");
            Check(merged.RootElement.GetProperty("packages").GetProperty("node_modules/@deepseek-ai/dsh").GetProperty("version").GetString()==Version,"lock DSH version merged");
        }
        foreach(string package in new[]{"docx","pptxgenjs","node","@user/plugin"}) Check(File.ReadAllText(Path.Combine(target,"node_modules",package,"keep"))==package,"extra package preserved "+package);
        Check(!File.Exists(Path.Combine(target,"node_modules","node","replace")),"existing Node runtime package never replaced");
        Check(File.ReadAllText(Path.Combine(target,"node_modules",".bin","custom.cmd"))=="keep-bin","unrelated bin shim preserved");
        string failWork = Path.Combine(Root, "fail-work"), failStage = Path.Combine(failWork, "stage");
        Directory.CreateDirectory(Path.Combine(failStage, "node_modules")); File.WriteAllText(Path.Combine(failStage, "node_modules", "new.txt"), "new");
        string oldPackage = File.ReadAllText(Path.Combine(target, "package.json"));
        Reject(() => DshOfflineBundle.Commit(failStage, target, CancellationToken.None), "commit failure triggers rollback");
        Check(File.ReadAllText(Path.Combine(target, "package.json")) == oldPackage, "old root package restored after commit failure");
        Check(File.Exists(Path.Combine(target, WellKnown.DshMarker)), "old dependency tree restored after commit failure");
        string rollbackWork=Path.Combine(Root,"rollback-work"), rollbackStage=Path.Combine(rollbackWork,"package");
        foreach(var file in Files()){string path=Path.Combine(rollbackStage,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,file.Value);}
        string targetBefore=File.ReadAllText(Path.Combine(target,"package.json"));
        Reject(()=>DshBundleDeployment.Commit(rollbackStage,target,CancellationToken.None,verifyCommitted:()=>throw new IOException("verification fixture failed")),"post-commit validation failure rolls back transaction");
        Check(File.ReadAllText(Path.Combine(target,"package.json"))==targetBefore,"root metadata restored byte-for-byte after post-commit failure");
        Check(File.Exists(Path.Combine(target,WellKnown.DshMarker)),"DSH restored after post-commit failure");
        Reject(() => DshOfflineBundle.Commit(target, target, CancellationToken.None), "overlapping stage and target rejected");
        var context = new InstallContext(new InstallOptions(), CancellationToken.None);
        context.Warn("plugin fixture failed");
        Check(context.Warnings.Contains("plugin fixture failed"), "optional failure warning survives successful core installation");
    }
    private static void RealBundle(string archive, string manifestPath, string node)
    {
        string version = JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement.GetProperty("version").GetString()!;
        var manifest = DshBundleManifest.Parse(File.ReadAllText(manifestPath), version);
        string target = Path.Combine(Root, "real-target");
        var percent = new List<double>();
        var extraction = new List<string>();
        double previous = -1;
        DshBundleDeployment.Deploy(archive, File.ReadAllText(manifestPath), version, target, node, (detail,value)=>{
            percent.Add(value);
            if(detail.Contains("解压"))extraction.Add(detail);
            if(value>=previous+10 || value==100){Console.WriteLine($"PROGRESS {value:F1} {detail}");previous=value;}
        },CancellationToken.None,Console.WriteLine);
        Check(Directory.GetFiles(Path.Combine(target,"node_modules"), "*", SearchOption.AllDirectories).Length > 20000, "real complete dependency archive deployed");
        Check(percent.Count>10 && percent[^1]==100 && percent.Zip(percent.Skip(1)).All(pair=>pair.First<=pair.Second),"real deployment percentage stays monotonic through all phases");
        Check(extraction.Count>2 && percent.Contains(80),"real extraction has intermediate byte and file progress through completion");
        Check(!Directory.EnumerateDirectories(Root,".dsh-bundle-stage-*").Any(),"successful real deployment cleans its owned staging directory");
        string cache = Path.Combine(target, "temp", "updatepackage");
        Check(!Directory.Exists(cache) || !Directory.EnumerateDirectories(cache).Any(), "successful real deployment leaves no package cache directory");
        DshOfflineBundle.ValidateLayout(target, version);
        Check(true, "real committed dependency layout has exact version intact");
    }
}
