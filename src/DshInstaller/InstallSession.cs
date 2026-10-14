using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller
{
    /// <summary>这次跑的是安装还是卸载。两个流程共用同一套向导与执行器。</summary>
    internal enum SessionMode
    {
        Install,
        Uninstall,
    }

    /// <summary>安装范围。</summary>
    internal enum InstallScope
    {
        CurrentUser,
        AllUsers,
    }

    /// <summary>
    /// 本次安装的所有选择。各页往里写,确认页和进度页读它。
    /// 单例就够了 —— 一个安装器一次只跑一遍。
    /// </summary>
    internal sealed class InstallSession
    {
        private static readonly InstallSession Instance = new InstallSession();

        private InstallSession()
        {
            string saved = ConfigStore.NormalizeSourcePreference(ConfigStore.LoadForUpdate().SourcePreference);
            if (saved != null) SourcePreference = saved;
            BackendDownloadSource.SelectedPreference = SourcePreference;
        }

        public static InstallSession Current
        {
            get { return Instance; }
        }

        /// <summary>当前是安装还是卸载。</summary>
        public SessionMode Mode { get; set; } = SessionMode.Install;

        /// <summary>
        /// 修复模式:按安装记录核对一遍,把缺的、坏的补回来,**不动用户数据**。
        /// 走的是同一套安装步骤 —— 它们本来就"有就跳过、没有才装",正好拿来当修复。
        /// </summary>
        public bool Repair { get; set; }

        /// <summary>
        /// 进入修复模式:把上次装到哪儿从记录里读回来,不重新问用户。
        /// 记录也读不到时返回 false —— 那种情况该老老实实走一遍安装。
        /// </summary>
        public bool SetupRepair()
        {
            InstallerState state = ConfigStore.Load();
            if (state == null || string.IsNullOrWhiteSpace(state.DshRoot))
            {
                return false;
            }

            Repair = true;
            Mode = SessionMode.Install;
            var restored = new InstallOptions();
            EffectiveInstallPlan.Restore(restored, state);
            Scope = restored.AllUsers ? InstallScope.AllUsers : InstallScope.CurrentUser;
            DshRoot = restored.DshRoot;
            LauncherRoot = string.IsNullOrWhiteSpace(state.LauncherRoot)
                ? System.IO.Path.Combine(state.DshRoot, WellKnown.LauncherFolder)
                : state.LauncherRoot;
            ComponentsRoot = string.IsNullOrWhiteSpace(state.ComponentsRoot)
                ? System.IO.Path.Combine(state.DshRoot, "components")
                : state.ComponentsRoot;
            SourcePreference = restored.SourcePreference;
            BackendDownloadSource.SelectedPreference = SourcePreference;

            DshRootChosen = true;
            LauncherChosen = true;
            ComponentsChosen = true;

            // 不要"用现有的就跳过本体" —— 修复的全部意义就是"发现缺了就补"。
            // 本体那一步自己会看标志文件:在就跳过,不在才装。
            UseExistingDsh = false;

            // 上次装过什么,这回按同样清单核对
            InstallGit = state.InstallGit;
            InstallPnpm = state.InstallPnpm;
            InstallPython = state.InstallPython;
            EnableAutostart = state.Autostart;
            CreateDesktopShortcut = state.DesktopShortcut;
            CreateStartMenuShortcut = restored.CreateStartMenuShortcut;
            AutostartChosen = true;

            return true;
        }

        /// <summary>卸载时的选择(装的时候没用)。</summary>
        public Shared.Install.UninstallOptions UninstallOptions { get; set; }

        /// <summary>装给谁。</summary>
        public InstallScope Scope { get; set; } = InstallScope.CurrentUser;

        /// <summary>
        /// 版本页选的 DSH 本体版本(空 = 跟默认)。
        ///
        /// 存的就是 npm 的版本说明,装的时候原样用 —— 不在这里做解析,
        /// 免得"界面上选的"和"真正装下去的"是两回事。
        /// </summary>
        public string DshVersionSpec { get; set; } = string.Empty;

        /// <summary>
        /// 版本页选的启动器版本(空 = 跟默认)。**这一项界面暂时禁用**,
        /// 字段先留着,以后接上不用再动数据结构。
        /// </summary>
        public string LauncherVersionSpec { get; set; } = string.Empty;

        /// <summary>
        /// 装完之后有没有真的导入过备份(导入页填)。
        /// 记下来只是为了让完成页能说一句"数据已恢复",不参与安装决策。
        /// </summary>
        public bool ImportedBackup { get; set; }

        /// <summary>
        /// 卸载前导出到的文件(导出页填)。只用来在完成页说一句"备份存在这儿",
        /// 只有实际生成文件后才填写。
        /// </summary>
        public string ExportedBackupPath { get; set; }

        public string ExportSourceHome { get; set; }
        public string ExportTargetDirectory { get; set; }
        public bool ExportToOfficialDsh { get; set; } = true;
        public bool ExportSucceeded { get; set; }
        public bool ExportSkipped { get; set; }
        public bool ExportInProgress { get; set; }
        public List<string> ExportSelectedGroupIds { get; set; }

        internal static string ExportUiSmokeRoot
        {
            get
            {
                if (!DevOptions.DryRun || Environment.GetEnvironmentVariable("DAFEIYU_EXPORT_UI_SMOKE") != "1") return null;
                string value = Environment.GetEnvironmentVariable("DAFEIYU_EXPORT_UI_SMOKE_ROOT");
                if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Export UI smoke requires an isolated fixture root.");
                string root = NormalizeExportPath(value);
                if (!string.Equals(Path.GetPathRoot(root), @"G:\", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Export UI smoke requires a dedicated fixture directory on G:.");
                foreach (string name in new[] { "DAFEIYU_INSTALLER_SETTINGS_DIRECTORY", "DAFEIYU_LAUNCHER_SETTINGS_DIRECTORY", "DAFEIYU_BACKUP_WORK_DIRECTORY", "DSH_HOME", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA" })
                {
                    string isolated = NormalizeExportPath(Environment.GetEnvironmentVariable(name));
                    if (!isolated.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Export UI smoke requires isolation for " + name);
                    for (string ancestor = isolated; !string.IsNullOrEmpty(ancestor); ancestor = Path.GetDirectoryName(ancestor))
                    {
                        if (File.Exists(ancestor) || Directory.Exists(ancestor))
                        {
                            FileAttributes attributes = File.GetAttributes(ancestor);
                            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                                throw new InvalidOperationException("Export UI smoke requires plain directories for " + name);
                        }
                    }
                }
                return root;
            }
        }

        internal sealed class ExportHomeCandidate
        {
            public string Path { get; set; }
            public string Origin { get; set; }
        }

        internal string ExportDshRoot => !string.IsNullOrWhiteSpace(DshRoot) ? DshRoot
            : !string.IsNullOrWhiteSpace(UninstallOptions?.DshRoot) ? UninstallOptions.DshRoot : ConfigStore.Load()?.DshRoot;

        internal List<ExportHomeCandidate> FindExportHomes()
        {
            var homes = new List<ExportHomeCandidate>();
            void Add(string value, string origin, bool requireExisting = false)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                try
                {
                    string path = NormalizeExportPath(value);
                    if (requireExisting && !Directory.Exists(path)) return;
                    var existing = homes.Find(home => string.Equals(home.Path, path, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) homes.Add(new ExportHomeCandidate { Path = path, Origin = origin });
                    else if (!existing.Origin.Contains(origin, StringComparison.Ordinal)) existing.Origin += "; " + origin;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (IOException) { }
            }

            Add(ExportSourceHome, Localization.IsChinese ? "上次选择" : "Previous selection");
            foreach (EnvironmentVariableTarget target in ExportUiSmokeRoot != null ? new[] { EnvironmentVariableTarget.Process }
                : new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
                Add(Environment.GetEnvironmentVariable("DSH_HOME", target), "DSH_HOME (" + target + ")");

            InstallerState state = ConfigStore.Load();
            var configs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ConfigStore.ConfigPath, LauncherSettingsFile };
            if (!string.IsNullOrWhiteSpace(state?.LauncherRoot)) configs.Add(Path.Combine(state.LauncherRoot, "LauncherSettings.json"));
            foreach (string config in configs)
            {
                if (!File.Exists(config)) continue;
                try
                {
                    using var input = File.OpenRead(config);
                    if (input.Length > 4 * 1024 * 1024) throw new InvalidDataException("Configuration is too large: " + config);
                    using var document = JsonDocument.Parse(input, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    void ReadHomes(JsonElement element, int depth)
                    {
                        if (depth > 4 || element.ValueKind != JsonValueKind.Object) return;
                        foreach (JsonProperty property in element.EnumerateObject())
                        {
                            if (property.Value.ValueKind == JsonValueKind.String
                                && (property.Name.Equals("DSH_HOME", StringComparison.OrdinalIgnoreCase)
                                    || property.Name.Equals("DataHome", StringComparison.OrdinalIgnoreCase)
                                    || property.Name.Equals("DshHome", StringComparison.OrdinalIgnoreCase)
                                    || property.Name.Equals("dsh_home", StringComparison.OrdinalIgnoreCase)
                                    || property.Name.Equals("DshHomeDirectory", StringComparison.OrdinalIgnoreCase)))
                                Add(property.Value.GetString(), Path.GetFileName(config));
                            else if (property.Value.ValueKind == JsonValueKind.Object) ReadHomes(property.Value, depth + 1);
                        }
                    }
                    ReadHomes(document.RootElement, 0);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    // An unreadable explicit configuration must not turn into an apparently complete source scan.
                    throw new InvalidDataException((Localization.IsChinese ? "无法读取数据来源配置：" : "Cannot read the data-source configuration: ") + config, exception);
                }
            }

            string root = ExportDshRoot;
            if (!string.IsNullOrWhiteSpace(root))
            {
                Add(Path.Combine(root, ".dsh"), Localization.IsChinese ? "安装目录内的数据" : "Data inside the installation", true);
                if (Directory.Exists(Path.Combine(root, "profiles")) || Directory.Exists(Path.Combine(root, "storages")))
                    Add(root, Localization.IsChinese ? "兼容数据目录" : "Compatible data directory", true);
            }
            if (ExportUiSmokeRoot == null)
                Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"),
                    Localization.IsChinese ? "用户默认目录" : "Default user home", true);
            return homes;
        }

        internal static string NormalizeExportPath(string value)
        {
            string path = Environment.ExpandEnvironmentVariables((value ?? string.Empty).Trim().Trim('"'));
            if (path == "~") path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            else if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException(Localization.IsChinese ? "请输入完整的绝对目录路径。" : "Enter a fully qualified directory path.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        internal string ValidateExportTarget(string value, string sourceHome)
        {
            string target = NormalizeExportPath(value);
            InstallerState state = ConfigStore.Load();
            var protectedRoots = new[] { ExportDshRoot, LauncherRoot, ComponentsRoot, state?.DshRoot,
                state?.LauncherRoot, state?.ComponentsRoot, state?.InstallerHome, UninstallOptions?.DshRoot,
                UninstallOptions?.LauncherRoot, UninstallOptions?.ComponentsRoot, UninstallOptions?.UserDataRoot,
                sourceHome, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh") };
            foreach (string valueRoot in protectedRoots)
            {
                if (string.IsNullOrWhiteSpace(valueRoot)) continue;
                string root = NormalizeExportPath(valueRoot);
                string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
                if (string.Equals(target, root, StringComparison.OrdinalIgnoreCase) || target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(Localization.IsChinese
                        ? "导出目录不能位于待卸载目录或数据来源内，请选择外部目录：" + root
                        : "Choose a destination outside the uninstall directories and the data source: " + root);
            }
            // Reject junctions in existing ancestors, including when the final destination does not exist yet.
            for (string ancestor = target; !string.IsNullOrEmpty(ancestor); ancestor = Path.GetDirectoryName(ancestor))
            {
                if (File.Exists(ancestor)) throw new IOException(Localization.IsChinese ? "目录路径指向文件：" + ancestor : "The directory path points to a file: " + ancestor);
                if (Directory.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(Localization.IsChinese ? "导出目录包含链接或 junction，请选择直接目录：" + ancestor : "Choose a directory without symbolic links or junctions: " + ancestor);
            }
            return target;
        }

        /// <summary>各组件根目录(便携组件解压到这儿)。</summary>
        public string ComponentsRoot { get; set; }

        /// <summary>DSH 本体目录。</summary>
        public string DshRoot { get; set; }

        /// <summary>启动器目录。</summary>
        public string LauncherRoot { get; set; }

        /// <summary>本机已有 DSH,直接用现有的、跳过下载。</summary>
        public bool UseExistingDsh { get; set; }

        public string DshChoice { get; set; }
        public string SelectedDshRoot { get; set; }
        public string OfficialMigrationInstallRoot { get; set; }
        public string OfficialMigrationHome { get; set; }
        public string LauncherSettingsFile { get; } = System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("DAFEIYU_LAUNCHER_SETTINGS_DIRECTORY") ?? ConfigStore.ConfigDirectory,
            "LauncherSettings.json");

        /// <summary>下载源偏好:国内镜像还是官方。</summary>
        public string SourcePreference { get; set; } = MirrorSource.China;

        /// <summary>勾选要装的便携组件。</summary>
        public bool InstallGit { get; set; }
        public bool InstallPnpm { get; set; }
        public bool InstallPython { get; set; }

        /// <summary>
        /// 强制重新下载组件:本机明明已经有够新的 Node / Git / pnpm / Python 也照样装便携版。
        /// 默认关 —— 默认是"能用就不下"。
        /// </summary>
        public bool ForceReinstall { get; set; }

        /// <summary>
        /// Git 和 pnpm 都已具备（本机已有或本次会安装）。
        /// 推荐插件下载不满足这个条件时必须整页跳过。
        /// </summary>
        public bool PluginToolsAvailable { get; set; }

        /// <summary>官方推荐插件页选中的安装表达式。</summary>
        public System.Collections.Generic.List<string> RecommendedPluginSpecs
        {
            get;
        } = new System.Collections.Generic.List<string>();

        /// <summary>装完要不要建桌面快捷方式 / 开机自启。</summary>
        public bool CreateDesktopShortcut { get; set; } = true;

        /// <summary>要不要在开始菜单里建一份(默认要)。</summary>
        public bool CreateStartMenuShortcut { get; set; } = true;

        /// <summary>
        /// 开机自启。默认**不勾** —— 它要注册"最高权限"的计划任务,必然弹 UAC,
        /// 而默认范围是"仅为本用户安装"。选了"给所有用户"时范围页会把它默认勾上。
        /// </summary>
        public bool EnableAutostart { get; set; }

        /// <summary>
        /// 用户是否已经在启动器那一页确认过"开机自启"这一项。
        ///
        /// 用来避免"回到范围页改一下,把人家的勾选冲掉" ——
        /// 只有他还没做过这个决定时,范围页才去动默认值。
        /// </summary>
        public bool AutostartChosen { get; set; }

        /// <summary>用户是否自己定过 DSH 本体目录。定过之后,别的页面就不再改它。</summary>
        public bool DshRootChosen { get; set; }

        /// <summary>用户是否自己定过启动器目录。</summary>
        public bool LauncherChosen { get; set; }

        /// <summary>用户是否自己定过组件目录。</summary>
        public bool ComponentsChosen { get; set; }

        public bool LaunchAfterwards { get; set; } = true;

        /// <summary>检测结果,检测页填。</summary>
        public ProbeReport Report { get; set; }

        /// <summary>装前体检结果(磁盘、端口、代理那几项),检测页填。</summary>
        public PreflightReport Preflight { get; set; }

        /// <summary>本次安装的结果,进度页填、完成页读。</summary>
        public Shared.Install.InstallReport Result { get; set; }

        /// <summary>回滚时撤销掉的东西(给失败页显示,让用户知道"现在是什么状态")。</summary>
        public System.Collections.Generic.List<string> RolledBack { get; set; } =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// 这次安装是不是需要管理员权限。
        ///
        /// 两种情况:
        ///   1. 装给所有用户 —— 要写 Program Files,必然要;
        ///   2. 需要开机自启 —— 注册"最高权限"的计划任务需要管理员。
        ///      启动器是 requireAdministrator,用 Run 键/启动文件夹在登录阶段会被静默拦掉,
        ///      只能走计划任务,而那个任务要管理员才能注册。
        ///
        /// 仅为本用户、且不要自启时全程不需要管理员 —— 这是那个选项的卖点。
        /// </summary>
        public bool NeedsElevation
        {
            get
            {
                return InstallerPlan.ElevationReasons(InstallerPlan.Build()).Count > 0;
            }
        }

        /// <summary>用户目录(仅为我安装时的落地位置)。</summary>
        public static string UserRoot
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeek Harness");
            }
        }

        /// <summary>公共目录(为所有用户安装时的落地位置)。</summary>
        public static string MachineRoot
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "DeepSeek Harness");
            }
        }

        /// <summary>按当前范围给出默认根目录。</summary>
        public string DefaultRoot
        {
            get { return Scope == InstallScope.AllUsers ? MachineRoot : UserRoot; }
        }
    }
}
