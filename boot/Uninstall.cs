// DSH 的独立卸载程序
//
// 为什么需要它(而不是让用户再跑一遍安装包):
//   安装器本体是引导程序解压到 %LOCALAPPDATA%\DeepSeekHarness\Boot 里的,
//   以前装完就清理掉了 —— 用户回头想卸载时,机器上**根本没有安装器可以跑**。
//   所以安装时会把这个 exe 和安装器一起留下,并注册到"应用和功能"。
//
// 它自己几乎不干实事,只做三件事:
//   1. 找到留着的那份 DSH-Installer.exe;
//   2. **把它复制到 %TEMP% 再运行** —— 直接从原目录跑的话,卸载时删不掉自己所在的目录
//      (文件被运行中的进程锁着),复制出去跑就绕开了这个死结;
//   3. 需要时申请一次提权,跑完把临时副本删掉。
//
// 编译(由 pack-release.ps1 自动执行):
//   csc /target:winexe /platform:anycpu /optimize+ /out:DSH-Uninstall.exe ^
//       /r:System.Windows.Forms.dll /r:System.Drawing.dll Uninstall.cs
//
// 命令行:所有参数原样转给安装器(会补上 --uninstall),
//         所以 "DSH-Uninstall.exe --silent" = 无人值守卸载。

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Uninstall
{
    private const string InstallerExeName = "DSH-Installer.exe";
    private const string ElevatedMarker = "--elevated";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Log("=== uninstaller started ===");
            Application.EnableVisualStyles();

            string home = FindInstallerHome();
            Log("installer home = " + (home == null ? "(找不到)" : home));

            if (home == null)
            {
                Fail(
                    "未找到安装程序的副本,无法继续卸载。\r\n\r\n"
                    + "请重新运行安装包(DSH-Installer-Setup.exe),"
                    + "该程序会恢复此文件,并可在向导中选择卸载。");
                return 2;
            }

            // 机器级安装要管理员才能删干净。已经提过权的不再提,免得来回弹。
            bool alreadyElevated = HasSwitch(args, ElevatedMarker);
            if (!alreadyElevated && NeedsElevation())
            {
                Log("需要管理员权限,申请提权");
                if (RelaunchElevated(args))
                {
                    return 0;
                }

                Log("提权失败或用户取消,按当前权限继续尝试");
            }

            // 复制到 %TEMP% 再跑:这样卸载过程可以放心删掉原目录
            string sandbox = Path.Combine(Path.GetTempPath(),
                "dsh-uninstall-" + Guid.NewGuid().ToString("N"));
            Log("copying installer to " + sandbox);
            if (CopyDirectory(home, sandbox) <= 0)
            {
                Fail("复制安装程序失败,无法继续卸载。\r\n\r\n位置:" + home);
                return 3;
            }

            string exe = Path.Combine(sandbox, InstallerExeName);
            if (!File.Exists(exe))
            {
                Fail("临时副本里没有 " + InstallerExeName + "。\r\n\r\n位置:" + exe);
                return 4;
            }

            string arguments = "--uninstall " + JoinArguments(RemoveSwitch(args, ElevatedMarker));
            Log("running: " + exe + " " + arguments);

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                WorkingDirectory = sandbox,
                UseShellExecute = false,
            };

            Process child = Process.Start(info);
            if (child == null)
            {
                Fail("无法启动卸载程序。");
                return 5;
            }

            // **交出去就退出,不要等它。**
            //
            // 为什么非这样不可:卸载过程中要删掉这个 exe —— 它就躺在 DSH 根目录里,
            // 而正在运行的 exe 锁着自己的镜像,删不掉。以前这里 WaitForExit,
            // 结果卸载永远删不掉自己,连带**整个安装目录**都留在硬盘上(实测:
            // "删除 DSH 本体与组件 -> Access to the path 'DSH-Uninstall.exe' is denied")。
            //
            // 现在:拉起安装器的卸载流程之后就退出,句柄随即释放,它删自己毫无障碍。
            // %TEMP% 里那份副本留给安装器自己收尾 —— 收不掉也只是临时目录里多几 MB。
            Log("installer spawned (pid " + child.Id + "), exiting without waiting");
            return 0;
        }        catch (Exception exception)
        {
            Log("FATAL: " + exception);
            Fail("卸载程序出错:" + exception.Message);
            return 1;
        }
    }

    // ------------------------------------------------------------------ 定位

    /// <summary>
    /// 按"最可靠"的顺序找那份留下的安装器:
    ///   1. 状态文件里记的 InstallerHome(现在指的是 &lt;启动器目录&gt;\.installer);
    ///   2. 自己旁边的 .installer 子目录(卸载程序就铺在启动器目录里);
    ///   3. 老版本的固定落脚点 %LOCALAPPDATA%\DeepSeekHarness\Boot(留着兼容);
    ///   4. 自己旁边(有人把卸载程序单独拷出来用)。
    /// </summary>
    private static string FindInstallerHome()
    {
        string self = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        string[] candidates = new string[]
        {
            ReadStateValue("InstallerHome"),
            Path.Combine(self, ".installer"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekHarness", "Boot"),
            self,
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string candidate = candidates[i];
            if (String.IsNullOrEmpty(candidate))
            {
                continue;
            }

            try
            {
                if (File.Exists(Path.Combine(candidate, InstallerExeName)))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    /// <summary>
    /// 从 installer-state.json 里抠一个字段。刻意不引 JSON 库:
    /// 这份文件是自己写的、格式固定,找 "字段名": "值" 就够,也少一个依赖。
    /// </summary>
    private static string ReadStateValue(string key)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekHarness", "installer-state.json");

            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            string needle = "\"" + key + "\"";
            int at = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return null;
            }

            int colon = json.IndexOf(':', at);
            int first = colon < 0 ? -1 : json.IndexOf('"', colon + 1);
            int last = first < 0 ? -1 : json.IndexOf('"', first + 1);
            if (first < 0 || last < 0)
            {
                return null;
            }

            string value = json.Substring(first + 1, last - first - 1);
            return value.Replace("\\\\", "\\");
        }
        catch
        {
            return null;
        }
    }

    private static bool NeedsElevation()
    {
        if (IsAdministrator())
        {
            return false;
        }

        // 装给"所有用户"时卸载项写在 HKLM 下,那一条只有管理员能删
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeepSeekHarness"))
            {
                if (key != null)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        // 开机自启是"最高权限"的计划任务,删它同样要管理员
        string scope = ReadStateValue("Scope");
        return String.Equals(scope, "machine", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RelaunchElevated(string[] args)
    {
        try
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = Assembly.GetExecutingAssembly().Location,
                Arguments = ElevatedMarker + " " + JoinArguments(args),
                UseShellExecute = true,
                Verb = "runas",
            };

            Process elevated = Process.Start(info);
            if (elevated == null)
            {
                return false;
            }

            elevated.WaitForExit();
            Log("elevated instance exited with " + elevated.ExitCode);
            return true;
        }
        catch (Exception exception)
        {
            Log("提权失败:" + exception.Message);
            return false;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ 小工具

    private static int CopyDirectory(string source, string target)
    {
        try
        {
            Directory.CreateDirectory(target);

            string[] files = Directory.GetFiles(source);
            int count = 0;
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    File.Copy(files[i], Path.Combine(target, Path.GetFileName(files[i])), true);
                    count++;
                }
                catch
                {
                }
            }

            // 子目录(zh-CN 之类)也要带上,不然界面会找不到资源
            string[] dirs = Directory.GetDirectories(source);
            for (int i = 0; i < dirs.Length; i++)
            {
                count += CopyDirectory(dirs[i], Path.Combine(target, Path.GetFileName(dirs[i])));
            }

            return count;
        }
        catch (Exception exception)
        {
            Log("复制目录失败:" + exception.Message);
            return 0;
        }
    }

    /// <summary>
    /// 删除临时副本。刚跑完的进程可能还没完全释放句柄,所以重试几次再放弃 ——
    /// 删不掉也不影响卸载结果,%TEMP% 迟早会被系统清。
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }

                return;
            }
            catch
            {
                Thread.Sleep(400);
            }
        }

        Log("临时目录没删掉:" + path);
    }

    private static bool HasSwitch(string[] args, string name)
    {
        if (args == null)
        {
            return false;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] RemoveSwitch(string[] args, string name)
    {
        if (args == null)
        {
            return new string[0];
        }

        System.Collections.Generic.List<string> kept = new System.Collections.Generic.List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (!String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(args[i]);
            }
        }

        return kept.ToArray();
    }

    private static string JoinArguments(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return String.Empty;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append('"').Append(args[i].Replace("\"", "\\\"")).Append('"');
        }

        return builder.ToString();
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "dsh-uninstall.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void Fail(string message)
    {
        try
        {
            MessageBox.Show(message, "DeepSeek Harness 卸载", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
        }
    }
}
