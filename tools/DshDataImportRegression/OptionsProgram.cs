using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using DshInstaller.Shared.Install;

string temp = Environment.GetEnvironmentVariable("DAFEIYU_INSTALLER_TEMP_ROOT");
if (String.IsNullOrWhiteSpace(temp) || Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(temp))) != "Temp_SetupandLauncher")
    throw new InvalidOperationException("DAFEIYU_INSTALLER_TEMP_ROOT must be the parent Temp_SetupandLauncher.");
string fixture = Path.Combine(Path.GetFullPath(temp), "migration-options-fixture-" + Guid.NewGuid().ToString("N"));
string official = Path.Combine(fixture, "official-app");
string home = Path.Combine(fixture, "original-user", ".dsh");
string target = Path.Combine(fixture, "independent-target");
int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}
InstallOptions Options() => new InstallOptions { DshRoot = target, LauncherRoot = Path.Combine(target, "launcher"), ComponentsRoot = Path.Combine(target, "components"),
    OfficialMigrationInstallRoot = official, OfficialMigrationHome = home };
void SetDestination(InstallOptions options, string role, string path)
{
    if (role == "dsh") options.DshRoot = path;
    else if (role == "launcher") options.LauncherRoot = path;
    else options.ComponentsRoot = path;
}
void Write(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    File.WriteAllText(path, "isolated marker");
}
void Link(string path, string destination)
{
    if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(path, destination); return; }
    var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
    start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command");
    start.ArgumentList.Add("New-Item -ItemType Junction -Path '" + path.Replace("'", "''") + "' -Target '" + destination.Replace("'", "''") + "' -ErrorAction Stop | Out-Null");
    using var process = Process.Start(start);
    string error = process.StandardError.ReadToEnd(); process.WaitForExit();
    if (process.ExitCode != 0) throw new Exception(error);
}
try
{
    Directory.CreateDirectory(home);
    Write(Path.Combine(official, "resources/app.asar"));
    Write(Path.Combine(official, "resources/runtime/cli/bin/dsh.cmd"));
    Check(Options().Validate() == null, "independent migration layout accepted");
    foreach (string role in new[] { "dsh", "launcher", "components" })
    {
        foreach (string path in new[] { official, Path.Combine(official, "nested/program") })
        {
            var options = Options(); SetDestination(options, role, path);
            Check(options.Validate() != null, role + " cannot equal or sit beneath official installation: " + Path.GetRelativePath(official, path));
            options.OfficialMigrationInstallRoot = null; options.OfficialMigrationHome = null;
            Check(options.Validate() != null, role + " official filesystem markers protect source even without migration fields");
        }
        foreach (string path in new[] { home, Path.Combine(home, "nested"), Path.GetDirectoryName(home) })
        {
            var options = Options(); SetDestination(options, role, path);
            Check(options.Validate() != null, role + " rejects equality or either containment direction with source data: " + Path.GetRelativePath(home, path));
        }
        var normalized = Options(); SetDestination(normalized, role, Path.Combine(home, "child", "..") + Path.DirectorySeparatorChar);
        Check(normalized.Validate() != null, role + " normalizes parent segments and trailing separators before overlap checks");
        var sibling = Options(); SetDestination(sibling, role, official + "-sibling");
        Check(sibling.Validate() == null, role + " allows official installation sibling with shared prefix");
        sibling = Options(); SetDestination(sibling, role, home + "-sibling");
        Check(sibling.Validate() == null, role + " allows source data sibling with shared prefix");
        foreach (string suffix in new[] { "", "nested" })
        {
            var recorded = Options(); recorded.OfficialMigrationInstallRoot = Path.Combine(fixture, "recorded-only-official");
            SetDestination(recorded, role, Path.Combine(recorded.OfficialMigrationInstallRoot, suffix));
            Check(recorded.Validate() != null, role + " recorded official root protects source even when filesystem markers are absent: " + suffix);
        }
    }
    var parentTarget = Options(); parentTarget.DshRoot = fixture;
    parentTarget.LauncherRoot = Path.Combine(fixture, "new-launcher"); parentTarget.ComponentsRoot = Path.Combine(fixture, "new-components");
    Check(parentTarget.Validate() != null, "installation target cannot contain either official source tree");

    var serializerOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    var original = Options();
    string json = JsonSerializer.Serialize(original, serializerOptions);
    string savedUser = Environment.GetEnvironmentVariable("USERPROFILE"), savedHome = Environment.GetEnvironmentVariable("DSH_HOME");
    string savedWorkingDirectory = Environment.CurrentDirectory;
    try
    {
        string administrator = Path.Combine(fixture, "elevated-user"); Directory.CreateDirectory(administrator);
        Environment.SetEnvironmentVariable("USERPROFILE", administrator);
        Environment.SetEnvironmentVariable("DSH_HOME", Path.Combine(administrator, "different-home"));
        Environment.CurrentDirectory = administrator;
        var restored = JsonSerializer.Deserialize<InstallOptions>(json);
        Check(restored.OfficialMigrationHome == home && restored.OfficialMigrationInstallRoot == official, "JSON roundtrip keeps original source home and installation root under changed user environment");
        Check(restored.DshRoot == target && restored.Validate() == null, "restored migration plan remains valid after working directory and user change");
        Check(JsonSerializer.Serialize(restored, serializerOptions) == json, "migration plan JSON roundtrip preserves every serialized option");
    }
    finally
    {
        Environment.CurrentDirectory = savedWorkingDirectory;
        Environment.SetEnvironmentVariable("USERPROFILE", savedUser);
        Environment.SetEnvironmentVariable("DSH_HOME", savedHome);
    }
    var fresh = Options(); fresh.OfficialMigrationInstallRoot = null; fresh.OfficialMigrationHome = null;
    var freshRoundtrip = JsonSerializer.Deserialize<InstallOptions>(JsonSerializer.Serialize(fresh, serializerOptions));
    Check(freshRoundtrip.OfficialMigrationHome == null && freshRoundtrip.OfficialMigrationInstallRoot == null && freshRoundtrip.Validate() == null, "fresh installation keeps migration fields unset across JSON roundtrip");

    var relative = Options(); relative.OfficialMigrationHome = "relative-user/.dsh";
    Check(relative.Validate() != null, "relative source home rejected before UAC can reinterpret working directory");
    relative = Options(); relative.OfficialMigrationInstallRoot = "relative-official";
    Check(relative.Validate() != null, "relative official installation root rejected");
    relative = Options(); relative.OfficialMigrationHome = @"G:drive-relative-home";
    Check(relative.Validate() != null, "drive-relative source home rejected");
    relative = Options(); relative.OfficialMigrationHome = "%USERPROFILE%/.dsh";
    Check(relative.Validate() != null, "unexpanded user variable cannot become an ambiguous elevated source home");

    string alias = Path.Combine(fixture, "source-alias");
    Link(alias, home);
    try
    {
        var options = Options(); options.ComponentsRoot = Path.Combine(alias, "components");
        Check(options.Validate() != null, "target junction cannot disguise a child of source data");
        options = Options(); options.OfficialMigrationHome = alias;
        Check(options.Validate() != null, "source junction cannot disguise overlap during elevated install");
    }
    finally { Directory.Delete(alias); }
    Console.WriteLine($"PASS {checks} isolated migration option, path and elevation JSON checks; no UAC, real user data or installation.");
}
finally
{
    string allowed = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
    if (Directory.Exists(fixture) && Path.GetFullPath(fixture).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) Directory.Delete(fixture, true);
}
