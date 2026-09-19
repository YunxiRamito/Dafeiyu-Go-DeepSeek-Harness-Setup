using System;
using System.Diagnostics;
using System.Text;

namespace DshInstaller.Shared
{
    /// <summary>
    /// 跑一段 PowerShell 拿结构化结果。
    /// 用系统自带的 Windows PowerShell 5.1,不依赖用户装 pwsh。
    /// </summary>
    public static class PowerShellScript
    {
        private static readonly string Executable = ResolvePowerShell();

        private static string ResolvePowerShell()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string classic = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"System32\WindowsPowerShell\v1.0\powershell.exe");
            if (System.IO.File.Exists(classic))
            {
                return classic;
            }

            return "powershell.exe";
        }

        public static string Run(string script, int timeoutMs = 15000)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = Executable,
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
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

                    // 脚本从标准输入喂进去,省得跟命令行转义打架
                    byte[] payload = Encoding.UTF8.GetBytes(script);
                    process.StandardInput.BaseStream.Write(payload, 0, payload.Length);
                    process.StandardInput.BaseStream.Flush();
                    process.StandardInput.Close();

                    StringBuilder output = new StringBuilder();
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (args.Data != null)
                        {
                            lock (output)
                            {
                                output.AppendLine(args.Data);
                            }
                        }
                    };
                    process.BeginOutputReadLine();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }

                    // 让异步读取把剩下的收完
                    process.WaitForExit();

                    lock (output)
                    {
                        return output.ToString();
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
