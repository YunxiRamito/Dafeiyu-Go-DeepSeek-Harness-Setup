using System;
using System.Collections.Generic;

namespace DshInstaller
{
    /// <summary>
    /// 界面文案。中英双语,键在下面各页里用 <see cref="L.T"/> 取。
    ///
    /// 英文一律用书面语,不要口语化(例如用 "Select the installation directory" 而不是
    /// "Pick where to put it");句末该带句号的带上。
    /// </summary>
    internal static class Localization
    {
        internal enum Language
        {
            Chinese,
            English,
        }

        private static Language _current = Detect();

        static Localization()
        {
            // 静态字段初始化跑在静态构造之前,所以这里 _current 已经是探测结果。
            // 同步一次,保证共享库的组件名称/说明也和界面语言一致。
            Shared.SharedText.UseChinese = _current == Language.Chinese;
        }

        private static readonly Dictionary<string, string[]> Words =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // 键 -> [中文, English]
            { "app.name",        new[] { "Dafeiyu-Go", "Dafeiyu-Go" } },
            { "app.subtitle",    new[] { "DeepSeek Harness 安装与启动管理", "Installer & Launcher for DeepSeek Harness" } },
            { "btn.back",        new[] { "上一步", "Back" } },
            { "btn.next",        new[] { "下一步", "Next" } },
            { "btn.start",       new[] { "开始安装", "Install" } },
            { "btn.continue",    new[] { "继续", "Continue" } },
            { "btn.finish",      new[] { "完成", "Finish" } },
            { "btn.browse",      new[] { "浏览…", "Browse…" } },
            { "btn.retry",       new[] { "重试", "Retry" } },
            { "btn.cancel",      new[] { "取消", "Cancel" } },

            // 导出日志:出问题时让用户把日志发过来,不用他翻目录找文件
            { "log.export.button",  new[] { "导出安装日志", "Export installation log" } },
            { "log.export.hint",    new[] { "安装失败或出现异常时，可提供日志协助排查。", "If installation fails or an error occurs, provide the log for troubleshooting." } },
            { "log.export.working", new[] { "正在收集日志…", "Collecting logs…" } },
            { "log.export.saved",   new[] { "已导出：", "Saved to:" } },
            { "log.export.failed",  new[] { "导出失败，请稍后再试。", "Could not export the log. Please try again." } },
            { "log.export.open",    new[] { "打开文件夹", "Open folder" } },
            { "log.export.title",   new[] { "安装日志", "Installation log" } },

            { "welcome.slogan",  new[] { "欢迎使用 Dafeiyu-Go", "Welcome to Dafeiyu-Go" } },
            { "welcome.desc",    new[] { "面向 DeepSeek Harness 的安装与启动管理工具", "Installer and launcher for DeepSeek Harness" } },
            { "welcome.terms",   new[] { "继续即表示已阅读并同意下方声明", "By continuing, you acknowledge the statements below." } },
            { "welcome.note1",   new[] { "安装程序为在线安装，可能会产生流量费用", "This is an online installation and may consume network data." } },
            { "welcome.note2",   new[] { "Node / Git / pnpm / Python 使用便携版本；运行库可能需要系统安装。提供独立卸载程序", "Node / Git / pnpm / Python use portable versions. Runtimes may require system installation. A separate uninstaller is provided." } },
            { "welcome.note3",   new[] { "只支持 Windows 10 (1809) 及 Windows 11 系统", "Supported on Windows 10 (1809) and Windows 11 only." } },
            { "welcome.repair",  new[] { "修复安装", "Repair installation" } },
            { "welcome.repair.hint", new[] { "补齐缺失或被安全软件移除的安装文件，保留用户数据。", "Restore installation files that are missing or removed by security software while retaining user data." } },
            { "welcome.repair.norecord", new[] { "未找到安装记录，无法修复。请先安装。", "No installation record was found. Install the program before using repair." } },

