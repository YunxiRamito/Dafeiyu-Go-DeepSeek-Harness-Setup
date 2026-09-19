using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DshInstaller.Shared.Detection
{
    /// <summary>
    /// 整机环境体检。
    ///
    /// 两个坑必须避开:
    /// 1. 安装器是提权跑的,进程环境里的 PATH 是管理员那条,看不到用户自己装的东西,
    ///    所以 PATH 从注册表分别读 HKLM 和 HKCU 再拼。
    /// 2. Environment.OSVersion 会被应用清单的兼容性设置骗,报个假版本号,
    ///    所以系统版本走 RtlGetVersion。
    /// </summary>
    public static class EnvironmentProbe
    {
        private const int CommandTimeoutMs = 8000;

        public static ProbeReport Run(string dshRootHint = null, bool deepScan = false)
        {
            ProbeReport report = new ProbeReport();
            ReadWindowsVersion(report);
            report.IsElevated = ElevationHelper.IsElevated();

            string path = BuildPath();

            report.Components.Add(ProbeNode(path));
            string nodePath = report["node"].Path;

            report.Components.Add(ProbeNpm(nodePath));
            report.Components.Add(ProbePnpm(path));
            report.Components.Add(ProbeGit(path));
            report.Components.Add(ProbePython(path));
            report.Components.Add(ProbeDsh(dshRootHint));
            report.Components.Add(ProbeWindowsAppRuntime());
            report.Components.Add(ProbeDotNetDesktop());

            return report;
        }

/// <summary>
        /// 安装器自己装出来的便携组件候选路径。
        ///
        /// 为什么要单独找一遍:便携组件(尤其 Python 和 pnpm)故意**不写进 PATH** ——
        /// 免得盖掉用户自己装的那份同名工具。于是就会出现"装是装上了,检测却查不到"
        /// 的怪现象(实测踩过:装完 Python 再跑安装器,检测页仍显示"可选")。
        ///
        /// 顺序:安装状态文件里的组件目录 -> 用户默认布局 -> 全局默认布局。
        /// </summary>
        private static List<string> PortableCandidates(params string[] relative)
        {
            List<string> roots = new List<string>();

            try
            {
                InstallerState state = ConfigStore.Load();
                if (state != null)
                {
                    if (!string.IsNullOrWhiteSpace(state.ComponentsRoot))
                    {
                        roots.Add(state.ComponentsRoot);
                    }

                    if (!string.IsNullOrWhiteSpace(state.DshRoot))
                    {
                        roots.Add(Path.Combine(state.DshRoot, "components"));
                    }
                }
            }
            catch
            {
            }

            try
            {
                roots.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeek Harness", "components"));

                roots.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "DeepSeek Harness", "components"));
            }
            catch
            {
            }

            List<string> result = new List<string>();
            for (int i = 0; i < roots.Count; i++)
            {
                for (int j = 0; j < relative.Length; j++)
                {
                    try
                    {
                        result.Add(Path.Combine(roots[i], relative[j]));
                    }
                    catch
                    {
                    }
                }
            }

            return result;
        }

