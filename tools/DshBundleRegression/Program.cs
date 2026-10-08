using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

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
        Check(!options.CanReuse("node"), "backend never reuses incompatible Node major");
        options.ReusableComponents[0].Version="v22.23.3";
        Check(options.CanReuse("node"), "backend allows existing Node22");
        options.SourcePreference="official"; options.ReusableComponents[0].Version="v26.7.0";
        Check(options.CanReuse("node"), "other sources preserve existing Node reuse policy");
    }
    private static Dictionary<string, object> ManifestValues() => new() { ["version"] = Version, ["platform"] = "win-x64", ["nodeMajor"] = 22,
        ["sha256"] = new string('a', 64), ["sizeBytes"] = 500, ["fileName"] = "dsh.zip" };
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
        DshOfflineBundle.ExtractAndValidate(zip, stage, manifest, CancellationToken.None);
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
        File.WriteAllText(Path.Combine(target, "package.json"), "previous-package");
        DshOfflineBundle.Commit(stage, target, CancellationToken.None);
        Check(File.ReadAllText(Path.Combine(target, ".dsh", "keep.txt")) == "user-data", "commit preserves existing user profiles");
        Check(File.ReadAllText(Path.Combine(target, "components", "keep.txt")) == "node-fixture", "commit preserves installed components");
        Check(File.Exists(Path.Combine(target, WellKnown.DshMarker)), "complete package committed without npm");
        Check(File.ReadAllText(Path.Combine(work, "previous", "package.json")) == "previous-package", "old package retained for transaction recovery");
        string failWork = Path.Combine(Root, "fail-work"), failStage = Path.Combine(failWork, "stage");
        Directory.CreateDirectory(Path.Combine(failStage, "node_modules")); File.WriteAllText(Path.Combine(failStage, "node_modules", "new.txt"), "new");
        string oldPackage = File.ReadAllText(Path.Combine(target, "package.json"));
        Reject(() => DshOfflineBundle.Commit(failStage, target, CancellationToken.None), "commit failure triggers rollback");
        Check(File.ReadAllText(Path.Combine(target, "package.json")) == oldPackage, "old root package restored after commit failure");
        Check(File.Exists(Path.Combine(target, WellKnown.DshMarker)), "old dependency tree restored after commit failure");
        Reject(() => DshOfflineBundle.Commit(target, target, CancellationToken.None), "overlapping stage and target rejected");
        var context = new InstallContext(new InstallOptions(), CancellationToken.None);
        context.Warn("plugin fixture failed");
        Check(context.Warnings.Contains("plugin fixture failed"), "optional failure warning survives successful core installation");
    }
    private static void RealBundle(string archive, string manifestPath, string node)
    {
        string version = JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement.GetProperty("version").GetString()!;
        var manifest = DshBundleManifest.Parse(File.ReadAllText(manifestPath), version);
        string work = Path.Combine(Root, "real-work"), stage = Path.Combine(work, "package");
        Directory.CreateDirectory(work);
        DshOfflineBundle.ExtractAndValidate(archive, stage, manifest, CancellationToken.None);
        Check(Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Length > 20000, "real complete dependency archive extracted");
        var validate = typeof(DshOfflineBundle).GetMethod("ValidateRuntime", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { validate.Invoke(null, new object[] { stage, node, version, work, CancellationToken.None, (Action<string>)Console.WriteLine }); }
        catch (TargetInvocationException e) { throw e.InnerException!; }
        Check(true, "real unpacked Windows Node22 CLI and native modules verified offline");
        string target = Path.Combine(Root, "real-target");
        DshOfflineBundle.Commit(stage, target, CancellationToken.None);
        DshOfflineBundle.ValidateLayout(target, version);
        Check(true, "real unpacked dependency tree committed with exact version intact");
    }
}
