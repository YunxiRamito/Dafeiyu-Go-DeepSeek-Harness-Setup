using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using DshInstaller.Shared.Detection;

namespace DshInstaller.Shared
{
    /// <summary>找 DSH 装在哪。越快越准越好:先问运行中的进程,再看注册表,最后才扫盘。</summary>
    public static class DshLocator
    {
        private const string OfficialCliMarker = @"resources\runtime\cli\bin\dsh.cmd";
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private static readonly Regex OfficialDisplayName = new Regex(
            @"\ADeepSeek Harness(?: (?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"
            + @"(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?"
            + @"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)?\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>All validated installations, hint first. Does not select a migration source.</summary>
        public static List<DshInstallation> FindInstallations(string rootHint = null)
        {
            return CollectInstallations(new[] { rootHint }.Concat(FindCandidates()), FindOfficialInstallations());
        }

        private static List<DshInstallation> CollectInstallations(
            IEnumerable<string> roots, IEnumerable<DshInstallation> official)
        {
            List<DshInstallation> result = new List<DshInstallation>();
            Dictionary<string, int> indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            // Registry metadata wins over a hint's CLI version for the same desktop root.
            foreach (DshInstallation candidate in roots.Select(root => InspectInstallation(root)).Concat(official))
            {
                if (candidate == null) continue;
                if (indices.TryGetValue(candidate.Root, out int index))
                {
                    if (!string.IsNullOrEmpty(candidate.Version)
                        && (string.IsNullOrEmpty(result[index].Version) || candidate.Kind == DshInstallationKind.OfficialDesktop))
                        result[index] = candidate;
                    continue;
                }
                indices.Add(candidate.Root, result.Count);
                result.Add(candidate);
            }
            return result;
        }

        /// <summary>Validate a root without running any executables. Invalid roots return null.</summary>
        public static DshInstallation InspectInstallation(string root, string displayVersion = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root)) return null;
                string path = Environment.ExpandEnvironmentVariables(root.Trim().Trim('"'));
                if (!Path.IsPathFullyQualified(path)) return null;
                path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                string cli = Path.Combine(path, OfficialCliMarker);
                if (IsOfficialDesktopRoot(path))
                {
                    string cliRoot = Path.Combine(path, "resources", "runtime", "cli");
                    return new DshInstallation
                    {
                        Kind = DshInstallationKind.OfficialDesktop,
                        Root = path,
                        DataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"),
                        CliPath = cli,
                        Version = string.IsNullOrWhiteSpace(displayVersion)
                            ? ReadPackageVersion(cliRoot) ?? ReadJsonVersion(Path.Combine(cliRoot, "package.json"))
                            : displayVersion.Trim(),
                    };
                }

