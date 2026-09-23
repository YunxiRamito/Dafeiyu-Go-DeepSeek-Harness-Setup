using System;
using System.Diagnostics;
using System.Text;

namespace DshInstaller.Shared.Install
{
    /// <summary>跑外部命令(解包、npm、schtasks 之类)的统一入口。</summary>
    public static class ProcessRunner
    {
        public sealed class Result
        {
            public int ExitCode { get; set; }
            public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public bool TimedOut { get; set; }
        public bool Cancelled { get; set; }

            public string Combined
            {
                get
                {
                    StringBuilder builder = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(StandardOutput))
                    {
                        builder.Append(StandardOutput);
                    }

                    if (!string.IsNullOrWhiteSpace(StandardError))
                    {
                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }

                        builder.Append(StandardError);
                    }

                    return builder.ToString();
                }
            }

            public bool Ok
            {
                get { return !TimedOut && ExitCode == 0; }
            }
        }

        /// <summary>
        /// 每执行一条外部命令就回调一次。
        /// 界面把它挂到日志抽屉上,用户就能看到"到底执行了什么",而不是只盯着一个进度条。
        /// </summary>
        public static Action<string> CommandObserver { get; set; }

        public static Result Run(
            string fileName,
            string arguments,
            string workingDirectory = null,
            int timeoutMs = 600000,
            Action<string> onOutput = null,
            string extraPath = null,
            System.Collections.Generic.IDictionary<string, string> environment = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            Result result = new Result();

            // 让界面能看到"执行了什么"。命令本身往往是排查问题最有力的线索。
            Action<string> observer = CommandObserver;
            if (observer != null)
            {
                try
                {
                    observer(fileName + " " + arguments);
                }
                catch
                {
                }
            }

            InstallLogger.Write("执行: " + fileName + " " + arguments);
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    info.WorkingDirectory = workingDirectory;
                }

                if (!string.IsNullOrWhiteSpace(extraPath))
                {
                    // 注意大小写:Windows 环境变量名不区分大小写,而 ProcessStartInfo.EnvironmentVariables
                    // 在 .NET 里是**区分大小写**的字典。当前进程里那个键通常叫 "Path",
                    // 直接写 EnvironmentVariables["PATH"] 会多出一个变量,子进程取到哪个不一定 ——
                    // 表现就是"明明加了 extraPath,子进程里还是找不到 node"(实测踩过)。
                    // 所以先不分大小写地把旧的删掉,再统一用 "Path" 写回去。
                    string current = null;
                    // 先快照键名再改,别在枚举时改集合
                    System.Collections.Generic.List<string> keys =
                        new System.Collections.Generic.List<string>();
                    foreach (string key in info.EnvironmentVariables.Keys)
                    {
                        keys.Add(key);
                    }

                    for (int i = 0; i < keys.Count; i++)
                    {
                        if (string.Equals(keys[i], "Path", StringComparison.OrdinalIgnoreCase))
                        {
                            current = info.EnvironmentVariables[keys[i]];
                            info.EnvironmentVariables.Remove(keys[i]);
                        }
                    }

                    if (string.IsNullOrEmpty(current))
                    {
                        current = Environment.GetEnvironmentVariable("PATH");
                    }

                    info.EnvironmentVariables["Path"] = extraPath + ";" + current;
                }

                if (environment != null)
                {
                    foreach (System.Collections.Generic.KeyValuePair<string, string> pair in environment)
                    {
                        info.EnvironmentVariables[pair.Key] = pair.Value;
                    }
                }

                StringBuilder stdout = new StringBuilder();
                StringBuilder stderr = new StringBuilder();

                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (args.Data == null)
                        {
                            return;
                        }

                        lock (stdout)
                        {
                            stdout.AppendLine(args.Data);
                        }

                        if (onOutput != null)
                        {
                            onOutput(args.Data);
                        }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (args.Data == null)
                        {
                            return;
                        }

                        lock (stderr)
                        {
                            stderr.AppendLine(args.Data);
                        }

                        if (onOutput != null)
                        {
                            onOutput(args.Data);
                        }
                    };

                    if (!process.Start())
                    {
                        result.ExitCode = -1;
                        result.StandardError = SharedText.T("进程启动失败: ", "Failed to start process: ") + fileName;
                        return result;
                    }

                    // 挂进"陪葬"作业:本进程无论怎么死(关窗口/崩溃/被 taskkill),
                    // 这颗子进程树都会被系统一起收掉。少了这一步,npm install 会在
                    // 安装被中断之后继续在后台跑(实测留下过 5 个 node)。
                    ProcessChildGuard.Attach(process);

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    int remaining = timeoutMs;
                    while (true)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            result.Cancelled = true;
                            try { process.Kill(true); } catch { }
                            break;
                        }

                        int wait = Math.Min(250, Math.Max(1, remaining));
                        if (process.WaitForExit(wait))
                        {
                            break;
                        }

                        remaining -= wait;
                        if (remaining <= 0)
                        {
                            result.TimedOut = true;
                            try { process.Kill(true); } catch { }
                            break;
                        }
                    }

                    if (!result.TimedOut && !result.Cancelled)
                    {
                        process.WaitForExit();
                    }

                    result.ExitCode = result.TimedOut || result.Cancelled
                        ? -1
                        : process.ExitCode;
                }

                lock (stdout)
                {
                    result.StandardOutput = stdout.ToString();
                }

                lock (stderr)
                {
                    result.StandardError = stderr.ToString();
                }
            }
            catch (Exception exception)
            {
                result.ExitCode = -1;
                result.StandardError = exception.Message;
            }

            return result;
        }
    }
}
