using System.Text.Json;
using System.Text.Json.Nodes;
using DshInstaller.Shared.Install;

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("An absolute test directory is required.");
string root = Path.Combine(args[0], "selection-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
string? Apply(InstallOptions options) => LauncherSelection.Apply(options, () => false);
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name);
    checks++;
}
try
{
    string settingsPath = Path.Combine(root, "LauncherSettings.json");
    var options = new InstallOptions
    {
        LauncherSettingsFile = settingsPath,
        DshRoot = Path.Combine(root, "new-dsh"),
        ComponentsRoot = Path.Combine(root, "components")
    };
    Check(Apply(options) == null && !File.Exists(settingsPath), "first install leaves launcher settings creation to the launcher");
    Directory.CreateDirectory(Path.Combine(options.ComponentsRoot, "node"));
    File.WriteAllText(Path.Combine(options.ComponentsRoot, "node", "node.exe"), "fixture; never executed");
    string original = new JsonObject
    {
        ["DshRoot"] = Path.Combine(root, "old-dsh"), ["NodePath"] = "old-node",
        ["ApiKeyProtected"] = "test-protected-key", ["GitHubTokenProtected"] = "test-github-key",
        ["ApiKeyValidatedUtc"] = "2026-10-01T00:00:00Z", ["ApiKeyLastValidationSucceeded"] = true,
        ["Theme"] = "Dark", ["SkillRoots"] = new JsonArray("custom-root"),
        ["FutureSetting"] = new JsonObject { ["enabled"] = true }
    }.ToJsonString();
    File.WriteAllText(settingsPath, original);
    byte[] oldBytes = File.ReadAllBytes(settingsPath);
    bool busyRejected = false;
    try { LauncherSelection.Apply(options, () => true); } catch (IOException) { busyRejected = true; }
    Check(busyRejected && File.ReadAllBytes(settingsPath).SequenceEqual(oldBytes), "running launcher prevents settings changes");
    string? backup = Apply(options);
    Check(backup != null && File.ReadAllBytes(backup).SequenceEqual(oldBytes), "existing settings have an exact recoverable backup");
    var settings = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
    Check(settings["dshRoot"]!.GetValue<string>() == Path.GetFullPath(options.DshRoot) && !settings.ContainsKey("DshRoot"), "explicit root selection replaces PascalCase without duplicate keys");
    Check(settings["nodePath"]!.GetValue<string>() == Path.Combine(options.ComponentsRoot, "node", "node.exe"), "portable Node follows the selected install plan");
    Check(settings["apiKeyProtected"]!.GetValue<string>() == "" && settings["apiKeyValidatedUtc"] == null
        && !settings["apiKeyLastValidationSucceeded"]!.GetValue<bool>(), "new DSH does not inherit the previous API key or validation state");
    Check(settings["GitHubTokenProtected"]!.GetValue<string>() == "test-github-key" && settings["Theme"]!.GetValue<string>() == "Dark"
        && settings["SkillRoots"]![0]!.GetValue<string>() == "custom-root" && settings["FutureSetting"]!["enabled"]!.GetValue<bool>(), "other credentials, appearance, custom roots and unknown fields are preserved");
    Check(settings["legacyApiKeyMigrationCompleted"]!.GetValue<bool>(), "legacy key import cannot restore the old install key");
    Check(Apply(options) == null && Directory.GetFiles(root, "*.bak").Length == 1, "unchanged selection neither rewrites settings nor creates another backup");
    Check(!Directory.GetFiles(root, "*.tmp").Any(), "atomic update leaves no temporary file");
    var roundTrip = JsonSerializer.Deserialize<InstallOptions>(JsonSerializer.Serialize(options))!;
    Check(roundTrip.LauncherSettingsFile == settingsPath, "elevated plan retains the original user's launcher settings path");
    File.WriteAllText(settingsPath, "invalid JSON");
    bool rejected = false;
    try { Apply(options); } catch (JsonException) { rejected = true; }
    Check(rejected && File.ReadAllText(settingsPath) == "invalid JSON", "malformed settings remain unchanged");
    options.LauncherSettingsFile = Path.Combine(root, "other.json");
    rejected = false;
    try { Apply(options); } catch (InvalidDataException) { rejected = true; }
    Check(rejected && !File.Exists(options.LauncherSettingsFile), "arbitrary JSON targets are rejected");
    Console.WriteLine($"PASS {checks} launcher selection checks; isolated files only");
}
finally { Directory.Delete(root, true); }