                // Partial desktop packages must not fall through to ordinary-install reuse.
                if (File.Exists(cli) || File.Exists(Path.Combine(path, "resources", "app.asar"))) return null;
                if (!LooksLikeDshRoot(path)) return null;
                return new DshInstallation
                {
                    Kind = DshInstallationKind.Npm,
                    Root = path,
                    DataHome = Path.Combine(path, ".dsh"),
                    CliPath = Path.Combine(path, WellKnown.DshMarker),
                    Version = ReadPackageVersion(path),
                };
            }
            catch
            {
                return null;
            }
        }

        public static bool IsOfficialDesktopRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                if (!Path.IsPathFullyQualified(path)) return false;
                return File.Exists(Path.Combine(path, OfficialCliMarker))
                    && File.Exists(Path.Combine(path, "resources", "app.asar"));
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<DshInstallation> FindOfficialInstallations()
        {
            List<DshInstallation> result = new List<DshInstallation>();
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
                    using RegistryKey uninstall = root.OpenSubKey(UninstallKey);
                    if (uninstall == null) continue;
                    foreach (string name in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using RegistryKey entry = uninstall.OpenSubKey(name);
                            if (entry == null) continue;
                            DshInstallation candidate = ReadUninstallEntry(
                                entry.GetValue("DisplayName") as string,
                                entry.GetValue("InstallLocation") as string,
                                entry.GetValue("DisplayIcon") as string,
                                entry.GetValue("UninstallString") as string,
                                entry.GetValue("DisplayVersion") as string);
                            if (candidate != null) result.Add(candidate);
                        }
                        catch
                        {
                            // An inaccessible entry must not hide the remaining installations.
                        }
                    }
                }
                catch
                {
                    // Continue with the other hive/view when a registry store is unavailable.
                }
            }
            return result;
        }

        private static DshInstallation ReadUninstallEntry(string displayName, string installLocation,
            string displayIcon, string uninstallString, string displayVersion)
        {
            if (string.IsNullOrWhiteSpace(displayName) || !OfficialDisplayName.IsMatch(displayName.Trim())) return null;
            foreach (string root in new[] { installLocation, CommandDirectory(displayIcon, true), CommandDirectory(uninstallString, false) })
            {
                DshInstallation candidate = InspectInstallation(root, displayVersion);
                if (candidate?.Kind == DshInstallationKind.OfficialDesktop) return candidate;
            }
            return null;
        }

        private static string CommandDirectory(string command, bool icon)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            try
            {
                string value = Environment.ExpandEnvironmentVariables(command.Trim());
                if (value.StartsWith('"'))
                {
                    int closingQuote = value.IndexOf('"', 1);
                    if (closingQuote < 0) return null;
                    value = value.Substring(1, closingQuote - 1);
                }
                else if (icon)
                {
                    int comma = value.LastIndexOf(',');
                    if (comma >= 0 && int.TryParse(value.Substring(comma + 1).Trim(), out _))
                        value = value.Substring(0, comma).TrimEnd();
                }
                else
                {
                    Match executable = Regex.Match(value, @"^(.+?\.exe)(?=\s|$)", RegexOptions.IgnoreCase);
                    if (!executable.Success) return null;
                    value = executable.Groups[1].Value;
                }
                return Path.IsPathFullyQualified(value) ? Path.GetDirectoryName(value) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>按"最可能"到"最不可能"的顺序给出候选根目录。</summary>
        public static List<string> FindCandidates()
        {
            List<string> result = new List<string>();
            Action<string> add = delegate(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                string trimmed = path.TrimEnd('\\');
                if (!result.Contains(trimmed))
                {
                    result.Add(trimmed);
                }
            };

            // 1) 正在跑的 DSH 进程
            foreach (string running in FindFromRunningProcess())
            {
                add(running);
            }

            // 2) 注册表启动项里的启动器
            foreach (string fromRun in FindFromRunKey())
            {
                add(fromRun);
            }

            // 3) 安装器自己记过的安装信息
            add(ConfigStore.ReadDshRoot());

            // 4) 常见位置
            foreach (string drive in EnumerateDriveRoots())
            {
                add(Path.Combine(drive, "DeepSeek DSH"));
                add(Path.Combine(drive, "DeepSeekHarness"));
                add(Path.Combine(drive, "DeepSeek Harness"));
            }

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
            {
                add(Path.Combine(profile, "DeepSeek DSH"));
                add(Path.Combine(profile, "Documents", "DeepSeek DSH"));
                add(Path.Combine(profile, "Desktop", "DeepSeek DSH"));
            }

            // 5) 有限深度扫一层(只扫盘根下的直接子目录)
            foreach (string drive in EnumerateDriveRoots())
            {
                try
                {
                    foreach (string dir in Directory.GetDirectories(drive))
                    {
                        string name = Path.GetFileName(dir);
                        if (name.IndexOf("dsh", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            add(dir);
                        }
                    }
                }
                catch
                {
                }
            }

            return result;
        }

        public static bool LooksLikeDshRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            try
            {
                return File.Exists(Path.Combine(root, WellKnown.DshMarker));
            }
            catch
            {
                return false;
            }
        }

        public static string ReadPackageVersion(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return null;
            return ReadJsonVersion(Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "package.json"));
        }

        private static string ReadJsonVersion(string packageJson)
        {
            try
            {
                if (!File.Exists(packageJson))
                {
                    return null;
                }

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(packageJson));
                return document.RootElement.TryGetProperty("version", out JsonElement version)
                    && version.ValueKind == JsonValueKind.String ? version.GetString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> FindFromRunningProcess(bool requireSuccessfulScan = false)
        {
            List<string> roots = new List<string>();
            try
            {
                // 用 PowerShell 一次问出所有 node 进程的命令行,不引 System.Management
                string script =
                    "$ErrorActionPreference='Stop'; Get-CimInstance Win32_Process -Filter \"Name='node.exe'\" | " +
                    "ForEach-Object { $_.ProcessId.ToString() + '|' + $_.CommandLine }; Write-Output 'DSH_PROCESS_SCAN_OK'";

                string output = PowerShellScript.Run(script, 12000);
                if (string.IsNullOrWhiteSpace(output) || !output.Contains("DSH_PROCESS_SCAN_OK", StringComparison.Ordinal))
                {
                    if (requireSuccessfulScan) throw new IOException("无法确认 DSH 服务是否已停止，请停止目标服务后重试。");
                    return roots;
                }

                foreach (string line in output.Replace("\r\n", "\n").Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('|');
                    if (separator < 0)
                    {
                        continue;
                    }

                    string commandLine = line.Substring(separator + 1);
                    Match match = Regex.Match(
                        commandLine,
                        "\"([^\"]*@deepseek-ai[\\\\/]dsh[\\\\/]lib[\\\\/]bin\\.js)\"",
                        RegexOptions.IgnoreCase);
                    if (!match.Success)
                    {
                        match = Regex.Match(
                            commandLine,
                            "(\\S*@deepseek-ai[\\\\/]dsh[\\\\/]lib[\\\\/]bin\\.js)",
                            RegexOptions.IgnoreCase);
                    }

                    if (!match.Success)
                    {
                        continue;
                    }

                    string suffix = "\\" + WellKnown.DshMarker;
                    string bin = match.Groups[1].Value;
                    if (bin.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        roots.Add(bin.Substring(0, bin.Length - suffix.Length));
                    }
                }
            }
            catch
            {
                if (requireSuccessfulScan) throw;
            }

            return roots;
        }

        public static bool IsRootRunning(string dshRoot)
        {
            if (String.IsNullOrWhiteSpace(dshRoot)) return false;
            string root = Path.GetFullPath(dshRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (IsOfficialDesktopRoot(root))
            {
                // Electron renderers and the bundled CLI/node may all keep the source in use.
                string script = "$ErrorActionPreference='Stop'; Get-CimInstance Win32_Process | "
                    + "ForEach-Object { if ($_.ExecutablePath) { $_.ExecutablePath } }; Write-Output 'DSH_PROCESS_SCAN_OK'";
                return OfficialProcessScanMatches(root, PowerShellScript.Run(script, 12000));
            }
            return FindFromRunningProcess(true).Any(candidate => String.Equals(
                Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root, StringComparison.OrdinalIgnoreCase));
        }

        private static bool OfficialProcessScanMatches(string root, string output)
        {
            string[] lines = (output ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            if (!lines.Any(line => line.Trim() == "DSH_PROCESS_SCAN_OK"))
                throw new IOException("Unable to confirm that the official DSH source has stopped. Stop it and retry.");
            string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            foreach (string line in lines)
            {
                string executable = line.Trim();
                if (!Path.IsPathFullyQualified(executable)) continue;
                if (Path.GetFullPath(executable).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static IEnumerable<string> FindFromRunKey()
        {
            List<string> roots = new List<string>();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(WellKnown.RegistryRunKey))
                {
                    if (key == null)
                    {
                        return roots;
                    }

                    object value = key.GetValue(WellKnown.RunValueName);
                    if (value == null)
                    {
                        return roots;
                    }

                    string target = value.ToString().Trim('"');
                    if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                    {
                        target = ShellLink.ReadTarget(target) ?? target;
                    }

                    // 目标通常是 <root>\DeepSeek Harness\DeepSeek Harness.exe
                    string dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        string parent = Path.GetDirectoryName(dir);
                        if (parent != null && LooksLikeDshRoot(parent))
                        {
                            roots.Add(parent);
                        }
                    }
                }
            }
            catch
            {
            }

            return roots;
        }

        private static IEnumerable<string> EnumerateDriveRoots()
        {
            List<string> drives = new List<string>();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        {
                            drives.Add(drive.RootDirectory.FullName.TrimEnd('\\'));
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return drives;
        }
    }
}
