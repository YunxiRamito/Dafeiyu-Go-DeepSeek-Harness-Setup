using System;
using System.Collections.Generic;
using System.IO;

namespace DshInstaller.Shared.Install
{
    /// <summary>一次 PATH 改动的结果。</summary>
    public sealed class PathChangeResult
    {
        /// <summary>是成功还是失败。</summary>
        public bool Ok { get; set; }

        /// <summary>事实上没动(本来就有,或者本来就没有)。</summary>
        public bool NoChange { get; set; }

        /// <summary>给用户/日志看的一句话。</summary>
        public string Message { get; set; }

        public static PathChangeResult Unchanged(string message)
        {
            return new PathChangeResult { Ok = true, NoChange = true, Message = message };
        }

        public static PathChangeResult Done(string message)
        {
            return new PathChangeResult { Ok = true, Message = message };
        }

        public static PathChangeResult Failed(string message)
        {
            return new PathChangeResult { Ok = false, Message = message };
        }
    }

    /// <summary>
    /// 环境变量 PATH 的读写。
    ///
    /// 为什么要小心:
    ///   - 用户 PATH 上限大约 2047 字符,超了 SetEnvironmentVariable 会直接抛异常,
    ///     而且**会把整条 PATH 写坏**,所以必须先算长度再写。
    ///   - PATH 里常见重复项和大小写差异,查重要按不区分大小写、并去掉尾部反斜杠。
    ///   - 写 PATH 之后已经在跑的进程不会更新,要广播 WM_SETTINGCHANGE 才会被新进程看到。
    /// </summary>
    public static class PathEditor
    {
        /// <summary>PATH 的长度上限,留一点余量。</summary>
        public const int MaxPathLength = 2040;

        private const string UserScope = "User";
        private const string MachineScope = "Machine";

        /// <summary>把目录加进用户 PATH。已存在就什么都不做。</summary>
        public static PathChangeResult AddToUserPath(string directory)
        {
            return Add(directory, UserScope);
        }

        /// <summary>把目录加进系统 PATH(需要管理员)。</summary>
        public static PathChangeResult AddToMachinePath(string directory)
        {
            return Add(directory, MachineScope);
        }

        public static PathChangeResult RemoveFromUserPath(string directory)
        {
            return Remove(directory, UserScope);
        }

        public static PathChangeResult RemoveFromMachinePath(string directory)
        {
            return Remove(directory, MachineScope);
        }

        /// <summary>这个目录在不在用户 PATH 里。</summary>
        public static bool IsOnUserPath(string directory)
        {
            return Contains(Read(UserScope), directory);
        }

        public static bool IsOnMachinePath(string directory)
        {
            return Contains(Read(MachineScope), directory);
        }

        /// <summary>读出某一段 PATH(原始字符串)。</summary>
        public static string Read(string scope)
        {
            try
            {
                string value = Environment.GetEnvironmentVariable(
                    "Path",
                    scope == MachineScope ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User);
                return value ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static PathChangeResult Add(string directory, string scope)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return PathChangeResult.Failed("目录为空");
            }

            string normalized = Normalize(directory);
            if (!Directory.Exists(normalized))
            {
                return PathChangeResult.Failed("目录不存在:" + normalized);
            }

            string current = Read(scope);
            if (Contains(current, normalized))
            {
                return PathChangeResult.Unchanged("已经在 PATH 里:" + normalized);
            }

            string combined = string.IsNullOrWhiteSpace(current)
                ? normalized
                : current.TrimEnd(';') + ";" + normalized;

            if (combined.Length > MaxPathLength)
            {
                return PathChangeResult.Failed(
                    "PATH 太长(" + combined.Length + " > " + MaxPathLength + "),没有写入以免写坏;"
                    + "可以先把不用的项清理掉再加");
            }

            try
            {
                Environment.SetEnvironmentVariable(
                    "Path",
                    combined,
                    scope == MachineScope ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User);
                BroadcastChange();
                return PathChangeResult.Done("已加入 PATH:" + normalized);
            }
            catch (Exception exception)
            {
                return PathChangeResult.Failed("写入 PATH 失败:" + exception.Message);
            }
        }

        private static PathChangeResult Remove(string directory, string scope)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return PathChangeResult.Unchanged("目录为空");
            }

            string normalized = Normalize(directory);
            string current = Read(scope);
            if (!Contains(current, normalized))
            {
                return PathChangeResult.Unchanged("PATH 里本来就没有:" + normalized);
            }

            List<string> kept = new List<string>();
            string[] parts = current.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (string.Equals(Normalize(part), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kept.Add(part);
            }

            try
            {
                Environment.SetEnvironmentVariable(
                    "Path",
                    string.Join(";", kept.ToArray()),
                    scope == MachineScope ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User);
                BroadcastChange();
                return PathChangeResult.Done("已从 PATH 移除:" + normalized);
            }
            catch (Exception exception)
            {
                return PathChangeResult.Failed("写入 PATH 失败:" + exception.Message);
            }
        }

        private static bool Contains(string pathValue, string directory)
        {
            if (string.IsNullOrEmpty(pathValue))
            {
                return false;
            }

            string wanted = Normalize(directory);
            string[] parts = pathValue.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (string.Equals(Normalize(part), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 拿环境变量占位符写的项(例如 %USERPROFILE%\bin)也展开比一次
                try
                {
                    string expanded = Environment.ExpandEnvironmentVariables(part);
                    if (string.Equals(Normalize(expanded), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Trim().TrimEnd('\\', '/');
        }

        /// <summary>
        /// 通知正在运行的程序"环境变量变了"。
        /// 不广播的话,已经开着的 Explorer / 终端不会看到新 PATH,得注销才生效。
        /// </summary>
        private static void BroadcastChange()
        {
            try
            {
                IntPtr result;
                SendMessageTimeout(
                    new IntPtr(0xFFFF), // HWND_BROADCAST
                    0x001A,             // WM_SETTINGCHANGE
                    IntPtr.Zero,
                    "Environment",
                    0x0002,             // SMTO_ABORTIFHUNG
                    3000,
                    out result);
            }
            catch
            {
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint message,
            IntPtr wParam,
            string lParam,
            uint flags,
            uint timeout,
            out IntPtr result);
    }
}
