using System;
using System.IO;

namespace DshInstaller
{
    /// <summary>
    /// 判断"当前用户不加提权能不能往这个目录里写东西"。
    ///
    /// 用途:向导里选目录时,如果范围是「仅为本用户安装」,而用户挑了个
    /// Program Files 之类的目录,得**当场**提醒他"这个目录照样要 UAC" ——
    /// 否则他会在点下安装之后才发现弹了管理员框,觉得程序在乱要权限(实测反馈)。
    ///
    /// 使用只读 ACL 估计,不创建探测文件:
    /// ACL、组策略、只读盘这些特殊情况都能如实反映出来,而白名单永远列不全。
    /// </summary>
    internal static class PathAccess
    {
        // Read-only ACL estimate: unknown permissions require elevation; no probe file is created.
        public static bool CanWriteWithoutElevation(string path)
        {
            try
            {
                string directory = Shared.Install.InstallPaths.Canonical(path);
                while (!Directory.Exists(directory))
                {
                    string parent = Path.GetDirectoryName(directory);
                    if (string.IsNullOrEmpty(parent)) return false;
                    directory = parent;
                }
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                var acl = new DirectoryInfo(directory).GetAccessControl();
                var rules = acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
                var needed = System.Security.AccessControl.FileSystemRights.Write;
                var allowed = (System.Security.AccessControl.FileSystemRights)0;
                foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules)
                {
                    if ((rule.PropagationFlags & System.Security.AccessControl.PropagationFlags.InheritOnly) != 0) continue;
                    var sid = (System.Security.Principal.SecurityIdentifier)rule.IdentityReference;
                    if (!sid.Equals(identity.User) && !principal.IsInRole(sid)) continue;
                    if (rule.AccessControlType == System.Security.AccessControl.AccessControlType.Deny
                        && (rule.FileSystemRights & needed) != 0) return false;
                    if (rule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow)
                        allowed |= rule.FileSystemRights;
                }
                return (allowed & needed) == needed;
            }
            catch { return false; }
        }
        /// <summary>
        /// 范围是「仅为本用户安装」、可这个目录又要管理员权限时给一句提醒;不需要就返回 null。
        /// </summary>
        public static string DescribeIfNeedsElevation(string path)
        {
            // 本来就是给所有用户装,弹 UAC 天经地义,不用多嘴
            if (InstallSession.Current.Scope == InstallScope.AllUsers)
            {
                return null;
            }

            if (CanWriteWithoutElevation(path))
            {
                return null;
            }

            return Localization.IsChinese
                ? "当前选择的是「仅为本用户安装」,但此目录需要管理员权限才能写入,安装时仍会弹出 UAC。"
                : "This is a per-user installation, but this directory requires administrator rights, so a UAC prompt will still appear.";
        }
    }
}
