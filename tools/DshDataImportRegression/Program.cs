using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DeepSeekHarnessLauncher.Backup;

string temp = Environment.GetEnvironmentVariable("DAFEIYU_INSTALLER_TEMP_ROOT");
if (String.IsNullOrWhiteSpace(temp)
    || !String.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(temp))), "Temp_SetupandLauncher", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("DAFEIYU_INSTALLER_TEMP_ROOT must point to the parent Temp_SetupandLauncher directory.");
string fixture = Path.Combine(Path.GetFullPath(temp), "official-import-fixture-" + Guid.NewGuid().ToString("N"));
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (InvalidDataException) { Check(true, name); return; }
    throw new Exception("FAIL expected InvalidDataException: " + name);
}
void Write(string root, string path, string text)
{
    string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(file));
    File.WriteAllText(file, text);
}
string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path));
JsonNode Manifest(string root) => JsonNode.Parse(Read(root, "profiles/web/package.json"));
bool Enabled(JsonNode manifest, string name) => manifest["dsh"]["profile"]["bundles"].AsArray().Any(node => node.GetValue<string>() == name);
OfficialImportPlan Preview(string source, string target) => DshDataImportService.Preview(source, target, "desktop", "web", CancellationToken.None);
OfficialImportResult Import(OfficialImportPlan plan, params string[] groups) => DshDataImportService.Import(plan, groups, null, CancellationToken.None);
Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
    .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
