using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DshInstaller.Shared
{
    /// <summary>管理员权限相关的判断与自我提权。</summary>
    public static class ElevationHelper
    {
        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>能不能往这个目录里写东西(顺带判断权限够不够)。</summary>
        public static bool CanWriteTo(string directory, out string error)
        {
            error = null;
            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string probe = Path.Combine(directory, ".dsh-write-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// 用 runas 拉起自己的一个提权实例,命令行参数原样带上并追加 worker 标记。
        /// 成功返回 true,用户点了"否"返回 false。
        /// </summary>
        public static bool StartElevatedWorker(string argumentLine, out string error)
        {
            error = null;
            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    error = SharedText.T("无法获取当前进程路径", "Cannot determine the current process path");
                    return false;
                }

                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = (argumentLine ?? string.Empty) + " " + WellKnown.WorkerArgument,
                    UseShellExecute = true,
                    Verb = "runas",
                };

                System.Diagnostics.Process.Start(info);
                return true;
            }
            catch (Exception exception)
            {
                // 用户在 UAC 上点"否"也会走到这里
                error = exception.Message;
                return false;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int IsUserAnAdmin();
    }
}
