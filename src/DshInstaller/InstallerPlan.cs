using System;
using System.IO;
using System.Text.Json;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller
{
    /// <summary>
    /// 安装选项的组装与传递。
    ///
    /// 为什么要落到文件:提权会**另起一个进程**(runas),新进程读不到旧进程的内存。
    /// 把选项写在临时文件里、路径通过命令行传过去,提权实例就能直接开跑,
    /// 不用把用户重新填一遍向导。
    /// </summary>
    internal static class InstallerPlan
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>按界面上的选择组装后端选项。确认页与进度页共用,避免两处逻辑漂移。</summary>
        public static InstallOptions Build()        {
            InstallSession session = InstallSession.Current;
            ProbeReport report = session.Report;

            InstallOptions options = new InstallOptions
            {
                AllUsers = session.Scope == InstallScope.AllUsers,
                ComponentsRoot = session.ComponentsRoot,
                DshRoot = session.DshRoot,
                LauncherRoot = session.LauncherRoot,
                UseExistingDsh = session.UseExistingDsh,
                SourcePreference = session.SourcePreference,
                InstallGit = session.InstallGit,
                InstallPnpm = session.InstallPnpm,
                InstallPython = session.InstallPython,
                CreateDesktopShortcut = session.CreateDesktopShortcut,
                CreateStartMenuShortcut = session.CreateStartMenuShortcut,
                EnableAutostart = session.EnableAutostart,
                LaunchAfterwards = session.LaunchAfterwards,
                DryRun = DevOptions.DryRun,
                TempRoot = Path.Combine(Path.GetTempPath(), "DSH-Installer"),
            };

            // 缺 Node 才装;检测结果缺席时按"要装"处理
            ComponentStatus node = report == null ? null : report["node"];
            options.InstallNode = node == null || !node.IsSatisfied;

            options.InstallDsh = !session.UseExistingDsh;
            options.InstallLauncher = true;

            // 运行库是必选步骤:装的那步自己会判断"已有就跳过",
            // 所以这里一律排上 —— 界面上也能让用户看到这两个前置被处理过了。
            options.InstallDotNetRuntime = true;
            options.InstallWindowsAppRuntime = true;

            // 目录没填就给兜底,免得 Validate 直接失败
            if (string.IsNullOrWhiteSpace(options.ComponentsRoot))
            {
                options.ComponentsRoot = Path.Combine(session.DefaultRoot, "components");
            }

            if (string.IsNullOrWhiteSpace(options.DshRoot))
            {
                // 直接用安装根目录,不再套一层 "DSH"。
                // 之前是 <根>\DSH,于是机器范围下出现
                // "C:\Program Files\DeepSeek Harness\DSH" —— 同一个东西被叫了两遍,
                // 看着就像套娃(用户点名过)。
                options.DshRoot = session.DefaultRoot;
            }

            if (string.IsNullOrWhiteSpace(options.LauncherRoot))
            {
                options.LauncherRoot = Path.Combine(options.DshRoot, WellKnown.LauncherFolder);
            }

            // --- 最后拿**目标目录本身**复核一遍"要不要装 Node",不信检测页那个结论。
            //
            // 为什么不统一口径:检测页是在整个系统里找 node(可能找到别的范围装的那份、
            // 或者系统 PATH 里的),而 DSH 那一步用的是 **<组件目录>\node\npm.cmd** 这一条路径。
            // 两边不一致时就会出现"检测说已就绪 → 计划里没有装 Node 这一步 →
            // DSH 报未找到 npm → 装不完"(实测,查了两轮)。
            //
            // 所以这里直接看文件:那个 npm.cmd 不在,就必须把 Node 步骤拉回来重装。
            try
            {
                string targetNpm = Path.Combine(options.ComponentsRoot, "node", "npm.cmd");
                if (!File.Exists(targetNpm))
                {
                    options.InstallNode = true;
                }
            }
            catch
            {
                options.InstallNode = true;
            }

            return options;
        }

        /// <summary>
        /// 无人值守:从命令行参数组装选项(--silent 那一套)。
        /// 目录没给就按范围取默认值,省得调用方每次都写全。
        /// </summary>
        public static InstallOptions FromCommandLine()
        {
            InstallOptions options = new InstallOptions
            {
                AllUsers = DevOptions.AllUsers,
                SourcePreference = DevOptions.Source,
                InstallGit = DevOptions.WantGit,
                InstallPnpm = DevOptions.WantPnpm,
                InstallPython = DevOptions.WantPython,
                CreateDesktopShortcut = !DevOptions.NoShortcut,
                // 无人值守里 --no-shortcut 是"别建快捷方式"的意思,两份都算
                CreateStartMenuShortcut = !DevOptions.NoShortcut,
                EnableAutostart = !DevOptions.NoAutostart,
                LaunchAfterwards = !DevOptions.NoLaunch,
                DryRun = DevOptions.DryRun,
                TempRoot = Path.Combine(Path.GetTempPath(), "DSH-Installer"),
            };

            string root = options.AllUsers ? InstallSession.MachineRoot : InstallSession.UserRoot;

            options.ComponentsRoot = string.IsNullOrWhiteSpace(DevOptions.ComponentsRoot)
                ? Path.Combine(root, "components")
                : DevOptions.ComponentsRoot;

            options.DshRoot = string.IsNullOrWhiteSpace(DevOptions.DshRoot)
                ? root
                : DevOptions.DshRoot;

            options.LauncherRoot = string.IsNullOrWhiteSpace(DevOptions.LauncherRoot)
                ? Path.Combine(options.DshRoot, WellKnown.LauncherFolder)
                : DevOptions.LauncherRoot;

            // 具体装不装由步骤自己判断(存在就跳过),这里一律先安排上
            options.InstallNode = true;
            options.InstallDsh = true;
            options.InstallLauncher = true;

            // 运行库同理:步骤自己会探测"已有就跳过"。--no-runtime 是给离线/已装好的场景留的开关。
            options.InstallDotNetRuntime = !DevOptions.NoRuntime;
            options.InstallWindowsAppRuntime = !DevOptions.NoRuntime;

            return options;
        }

        /// <summary>把安装结果写成 JSON,给无人值守/自动化测试读。</summary>
        public static void WriteReport(string path, InstallOptions options, InstallReport report)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                var payload = new
                {
                    succeeded = report != null && report.Succeeded,
                    cancelled = report != null && report.Cancelled,
                    error = report == null ? "no report" : report.Error,
                    dshRoot = options == null ? null : options.DshRoot,
                    launcherRoot = options == null ? null : options.LauncherRoot,
                    componentsRoot = options == null ? null : options.ComponentsRoot,
                    dryRun = options != null && options.DryRun,
                    steps = report == null ? null : report.Steps,
                };

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, JsonSerializer.Serialize(payload, Options), new System.Text.UTF8Encoding(false));
            }
            catch
            {
            }
        }
        /// <summary>把选项写成临时文件,返回路径。</summary>
        public static string Save(InstallOptions options)
        {
            string directory = Path.Combine(Path.GetTempPath(), "DSH-Installer");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, "plan-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(options, Options), new System.Text.UTF8Encoding(false));

            // 语言也要带过去,不然提权实例会按系统区域重新猜一次
            string languagePath = path + ".lang";
            File.WriteAllText(languagePath, Localization.IsChinese ? "zh" : "en", new System.Text.UTF8Encoding(false));

            return path;
        }

        /// <summary>读回选项;失败返回 null。</summary>
        public static InstallOptions Load(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                InstallOptions options = JsonSerializer.Deserialize<InstallOptions>(json);
                if (options == null)
                {
                    return null;
                }

                // 顺带把语言也恢复过来
                try
                {
                    string languagePath = path + ".lang";
                    if (File.Exists(languagePath))
                    {
                        string code = File.ReadAllText(languagePath).Trim();
                        Localization.Current = code.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                            ? Localization.Language.English
                            : Localization.Language.Chinese;
                    }
                }
                catch
                {
                }

                // 提权实例要真的装,演练标记不继承
                options.DryRun = DevOptions.DryRun;
                return options;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>用完就删,别在 %TEMP% 里留一堆计划文件。</summary>
        public static void Cleanup(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }

                string languagePath = path + ".lang";
                if (File.Exists(languagePath))
                {
                    File.Delete(languagePath);
                }
            }
            catch
            {
            }
        }
    }
}
