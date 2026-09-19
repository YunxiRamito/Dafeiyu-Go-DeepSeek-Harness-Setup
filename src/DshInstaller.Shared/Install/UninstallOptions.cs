using System.Collections.Generic;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 卸载要用到的选择。
    ///
    /// 默认从安装时留下的状态文件(<see cref="InstallerState"/>)推导,
    /// 但用户可以覆盖 —— 尤其是"要不要删 .dsh 数据"和"要不要清 PATH",
    /// 这两项必须在界面上让用户自己决定(里面可能有会话记录和凭据)。
    /// </summary>
    public sealed class UninstallOptions
    {
        /// <summary>DSH 本体目录。</summary>
        public string DshRoot { get; set; }

        /// <summary>启动器目录。</summary>
        public string LauncherRoot { get; set; }

        /// <summary>便携组件目录。</summary>
        public string ComponentsRoot { get; set; }

        /// <summary>是不是装给所有用户的(决定清哪一段 PATH、删哪个目录)。</summary>
        public bool AllUsers { get; set; }

        /// <summary>
        /// 要不要连用户数据一起删(<c>%USERPROFILE%\.dsh</c>)。
        /// 里面有会话记录和登录凭据,默认 **false** —— 只删程序不删数据。
        /// </summary>
        public bool RemoveUserData { get; set; }

        // 逐项开关:界面上每一项都有勾选框,不是本安装器装的项会被禁用,自然也就不会被勾上。
        // 步骤按这些开关决定跑不跑。

        /// <summary>要不要删 DSH 本体。</summary>
        public bool RemoveDshCore { get; set; } = true;

        /// <summary>要不要删启动器。</summary>
        public bool RemoveLauncher { get; set; } = true;

        /// <summary>要不要删便携组件目录。</summary>
        public bool RemoveComponents { get; set; } = true;

        /// <summary>要不要清理安装时写进 PATH 的那些项。默认 true。</summary>
        public bool CleanPath { get; set; } = true;

        /// <summary>要不要删快捷方式。默认 true。</summary>
        public bool RemoveShortcuts { get; set; } = true;

        /// <summary>要不要删开机自启计划任务。默认 true。</summary>
        public bool RemoveAutostart { get; set; } = true;

        /// <summary>安装时写进 PATH 的项(从状态文件里读的)。</summary>
        public List<string> PathEntries { get; set; } = new List<string>();

        /// <summary>用户的 .dsh 目录(要删数据时用)。</summary>
        public string UserDataRoot { get; set; }

        /// <summary>演练模式:只走流程不落盘。</summary>
        public bool DryRun { get; set; }

        /// <summary>从状态文件推导默认值。</summary>
        public static UninstallOptions FromState(InstallerState state, bool allUsers)
        {
            UninstallOptions options = new UninstallOptions { AllUsers = allUsers };

            if (state != null)
            {
                options.DshRoot = state.DshRoot;
                options.LauncherRoot = state.LauncherRoot;
                options.ComponentsRoot = state.ComponentsRoot;
                options.PathEntries = state.PathEntries ?? new List<string>();
            }

            return options;
        }
    }
}