/// <summary>
        /// 把版本命令的输出收拾成"只有版本号"的样子。
        ///
        /// 各家的写法五花八门:node 是 "v22.23.2",git 是 "git version 2.50.1.windows.1",
        /// python 是 "Python 3.12.8"。界面上只想要版本号本身,所以在这里剥掉前缀。
        /// 有些程序版本写 stderr,RunVersion 已经把两路合并了,这里只取第一行有效内容。
        /// </summary>
        private static string CleanVersion(string raw, params string[] prefixes)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string value = raw.Trim();
            string[] lines = value.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length > 0)
                {
                    value = line;
                    break;
                }
            }

            for (int i = 0; i < prefixes.Length; i++)
            {
                if (!string.IsNullOrEmpty(prefixes[i])
                    && value.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(prefixes[i].Length).Trim();
                }
            }

            return value.Length == 0 ? null : value;
        }

        /// <summary>候选里第一个真实存在的文件;都没有返回 null。</summary>
        private static string FirstExisting(List<string> candidates)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                try
                {
                    if (File.Exists(candidates[i]))
                    {
                        return candidates[i];
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static void ReadWindowsVersion(ProbeReport report)
        {
            RtlOsVersionInfo info = new RtlOsVersionInfo();
            info.Size = Marshal.SizeOf(typeof(RtlOsVersionInfo));
            int status = RtlGetVersion(ref info);
            if (status != 0)
            {
                // 兜底,总比没有强
                report.WindowsBuild = Environment.OSVersion.Version.Build;
                report.WindowsName = SharedText.T("Windows（未知）", "Windows (unknown)");
            }
            else
            {
                report.WindowsBuild = info.BuildNumber;
                report.WindowsName = SharedText.T("Windows ", "Windows ") + info.MajorVersion + "." + info.MinorVersion;
            }

            report.IsWindowsSupported = report.WindowsBuild >= WellKnown.MinimumWindowsBuild;
            report.WindowsDisplayVersion = ReadDisplayVersion();
            if (!string.IsNullOrEmpty(report.WindowsDisplayVersion))
            {
                report.WindowsName += " (" + report.WindowsDisplayVersion + ")";
            }
        }

        private static string ReadDisplayVersion()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null)
                    {
                        return null;
                    }

                    object display = key.GetValue("DisplayVersion");
                    if (display != null)
                    {
                        return display.ToString();
                    }

                    object release = key.GetValue("ReleaseId");
                    return release != null ? release.ToString() : null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把机器级和用户级 PATH 拼起来,当前进程的 PATH 放最前面。</summary>
        public static string BuildPath()
        {
            List<string> parts = new List<string>();
            string current = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(current))
            {
                parts.Add(current);
            }

            AppendPath(parts, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
            AppendPath(parts, Registry.CurrentUser, "Environment");
            return string.Join(";", parts.ToArray());
        }

        private static void AppendPath(List<string> parts, RegistryKey root, string subKey)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(subKey))
                {
                    if (key == null)
                    {
                        return;
                    }

                    object value = key.GetValue("Path");
                    if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                    {
                        parts.Add(value.ToString());
                    }
                }
            }
            catch
            {
            }
        }

        // ---------------------------------------------------------------- 各组件

        private static ComponentStatus ProbeNode(string path)
        {
            ComponentStatus status = new ComponentStatus
            {
                Id = "node",
                DisplayName = "Node.js",
                Required = true,
                Installable = true,
            };

            string exe = FirstExisting(PortableCandidates(@"node\node.exe")) ?? FindOnPath(path, "node.exe", new[]
            {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
            });

            if (exe == null)
            {
                status.State = DetectState.Missing;
                status.Notes = SharedText.T("必选，为 DSH 的前置应用。", "Required — a prerequisite for DSH.");
                return status;
            }

            status.Path = exe;
            status.DetectedVersion = RunVersion(exe, "--version");

            // 光看文件在不够 —— 真跑一次版本命令,跑不通就说明这份装得不完整。
            status.DetectedVersion = CleanVersion(status.DetectedVersion, "v");
            if (string.IsNullOrWhiteSpace(status.DetectedVersion))
            {
                status.State = DetectState.Missing;
                status.Notes = SharedText.T("已找到 node.exe，但执行 --version 无输出，该安装可能不完整。",
                    "node.exe was found, but running --version produced no output; this installation may be incomplete.");
                return status;
            }
            Version version = ParseVersion(status.DetectedVersion);
            Version minimum = ParseVersion(WellKnown.MinimumNodeVersion);
            status.VersionOk = version != null && minimum != null && version >= minimum;
            status.State = status.VersionOk ? DetectState.Ready : DetectState.Outdated;
            status.Notes = status.VersionOk
                ? SharedText.T("版本达标。", "Version requirement met.")
                : SharedText.T("低于 " + WellKnown.MinimumNodeVersion + "，DSH 不一定能正常运行。",
                               "Lower than " + WellKnown.MinimumNodeVersion + "; DSH may not run correctly.");
            return status;
        }

        private static ComponentStatus ProbeNpm(string nodePath)
        {
            ComponentStatus status = new ComponentStatus
            {
                Id = "npm",
                DisplayName = "npm",
                Required = true,
                Installable = true,
            };

            // npm 跟着 Node 走,直接找同目录的 npm.cmd
            if (!string.IsNullOrEmpty(nodePath))
            {
                string dir = Path.GetDirectoryName(nodePath);
                string npmCmd = Path.Combine(dir, "npm.cmd");
                if (File.Exists(npmCmd))
                {
                    status.Path = npmCmd;
                    status.DetectedVersion = RunVersion("cmd.exe", "/c \"" + npmCmd + "\" --version");
                    status.VersionOk = !string.IsNullOrEmpty(status.DetectedVersion);
                    status.State = status.VersionOk ? DetectState.Ready : DetectState.Unknown;
                    status.Notes = SharedText.T("随 Node 一并安装。", "Installed together with Node.");
                    return status;
                }
            }

            status.State = DetectState.Missing;
            status.Notes = SharedText.T("必选。", "Required.");
            return status;
        }

        private static ComponentStatus ProbePnpm(string path)
        {
            ComponentStatus status = new ComponentStatus
            {
                Id = "pnpm",
                DisplayName = "pnpm",
                Required = false,
                Installable = true,
            };

            string exe = FirstExisting(PortableCandidates(@"pnpm\pnpm.exe"))
                ?? FindOnPath(path, "pnpm.exe", null)
                ?? FindOnPath(path, "pnpm.cmd", null);
            if (exe == null)
            {
                status.State = DetectState.Optional;
                status.Notes = SharedText.T("可选，安装插件时使用。", "Optional. Used when installing plug-ins.");
                return status;
            }

            status.Path = exe;
            status.DetectedVersion = RunVersion(exe, "--version");

            // 真跑一次版本命令:文件在但跑不起来(解压不全 / 缺运行库 / 被杀软隔离)不能算就绪
            status.DetectedVersion = CleanVersion(status.DetectedVersion);
            if (string.IsNullOrWhiteSpace(status.DetectedVersion))
            {
                status.State = DetectState.Optional;
                status.Notes = SharedText.T("已找到，但执行 --version 无输出，可能未完整安装。",
                    "Found, but running --version produced no output; the installation may be incomplete.");
                return status;
            }
            status.VersionOk = true;
            status.State = DetectState.Ready;
            status.Notes = SharedText.T("已就绪。", "Ready.");
            return status;
        }

        private static ComponentStatus ProbeGit(string path)
        {
            ComponentStatus status = new ComponentStatus
            {
                Id = "git",
                DisplayName = "Git",
                Required = false,
                Installable = true,
            };

            string exe = FirstExisting(PortableCandidates(@"git\cmd\git.exe")) ?? FindOnPath(path, "git.exe", new[]
            {
                @"C:\Program Files\Git\cmd\git.exe",
                @"C:\Program Files (x86)\Git\cmd\git.exe",
            });

            if (exe == null)
            {
                status.State = DetectState.Optional;
                status.Notes = SharedText.T("可选，从 Github 上安装插件时使用。", "Optional. Used when installing plug-ins from GitHub.");
                return status;
            }

            status.Path = exe;
            status.DetectedVersion = RunVersion(exe, "--version");

            // 真跑一次版本命令:文件在但跑不起来(解压不全 / 缺运行库 / 被杀软隔离)不能算就绪
            status.DetectedVersion = CleanVersion(status.DetectedVersion, "git version");
            if (string.IsNullOrWhiteSpace(status.DetectedVersion))
            {
                status.State = DetectState.Optional;
                status.Notes = SharedText.T("已找到，但执行 --version 无输出，可能未完整安装。",
                    "Found, but running --version produced no output; the installation may be incomplete.");
                return status;
            }
            status.VersionOk = true;
            status.State = DetectState.Ready;
            status.Notes = SharedText.T("已就绪。", "Ready.");
            return status;
        }

        /// <summary>
        /// 找 Python。找的顺序有讲究:
        ///   1. 本安装器装的便携版(故意不在 PATH 里,所以先查安装记录);
        ///   2. PATH,但跳过 WindowsApps 里的商店占位程序(调它会弹应用商店,不是真 Python);
        ///   3. 注册表 Software\Python\PythonCore\*\InstallPath(官方安装器会登记);
        ///      登记路径必须验证 exe 真的存在 —— 卸载后残留的登记不能当数(实测本机就有这种残留);
        ///   4. 常见安装目录。
        /// </summary>
        private static string FindPython(string path)
        {
            string exe = FirstExisting(PortableCandidates(@"python\python.exe"));
            if (exe != null)
            {
                return exe;
            }

            exe = FindOnPathExcluding(path, "python.exe", @"\WindowsApps\");
            if (exe == null)
            {
                exe = FindOnPathExcluding(path, "python3.exe", @"\WindowsApps\");
            }

            if (exe != null)
            {
                return exe;
            }

            exe = FindRegisteredPython();
            if (exe != null)
            {
                return exe;
            }

            List<string> guesses = new List<string>();
            try
            {
                string localPrograms = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Python");
                if (Directory.Exists(localPrograms))
                {
                    string[] dirs = Directory.GetDirectories(localPrograms, "Python3*");
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        guesses.Add(Path.Combine(dirs[i], "python.exe"));
                    }
                }

                guesses.Add(@"C:\Python312\python.exe");
                guesses.Add(@"C:\Python311\python.exe");
                guesses.Add(@"C:\Python310\python.exe");
            }
            catch
            {
            }

            return FirstExisting(guesses);
        }

        /// <summary>在注册表里找 Python 的安装位置(官方安装器登记在这)。</summary>
        private static string FindRegisteredPython()
        {
            string[] roots = new string[]
            {
                @"Software\Python\PythonCore",
                @"Software\WOW6432Node\Python\PythonCore",
            };

            for (int r = 0; r < roots.Length; r++)
            {
                try
                {
                    Microsoft.Win32.RegistryKey root =
                        Microsoft.Win32.Registry.CurrentUser.OpenSubKey(roots[r]);
                    if (root == null)
                    {
                        root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(roots[r]);
                    }

                    if (root == null)
                    {
                        continue;
                    }

                    using (root)
                    {
                        string[] versions = root.GetSubKeyNames();
                        for (int v = 0; v < versions.Length; v++)
                        {
                            using (Microsoft.Win32.RegistryKey install =
                                root.OpenSubKey(versions[v] + @"\InstallPath"))
                            {
                                if (install == null)
                                {
                                    continue;
                                }

                                string dir = install.GetValue(null) as string;
                                if (string.IsNullOrWhiteSpace(dir))
                                {
                                    continue;
                                }

                                string candidate = Path.Combine(
                                    dir.Trim().TrimEnd('\\'), "python.exe");

                                // 卸载后注册表常有残留 —— 必须验证文件真的在
                                if (File.Exists(candidate))
                                {
                                    return candidate;
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static ComponentStatus ProbePython(string path)
        {
            ComponentStatus status = new ComponentStatus
            {
                Id = "python",
                DisplayName = "Python",
                Required = false,
                Installable = true,
            };

            string exe = FindPython(path);

            if (exe == null)
            {
                status.State = DetectState.Optional;
                status.Notes = SharedText.T("可选，使用 DSH 编程时使用。", "Optional. Used when programming DSH.");
                return status;
            }

            status.Path = exe;
            status.DetectedVersion = CleanVersion(RunVersion(exe, "--version"), "Python");
            if (string.IsNullOrWhiteSpace(status.DetectedVersion))
            {
                status.State = DetectState.Optional;
                status.Notes = SharedText.T("已找到，但执行 --version 无输出，可能未完整安装。",
                    "Found, but running --version produced no output; the installation may be incomplete.");
                return status;
            }

            status.VersionOk = true;
            status.State = DetectState.Ready;
            status.Notes = SharedText.T("已就绪。", "Ready.");
            return status;
        }
        private static ComponentStatus ProbeDsh(string rootHint)
        {
            ComponentStatus status = new ComponentStatus
            {
                Id = "dsh",
                DisplayName = SharedText.T("DeepSeek Harness 本体", "DeepSeek Harness core"),
                Required = true,
                Installable = true,
            };

            List<string> roots = new List<string>();
            if (!string.IsNullOrWhiteSpace(rootHint))
            {
                roots.Add(rootHint);
            }

            foreach (string candidate in DshLocator.FindCandidates())
            {
                if (!roots.Contains(candidate))
                {
                    roots.Add(candidate);
                }
            }

            for (int index = 0; index < roots.Count; index++)
            {
                string root = roots[index];
                if (DshLocator.LooksLikeDshRoot(root))
                {
                    status.Path = root;
                    status.DetectedVersion = DshLocator.ReadPackageVersion(root);
                    status.VersionOk = true;
                    status.State = DetectState.Ready;
                    status.Notes = SharedText.T("已装在 ", "Installed at ") + root + SharedText.T("。", ".");
                    return status;
                }
            }

            status.State = DetectState.Missing;
            status.Notes = SharedText.T("未发现 DSH，即将安装。", "DSH was not found and will be installed.");
            return status;
        }

        private static ComponentStatus ProbeWindowsAppRuntime()
        {
            ComponentStatus status = new ComponentStatus
            {
                Id = "winappruntime",
                DisplayName = "Windows App Runtime 1.8",
                Required = true,
                Installable = true,
            };

            string version = FindWindowsAppRuntimeVersion();
            if (version == null)
            {
                status.State = DetectState.Missing;
                status.Notes = SharedText.T("必选。", "Required.");
                return status;
            }

            // 装了 ≠ 能用。旧版本的 WinAppRuntime 一样会让安装器起不来,弹的还是
            // "Required components of the Windows App Runtime are missing"(实测踩过:
            // 自动装成了 8000.921.1539.0,而 WindowsAppSDK 1.8.260804001 要 8000.946.1701.0)。
            if (!RuntimeProbe.VersionAtLeast(version, WellKnown.WindowsAppRuntimeMinimumVersion))
            {
                status.DetectedVersion = version;
                status.VersionOk = false;
                status.State = DetectState.Missing;
                status.Notes = SharedText.T(
                    "已安装 " + version + ",版本过低,需要 " + WellKnown.WindowsAppRuntimeMinimumVersion + " 或更高,将更新。",
                    "Version " + version + " is too old; "
                    + WellKnown.WindowsAppRuntimeMinimumVersion + " or later is required and will be installed.");
                return status;
            }

            status.DetectedVersion = version;
            status.VersionOk = true;
            status.State = DetectState.Ready;
            status.Notes = SharedText.T("已就绪。", "Ready.");
            return status;
        }

        /// <summary>在 Appx 存储里找 WinAppRuntime 1.8 及其版本号。</summary>
        public static string FindWindowsAppRuntimeVersion()
        {
            const string keyPath =
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null)
                    {
                        return null;
                    }

                    const string prefix = "MicrosoftCorporationII.WinAppRuntime.Main.1.8_";
                    foreach (string name in key.GetSubKeyNames())
                    {
                        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string remainder = name.Substring(prefix.Length);
                            int end = remainder.IndexOf('_');
                            return end >= 0 ? remainder.Substring(0, end) : remainder;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static ComponentStatus ProbeDotNetDesktop()
        {
            ComponentStatus status = new ComponentStatus
            {
                Id = "dotnet8",
                DisplayName = ".NET 8 桌面运行时",
                Required = true,
                Installable = true,
            };

            string version = FindDotNetDesktopVersion(8);
            if (version == null)
            {
                status.State = DetectState.Missing;
                status.Notes = SharedText.T("必选。", "Required.");
                return status;
            }

            status.DetectedVersion = version;
            status.VersionOk = true;
            status.State = DetectState.Ready;
            status.Notes = SharedText.T("已就绪。", "Ready.");
            return status;
        }

        public static string FindDotNetDesktopVersion(int major)
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"dotnet\shared\Microsoft.WindowsDesktop.App");
                if (!Directory.Exists(root))
                {
                    return null;
                }

                foreach (string dir in Directory.GetDirectories(root))
                {
                    Version parsed;
                    string name = Path.GetFileName(dir);
                    if (Version.TryParse(name, out parsed) && parsed.Major == major)
                    {
                        return name;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        // ---------------------------------------------------------------- 小工具

        /// <summary>
        /// 在 PATH 里找,但跳过落在某个子串之下的结果。
        ///
        /// 专门为 Python 准备:WindowsApps 里那个 python.exe 是应用商店的占位程序,
        /// 调它会弹商店。以前找到它就放弃了,于是本机装了 Python 却检测不到。
        /// 现在跳过它继续往后找。
        /// </summary>
        public static string FindOnPathExcluding(string path, string fileName, string excludeSubstring)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string[] parts = path.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string dir = parts[i].Trim();
                if (dir.Length == 0)
                {
                    continue;
                }

                try
                {
                    string candidate = Path.Combine(dir, fileName);
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(excludeSubstring)
                        && candidate.IndexOf(excludeSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    return candidate;
                }
                catch
                {
                }
            }

            return null;
        }

        public static string FindOnPath(string path, string fileName, string[] extraCandidates)
        {
            if (!string.IsNullOrEmpty(path))
            {
                string[] dirs = path.Split(';');
                for (int index = 0; index < dirs.Length; index++)
                {
                    string dir = dirs[index].Trim().Trim('"');
                    if (dir.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        string full = Path.Combine(dir, fileName);
                        if (File.Exists(full))
                        {
                            return full;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (extraCandidates != null)
            {
                for (int index = 0; index < extraCandidates.Length; index++)
                {
                    if (File.Exists(extraCandidates[index]))
                    {
                        return extraCandidates[index];
                    }
                }
            }

            return null;
        }

        public static string RunVersion(string exe, string arguments)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using (Process process = Process.Start(info))
                {
                    if (process == null)
                    {
                        return null;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(CommandTimeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }

                    string text = string.IsNullOrWhiteSpace(output) ? error : output;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return null;
                    }

                    return FirstLine(text).Trim();
                }
            }
            catch
            {
                return null;
            }
        }

        private static string FirstLine(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(lines[index]))
                {
                    return lines[index];
                }
            }

            return text;
        }

        public static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(text, @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!match.Success)
            {
                return null;
            }

            int major = int.Parse(match.Groups[1].Value);
            int minor = int.Parse(match.Groups[2].Value);
            int build = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            return new Version(major, minor, build);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RtlOsVersionInfo
        {
            public int Size;
            public int MajorVersion;
            public int MinorVersion;
            public int BuildNumber;
            public int PlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string CSDVersion;
        }

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RtlGetVersion(ref RtlOsVersionInfo versionInfo);
    }
}