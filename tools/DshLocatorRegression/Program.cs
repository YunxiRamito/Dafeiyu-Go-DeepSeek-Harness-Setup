using System.Diagnostics;
using System.Reflection;
using DshInstaller.Shared;
using DshInstaller.Shared.Detection;

internal static class Program
{
    private static int _checks;
    private static string _root = null!;

    private static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2 || !Path.IsPathFullyQualified(args[0]))
            throw new ArgumentException("Supply an absolute sandbox parent under Temp_SetupandLauncher.");
        _root = Path.Combine(args[0], "locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("DAFEIYU_INSTALLER_SETTINGS_DIRECTORY", Path.Combine(_root, "settings"));
        try
        {
            LayoutsAndMetadata();
            MultipleAndCompatibility();
            ProcessMatching();
            RuntimePackageFiltering();
            LiveProbe();
            if (args.Length == 2) RegisteredOfficialInstallation(args[1]);
            Console.WriteLine($"PASS {_checks} DSH locator checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(_root, true); }
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL " + name);
        Console.WriteLine("PASS " + name);
        _checks++;
    }

    private static T Invoke<T>(Type type, string method, params object?[] args)
    {
        try { return (T)type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!; }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }

    private static void FileAt(string root, string relative, string text = "fixture")
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Desktop(string name)
    {
        string root = Path.Combine(_root, name);
        FileAt(root, @"resources\runtime\cli\bin\dsh.cmd");
        FileAt(root, @"resources\app.asar");
        return root;
    }

    private static string Npm(string name, string version)
    {
        string root = Path.Combine(_root, name);
        FileAt(root, WellKnown.DshMarker);
        FileAt(root, @"node_modules\@deepseek-ai\dsh\package.json", "{\"nested\":{\"version\":\"wrong\"},\"version\":\"" + version + "\"}");
        return root;
    }

    private static DshInstallation? Entry(string? name, string? location, string? icon = null, string? command = null, string? version = null)
        => Invoke<DshInstallation?>(typeof(DshLocator), "ReadUninstallEntry", name, location, icon, command, version);

    private static void LayoutsAndMetadata()
    {
        string npm = Npm("npm with spaces", "0.2.1-alpha.1");
        var ordinary = DshLocator.InspectInstallation(npm)!;
        Check(ordinary.Kind == DshInstallationKind.Npm && ordinary.Root == npm, "ordinary marker retains npm kind and root");
        Check(ordinary.Version == "0.2.1-alpha.1", "JSON top-level version used, not nested version");
        Check(ordinary.CliPath == Path.Combine(npm, WellKnown.DshMarker) && ordinary.DataHome == Path.Combine(npm, ".dsh"), "npm CLI and data home contract");
        Check(!DshLocator.IsOfficialDesktopRoot(npm), "ordinary installation is not desktop");
        string desktop = Desktop("arbitrary desktop location");
        Check(DshLocator.IsOfficialDesktopRoot(desktop), "two real desktop files validate arbitrary root");
        Check(!DshLocator.LooksLikeDshRoot(desktop), "desktop does not change legacy npm root contract");
        var candidate = Entry("DeepSeek Harness", desktop, version: "1.4.2")!;
        Check(candidate.Kind == DshInstallationKind.OfficialDesktop && candidate.Root == desktop && candidate.Version == "1.4.2", "uninstall display name, root and version retained");
        Check(candidate.CliPath == Path.Combine(desktop, @"resources\runtime\cli\bin\dsh.cmd"), "desktop CLI shim exposed");
        Check(candidate.DataHome == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"), "desktop data home belongs to current user");
        Check(Entry("unrelated", desktop) == null && Entry("DeepSeek Harness Helper", desktop) == null, "display name must match whole product name");
        Check(Entry(" deepseek harness ", desktop) != null, "display name whitespace and casing accepted");
        foreach (string version in new[] { "0.2.0-rc.2", "1.2.3", "1.2.3-alpha.1+build.4", "0.0.0", "1.2.3+001" })
            Check(Entry("DeepSeek Harness " + version, desktop, version: version)?.Version == version,
                "valid product version suffix accepted: " + version);
        foreach (string suffix in new[] { "Helper", "0.2", "v0.2.0", "01.2.3", "1.2.3-01", "1.2.3-", "1.2.3+", "1.2.3-..", "1.2.3 extra", "1.2.3.4" })
            Check(Entry("DeepSeek Harness " + suffix, desktop) == null, "invalid product suffix rejected: " + suffix);
        Check(Entry("Other DeepSeek Harness 0.2.0", desktop) == null, "product name prefix cannot be spoofed");
        Check(Entry("DeepSeek Harness", null, "\"" + Path.Combine(desktop, "Harness.exe") + "\",0")?.Root == desktop, "quoted DisplayIcon with resource index parsed");
        Check(Entry("DeepSeek Harness", "missing", Path.Combine(desktop, "Harness.exe") + ",-12")?.Root == desktop, "unquoted DisplayIcon with spaces and negative index parsed");
        Check(Entry("DeepSeek Harness", null, command: "\"" + Path.Combine(desktop, "Uninstall Harness.exe") + "\" /S")?.Root == desktop, "quoted uninstall command parsed without execution");
        Check(Entry("DeepSeek Harness", null, command: Path.Combine(desktop, "Uninstall Harness.exe") + " /S")?.Root == desktop, "unquoted uninstall command with spaces parsed");
        Environment.SetEnvironmentVariable("DAFEIYU_LOCATOR_FIXTURE_ROOT", desktop);
        try { Check(Entry("DeepSeek Harness", "%DAFEIYU_LOCATOR_FIXTURE_ROOT%")?.Root == desktop, "environment variables expanded"); }
        finally { Environment.SetEnvironmentVariable("DAFEIYU_LOCATOR_FIXTURE_ROOT", null); }
        Check(Entry("DeepSeek Harness", null, "\"unterminated", "msiexec /x test") == null, "malformed commands rejected");
        string pwa = Path.Combine(_root, "Chrome Web Applications");
        FileAt(pwa, "DeepSeek Harness.ico");
        Check(Entry("DeepSeek Harness", pwa, Path.Combine(pwa, "DeepSeek Harness.ico"), "chrome.exe --uninstall-app-id=abc", "1.0") == null, "same-name Chrome PWA rejected");
        Check(Entry("DeepSeek Harness 0.2.0-rc.2", pwa) == null, "version-suffixed Chrome PWA still rejected by structure");
        foreach (string marker in new[] { @"resources\app.asar", @"resources\runtime\cli\bin\dsh.cmd" })
        {
            string partial = Npm("partial-" + Guid.NewGuid().ToString("N"), "0.2.0");
            FileAt(partial, marker);
            Check(!DshLocator.IsOfficialDesktopRoot(partial) && DshLocator.InspectInstallation(partial) == null, "partial desktop cannot become npm reuse: " + marker);
        }
        string directoryMarker = Path.Combine(_root, "marker-is-directory");
        Directory.CreateDirectory(Path.Combine(directoryMarker, @"resources\runtime\cli\bin\dsh.cmd"));
        FileAt(directoryMarker, @"resources\app.asar");
        Check(!DshLocator.IsOfficialDesktopRoot(directoryMarker), "desktop markers must be files");
        Check(DshLocator.InspectInstallation("relative-path") == null && DshLocator.InspectInstallation(null!) == null, "relative and null roots rejected");
        FileAt(desktop, @"resources\runtime\cli\node_modules\@deepseek-ai\dsh\package.json", "{\"version\":\"0.3.0\"}");
        Check(DshLocator.InspectInstallation(desktop)?.Version == "0.3.0", "CLI package version used without registry version");
        Check(Entry("DeepSeek Harness", desktop, version: "9.0.0")?.Version == "9.0.0", "registry display version takes priority");
        FileAt(desktop, @"resources\runtime\cli\node_modules\@deepseek-ai\dsh\package.json", "broken JSON");
        FileAt(desktop, @"resources\runtime\cli\package.json", "{\"version\":\"0.4.0\"}");
        Check(DshLocator.InspectInstallation(desktop)?.Version == "0.4.0", "CLI root manifest fallback survives malformed dependency JSON");
    }

    private static void MultipleAndCompatibility()
    {
        string first = Npm("first npm", "0.2.0"), second = Npm("second npm", "0.3.0");
        string desktop = Desktop("first desktop"), otherDesktop = Desktop("second desktop");
        FileAt(desktop, @"resources\runtime\cli\package.json", "{\"version\":\"0.3.0\"}");
        var official = new[] { Entry("DeepSeek Harness", desktop, version: "1.0")!, Entry("DeepSeek Harness", otherDesktop, version: "2.0")! };
        var installations = Invoke<List<DshInstallation>>(typeof(DshLocator), "CollectInstallations",
            new[] { desktop, first, first.ToUpperInvariant() + "\\", second, desktop + "\\.", "missing" }, official.Concat(official).ToArray());
        Check(installations.Count == 4, "multiple npm and desktop installations retained, duplicate hive/views removed");
        Check(installations[0].Root == desktop && installations[0].Version == "1.0", "hint stays first and registry version overrides CLI fallback");
        Check(installations.Count(i => i.Kind == DshInstallationKind.Npm) == 2, "all npm candidates retained");
        var report = new ProbeReport();
        report.DshInstallations.AddRange(installations);
        Check(report.DshInstallations.Count == 4, "report stores all candidates");
        var status = Invoke<ComponentStatus>(typeof(EnvironmentProbe), "ProbeDsh", installations);
        Check(status.IsSatisfied && status.Path == first && status.DetectedVersion == "0.2.0", "legacy component skips desktop and selects first npm");
        var desktopOnly = Invoke<ComponentStatus>(typeof(EnvironmentProbe), "ProbeDsh", official.ToList());
        Check(desktopOnly.State == DetectState.Missing && desktopOnly.Path == null, "desktop-only system still installs a new npm root");
    }

    private static void ProcessMatching()
    {
        string root = Desktop("running source");
        bool Matches(string output) => Invoke<bool>(typeof(DshLocator), "OfficialProcessScanMatches", root, output);
        Check(Matches(Path.Combine(root, "Renamed Electron.exe") + "\nDSH_PROCESS_SCAN_OK\n"), "Electron executable beneath selected root is running");
        Check(Matches(Path.Combine(root, @"resources\runtime\node.exe") + "\r\nDSH_PROCESS_SCAN_OK\r\n"), "bundled child process keeps source busy");
        Check(!Matches(Path.Combine(root + "-other", "Electron.exe") + "\nDSH_PROCESS_SCAN_OK"), "sibling path prefix cannot match selected root");
        Check(!Matches("DSH_PROCESS_SCAN_OK"), "successful empty process scan means stopped");
        foreach (string output in new[] { "", "error DSH_PROCESS_SCAN_OK extra" })
        {
            try { Matches(output); throw new Exception("FAIL missing scan completion accepted"); }
            catch (IOException) { Check(true, "incomplete process scan fails closed"); }
        }
        string executable = Path.Combine(root, "Renamed Electron.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), executable);
        Check(!DshLocator.IsRootRunning(root), "live CIM scan detects stopped official fixture");
        using (var process = Process.Start(new ProcessStartInfo(executable, "/d /q")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!)
        {
            try { Check(!process.HasExited && DshLocator.IsRootRunning(root), "live CIM detects renamed executable in official source"); }
            finally { if (!process.HasExited) process.Kill(true); process.WaitForExit(); }
        }
        Check(!DshLocator.IsRootRunning(root), "live CIM detects fixture exit");
    }

    private static void LiveProbe()
    {
        string hint = Npm("live npm hint", "0.6.0");
        var installations = DshLocator.FindInstallations(hint);
        Check(installations[0].Root == hint && installations[0].Kind == DshInstallationKind.Npm, "public discovery honors npm hint");
        var report = EnvironmentProbe.Run(hint);
        Check(report.DshInstallations.Any(i => i.Root == hint) && report["dsh"].Path == hint, "full environment report retains hint and legacy component");
        foreach (string prefix in new[] { "MicrosoftCorporationII.WinAppRuntime.Main.1.8_", "Microsoft.WindowsAppRuntime.1.8_" })
            Check(Invoke<string>(typeof(EnvironmentProbe), "ParseWindowsAppRuntimePackageVersion", prefix + "8000.994.2142.0_x64__8wekyb3d8bbwe") == "8000.994.2142.0", "predecessor Runtime prefix retained: " + prefix);
        Check(Invoke<string?>(typeof(EnvironmentProbe), "ParseWindowsAppRuntimePackageVersion", "Microsoft.WinAppRuntime.DDLM_8000.994.2142.0_x64") == null, "unrelated Runtime package still rejected");
        Console.WriteLine("LIVE Runtime " + (EnvironmentProbe.FindWindowsAppRuntimeVersion() ?? "missing"));
        Console.WriteLine("LIVE " + string.Join("; ", report.DshInstallations.Select(i => $"{i.Kind} {i.Version} {i.Root}")));
    }

    private static void RuntimePackageFiltering()
    {
        const string main = "MicrosoftCorporationII.WinAppRuntime.Main.1.8_";
        const string framework = "Microsoft.WindowsAppRuntime.1.8_";
        string? Parse(string? name) => Invoke<string?>(typeof(EnvironmentProbe), "ParseWindowsAppRuntimePackageVersion", name);
        foreach (string name in new[] { main + "8000.994.2142.0_x64__8wekyb3d8bbwe",
            main + "8000.994.2142.0_neutral__8wekyb3d8bbwe", framework + "8000.994.2142.0_x64__8wekyb3d8bbwe" })
            Check(Parse(name) == "8000.994.2142.0", "Runtime package accepted: " + name);
        foreach (string? name in new[] { null, "", main + "8000.994.2142.0_x86__8wekyb3d8bbwe",
            framework + "9999.0.0.0_x86__8wekyb3d8bbwe", framework + "8000.994.2142.0_arm64__8wekyb3d8bbwe",
            framework + "8000.994.2142.0_neutral__8wekyb3d8bbwe", main + "8000.994.2142.0_arm64__8wekyb3d8bbwe",
            main + "1.8_x64__8wekyb3d8bbwe", main + "1.8.3_x64__8wekyb3d8bbwe", main + "bad_x64__8wekyb3d8bbwe",
            main + "8000.994.2142.0", main + "8000.994.2142.0_x64_8wekyb3d8bbwe",
            main + "8000.994.2142.0_x64__8wekyb3d8bbwe_extra", "Microsoft.WindowsAppRuntime.1.7_8000.994.2142.0_x64__8wekyb3d8bbwe" })
            Check(Parse(name) == null, "Runtime package rejected: " + (name ?? "null"));
        string? Scan(Func<int, IEnumerable<string>> readNames) => Invoke<string?>(typeof(EnvironmentProbe), "FindWindowsAppRuntimeVersionFromStores", readNames);
        string valid = framework + "8000.994.2142.0_x64__8wekyb3d8bbwe";
        for (int failedIndex = 0; failedIndex < 2; failedIndex++)
        {
            int unavailable = failedIndex;
            var attempted = new List<int>();
            string? actual = Scan(index =>
            {
                attempted.Add(index);
                if (index == unavailable) throw new UnauthorizedAccessException("injected registry denial");
                return new[] { valid };
            });
            Check(actual == "8000.994.2142.0" && attempted.SequenceEqual(new[] { 0, 1 }), "denied Runtime hive does not hide the other: " + unavailable);
        }
        Check(Scan(_ => throw new System.Security.SecurityException("injected registry denial")) == null, "both denied Runtime stores produce unknown version");
        Check(Scan(index => index == 0 ? new[] { valid, framework + "9999.0.0.0_x86__8wekyb3d8bbwe" }
            : new[] { main + "8000.995.1.0_neutral__8wekyb3d8bbwe" }) == "8000.995.1.0", "highest compatible version selected across both stores; higher x86 excluded");
        IEnumerable<string> PartialStore()
        {
            yield return valid;
            throw new IOException("injected enumeration failure");
        }
        Check(Scan(index => index == 0 ? PartialStore() : new[] { main + "8000.996.1.0_x64__8wekyb3d8bbwe" }) == "8000.996.1.0", "failed enumeration preserves previous results and scans next store");
    }

    private static void RegisteredOfficialInstallation(string expectedRoot)
    {
        if (!Path.IsPathFullyQualified(expectedRoot)) throw new ArgumentException("Expected official root must be absolute.");
        expectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedRoot));
        var registered = Invoke<IEnumerable<DshInstallation>>(typeof(DshLocator), "FindOfficialInstallations");
        Check(registered.Any(candidate => string.Equals(candidate.Root, expectedRoot, StringComparison.OrdinalIgnoreCase)
            && candidate.Version == "0.2.0-rc.2"), "real official root found specifically by uninstall registry scan");
        // No hint: this root must be discovered through real uninstall registration.
        var candidates = DshLocator.FindInstallations();
        var official = candidates.Where(candidate => candidate.Kind == DshInstallationKind.OfficialDesktop
            && string.Equals(candidate.Root, expectedRoot, StringComparison.OrdinalIgnoreCase)).ToList();
        Check(official.Count == 1, "real registered official root discovered without hint: " + expectedRoot);
        Check(official[0].Version == "0.2.0-rc.2", "real version-suffixed uninstall entry retains DisplayVersion");
        Console.WriteLine("LIVE REGISTERED " + official[0].Kind + " " + official[0].Version + " " + official[0].Root);
    }
}