            { "preview.notice", new[] { "以下为只读安装计划；查看计划不会开始安装。安装包引导阶段仍可能解压、写日志或补装运行库。", "This is a read-only installation plan; viewing it does not start installation. The package bootstrap may already have extracted files, written logs or installed runtimes." } },
            { "preview.system", new[] { "系统改动", "System changes" } },
            { "preview.changes", new[] { "写入所选目录、安装记录及卸载入口；按选择创建快捷方式、自启计划任务和组件 PATH；缺失运行库将按计划补装。", "Write selected directories, install records and uninstall entry; create selected shortcuts, startup task and component PATH entries; install missing runtimes when enabled." } },
            { "preview.data", new[] { "保留用户数据", "Preserve user data" } },
            { "preview.keep", new[] { "保留 .dsh 和 plugins 中的设置、技能、插件与会话；安装或修复仍可能更新程序文件及所选插件。", "Retain settings, skills, plug-ins and sessions in .dsh and plugins; installation or repair may update program files and selected plug-ins." } },
            { "preview.network", new[] { "联网要求", "Network requirement" } },
            { "preview.online", new[] { "在线部署启动器及缺失组件需要联网；修复也不保证离线可用。", "Online launcher deployment and missing components require network access; repair is not guaranteed to work offline." } },
            { "preview.components", new[] { "组件处理", "Component actions" } },
            { "preview.reuse", new[] { "复用", "Reuse" } },
            { "preview.install", new[] { "检查现有文件；缺失时安装", "Check existing files; install if missing" } },
            { "preview.permissions", new[] { "只读权限估计；实际写入仍受 ACL、策略及目录变动影响", "Read-only permission estimate; actual writes remain subject to ACLs, policy and directory changes" } },
            { "preview.reason.scope", new[] { "所有用户范围", "all-user scope" } },
            { "preview.reason.autostart", new[] { "注册自启任务", "register startup task" } },
            { "preview.reason.runtime", new[] { "补装系统运行库", "install system runtimes" } },
            { "preview.reason.directory", new[] { "目标目录权限不足或无法确认", "target directory access denied or uncertain" } },
            { "scope.title",     new[] { "选择安装范围", "Installation Scope" } },
            { "scope.desc",      new[] { "决定安装的用户", "Choose which users the installation applies to." } },
            { "scope.user",      new[] { "仅为本用户安装", "Install for this user only" } },
            { "scope.user.desc", new[] { "安装到当前用户目录；开机自启、运行库或受保护目录可能需要管理员权限", "Installs to the current user's directory. Startup tasks, runtimes or protected locations may require administrator rights." } },
            { "scope.machine",   new[] { "为所有用户安装", "Install for all users" } },
            { "scope.machine.desc", new[] { "需要管理员（UAC）权限", "Requires administrator (UAC) rights." } },

            { "detect.title",    new[] { "环境检测", "Environment Check" } },
            { "detect.desc",     new[] { "检测DSH安装前置环境", "Checks the prerequisites for installing DSH." } },

            { "components.title", new[] { "安装缺失组件", "Install Missing Components" } },
            { "components.desc",  new[] { "选择安装位置", "Choose the installation location." } },
            // 本机已有够新的组件:默认直接用,不再下一份便携版
            { "components.reuse",       new[] { "已检测到，将直接使用", "Detected; will be reused" } },
            { "components.reuse.force", new[] { "已检测到；勾选则重新下载便携版", "Detected; select to download a portable copy" } },
            { "components.force",       new[] { "强制重新下载组件", "Re-download components" } },
            { "components.force.hint",  new[] { "默认复用符合版本要求的现有组件。勾选后将重新下载便携版本。", "Existing components that meet the version requirements are reused by default. Select this option to download portable copies." } },

            { "dsh.title",       new[] { "DSH 安装位置", "DSH Installation Directory" } },
            { "dsh.desc",        new[] { "选择 DeepSeek Harness 本体的安装目录", "Select the installation directory for the DeepSeek Harness core." } },

            { "launcher.title",  new[] { "启动器安装位置", "Launcher Installation Directory" } },
            { "launcher.desc",   new[] { "选择 Dafeiyu-Go 启动器的安装目录", "Select the installation directory for the Dafeiyu-Go launcher." } },

