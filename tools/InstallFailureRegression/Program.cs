using System.Reflection;
using System.Text.Json.Nodes;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using DshInstaller.Shared.Install;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}
try
{
    // Invoke the production writer: JsonValue<T> requires an explicit resolver on .NET 8.
    var write = typeof(RecommendedPluginInstaller).GetMethod("WriteJson", BindingFlags.Static | BindingFlags.NonPublic)!;
    string profilePath = Path.Combine(root, "package.json");
    var profile = new JsonObject { ["name"] = "fixture", ["private"] = true, ["dependencies"] = new JsonObject { ["local-fixture"] = "file:../plugin" } };
    write.Invoke(null, new object[] { profilePath, profile });
    var saved = JsonNode.Parse(File.ReadAllText(profilePath))!;
    Check(saved["private"]!.GetValue<bool>(), "production profile JSON serializes typed booleans");
    Check(saved["dependencies"]!["local-fixture"]!.GetValue<string>() == "file:../plugin", "production profile JSON preserves dependencies");
    profile["private"] = false;
    write.Invoke(null, new object[] { profilePath, profile });
    Check(!JsonNode.Parse(File.ReadAllText(profilePath))!["private"]!.GetValue<bool>(), "production profile writer atomically replaces existing file");
    Check(!File.Exists(profilePath + ".tmp"), "profile writer leaves no temporary file");

    string components = Path.Combine(root, "components");
    Directory.CreateDirectory(components);
    File.WriteAllText(Path.Combine(components, "fixture.txt"), "fixture-component");
    var install = new InstallOptions { DshRoot = Path.Combine(root, "dsh"), LauncherRoot = Path.Combine(root, "launcher"), ComponentsRoot = components,
        InstallDsh = false, InstallLauncher = false, SourcePreference = "backend" };
    int recorded = InstallManifest.Write(install, "1.7.0", _ => { });
    Check(recorded == 1, "production install manifest writes typed numbers without resolver error");
    var manifest = JsonNode.Parse(File.ReadAllText(InstallManifest.ResolvePath(install)))!;
    Check(manifest["fileCount"]!.GetValue<int>() == 1 && manifest["sourcePreference"]!.GetValue<string>() == "backend", "manifest retains file count and effective source");

    string preserved = Path.Combine(root, "preserved-dsh");
    Directory.CreateDirectory(preserved);
    File.WriteAllText(Path.Combine(preserved, "sentinel.txt"), "existing-user-content");
    var context = new InstallContext(install, CancellationToken.None);
    // Reproduce first-install cancellation: no new DSH root, but components were created.
    var rollback = new UninstallOptions { DshRoot = null, ComponentsRoot = components, RemoveDshCore = true,
        RemoveComponents = true, KeepUserData = false };
    var step = UninstallSteps.BuildPlan(rollback).Single(item => item.Id == UninstallSteps.IdDsh);
    await step.Run(context, CancellationToken.None);
    Check(!Directory.Exists(components), "rollback skips absent DSH path and deletes created components");
    Check(File.ReadAllText(Path.Combine(preserved, "sentinel.txt")) == "existing-user-content", "partial rollback preserves unrelated existing DSH content");
    rollback.ComponentsRoot = null;
    await UninstallSteps.BuildPlan(rollback).Single(item => item.Id == UninstallSteps.IdDsh).Run(context, CancellationToken.None);
    Check(Directory.Exists(preserved), "rollback with both unrecorded paths is a no-op");
    Check(UninstallSteps.BuildPlan(new UninstallOptions { RemoveDshCore = false, RemoveComponents = false }).All(item => item.Id != UninstallSteps.IdDsh),
        "rollback omits core removal when no installation directory was created");
    string MakeTar(string label, params TarEntry[] entries)
    {
        string path = Path.Combine(root, label + ".tar.gz");
        using var output = File.Create(path); using var gzip = new GZipStream(output, CompressionLevel.Fastest); using var writer = new TarWriter(gzip);
        foreach (var entry in entries) writer.WriteEntry(entry);
        return path;
    }
    TarEntry Regular(string name, string text) => new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) };
    string chinese = "仓库/docs/方案/上下文增强-验收记录.md";
    string goodTar = MakeTar("utf8", new PaxGlobalExtendedAttributesTarEntry(new Dictionary<string, string> { ["comment"] = "github-commit-fixture" }),
        new PaxTarEntry(TarEntryType.Directory, "仓库/"), Regular(chinese, "中文正文"), Regular("仓库/package.json", "{\"name\":\"fixture\"}"));
    string extracted = Path.Combine(root, "utf8-extracted");
    ArchiveExtractor.Extract(goodTar, extracted);
    Check(File.ReadAllText(Path.Combine(extracted, chinese.Replace('/', Path.DirectorySeparatorChar))) == "中文正文", "PAX UTF-8 paths extract without system codepage or tar.exe");
    Check(File.Exists(Path.Combine(extracted, "仓库", "package.json")), "managed tar preserves plugin package.json");
    Check(!Directory.GetFiles(extracted, "*", SearchOption.AllDirectories).Any(path => Path.GetFileName(path).Contains("GlobalExtendedAttributes", StringComparison.Ordinal)), "GitHub PAX global metadata is not extracted as a file");
    void RejectTar(string label, params TarEntry[] entries)
    {
        bool rejected = false;
        try { ArchiveExtractor.Extract(MakeTar(label, entries), Path.Combine(root, label)); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "managed tar rejects " + label);
    }
    RejectTar("parent-traversal", Regular("../outside.txt", "no"));
    RejectTar("absolute", Regular("/outside.txt", "no"));
    RejectTar("windows-drive", Regular("C:/outside.txt", "no"));
    RejectTar("alternate-stream", Regular("file.txt:hidden", "no"));
    RejectTar("symlink", new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../outside" });
    RejectTar("hardlink", new PaxTarEntry(TarEntryType.HardLink, "link") { LinkName = "../outside" });
    RejectTar("duplicate-case", Regular("A.txt", "one"), Regular("a.txt", "two"));
    using (var canceled = new CancellationTokenSource())
    {
        canceled.Cancel(); bool stopped = false;
        try { ArchiveExtractor.Extract(goodTar, Path.Combine(root, "cancel-tar"), canceled.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "managed tar extraction honors install cancellation");
    }
    string fakePnpm = Path.Combine(components, "pnpm", "pnpm.cmd"); Directory.CreateDirectory(Path.GetDirectoryName(fakePnpm)!); File.WriteAllText(fakePnpm, "fake executable never invoked");
    var pluginWarnings = new List<string>();
    RecommendedPluginInstaller.Install(install, new[] { "invalid::::first", "invalid::::second" }, (_, _) => { }, _ => { }, CancellationToken.None, pluginWarnings.Add);
    Check(pluginWarnings.Count == 2 && pluginWarnings[0].Contains("first") && pluginWarnings[1].Contains("second"), "per-plugin failures continue and retain individual warnings");
    var logs = new List<string>();
    var warningContext = new InstallContext(install, CancellationToken.None, logs.Add);
    var warningReport = await new InstallRunner(new[] { new InstallStep { Id = "test-warning", Title = "Optional fixture", Required = false, Run = (ctx, _) => { ctx.Warn("fixture optional warning"); return Task.CompletedTask; } } }, warningContext).RunAsync();
    Check(warningReport.Succeeded && warningReport.Warnings.SequenceEqual(new[] { "fixture optional warning" }), "nonfatal warning reaches completed installation report");
    Check(logs.Any(line => line.Contains("步骤耗时:Optional fixture")), "completed step records actual duration");
    Check(logs.Any(line => line.Contains("项警告")), "completion log does not claim all optional steps succeeded");
    Console.WriteLine($"PASS {checks} isolated installation-failure checks; no network, registry, process termination or real installation.");
}
finally
{
    // Only the unique, absolute fixture directory created above may be cleaned up.
    string allowed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures")) + Path.DirectorySeparatorChar;
    if (root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
}
