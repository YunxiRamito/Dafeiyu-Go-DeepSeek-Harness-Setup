using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using DshInstaller.Shared.Detection;

namespace DshInstaller.Shared
{
    /// <summary>找 DSH 装在哪。越快越准越好:先问运行中的进程,再看注册表,最后才扫盘。</summary>
    public static class DshLocator
    {
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
            try
            {
                string packageJson = Path.Combine(root, "node_modules", "@deepseek-ai", "dsh", "package.json");
                if (!File.Exists(packageJson))
                {
                    return null;
                }

                string text = File.ReadAllText(packageJson);
                Match match = Regex.Match(text, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> FindFromRunningProcess()
        {
            List<string> roots = new List<string>();
            try
            {
                // 用 PowerShell 一次问出所有 node 进程的命令行,不引 System.Management
                string script =
                    "Get-CimInstance Win32_Process -Filter \"Name='node.exe'\" | " +
                    "ForEach-Object { $_.ProcessId.ToString() + '|' + $_.CommandLine }";

                string output = PowerShellScript.Run(script, 12000);
                if (string.IsNullOrWhiteSpace(output))
                {
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
            }

            return roots;
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