void Link(string path, string destination)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(path, destination); return; }
    var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
    start.ArgumentList.Add("-NoProfile");
    start.ArgumentList.Add("-Command");
    start.ArgumentList.Add("New-Item -ItemType Junction -Path '" + path.Replace("'", "''") + "' -Target '" + destination.Replace("'", "''") + "' -ErrorAction Stop | Out-Null");
    using var process = Process.Start(start);
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new Exception(error);
}
try
{
    string source = Path.Combine(fixture, "official", ".dsh");
    string target = Path.Combine(fixture, "web-home");
    string imageHash = new string('a', 64), fileHash = new string('b', 64);
    string image = "attachments/v1/objects/aa/" + imageHash;
    string fileObject = "attachments/v1/file-objects/bb/" + fileHash;
    string fileName = "attachments/v1/files/bb/" + fileHash + "/report.txt";
    Write(source, "sessions/chat/session.jsonl", "{\"attachmentId\":\"sha256:" + imageHash + "\"}\n");
    Write(source, "storages/metadata/chat.json", "{\"title\":\"desktop conversation\"}");
    Write(source, image, "image fixture bytes");
    Write(source, fileObject, "file fixture bytes");
    Write(source, fileName, "file fixture bytes");
    Write(source, "skills/example/SKILL.md", "example skill");
    Write(source, "profiles/desktop/package.json", """{"dependencies":{"custom":"1.0.0","@deepseek-ai/dsh-base":"1","@deepseek-ai/dsh-desktop-app":"1"},"dsh":{"profile":{"bundles":["@deepseek-ai/dsh-base","@deepseek-ai/dsh-desktop-app","custom"]}},"scripts":{"postinstall":"do not copy"},"secret":"do not copy"}""");
    Write(source, "profiles/desktop/node_modules/custom/package.json", """{"name":"custom","version":"1.0.0"}""");
    Write(source, "profiles/desktop/node_modules/custom/index.js", "custom plugin");
    Write(source, "profiles/desktop/node_modules/@deepseek-ai/dsh-desktop-app/index.js", "desktop only");
    Write(source, "profiles/node_modules/@deepseek-ai/dsh-base/index.js", "default bundle");
    Write(source, "profiles/desktop/node_modules/custom/node_modules/@deepseek-ai/dsh-desktop-app/index.js", "nested desktop only");
    foreach (string secret in new[] { ".credentials.yaml", ".env", "config/credentials.json", "cordis.patch.yml", "profiles/desktop/.env", "profiles/desktop/cordis.patch.yml" })
        Write(source, secret, "synthetic credential, must stay at source");
    var before = Snapshot(source);
    var plan = Preview(source + Path.DirectorySeparatorChar, target);
    Check(plan.SourceProfile == "desktop" && plan.TargetProfile == "web", "desktop profile maps to web");
    Check(plan.Groups.Select(group => group.Id).SequenceEqual(new[] { "sessions", "skills", "plugins" }), "attachments share the selectable conversation group");
    Check(plan.Files.Where(item => item.Relative.StartsWith("attachments")).All(item => item.Group == "sessions" && item.Unit == item.Relative), "attachments conflict per file rather than per v1 directory");
    Check(!plan.Files.Any(item => item.Relative.Contains("dsh-desktop-app") || item.Relative.Contains("dsh-base")), "top-level, shared and nested default bundles excluded");
    Check(plan.Files.Count == 8, "preview includes session, metadata, three attachment files, skill and custom plugin only");
    var imported = Import(plan, "sessions", "skills", "plugins");
    Check(imported.Ok && imported.Imported == 8 && imported.Skipped == 0, "complete migration imports exactly the previewed files: " + imported.Error);
    Check(Read(target, image) == Read(source, image) && Read(target, fileObject) == Read(source, fileObject) && Read(target, fileName) == Read(source, fileName), "durable image and generic file paths survive byte-for-byte");
    Check(!Directory.Exists(Path.Combine(target, "profiles/desktop")), "no desktop profile copied into target");
    var merged = Manifest(target);
    Check(Enabled(merged, "@deepseek-ai/dsh-base") && Enabled(merged, "@deepseek-ai/dsh-web-app") && Enabled(merged, "custom") && !Enabled(merged, "@deepseek-ai/dsh-desktop-app"), "new target activates web core and imported custom plugin");
    Check(merged["dependencies"]["@deepseek-ai/dsh-desktop-app"] == null && merged["secret"] == null && merged["scripts"] == null, "source app dependency and arbitrary manifest settings excluded");
    Check(!File.Exists(Path.Combine(target, ".credentials.yaml")) && !Directory.Exists(Path.Combine(target, "config")) && !File.Exists(Path.Combine(target, ".env")), "default migration excludes root credentials and environment");
    Check(before.OrderBy(pair => pair.Key).SequenceEqual(Snapshot(source).OrderBy(pair => pair.Key)), "all source paths and file hashes unchanged after migration");
    var repeated = Import(Preview(source, target), "sessions", "skills", "plugins");
    Check(repeated.Ok && repeated.Imported == 0 && repeated.Skipped == 8, "repeat migration skips every existing data file");

    string conflictTarget = Path.Combine(fixture, "conflict-target");
    Write(conflictTarget, "sessions/chat/target-only.txt", "target conversation");
    Write(conflictTarget, "skills/example/target-only.txt", "target skill");
    Write(conflictTarget, image, "existing image");
    Write(conflictTarget, "storages/metadata/chat.json", "existing metadata");
    Write(conflictTarget, "profiles/web/node_modules/custom/index.js", "existing plugin");
    string originalManifest = """{"dependencies":{"custom":"0.5.0"},"dsh":{"profile":{"bundles":["@deepseek-ai/dsh-base","@deepseek-ai/dsh-web-app"]}},"retained":42}""";
    Write(conflictTarget, "profiles/web/package.json", originalManifest);
    var conflictPlan = Preview(source, conflictTarget);
    Check(conflictPlan.Groups.Single(group => group.Id == "sessions").ExistingUnits == 3, "preview counts conversation, metadata and attachment conflicts");
    var conflicts = Import(conflictPlan, "sessions", "skills", "plugins");
    Check(conflicts.Ok && conflicts.Imported == 2 && conflicts.Skipped == 6, "existing units skip while new attachment files merge");
    Check(!File.Exists(Path.Combine(conflictTarget, "sessions/chat/session.jsonl")) && !File.Exists(Path.Combine(conflictTarget, "skills/example/SKILL.md")), "conversation and skill conflicts preserve whole target units");
    Check(Read(conflictTarget, image) == "existing image" && Read(conflictTarget, "storages/metadata/chat.json") == "existing metadata", "existing attachment and metadata bytes preserved");
    var kept = Manifest(conflictTarget);
    Check(kept["dependencies"]["custom"].GetValue<string>() == "0.5.0" && !Enabled(kept, "custom") && kept["retained"].GetValue<int>() == 42, "target dependency, disabled plugin and custom setting preserved");
    Check(Directory.EnumerateFiles(Path.Combine(conflictTarget, "profiles/web"), "package.json.before-import-*.json").Select(File.ReadAllText).Single() == originalManifest, "manifest backup preserves exact previous content");

    string local = Path.Combine(fixture, "local-home");
    string localTarget = Path.Combine(fixture, "local-target");
    Write(local, "profiles/desktop/package.json", """{"dependencies":{"relative":"file:../../plugins/relative","@custom/linked":"link:../../plugins/linked","missing":"file:../../plugins/missing","external":"link:../../../private-plugin","empty":"file:../../plugins/empty","archive":"file:../../plugins/archive.tgz","registry":"^1.2.0"},"dsh":{"profile":{"bundles":["relative","@custom/linked","missing","external","empty","archive","registry"]}}}""");
    Write(local, "plugins/relative/package.json", """{"name":"relative"}""");
    Write(local, "plugins/relative/index.js", "relative code");
    Write(local, "plugins/linked/package.json", """{"name":"@custom/linked"}""");
    Write(local, "plugins/linked/index.js", "scoped local code");
    Write(local, "plugins/archive.tgz", "unsupported local tarball");
    Directory.CreateDirectory(Path.Combine(local, "plugins/empty"));
    Write(fixture, "private-plugin/index.js", "outside selected source");
    var localPlan = Preview(local, localTarget);
    Check(localPlan.UnavailableModules.SetEquals(new[] { "missing", "external", "empty", "archive" }), "missing, external, empty and tarball local dependencies marked unavailable");
    Check(localPlan.Warnings.Count == 4, "each unavailable local dependency has a preview warning");
    Check(Import(localPlan, "plugins").Ok, "safe local dependencies migrate despite unavailable peers");
    var portable = Manifest(localTarget);
    Check(portable["dependencies"]["relative"].GetValue<string>() == "file:node_modules/relative" && portable["dependencies"]["@custom/linked"].GetValue<string>() == "file:node_modules/@custom/linked", "relative file and link specs rewritten to collected target packages");
    Check(Read(localTarget, "profiles/web/node_modules/relative/index.js") == "relative code" && Read(localTarget, "profiles/web/node_modules/@custom/linked/index.js") == "scoped local code", "uninstalled in-home local packages materialized under web profile");
    Check(new[] { "missing", "external", "empty", "archive" }.All(name => portable["dependencies"][name] == null && !Enabled(portable, name)), "unavailable local dependencies and activations removed");
    Check(portable["dependencies"]["registry"].GetValue<string>() == "^1.2.0" && Enabled(portable, "registry"), "registry dependency stays reinstallable without installed files");
    Check(!Directory.Exists(Path.Combine(localTarget, "profiles/web/node_modules/external")), "external referenced package never copied");

    string legacy = Path.Combine(fixture, "legacy-installation");
    string legacyHome = Path.Combine(legacy, ".dsh");
    string legacyTarget = Path.Combine(fixture, "legacy-target");
    Write(legacyHome, "profiles/desktop/package.json", """{"dependencies":{"legacy-plugin":"file:../../../plugins/legacy-plugin"},"dsh":{"profile":{"bundles":["legacy-plugin"]}}}""");
    Write(legacy, "plugins/legacy-plugin/package.json", """{"name":"legacy-plugin"}""");
    Write(legacy, "plugins/legacy-plugin/index.js", "legacy plugin");
    Write(legacy, "skills/legacy-skill/SKILL.md", "legacy skill");
    Write(legacy, ".env", "legacy credential");
    var legacyBefore = Snapshot(legacy);
    var legacyPlan = Preview(legacy + Path.DirectorySeparatorChar, legacyTarget);
    Check(legacyPlan.LegacyRoot == legacy && legacyPlan.SourceHome == legacyHome, "legacy installation picker resolves .dsh and permitted sibling roots");
    Check(Import(legacyPlan, "plugins", "skills").Ok && Read(legacyTarget, "profiles/web/node_modules/legacy-plugin/index.js") == "legacy plugin", "relative dependency into legacy plugins is materialized for web");
    Check(Read(legacyTarget, "skills/legacy-skill/SKILL.md") == "legacy skill" && !File.Exists(Path.Combine(legacyTarget, ".env")), "legacy skill migrates while installation environment stays excluded");
    Check(legacyBefore.OrderBy(pair => pair.Key).SequenceEqual(Snapshot(legacy).OrderBy(pair => pair.Key)), "legacy installation files and hashes remain unchanged");

    string invalidHome = Path.Combine(fixture, "invalid-module-home");
    Write(invalidHome, "profiles/desktop/package.json", """{"dependencies":{"../escape":"file:../../plugins/escape"}}""");
    Reject(() => Preview(invalidHome, Path.Combine(fixture, "invalid-module-target")), "dependency name cannot traverse outside profile node_modules");

    string brokenHome = Path.Combine(fixture, "broken-package-home");
    string brokenTarget = Path.Combine(fixture, "broken-package-target");
    Write(brokenHome, "sessions/healthy/log.jsonl", "healthy session");
    Write(brokenHome, "profiles/desktop/package.json", """{"dependencies":{"broken":"file:node_modules/broken"},"dsh":{"profile":{"bundles":["broken"]}}}""");
    Write(brokenHome, "profiles/desktop/node_modules/broken/a-readable.js", "must not publish partially");
    Write(brokenHome, "profiles/desktop/node_modules/broken/package.json", "{malformed-json");
    var brokenPlan = Preview(brokenHome, brokenTarget);
    Check(brokenPlan.UnavailableModules.Contains("broken") && !brokenPlan.Files.Any(item => item.Group == "plugins"), "malformed installed package isolated with no partial module files");
    Check(Import(brokenPlan, "sessions", "plugins").Ok && !Directory.Exists(Path.Combine(brokenTarget, "profiles/web/node_modules/broken")), "malformed local plugin cannot block healthy conversations");

    string pnpm = Path.Combine(fixture, "pnpm-home");
    string pnpmTarget = Path.Combine(fixture, "pnpm-target");
    string store = Path.Combine(pnpm, "profiles/desktop/node_modules/.pnpm/demo/node_modules");
    Write(pnpm, "profiles/desktop/package.json", """{"dependencies":{"demo":"link:node_modules/demo"},"dsh":{"profile":{"bundles":["demo"]}}}""");
    Write(store, "demo/package.json", """{"name":"demo","dependencies":{"@scope/runtime":"1"},"optionalDependencies":{"optional":"1"}}""");
    Write(store, "demo/index.js", "demo code");
    Write(store, "@scope/runtime/package.json", """{"name":"@scope/runtime","dependencies":{"demo":"1"}}""");
    Write(store, "@scope/runtime/index.js", "runtime code");
    Write(store, "optional/index.js", "optional code");
    string pnpmLink = Path.Combine(pnpm, "profiles/desktop/node_modules/demo");
    Link(pnpmLink, Path.Combine(store, "demo"));
    try
    {
        var pnpmPlan = Preview(pnpm, pnpmTarget);
        Check(!pnpmPlan.UnavailableModules.Contains("demo") && pnpmPlan.Warnings.Count == 0, "pnpm cyclic package references retain the plugin");
        Check(Import(pnpmPlan, "plugins").Ok, "pnpm plugin migration succeeds");
        Check(Read(pnpmTarget, "profiles/web/node_modules/demo/node_modules/@scope/runtime/index.js") == "runtime code" && Read(pnpmTarget, "profiles/web/node_modules/demo/node_modules/optional/index.js") == "optional code", "pnpm scoped runtime and optional sibling dependencies materialized");
        Check((File.GetAttributes(Path.Combine(pnpmTarget, "profiles/web/node_modules/demo")) & FileAttributes.ReparsePoint) == 0, "target pnpm package is independent of source junction");
    }
    finally { Directory.Delete(pnpmLink); }

    string unsafeHome = Path.Combine(fixture, "unsafe-home");
    Write(unsafeHome, "sessions/good/log.jsonl", "healthy session");
    Write(unsafeHome, "profiles/desktop/package.json", """{"dependencies":{"bad":"link:node_modules/bad","healthy":"1"},"dsh":{"profile":{"bundles":["bad","healthy"]}}}""");
    Write(unsafeHome, "profiles/desktop/node_modules/healthy/index.js", "healthy plugin");
    string unsafeLink = Path.Combine(unsafeHome, "profiles/desktop/node_modules/bad");
    Link(unsafeLink, Path.Combine(fixture, "private-plugin"));
    try
    {
        var unsafePlan = Preview(unsafeHome, Path.Combine(fixture, "unsafe-target"));
        Check(unsafePlan.UnavailableModules.Contains("bad") && unsafePlan.Files.All(item => !item.Relative.Contains("node_modules/bad")), "external plugin junction isolated without partial files");
        Check(Import(unsafePlan, "sessions", "plugins").Ok, "unsafe plugin does not block healthy sessions and plugin");
    }
    finally { Directory.Delete(unsafeLink); }

    Reject(() => Preview(source, source), "same source and target rejected");
    Reject(() => Preview(source + Path.DirectorySeparatorChar, source), "trailing separator cannot bypass equality");
    Reject(() => Preview(source, Path.Combine(source, "nested")), "target contained by source rejected");
    Reject(() => Preview(source, Path.GetDirectoryName(source)), "source contained by target rejected");
    Reject(() => Preview(Path.GetDirectoryName(source), Path.Combine(Path.GetDirectoryName(source), "plugins/new-target")), "target inside selected legacy installation rejected");
    Reject(() => DshDataImportService.Preview(source, target, "../desktop", "web", CancellationToken.None), "source profile traversal rejected");
    Reject(() => DshDataImportService.Preview(source, target, "desktop", "../web", CancellationToken.None), "target profile traversal rejected");
    var exportPlan = DshDataImportService.Preview(source, source, "desktop", "web", CancellationToken.None, forExport: true);
    var exportBefore = Snapshot(source);
    Check(!Import(exportPlan, "sessions").Ok && exportBefore.OrderBy(pair => pair.Key).SequenceEqual(Snapshot(source).OrderBy(pair => pair.Key)), "export plan cannot bypass import root validation or modify source");
    string targetLink = Path.Combine(fixture, "target-link");
    Link(targetLink, target);
    try { Reject(() => Preview(source, targetLink), "target junction rejected"); }
    finally { Directory.Delete(targetLink); }
    string sourceLink = Path.Combine(source, "skills/external");
    Link(sourceLink, Path.Combine(fixture, "private-plugin"));
    try { Reject(() => Preview(source, Path.Combine(fixture, "escaped-target")), "escaping skill junction rejected"); }
    finally { Directory.Delete(sourceLink); }
    string attachmentLink = Path.Combine(source, "attachments/external");
    Link(attachmentLink, Path.Combine(fixture, "private-plugin"));
    try { Reject(() => Preview(source, Path.Combine(fixture, "attachment-escape-target")), "escaping attachment junction rejected"); }
    finally { Directory.Delete(attachmentLink); }

    string changedTarget = Path.Combine(fixture, "changed-target");
    var changedPlan = Preview(source, changedTarget);
    Write(source, image, "modified after preview");
    var changed = Import(changedPlan, "sessions");
    Check(!changed.Ok && changed.Imported == 0 && !Directory.Exists(Path.Combine(changedTarget, "sessions")), "attachment change after preview aborts before publishing conversations");
    Check(!Directory.EnumerateDirectories(changedTarget, ".dafeiyu-import-*").Any(), "failed attachment validation cleans staging");
    string onlySkills = Path.Combine(fixture, "skills-only");
    Check(Import(Preview(source, onlySkills), "skills").Ok && !Directory.Exists(Path.Combine(onlySkills, "sessions")) && !Directory.Exists(Path.Combine(onlySkills, "attachments")) && !Directory.Exists(Path.Combine(onlySkills, "profiles")), "unselected groups leave no conversations, attachments or plugins");
    string cancelTarget = Path.Combine(fixture, "cancel-target");
    var cancelPlan = Preview(source, cancelTarget);
    using var cancellation = new CancellationTokenSource();
    var canceled = DshDataImportService.Import(cancelPlan, new[] { "sessions" }, (_, _) => cancellation.Cancel(), cancellation.Token);
    Check(canceled.Canceled && canceled.Imported == 0 && !Directory.Exists(Path.Combine(cancelTarget, "sessions")), "preparation cancellation publishes no conversation");
    Check(!Directory.EnumerateDirectories(cancelTarget, ".dafeiyu-import-*").Any(), "cancellation cleans staging");

    string attachmentsOnly = Path.Combine(fixture, "attachments-only");
    Write(attachmentsOnly, image, "retained attachment");
    var attachmentsOnlyPlan = Preview(attachmentsOnly, Path.Combine(fixture, "attachments-only-target"));
    Check(attachmentsOnlyPlan.Groups.Single().Id == "sessions" && Import(attachmentsOnlyPlan, "sessions").Ok, "attachment-only home is recognized as conversation data");
    Console.WriteLine($"PASS {checks} isolated official desktop-to-web migration checks; no real user data, install, registry or network operations.");
}
finally
{
    string allowed = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
    if (Directory.Exists(fixture) && Path.GetFullPath(fixture).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) Directory.Delete(fixture, true);
}