            { "confirm.title",   new[] { "确认安装", "Confirm Installation" } },
            { "confirm.desc",    new[] { "确认安装清单", "Review the installation summary below." } },

            { "progress.title",  new[] { "正在安装", "Installing" } },
            { "progress.subtitle", new[] { "正在本机部署 Dafeiyu-Go 与 DeepSeek Harness 相关组件，请不要关闭此窗口。", "Dafeiyu-Go and its DeepSeek Harness components are being installed. Please do not close this window." } },
            { "progress.showlog", new[] { "查看日志", "View log" } },
            { "log.exec", new[] { "执行:", "Run:" } },
            { "progress.preparing", new[] { "准备中…", "Preparing…" } },
            { "progress.dryrun", new[] { "演练模式：跳过安装操作，仍可能写入日志与临时文件。外层引导程序仍会处理解压与运行库。", "Dry run: installation actions are skipped; logs and temporary files may still be written. The bootstrapper still handles extraction and runtimes." } },
            { "progress.begin",  new[] { "开始安装。", "Starting the installation." } },
            { "progress.crashed", new[] { "安装过程出现意外错误：", "The installation encountered an unexpected error: " } },
            { "progress.alldone", new[] { "全部完成", "All tasks completed" } },
            { "progress.cancontinue", new[] { "可以进入下一步", "You may proceed." } },
            { "progress.stopped", new[] { "停止", "Stopped" } },
            { "progress.finished", new[] { "安装完成。", "Installation complete." } },
            { "progress.finishedbad", new[] { "安装未全部完成，详情见上方日志。", "The installation did not complete. See the log above for details." } },
            { "progress.steprunning", new[] { "进行中", "In progress" } },
            { "progress.stepdone", new[] { "完成", "Done" } },
            { "progress.stepskip", new[] { "跳过", "Skipped" } },
            { "progress.stepfail", new[] { "失败", "Failed" } },
            { "progress.cancelling", new[] { "正在取消…", "Cancelling…" } },
            { "rollback.begin", new[] { "正在回滚：撤销本次安装已完成的改动。", "Rolling back: undoing the changes made by this installation." } },
            { "rollback.running", new[] { "回滚中", "Rolling back" } },
            { "rollback.nothing", new[] { "暂无需要回滚的改动。", "No changes require rollback." } },
            { "rollback.done", new[] { "回滚完成。", "Rollback complete." } },
            { "progress.closing", new[] { "正在回滚已完成的改动，完成后将自动退出。", "Undoing completed changes. Setup will exit when rollback finishes." } },
            { "progress.exiting", new[] { "已回滚，正在退出。", "Changes have been undone; exiting." } },
            { "rollback.failed", new[] { "回滚过程中出现问题：", "A problem occurred during rollback: " } },
            { "done.title",      new[] { "安装完成", "Installation Complete" } },
            { "done.partial.title", new[] { "安装未完全完成", "Installation partially complete" } },
            { "done.partial.desc", new[] { "主要组件已就绪，现在即可使用 Dafeiyu-Go。下列可选组件未能安装，不影响使用。", "The main components are ready and Dafeiyu-Go can be used now. The optional components below were not installed; this does not affect normal use." } },
            { "done.partial.label", new[] { "未安装（可选）", "Not installed (optional)" } },

