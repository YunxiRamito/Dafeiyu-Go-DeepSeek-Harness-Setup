using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DshInstaller.Shared.Backup
{
    /// <summary>压缩包里的一个条目。</summary>
    public sealed class DymEntry
    {
        /// <summary>包内相对路径(用 / 分隔)。</summary>
        public string Path { get; set; }

        public long Size { get; set; }

        public bool IsDirectory { get; set; }
    }

    /// <summary>
    /// .dym 备份包 —— 名字是自家的,**格式是货真价实的 7z**。
    ///
    /// 为什么用真 7z 而不是"改个扩展名的 zip":用户的备份不该被自家工具锁死。
    /// 出问题时拿 7-Zip / WinRAR 双击就能打开、能一个个文件翻 —— 这一点比省那 580 KB 重要。
    ///
    /// 工具用的是内嵌的 <c>7zr.exe</c>(588 KB,单文件、无依赖、公开领域)。
    /// 运行时释放到临时目录,用完不管 —— 它没有安装过程,也不写注册表。
    ///
    /// 这里只做"打包 / 列内容 / 解包"三件事,而且**只碰传进来的路径**:
    /// 哪些目录算"用户数据"由调用方决定(安装器和启动器各有各的判断)。
    /// </summary>
    public static class DymArchive
    {
        /// <summary>备份包扩展名。</summary>
        public const string Extension = ".dym";

        private const string ToolResourceName = "DshInstaller.Shared.Backup.7zr.exe";

        private static readonly Regex PercentPattern =
            new Regex(@"(\d{1,3})%", RegexOptions.Compiled);

        private static string _toolPath;

        /// <summary>
        /// 把内嵌的 7zr.exe 释放到临时目录,返回它的路径。
        /// 失败时返回 null 并给出原因(调用方负责告诉用户,不要静默)。
        /// </summary>
        public static string EnsureTool(out string error)
        {
            error = null;

            if (!String.IsNullOrWhiteSpace(_toolPath) && File.Exists(_toolPath))
            {
                return _toolPath;
            }

            try
            {
                string directory = Path.Combine(
                    String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEMP"))
                        ? Path.GetTempPath()
                        : Environment.GetEnvironmentVariable("TEMP"),
                    "Dafeiyu-Go");
                Directory.CreateDirectory(directory);

                string target = Path.Combine(directory, "7zr.exe");

                Assembly assembly = typeof(DymArchive).GetTypeInfo().Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(ToolResourceName))
                {
                    if (stream == null)
                    {
                        // 资源名写错 / 忘了 Embed:这种错必须说清楚,不然用户只看到"备份失败"
                        error = "安装包里没有带上 7z 工具(" + ToolResourceName + ")。";
                        return null;
                    }

                    // 已经释放过且大小一致就不重写 —— 每次备份都写一遍会被杀软盯上
                    FileInfo existing = new FileInfo(target);
                    if (existing.Exists && existing.Length == stream.Length)
                    {
                        _toolPath = target;
                        return target;
                    }

                    using (FileStream output = new FileStream(
                        target,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        stream.CopyTo(output);
                    }
                }

                _toolPath = target;
                return target;
            }
            catch (Exception exception)
            {
                error = "释放 7z 工具失败:" + exception.Message;
                return null;
            }
        }

        /// <summary>读出压缩包里的条目(不解包)。读不到就返回空列表并给出原因。</summary>
        public static List<DymEntry> List(
            string archive,
            Action<string> log,
            out string error)
        {
            error = null;
            List<DymEntry> entries = new List<DymEntry>();

            if (String.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
            {
                error = "备份包不存在:" + archive;
                return entries;
            }

            string tool = EnsureTool(out error);
            if (tool == null)
            {
                return entries;
            }

            ProcessResult result = RunProcess(
                tool,
                "l -slt -sccUTF-8 \"" + archive + "\"",
                Path.GetDirectoryName(archive),
                log,
                null,
                CancellationToken.None);

            if (!result.Ok)
            {
                error = DescribeFailure("读取备份包失败", result);
                return entries;
            }

            // -slt 是"每项一段 key = value"的格式:两段之间用空行隔开。
            // 分隔线(----)之前是**包自己的表头**(里面也有一行 Path = 备份包本身),
            // 不跳过的话列表里会混进这个包自己(实测看到过)。
            bool afterSeparator = false;
            DymEntry current = null;
            string[] lines = result.Output.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (!afterSeparator)
                {
                    if (line.StartsWith("----------", StringComparison.Ordinal))
                    {
                        afterSeparator = true;
                    }

                    continue;
                }

                if (line.Length == 0)
                {
                    if (current != null && !String.IsNullOrWhiteSpace(current.Path))
                    {
                        entries.Add(current);
                    }

                    current = null;
                    continue;
                }

                int separator = line.IndexOf(" = ", StringComparison.Ordinal);
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator);
                string value = line.Substring(separator + 3);

                if (String.Equals(key, "Path", StringComparison.Ordinal))
                {
                    if (current != null && !String.IsNullOrWhiteSpace(current.Path))
                    {
                        entries.Add(current);
                    }

                    // 统一成 / 分隔:界面和日志里更整齐,也免得"包内路径"看起来像本机路径
                    current = new DymEntry { Path = value.Replace('\\', '/') };
                }
                else if (current != null
                    && String.Equals(key, "Size", StringComparison.Ordinal))
                {
                    long size;
                    if (Int64.TryParse(value, out size))
                    {
                        current.Size = size;
                    }
                }
                else if (current != null
                    && String.Equals(key, "Attributes", StringComparison.Ordinal))
                {
                    current.IsDirectory = value.IndexOf('D') >= 0;
                }
            }

            if (current != null && !String.IsNullOrWhiteSpace(current.Path))
            {
                entries.Add(current);
            }

            return entries;
        }

        /// <summary>
        /// 打包。sourcePaths 是要收进去的文件/目录(绝对路径),
        /// workingDirectory 决定包内的相对路径(一般传这几条路径的共同父目录)。
        /// </summary>
        public static bool Create(
            string archive,
            IList<string> sourcePaths,
            string workingDirectory,
            Action<string, double> report,
            Action<string> log,
            CancellationToken token,
            bool append = false)
        {
            if (String.IsNullOrWhiteSpace(archive))
            {
                return false;
            }

            if (sourcePaths == null || sourcePaths.Count == 0)
            {
                return false;
            }

            string error;
            string tool = EnsureTool(out error);
            if (tool == null)
            {
                if (log != null)
                {
                    log(error);
                }

                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(archive);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!append && File.Exists(archive))
                {
                    File.Delete(archive);
                }
            }
            catch (Exception exception)
            {
                if (log != null)
                {
                    log("准备备份文件失败:" + exception.Message);
                }

                return false;
            }

            StringBuilder arguments = new StringBuilder();
            arguments.Append("a -t7z -mx=5 -bsp1 -bb1 -sccUTF-8 -y \"");
            arguments.Append(archive);
            arguments.Append('"');

            for (int index = 0; index < sourcePaths.Count; index++)
            {
                string path = sourcePaths[index];
                if (String.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                arguments.Append(" \"");
                arguments.Append(path);
                arguments.Append('"');
            }

            ProcessResult result = RunProcess(
                tool,
                arguments.ToString(),
                workingDirectory,
                log,
                report,
                token);

            if (!result.Ok)
            {
                if (log != null)
                {
                    log(DescribeFailure("打包失败", result));
                }

                return false;
            }

            return File.Exists(archive);
        }

        /// <summary>解包到 destination(不存在会自动建)。</summary>
        public static bool Extract(
            string archive,
            string destination,
            Action<string, double> report,
            Action<string> log,
            CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
            {
                if (log != null)
                {
                    log("备份包不存在:" + archive);
                }

                return false;
            }

            string error;
            string tool = EnsureTool(out error);
            if (tool == null)
            {
                if (log != null)
                {
                    log(error);
                }

                return false;
            }

            try
            {
                Directory.CreateDirectory(destination);
            }
            catch (Exception exception)
            {
                if (log != null)
                {
                    log("建目标目录失败:" + exception.Message);
                }

                return false;
            }

            string arguments = "x -bsp1 -bb1 -sccUTF-8 -y \""
                + archive
                + "\" -o\""
                + destination
                + "\"";

            ProcessResult result = RunProcess(
                tool,
                arguments,
                Path.GetDirectoryName(archive),
                log,
                report,
                token);

            if (!result.Ok)
            {
                if (log != null)
                {
                    log(DescribeFailure("解包失败", result));
                }

                return false;
            }

            return true;
        }

        private static string DescribeFailure(string headline, ProcessResult result)
        {
            if (result == null)
            {
                return headline + "：没有拿到执行结果。";
            }

            if (result.TimedOut)
            {
                return headline + "：超时。";
            }

            string tail = Tail(result.Output);
            return String.IsNullOrWhiteSpace(tail)
                ? headline + "（退出码 " + result.ExitCode + "）"
                : headline + "（退出码 " + result.ExitCode + "）" + tail;
        }

        /// <summary>取输出的最后几行有用的东西(7z 的报错就在末尾)。</summary>
        private static string Tail(string output)
        {
            if (String.IsNullOrWhiteSpace(output))
            {
                return String.Empty;
            }

            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            List<string> useful = new List<string>();
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                // 进度行没有信息量,报错才有
                if (PercentPattern.IsMatch(line) && line.Length < 40)
                {
                    continue;
                }

                useful.Add(line);
            }

            if (useful.Count == 0)
            {
                return String.Empty;
            }

            int take = Math.Min(3, useful.Count);
            return " " + String.Join(" / ", useful.GetRange(useful.Count - take, take).ToArray());
        }

        private sealed class ProcessResult
        {
            public int ExitCode = -1;
            public string Output = String.Empty;
            public bool TimedOut;
            public bool Ok
            {
                get { return !TimedOut && ExitCode == 0; }
            }
        }

        /// <summary>
        /// 跑 7zr。stdout/stderr 一起收 —— 7z 的报错有时候走 stderr、有时候走 stdout,
        /// 只收一个就会漏掉真正的原因(和 pnpm 那次一样,不能只看退出码)。
        /// </summary>
        private static ProcessResult RunProcess(
            string tool,
            string arguments,
            string workingDirectory,
            Action<string> log,
            Action<string, double> report,
            CancellationToken token)
        {
            ProcessResult result = new ProcessResult();

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(tool, arguments);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
                if (!String.IsNullOrWhiteSpace(workingDirectory)
                    && Directory.Exists(workingDirectory))
                {
                    startInfo.WorkingDirectory = workingDirectory;
                }

                StringBuilder buffer = new StringBuilder();

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        HandleLine(args.Data, buffer, log, report);
                        if (args.Data != null && report != null)
                        {
                            Match match = PercentPattern.Match(args.Data);
                            if (match.Success)
                            {
                                int percent;
                                if (Int32.TryParse(match.Groups[1].Value, out percent))
                                {
                                    report(null, Math.Max(0, Math.Min(100, percent)));
                                }
                            }
                        }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        HandleLine(args.Data, buffer, log, report);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    int waited = 0;
                    while (!process.WaitForExit(500))
                    {
                        if (token.IsCancellationRequested)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch
                            {
                            }

                            result.TimedOut = false;
                            result.ExitCode = -1;
                            result.Output = buffer.ToString();
                            return result;
                        }

                        waited += 500;
                        if (waited > 60 * 60 * 1000)
                        {
                            result.TimedOut = true;
                            try
                            {
                                process.Kill();
                            }
                            catch
                            {
                            }

                            result.Output = buffer.ToString();
                            return result;
                        }
                    }

                    result.ExitCode = process.ExitCode;
                }

                result.Output = buffer.ToString();
                return result;
            }
            catch (Exception exception)
            {
                result.Output = exception.Message;
                result.ExitCode = -1;
                return result;
            }
        }

        private static void HandleLine(
            string line,
            StringBuilder buffer,
            Action<string> log,
            Action<string, double> report)
        {
            if (String.IsNullOrWhiteSpace(line))
            {
                return;
            }

            string trimmed = line.Trim();

            // stdout / stderr 是两个线程在回调里同时进这里,StringBuilder 不是线程安全的,
            // 不加锁轻则内容串行错乱、重则抛异常(现有 ProcessRunner 也是这么锁的)。
            lock (buffer)
            {
                buffer.AppendLine(trimmed);
            }

            if (log == null)
            {
                return;
            }

            // -bb1 会把每个文件打出来("Extracting  xxx"),这行交给界面,
            // 用户就能看到"正在恢复哪个文件"而不是一个干巴巴的百分比。
            if (trimmed.StartsWith("Extracting", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Compressing", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Add new data", StringComparison.OrdinalIgnoreCase))
            {
                log(trimmed);
            }
        }
    }
}
