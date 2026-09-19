using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DshInstaller.Shared
{
    /// <summary>用 WScript.Shell 读/写 .lnk,免得引 COM 互操作包。</summary>
    public static class ShellLink
    {
        /// <summary>读快捷方式的目标路径。</summary>
        public static string ReadTarget(string shortcutPath)
        {
            string script =
                "$ErrorActionPreference='SilentlyContinue'\r\n" +
                "$s = New-Object -ComObject WScript.Shell\r\n" +
                "$l = $s.CreateShortcut(" + Quote(shortcutPath) + ")\r\n" +
                "Write-Output $l.TargetPath\r\n";

            string output = PowerShellScript.Run(script, 8000);
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            return output.Trim();
        }

        public sealed class ShortcutSpec
        {
            public string Path { get; set; }
            public string Target { get; set; }
            public string Arguments { get; set; }
            public string WorkingDirectory { get; set; }
            public string Description { get; set; }
            public string IconLocation { get; set; }
        }

        /// <summary>创建或覆盖一个快捷方式。</summary>
        public static bool Create(ShortcutSpec spec)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.Path) || string.IsNullOrWhiteSpace(spec.Target))
            {
                return false;
            }

            StringBuilder script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference='Stop'");
            script.AppendLine("$s = New-Object -ComObject WScript.Shell");
            script.AppendLine("$l = $s.CreateShortcut(" + Quote(spec.Path) + ")");
            script.AppendLine("$l.TargetPath = " + Quote(spec.Target));
            script.AppendLine("$l.Arguments = " + Quote(spec.Arguments ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(spec.WorkingDirectory))
            {
                script.AppendLine("$l.WorkingDirectory = " + Quote(spec.WorkingDirectory));
            }

            if (!string.IsNullOrWhiteSpace(spec.Description))
            {
                script.AppendLine("$l.Description = " + Quote(spec.Description));
            }

            script.AppendLine("$l.IconLocation = " + Quote(spec.IconLocation ?? (spec.Target + ",0")));
            script.AppendLine("$l.Save()");
            script.AppendLine("Write-Output 'ok'");

            string output = PowerShellScript.Run(script.ToString(), 12000);
            return output != null && output.IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>把文件或目录钉到桌面/开始菜单(不走 Shell.Application,直接写 .lnk)。</summary>
        public static bool Delete(string shortcutPath)
        {
            try
            {
                if (System.IO.File.Exists(shortcutPath))
                {
                    System.IO.File.Delete(shortcutPath);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "''";
            }

            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
