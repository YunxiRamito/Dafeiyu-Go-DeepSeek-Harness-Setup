using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DshInstaller.Shared.Backup;
using DshInstaller.Shared.Backup.Official;

class Program
{
    static string fixture, helper, install, home, agents, output;
    static int passed;
    static void Main(string[] args)
    {
        string task = Path.GetFullPath(args.Single());
        if (!task.StartsWith("G:\\", StringComparison.OrdinalIgnoreCase)) throw new Exception("Use G: fixtures only.");
        fixture = Path.Combine(task, "f-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(fixture);
        home = Path.Combine(fixture, "portable", ".dsh"); agents = Path.Combine(fixture, "agents"); install = Path.Combine(fixture, "official-install"); output = Path.Combine(fixture, "exports", "migration.exe");
        Environment.SetEnvironmentVariable("DSH_HOME", Path.Combine(fixture, "wrong-environment-home"));
        Environment.SetEnvironmentVariable("DSH_AGENTS_HOME", agents);
        using (Stream template = typeof(Migration).Assembly.GetManifestResourceStream("DshInstaller.OfficialDshRestore.exe"))
        using (FileStream dest = File.Create(helper = Path.Combine(fixture, "OfficialDshRestore.exe"))) template.CopyTo(dest);
        Put("portable/.dsh/profiles/web/package.json", "{\"dependencies\":{\"fixture-plugin\":\"1.0.0\",\"local-plugin\":\"file:../../plugins/local\",\"outside-plugin\":\"file:../../../outside\"},\"dsh\":{\"profile\":{\"bundles\":[\"@deepseek-ai/dsh-base\",\"@deepseek-ai/dsh-web-app\",\"fixture-plugin\",\"local-plugin\",\"outside-plugin\"]}}}");
        Put("portable/.dsh/profiles/web/cordis.yml", "fixture-plugin:\n  enabled: true\n");
        Put("portable/.dsh/profiles/web/cordis.patch.yml", "# user overrides\n");
        Put("portable/.dsh/profiles/web/node_modules/should-not-copy/index.js", "this is a runtime dependency");
        Put("portable/.dsh/plugins/local/package.json", "{\"name\":\"local-plugin\",\"version\":\"1.0.0\"}");
        Put("portable/.dsh/plugins/local/index.js", "module.exports = {};");
        Put("portable/.dsh/sessions/session-a/events.jsonl", "{\"id\":\"fixture-session\"}\n");
        Put("portable/.dsh/storages/session-meta/session-a.json", "{\"title\":\"Fixture\"}");
        Put("portable/.dsh/attachments/image.txt", "attachment fixture");
        Put("portable/.dsh/skills/private/SKILL.md", "# Private fixture\n");
        Put("agents/skills/shared/SKILL.md", "# Shared fixture\n");
        Put("portable/.dsh/.credentials.yaml", "fixture_token: source-secret\n");
        Put("portable/.dafeiyu/config/providers.json", "{\"fixture\":true}");
        Put("portable/.dafeiyu/aries-identity/private_key.pem", "never-copy");
        Put("outside/secret.txt", "outside secret");
        Put("official-install/resources/runtime/cli/bin/dsh.cmd", "@echo off");
        Put("official-install/resources/runtime/office-skills/builtin/SKILL.md", "immutable built-in");
        string root = Path.Combine(fixture, "portable");
        Test("explicit missing HOME is exact and cannot silently fall back", () =>
        { string missing = Path.Combine(fixture, "missing"); Assert(OfficialDshExport.ResolveHome(root, missing) == missing); Assert(!OfficialDshExport.Export(root, missing, Chosen("sessions"), output, null, null, CancellationToken.None).Ok); });
        Test("portable default follows launcher root/.dsh despite env", () => Assert(OfficialDshExport.ResolveHome(root) == home));
        Test("official desktop default honors explicit env", () => Assert(OfficialDshExport.ResolveHome(install) == Environment.GetEnvironmentVariable("DSH_HOME")));
        Test("credentials-only HOME is described", () => { string alone = Path.Combine(fixture, "credentials-only"); Put("credentials-only/.credentials.yaml", "key: fixture"); Assert(OfficialDshExport.Describe(root, alone).Any(g => g.Id == OfficialDshExport.GroupCredentials)); });
        Test("profile-only plugin group is described", () => { Put("profile-only/profiles/web/package.json", "{}"); Assert(OfficialDshExport.Describe(root, Path.Combine(fixture, "profile-only")).Any(g => g.Id == "plugins")); });
        Test("shared Skill group is described", () => Assert(OfficialDshExport.Describe(root, home).Any(g => g.Id == OfficialDshExport.GroupSharedSkills)));
        Test("source junction is skipped without traversal", () => { string link = Path.Combine(home, "sessions", "escape"); CreateDirectoryJunction(link, Path.Combine(fixture, "outside")); });
        string[] ids = { "sessions", "skills", "plugins", "profiles", "official-credentials", "official-compatibility" };
        byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(home, "profiles/web/package.json")));
        Test("official SFX exports and validates", () => { var r = OfficialDshExport.Export(root, home, Chosen(ids), output, null, Console.WriteLine, CancellationToken.None); Assert(r.Ok, r.Error); Assert(r.Warnings.Any(w => w.Contains("outside-plugin"))); });
        PayloadManifest manifest = Protocol.Validate(output, CancellationToken.None);
        Test("mapping includes desktop/attachments/shared skills and excludes resources/node_modules/junction", () =>
        { Assert(manifest.Files.Any(f => f.Target == "home/profiles/desktop/package.json")); Assert(manifest.Files.Any(f => f.Target == "home/attachments/image.txt")); Assert(manifest.Files.Any(f => f.Target == "agents/skills/shared/SKILL.md")); Assert(manifest.Files.All(f => !f.Target.Contains("node_modules") && !f.Target.Contains("escape") && !f.Target.Contains("resources"))); });
        Test("sources remain byte-identical", () => Assert(SHA256.HashData(File.ReadAllBytes(Path.Combine(home, "profiles/web/package.json"))).SequenceEqual(sourceHash)));
        Test("actual self-extracting EXE verifies itself", () => Run(output, "--verify", 0));
        string target = Path.Combine(fixture, "target-home"), targetAgents = Path.Combine(fixture, "target-agents");
        Put("target-home/profiles/desktop/package.json", "{\"dependencies\":{\"existing-plugin\":\"2.0.0\"},\"dsh\":{\"profile\":{\"bundles\":[\"@deepseek-ai/dsh-base\",\"@deepseek-ai/dsh-web-app\",\"existing-plugin\"]}}}");
        Put("target-home/.credentials.yaml", "fixture_token: target-secret\n");
        Test("actual EXE restore roundtrip and safe desktop merge", () => Run(output, RestoreArgs(target, targetAgents, ids), 0));
        Test("roundtrip data, credentials preservation, bundles and local specs", () =>
        {
            Assert(File.ReadAllText(Path.Combine(target, "attachments/image.txt")) == "attachment fixture"); Assert(File.Exists(Path.Combine(target, "sessions/session-a/events.jsonl"))); Assert(File.Exists(Path.Combine(targetAgents, "skills/shared/SKILL.md")));
            Assert(File.ReadAllText(Path.Combine(target, ".credentials.yaml")).Contains("target-secret"));
            string json = File.ReadAllText(Path.Combine(target, "profiles/desktop/package.json")); Assert(json.Contains("existing-plugin") && json.Contains("fixture-plugin") && json.Contains("@deepseek-ai/dsh-web-app")); Assert(json.Contains("file:../../plugins/migration/local-plugin")); Assert(!json.Contains("outside-plugin"));
            Assert(File.Exists(Path.Combine(target, "profiles/desktop/cordis.yml"))); Assert(Directory.GetFiles(Path.Combine(target, "profiles/desktop"), "*.before-official-restore-*.json").Length == 1);
        });
        Test("actual EXE creates missing desktop with official default bundles", () =>
        {
            string clean = Path.Combine(fixture, "new-desktop-home");
            Run(output, RestoreArgs(clean, Path.Combine(fixture, "new-desktop-agents"), new[] { "plugins" }), 0);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(clean, "profiles/desktop/package.json")));
            var bundles = json.RootElement.GetProperty("dsh").GetProperty("profile").GetProperty("bundles").EnumerateArray().Select(item => item.GetString()).ToList();
            Assert(bundles.Take(2).SequenceEqual(new[] { "@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app" }));
            Assert(!bundles.Contains("@deepseek-ai/dsh-desktop-app") && bundles.Contains("fixture-plugin"));
            Assert(!Directory.Exists(Path.Combine(clean, "profiles/desktop/node_modules")));
        });
        Test("actual EXE merge-disabled preserves desktop manifest byte-for-byte", () =>
        {
            const string original = "{\"private\":true,\"dependencies\":{\"fixture-plugin\":\"9.0.0\"},\"dsh\":{\"profile\":{\"bundles\":[\"@deepseek-ai/dsh-desktop-app\",\"target-custom-bundle\"]}}}\n";
            Put("no-merge-home/profiles/desktop/package.json", original);
            string manifestPath = Path.Combine(fixture, "no-merge-home/profiles/desktop/package.json");
            byte[] before = File.ReadAllBytes(manifestPath);
            Run(output, RestoreArgs(Path.Combine(fixture, "no-merge-home"), Path.Combine(fixture, "no-merge-agents"), new[] { "plugins" }).Where(arg => arg != "--merge-plugins").ToArray(), 0);
            Assert(File.ReadAllBytes(manifestPath).SequenceEqual(before));
            Assert(!Directory.GetFiles(Path.GetDirectoryName(manifestPath), "*.before-official-restore-*.json").Any());
        });
        byte[] targetHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(target, ".credentials.yaml")));
        Test("second actual restore preserves all existing files and reports no false import", () => { Run(output, RestoreArgs(target, targetAgents, ids), 1); Assert(SHA256.HashData(File.ReadAllBytes(Path.Combine(target, ".credentials.yaml"))).SequenceEqual(targetHash)); });
        Test("existing output is not overwritten", () => { byte[] hash = SHA256.HashData(File.ReadAllBytes(output)); Assert(!OfficialDshExport.Export(root, home, Chosen(ids), output, null, null, CancellationToken.None).Ok); Assert(SHA256.HashData(File.ReadAllBytes(output)).SequenceEqual(hash)); });
        Test("pre-canceled export leaves no output or staging", () => { using var cts = new CancellationTokenSource(); cts.Cancel(); string canceled = Path.Combine(fixture, "exports", "canceled.exe"); var r = OfficialDshExport.Export(root, home, Chosen(ids), canceled, null, null, cts.Token); Assert(r.Canceled && !File.Exists(canceled)); Assert(Directory.GetDirectories(Path.Combine(fixture, "exports"), ".official-dsh-export-*").Length == 0); });
        Test("cancel during packing leaves no partial EXE", () => { using var cts = new CancellationTokenSource(); string canceled = Path.Combine(fixture, "exports", "packing-canceled.exe"); var r = OfficialDshExport.Export(root, home, Chosen(ids), canceled, (s,p) => { if (s.StartsWith("Packing")) cts.Cancel(); }, null, cts.Token); Assert(r.Canceled && !File.Exists(canceled)); Assert(Directory.GetDirectories(Path.Combine(fixture, "exports"), ".official-dsh-export-*").Length == 0); });
        Test("cancel after commit never destroys completed output", () => { using var cts = new CancellationTokenSource(); string committed = Path.Combine(fixture, "exports", "committed.exe"); var r = OfficialDshExport.Export(root, home, Chosen("sessions"), committed, (s,p) => { if (p == 100) cts.Cancel(); }, null, cts.Token); Assert(r.Ok && File.Exists(committed)); });
        Test("output in install root/HOME rejected", () => { Assert(!OfficialDshExport.Export(root, home, Chosen("sessions"), Path.Combine(root, "bad.exe"), null, null, CancellationToken.None).Ok); Assert(!OfficialDshExport.Export(root, home, Chosen("sessions"), Path.Combine(home, "bad.exe"), null, null, CancellationToken.None).Ok); });
        Test("tampered payload cannot write target", () => { string bad = Path.Combine(fixture, "corrupt.exe"); byte[] bytes = File.ReadAllBytes(output); bytes[^70] ^= 0xff; File.WriteAllBytes(bad, bytes); string badTarget = Path.Combine(fixture, "bad-target"); Run(helper, RestoreArgs(badTarget, targetAgents, ids).Concat(new[] { "--archive", bad }).ToArray(), 1); Assert(!Directory.Exists(badTarget)); });
        Test("destination junction fails before writes", () => { string badTarget = Path.Combine(fixture, "junction-target"); Directory.CreateDirectory(badTarget); CreateDirectoryJunction(Path.Combine(badTarget, "sessions"), Path.Combine(fixture, "outside")); Run(output, RestoreArgs(badTarget, targetAgents, new[] { "sessions" }), 1); Assert(!File.Exists(Path.Combine(fixture, "outside", "session-a/events.jsonl"))); });
        Test("traversal ZIP rejected with valid footer", () => BadArchive("traversal", "data/home/sessions/../outside.txt", "home/sessions/../outside.txt", false, false));
        Test("duplicate ZIP rejected with valid footer", () => BadArchive("duplicate", "data/home/sessions/a.json", "home/sessions/a.json", true, false));
        Test("unlisted extra entries rejected", () => BadArchive("unlisted", "data/home/sessions/a.json", "home/sessions/a.json", false, true));
        Test("resources mapping rejected", () => BadArchive("resources", "data/home/resources/a.json", "home/resources/a.json", false, false));
        Test("per-file hash mismatch rejected", () => BadArchive("hash", "data/home/sessions/a.json", "home/sessions/a.json", false, false, true));
        Test("absolute path rejected", () => BadArchive("absolute", "C:/outside.json", "C:/outside.json", false, false));
        Test("Windows reserved device name rejected", () => BadArchive("reserved", "data/home/sessions/CON.json", "home/sessions/CON.json", false, false));
        Test("trailing-dot alias rejected", () => BadArchive("trailing-dot", "data/home/sessions/a.json.", "home/sessions/a.json.", false, false));
        Test("credentials cannot be smuggled in sessions group", () => BadArchive("credentials-group", "data/home/.credentials.yaml", "home/.credentials.yaml", false, false));
        Test("compression ratio bomb rejected", () => BadArchive("ratio", "data/home/sessions/a.json", "home/sessions/a.json", false, false, false, new byte[4 * 1024 * 1024]));
        Test("ZIP symbolic link metadata rejected", () => BadArchive("symlink", "data/home/sessions/a.json", "home/sessions/a.json", false, false, false, null, true));
        Test("restore staging cancellation commits nothing", () =>
        {
            using var cts = new CancellationTokenSource(); string canceledHome = Path.Combine(fixture, "restore-canceled");
            var r = Migration.Restore(output, canceledHome, Path.Combine(fixture, "restore-canceled-agents"), install, ids, true, true, (s,p) => { if (s.StartsWith("Prepared")) cts.Cancel(); }, cts.Token);
            Assert(r.Canceled && r.Imported == 0 && !Directory.Exists(canceledHome));
        });
        Test("close confirmation is mandatory", () =>
        { string refused = Path.Combine(fixture, "not-confirmed"); var r = Migration.Restore(output, refused, targetAgents, install, ids, true, false, null, CancellationToken.None); Assert(!r.Ok && !Directory.Exists(refused)); });
        Test("no restore staging survives", () => Assert(!Directory.GetDirectories(fixture, ".official-dsh-restore-*", SearchOption.TopDirectoryOnly).Any() && !Directory.GetDirectories(Path.Combine(fixture, "exports"), ".official-dsh-restore-*").Any()));
        Console.WriteLine("PASS " + passed + " filesystem/SFX/actual EXE cases. Evidence: " + fixture);
    }
    static void CreateDirectoryJunction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link));
        if (OperatingSystem.IsWindows())
        {
            // Real directory reparse points need no symbolic-link privilege. Both
            // source traversal and destination rejection assertions remain active.
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' -ErrorAction Stop | Out-Null");
            using var process = Process.Start(start);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(true); throw new Exception("Junction fixture creation timed out."); }
            Task.WaitAll(stdout, stderr);
            Assert(process.ExitCode == 0, "Junction fixture creation failed: " + stderr.Result + stdout.Result);
        }
        else Directory.CreateSymbolicLink(link, target);
        Assert((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Fixture must be a real reparse point.");
        Assert(String.Equals(Path.GetFullPath(Directory.ResolveLinkTarget(link, true).FullName), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase), "Fixture must point at the external target.");
    }
    static string[] RestoreArgs(string target, string shared, string[] ids) => new[] { "--restore", "--home", target, "--agents", shared, "--official-root", install, "--groups", string.Join(',', ids), "--confirm-closed", "--merge-plugins" };
    static void BadArchive(string name, string path, string target, bool duplicate, bool extra, bool wrongHash = false, byte[] data = null, bool symlink = false)
    {
        string zipPath = Path.Combine(fixture, name + ".zip"), exe = Path.Combine(fixture, name + ".exe"); byte[] content = data ?? Encoding.UTF8.GetBytes("fixture data");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry(path); if (symlink) entry.ExternalAttributes = 0xa000 << 16;
            using (var stream = entry.Open()) stream.Write(content);
            if (duplicate) using (var stream = zip.CreateEntry(path.ToUpperInvariant()).Open()) stream.Write(content);
            if (extra) using (var stream = zip.CreateEntry("extra.txt").Open()) stream.Write(content);
            using (var stream = zip.CreateEntry("manifest.json").Open()) Protocol.ManifestWrite(stream, new PayloadManifest { Files = new List<PayloadFile> { new PayloadFile { Path = path, Target = target, Group = "sessions", Length = content.Length, Hash = wrongHash ? new string('0', 64) : Protocol.Hex(SHA256.HashData(content)) } } });
        }
        using (var destination = File.Create(exe)) { using (var template = File.OpenRead(helper)) template.CopyTo(destination); using (var zip = File.OpenRead(zipPath)) { zip.CopyTo(destination); zip.Position = 0; Protocol.Footer(destination, zip.Length, SHA256.HashData(zip)); } }
        Run(helper, new[] { "--verify", "--archive", exe }, 1);
    }
    static void Run(string exe, string arg, int expected) => Run(exe, new[] { arg }, expected);
    static void Run(string exe, string[] args, int expected)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = fixture };
        foreach (string arg in args) info.ArgumentList.Add(arg); info.ArgumentList.Add("--report"); string report = Path.Combine(fixture, "report-" + Guid.NewGuid().ToString("N") + ".txt"); info.ArgumentList.Add(report);
        using var process = Process.Start(info); if (!process.WaitForExit(60000)) throw new Exception("Helper did not exit within 60 seconds.");
        Assert(process.ExitCode == expected, "Exit " + process.ExitCode + " expected " + expected + ": " + (File.Exists(report) ? File.ReadAllText(report) : "no report"));
    }
    static List<BackupGroup> Chosen(params string[] ids) => ids.Select(id => new BackupGroup { Id = id, Exists = true }).ToList();
    static void Put(string relative, string content) { string path = Path.Combine(fixture, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content, new UTF8Encoding(false)); }
    static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS: " + name); }
    static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
}