            { "done.skippedcount", new[] { " · 跳过 ", " · skipped " } },
            { "uninstall.title", new[] { "卸载 Dafeiyu-Go", "Uninstall Dafeiyu-Go" } },
            { "uninstall.items.title", new[] { "选择要删除的内容", "Choose what to remove" } },
            { "uninstall.notOurs", new[] { "非本安装程序安装，不提供卸载。", "Not installed by this setup; it will not be removed." } },
            { "uninstall.noRecord", new[] { "未找到本程序的安装记录，因此下列各项均已被禁用。可以直接删除对应目录，或保留它们。", "No installation record from this setup was found, so all items below are disabled. You may delete the directories manually or keep them." } },
            { "uninstall.item.launcher.desc", new[] { "启动器目录及其中全部文件。", "The launcher directory and everything in it." } },
            { "uninstall.item.dsh.desc", new[] { "DSH 本体的 node_modules 与运行文件。", "The DSH core (node_modules and runtime files)." } },
            { "uninstall.item.components.desc", new[] { "本程序解压的便携版 Node / Git / pnpm / Python。手动放入此目录的文件也会一并被删除。", "The portable Node / Git / pnpm / Python extracted by this setup. Anything you placed in this directory will be removed as well." } },
            { "uninstall.item.path.desc", new[] { "安装时写入 PATH 的条目；自行添加的条目不受影响。", "The PATH entries added during setup; entries you added yourself are unaffected." } },
            { "uninstall.item.shortcut.desc", new[] { "桌面与开始菜单里由本程序创建的快捷方式。", "Shortcuts created by this setup on the desktop and in the Start menu." } },
            { "uninstall.item.autostart.desc", new[] { "开机自启的计划任务。", "The scheduled task that starts the program at sign-in." } },
            { "uninstall.items.desc", new[] { "以下内容将被移除；可以单独决定是否清理 PATH 与用户数据。", "The items below will be removed. You can decide separately whether to clean PATH and user data." } },
            { "uninstall.confirm.question", new[] { "确定要卸载 Dafeiyu-Go 吗？", "Are you sure you want to uninstall Dafeiyu-Go?" } },
            { "uninstall.confirm.desc", new[] { "请确认。下一步可以选择具体删除哪些内容。", "Please confirm. You can choose exactly what to remove in the next step." } },
            { "uninstall.confirm.detail", new[] { "卸载会移除 DSH 本体、启动器、便携组件、快捷方式与开机自启。已经开始的会话会中断。", "Uninstalling removes the DSH core, the launcher, portable components, shortcuts and the startup entry. Running sessions will be interrupted." } },
            { "uninstall.confirm.keep", new[] { "予以保留的内容", "What is kept" } },
            { "uninstall.confirm.keep.desc", new[] { "用户数据（设置、技能、插件、会话记录与登录凭据）默认保留；PATH 里自行添加的条目不会被修改。", "User data (settings, skills, plug-ins, session history and credentials) is kept by default, and PATH entries you added yourself are left untouched." } },
            { "uninstall.desc",  new[] { "以下内容将被移除，请确认。", "The following items will be removed. Please confirm." } },
            { "uninstall.options", new[] { "卸载选项", "Removal options" } },
            { "uninstall.cleanpath", new[] { "清理安装程序写入的 PATH 条目", "Remove the PATH entries added by setup" } },
            { "uninstall.cleanpath.hint", new[] { "仅移除安装程序添加的条目，原有 PATH 不受影响。", "Only entries added by setup are removed; the existing PATH is unaffected." } },
            { "uninstall.userdata", new[] { "保留我的设置、技能、插件与会话记录", "Keep my settings, skills, plug-ins and sessions" } },
            { "uninstall.userdata.hint", new[] { "默认保留，重新安装后可继续使用。取消勾选将一并删除，无法恢复。", "Kept by default for use after reinstallation. Clear this option to delete the data; deletion cannot be undone." } },
            { "uninstall.item.launcher", new[] { "启动器", "Launcher" } },
            { "uninstall.item.dsh", new[] { "DSH 本体", "DSH core" } },
            { "uninstall.item.components", new[] { "便携组件", "Portable components" } },
            { "uninstall.item.path", new[] { "PATH 项", "PATH entry" } },
            { "uninstall.item.autostart", new[] { "开机自启计划任务", "Startup scheduled task" } },
            { "uninstall.item.shortcut", new[] { "快捷方式", "Shortcuts" } },
            { "uninstall.nothing", new[] { "未找到安装记录，目录可能已被手动删除。继续操作仍会清理快捷方式与开机自启。", "No installation record was found; the directories may already have been removed. Continuing will still clean up shortcuts and startup entries." } },
            { "uninstall.progress.title", new[] { "正在卸载", "Uninstalling" } },
            { "uninstall.progress.subtitle", new[] { "正在移除已安装的文件与配置，请勿关闭此窗口。", "Installed files and settings are being removed. Do not close this window." } },
            { "uninstall.done.title", new[] { "卸载完成", "Uninstall complete" } },
            { "failed.title", new[] { "安装未完成", "Installation did not complete" } },
            { "failed.title.cancelled", new[] { "安装已取消", "Installation cancelled" } },
            { "failed.desc", new[] { "部分必需组件未能安装。已完成的改动已全部撤销，本机已恢复到安装前的状态。", "Some required components could not be installed. All changes have been undone; the computer is back to its previous state." } },
            { "failed.desc.cancelled", new[] { "安装被取消，已经做过的改动已全部撤销。", "The installation was cancelled. All changes have been undone." } },
            { "failed.reasons", new[] { "未能完成的步骤", "Steps that did not complete" } },
            { "failed.rollback", new[] { "已撤销的内容", "Changes that were undone" } },
            { "failed.manual", new[] { "也可以手动下载后重试", "You may also download manually and try again" } },
            { "failed.nothing", new[] { "没有记录到具体原因，详情见日志文件。", "No specific reason was recorded. See the log file for details." } },
            { "failed.norollback", new[] { "没有需要撤销的改动。", "There were no changes to undo." } },
            { "failed.link.launcher", new[] { "启动器", "Launcher" } },
            { "uninstall.done.desc", new[] { "Dafeiyu-Go 已从本机移除。", "Dafeiyu-Go has been removed from this computer." } },
            { "uninstall.leftover", new[] { "部分文件未能删除，通常因文件被占用，重启后可再次尝试。", "Some files could not be deleted, usually because they are in use. Try again after a restart." } },
            { "btn.uninstall", new[] { "卸载", "Uninstall" } },
            { "done.failedcount", new[] { " · 失败 ", " · failed " } },

