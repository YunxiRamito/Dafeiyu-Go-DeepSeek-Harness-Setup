using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace DshInstaller.Shared
{
    /// <summary>导出结果。成功时 <see cref="Path"/> 是产出的 zip;失败时 <see cref="Error"/> 是人话。</summary>
    public sealed class LogBundleResult
    {
        public bool Succeeded { get; set; }

        /// <summary>zip 的完整路径(成功时才有)。</summary>
        public string Path { get; set; }

        /// <summary>打进去的日志文件数。</summary>
        public int Count { get; set; }

        /// <summary>失败原因(已经是可以直接给用户看的短句)。</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// 把日志打成一个 zip,方便用户发给我们看。
    ///
    /// 只收**日志**:
    ///   · 目录里的 installer*.log / launcher*.log(安装在 %LOCALAPPDATA%\DeepSeekHarness);
    ///   · 引导程序落在临时目录的 dsh-boot.log(装到一半就崩的时候,只有它有内容)。
    ///
    /// 两条硬规矩:
    ///   1. 名字里带 token / secret / key / credential / password 的文件**一律不收**;
    ///   2. 收进来的日志**逐行擦一遍**:`token=xxx`、`Bearer xxx`、`sk-xxxx` 这类值换成 ***。
    ///      日志是用户要发出去的,不能顺手把凭据也发出去。
    /// </summary>
    public static class LogBundle
    {
        /// <summary>文件名的黑名单。命中就不收。</summary>
        private static readonly string[] ForbiddenNameParts =
        {
            "token", "secret", "credential", "password", "passwd", "apikey", "api-key", "private",
        };

        /// <summary>行内敏感值:key=value / key: value 形式。</summary>
        private static readonly Regex SecretPair = new Regex(
            @"(?i)\b(api[_-]?key|access[_-]?key|token|secret|password|passwd|authorization)\b(\s*[:=]\s*)(""[^""]*""|\S+)",
            RegexOptions.Compiled);

        /// <summary>
        /// `Authorization: Bearer xxxx` 这种:值跟关键字之间只有空格,**没有等号**。
        /// 必须先擦掉它再走 key=value 规则 —— 反过来的话,`Bearer` 会被当成值擦成 ***,
        /// 真正的令牌原样留在后面(自测时真踩过)。
        /// </summary>
        private static readonly Regex BearerValue = new Regex(
            @"(?i)\b(bearer)\s+[A-Za-z0-9._\-]+",
            RegexOptions.Compiled);

        /// <summary>孤零零出现的密钥样式(sk-xxxx、ghp_xxxx、AKIAxxxx)。</summary>
        private static readonly Regex SecretToken = new Regex(
            @"(?i)\b(sk-[A-Za-z0-9_\-]{6,}|gh[pousr]_[A-Za-z0-9]{6,}|AKIA[0-9A-Z]{8,})\b",
            RegexOptions.Compiled);

        /// <summary>日志默认落在哪(和日志同一个目录,用户随手就能找到)。</summary>
        public static string DefaultFolder
        {
            get { return ConfigStore.ConfigDirectory; }
        }

        /// <summary>
        /// 收集日志文件清单。返回的是**已经过滤过黑名单**的文件,按修改时间新→旧。
        /// 抽出来单独给测试用,免得为了验一个文件名规则去点界面。
        /// </summary>
        public static List<string> CollectFiles()
        {
            List<string> found = new List<string>();

            try
            {
                string directory = ConfigStore.ConfigDirectory;
                if (Directory.Exists(directory))
                {
                    string[] candidates = Directory.GetFiles(directory, "*.log");
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        // 只收安装器/启动器的日志。同目录以后要是多出别的 .log,不属于这次范围。
                        string name = Path.GetFileName(candidates[i]);
                        if (name.StartsWith("installer", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith("launcher", StringComparison.OrdinalIgnoreCase))
                        {
                            found.Add(candidates[i]);
                        }
                    }
                }

                // 引导程序的日志在临时目录:装到一半失败时,它比 installer.log 先有内容。
                string bootLog = Path.Combine(Path.GetTempPath(), "dsh-boot.log");
                if (File.Exists(bootLog))
                {
                    found.Add(bootLog);
                }
            }
            catch
            {
            }

            List<string> allowed = new List<string>();
            for (int i = 0; i < found.Count; i++)
            {
                if (IsAllowed(found[i]) && SafeExists(found[i]))
                {
                    allowed.Add(found[i]);
                }
            }

            allowed.Sort(delegate (string left, string right)
            {
                return SafeWriteTime(right).CompareTo(SafeWriteTime(left));
            });

            return allowed;
        }

        /// <summary>文件名黑名单检查。</summary>
        private static bool IsAllowed(string path)
        {
            string name = Path.GetFileName(path).ToLowerInvariant();
            for (int i = 0; i < ForbiddenNameParts.Length; i++)
            {
                if (name.Contains(ForbiddenNameParts[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SafeExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static DateTime SafeWriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTime(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// 打包。targetFolder 为空时放在日志目录下。
        /// 同名文件(同一秒内连点两次)会自动让路,不覆盖已有的 zip。
        /// </summary>
        public static LogBundleResult Create(string targetFolder)
        {
            LogBundleResult result = new LogBundleResult();

            try
            {
                List<string> files = CollectFiles();
                if (files.Count == 0)
                {
                    result.Error = "没有找到日志文件。";
                    return result;
                }

                string folder = string.IsNullOrWhiteSpace(targetFolder) ? DefaultFolder : targetFolder;
                Directory.CreateDirectory(folder);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string target = Path.Combine(folder, "Dafeiyu-Go-logs-" + stamp + ".zip");
                int bump = 2;
                while (File.Exists(target) && bump < 100)
                {
                    target = Path.Combine(folder, "Dafeiyu-Go-logs-" + stamp + "-" + bump + ".zip");
                    bump++;
                }

                int written = 0;
                string lastError = null;
                HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create))
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        if (AddFile(archive, files[i], usedNames, out lastError))
                        {
                            written++;
                        }
                    }
                }

                if (written == 0)
                {
                    // 一个都没打进去就别留个空壳骗用户。原因要如实说 ——
                    // 上一版这里统一写成"文件被占用",而真实原因是 zip 的 API 用法错了,
                    // 用户按提示重试一百次也没用(教训:别猜,把原始错误带出来)。
                    SafeDelete(target);
                    result.Error = string.IsNullOrWhiteSpace(lastError)
                        ? "没有可以读取的日志内容。"
                        : "导出失败：" + lastError;
                    return result;
                }

                result.Succeeded = true;
                result.Path = target;
                result.Count = written;
                return result;
            }
            catch (Exception exception)
            {
                result.Error = "导出失败：" + exception.Message;
                return result;
            }
        }

        /// <summary>
        /// 把一个文件按擦洗后的内容写进 zip。重名自动加序号。
        ///
        /// 重名靠调用方传进来的 <paramref name="usedNames"/> 记,**不要去问 archive.Entries** ——
        /// Create 模式下读那个属性直接抛 NotSupportedException("Cannot access entries in Create mode"),
        /// 而这个异常如果被 catch 吞掉,表现就是"每个文件都悄悄失败、最后报一句莫名其妙的错"。
        /// </summary>
        private static bool AddFile(ZipArchive archive, string path, HashSet<string> usedNames, out string error)
        {
            error = null;

            try
            {
                string entryName = UniquifyName(usedNames, Path.GetFileName(path));

                // 日志此刻可能正被安装器自己写着,所以共享读,不独占。
                using (FileStream input = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(input, Encoding.UTF8, true))
                using (StreamWriter writer = new StreamWriter(
                    archive.CreateEntry(entryName, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        writer.WriteLine(Scrub(line));
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = Path.GetFileName(path) + "：" + exception.Message;
                return false;
            }
        }

        private static string UniquifyName(HashSet<string> usedNames, string name)
        {
            string candidate = name;
            int index = 2;
            while (usedNames.Contains(candidate) && index < 100)
            {
                candidate = Path.GetFileNameWithoutExtension(name) + "-" + index
                    + Path.GetExtension(name);
                index++;
            }

            usedNames.Add(candidate);
            return candidate;
        }

        /// <summary>把一行里的敏感值换成 ***。抽出来是为了能单独验。</summary>
        public static string Scrub(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }

            string scrubbed = BearerValue.Replace(line, "$1 ***");
            scrubbed = SecretPair.Replace(scrubbed, "$1$2***");
            return SecretToken.Replace(scrubbed, "***");
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
