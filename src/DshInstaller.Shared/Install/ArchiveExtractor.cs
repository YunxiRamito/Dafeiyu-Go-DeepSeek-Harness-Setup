using System;
using System.IO;
using System.IO.Compression;

namespace DshInstaller.Shared.Install
{
    /// <summary>解包 zip / 7z / tar.gz。优先用系统自带能力,避免带一堆私有 DLL。</summary>
    /// <summary>一次解压的结果:解了多少个文件、多大。</summary>
    public sealed class ExtractionResult
    {
        /// <summary>解出来的文件数。</summary>
        public int FileCount { get; set; }

        /// <summary>解出来的总字节数。</summary>
        public long TotalBytes { get; set; }

        /// <summary>用的哪种解包方式(zip / tar / powershell),给日志用。</summary>
        public string Method { get; set; }

        /// <summary>一句话摘要,直接可以写进日志或界面。</summary>
        public string Describe()
        {
            return FileCount + " 个文件," + DownloadProgress.FormatBytes(TotalBytes)
                + (string.IsNullOrEmpty(Method) ? string.Empty : "(" + Method + ")");
        }
    }
    public static class ArchiveExtractor
    {
        public static void Extract(string archivePath, string destination)
        {
            if (!Directory.Exists(destination))
            {
                Directory.CreateDirectory(destination);
            }

            string extension = Path.GetExtension(archivePath).ToLowerInvariant();
            if (extension == ".zip")
            {
                ExtractZip(archivePath, destination);
                return;
            }

            if (extension == ".7z")
            {
                Extract7z(archivePath, destination);
                return;
            }

            if (extension == ".gz" || archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                ExtractWithTar(archivePath, destination);
                return;
            }

            if (extension == ".exe")
            {
                throw new InvalidOperationException("自解压包不应进入此分支: " + archivePath);
            }

            // 不认识的格式,试试 zip(有些 .7z 其实是 zip)
            try
            {
                ExtractZip(archivePath, destination);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("无法识别的压缩格式: " + archivePath, exception);
            }
        }

        /// <summary>
        /// 跟 <see cref="Extract"/> 一样,但会把"解了多少东西"回报出来。
        /// 界面上的"安装中 · 正在解压"太笼统,用户想知道到底在解什么、有多少。
        /// </summary>
        public static ExtractionResult ExtractDetailed(string archivePath, string destination)
        {
            Extract(archivePath, destination);

            ExtractionResult result = new ExtractionResult();
            try
            {
                if (Directory.Exists(destination))
                {
                    string[] files = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
                    result.FileCount = files.Length;
                    long total = 0;
                    for (int i = 0; i < files.Length; i++)
                    {
                        try
                        {
                            total += new FileInfo(files[i]).Length;
                        }
                        catch
                        {
                        }
                    }

                    result.TotalBytes = total;
                }

                string extension = Path.GetExtension(archivePath);
                if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    result.Method = "zip";
                }
                else if (string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase))
                {
                    result.Method = "7z";
                }
                else
                {
                    result.Method = extension.TrimStart('.').ToLowerInvariant();
                }
            }
            catch
            {
            }

            return result;
        }
        private static void ExtractZip(string archivePath, string destination)
        {
            try
            {
                // 用 .NET 自带实现,不弹进度窗
                ZipFile.ExtractToDirectory(archivePath, destination, true);
                InstallLogger.Write("解包(zip): " + archivePath + " -> " + destination);
                return;
            }
            catch (Exception exception)
            {
                InstallLogger.Write("ZipFile 解包失败,改用 PowerShell: " + exception.Message);
            }

            ProcessRunner.Result result = ProcessRunner.Run(
                "powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                "\"Expand-Archive -LiteralPath '" + Escape(archivePath) + "' -DestinationPath '" +
                Escape(destination) + "' -Force\"",
                null,
                300000);

            if (!result.Ok)
            {
                throw new InvalidOperationException("解压 zip 失败: " + result.Combined);
            }

            InstallLogger.Write("解包(PowerShell): " + archivePath);
        }

        private static void Extract7z(string archivePath, string destination)
        {
            string sevenZip = FindOnPath("7z.exe");
            if (sevenZip != null)
            {
                ProcessRunner.Result result = ProcessRunner.Run(
                    sevenZip,
                    "x \"" + archivePath + "\" -o\"" + destination + "\" -y",
                    null,
                    600000);
                if (result.Ok)
                {
                    InstallLogger.Write("解包(7z): " + archivePath);
                    return;
                }

                InstallLogger.Write("7z 解包失败: " + result.Combined);
            }

            // Win10 1803+ 自带的 bsdtar 能读 7z
            ExtractWithTar(archivePath, destination, "7z");
        }

        private static void ExtractWithTar(string archivePath, string destination, string label = "tar")
        {
            ProcessRunner.Result result = ProcessRunner.Run(
                "tar.exe",
                "-xf \"" + archivePath + "\" -C \"" + destination + "\"",
                null,
                600000);

            if (!result.Ok)
            {
                throw new InvalidOperationException("解包失败(" + label + "): " + result.Combined);
            }

            InstallLogger.Write("解包(" + label + "): " + archivePath);
        }

        private static string FindOnPath(string fileName)
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (path == null)
                {
                    return null;
                }

                foreach (string dir in path.Split(';'))
                {
                    string trimmed = dir.Trim().Trim('"');
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        string full = Path.Combine(trimmed, fileName);
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
            catch
            {
            }

            return null;
        }

        private static string Escape(string value)
        {
            return value.Replace("'", "''");
        }
    }
}
