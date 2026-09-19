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
    /// 判据不是"路径字符串像不像系统目录",而是**真的试着写一个临时文件**:
    /// ACL、组策略、只读盘这些特殊情况都能如实反映出来,而白名单永远列不全。
    /// </summary>
    internal static class PathAccess
    {
        public static bool CanWriteWithoutElevation(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return true;
                }

                string directory;
                try
                {
                    directory = Path.GetFullPath(path.Trim().Trim('"'));
                }
                catch
                {
                    // 路径本身就不合法,这里不负责报错(别的地方会管),当作"不拦"
                    return true;
                }

                // 目录可能还不存在,往上找最近的一个已存在的祖先
                while (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    string parent = Path.GetDirectoryName(directory.TrimEnd('\\'));
                    if (string.IsNullOrEmpty(parent)
                        || string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    directory = parent;
                }

                if (!Directory.Exists(directory))
                {
                    // 连盘符都没解析出来 —— 不猜,当作能写,免得误报
                    return true;
                }

                string probe = Path.Combine(
                    directory, ".dsh-write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");

                try
                {
                    using (FileStream stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write))
                    {
                        stream.WriteByte(0);
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(probe))
                        {
                            File.Delete(probe);
                        }
                    }
                    catch
                    {
                    }
                }

                return true;
            }
            catch
            {
                // 写不进去(拒绝访问之类)就是我们要找的信号
                return false;
            }
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