            // 组件行右侧的状态词
            { "state.pending",   new[] { "等待", "Pending" } },
            { "state.checking",  new[] { "检查中", "Checking" } },
            { "state.ready",     new[] { "已就绪", "Ready" } },
            { "state.missing",   new[] { "缺失", "Missing" } },
            { "state.optional",  new[] { "可选", "Optional" } },
            { "state.error",     new[] { "异常", "Error" } },

            { "detect.running",  new[] { "正在检测本机环境…", "Checking your system…" } },
            { "detect.health",   new[] { "安装前检查", "Pre-installation checks" } },
            { "detect.health.hint", new[] { "检查影响安装的项目，并显示问题及处理建议。", "Checks items that affect installation and displays issues with suggested actions." } },
            { "detect.health.ok", new[] { "检查未发现问题，可以继续。", "No issues were found by these checks. You may proceed." } },
            { "detect.allgood",  new[] { "环境齐全，可以直接安装", "All prerequisites are present. You may proceed." } },
            { "detect.needfix",  new[] { "有组件缺失，可能需要补齐", "Some components are missing and may need to be installed." } },
            { "detect.winbad",   new[] { "系统版本过低，无法继续", "This version of Windows is not supported. Setup cannot continue." } },
        };

        public static Language Current
        {
            get { return _current; }
            set
            {
                _current = value;
                // 共享库的组件名称与说明也归这里管
                Shared.SharedText.UseChinese = value == Language.Chinese;
            }
        }

        public static bool IsChinese
        {
            get { return _current == Language.Chinese; }
        }

        /// <summary>取文案。找不到键就把键本身返回,方便一眼看出漏翻。</summary>
        public static string T(string key)
        {
            string[] pair;
            if (Words.TryGetValue(key, out pair))
            {
                return IsChinese ? pair[0] : pair[1];
            }

            return key;
        }

        private static Language Detect()
        {
            try
            {
                string name = System.Globalization.CultureInfo.CurrentUICulture.Name;
                if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return Language.Chinese;
                }
            }
            catch
            {
            }

            return Language.English;
        }
    }
}
