using System;
using System.Collections.Generic;

namespace DshInstaller.Shared.Detection
{
    /// <summary>
    /// 两个必装运行库的存在性检查。
    ///
    /// 为什么会单独有这么个小东西:安装器**自己**就跑在 WinUI3 上,
    /// 缺了这两个库连界面都出不来。所以"要不要提权"这件事必须能在**启动早期**判断,
    /// 那时候还没有检测报告可用。界面层、无人值守分支、安装步骤都从这里取同一个结论,
    /// 免得三处各写一份探测逻辑、哪天口径不一致。
    /// </summary>
    public static class RuntimeProbe
    {
        /// <summary>.NET 桌面运行时的版本号(没装返回 null)。</summary>
        public static string DotNetDesktopVersion
        {
            get { return EnvironmentProbe.FindDotNetDesktopVersion(WellKnown.DotNetDesktopMajor); }
        }

        /// <summary>Windows App Runtime 的版本号(没装返回 null)。</summary>
        public static string WindowsAppRuntimeVersion
        {
            get { return EnvironmentProbe.FindWindowsAppRuntimeVersion(); }
        }

        public static bool DotNetDesktopPresent
        {
            get { return !string.IsNullOrEmpty(DotNetDesktopVersion); }
        }

        public static bool WindowsAppRuntimePresent
        {
            get { return !string.IsNullOrEmpty(WindowsAppRuntimeVersion); }
        }

        /// <summary>
        /// Windows App Runtime 装了、**而且版本够**。
        ///
        /// 只看 Present 是不够的:旧版本一样会让安装器弹出
        /// "Required components ... version >= 8000.946.1701.0" 然后起不来(实测踩过)。
        /// </summary>
        public static bool WindowsAppRuntimeOk
        {
            get
            {
                return VersionAtLeast(WindowsAppRuntimeVersion,
                    WellKnown.WindowsAppRuntimeMinimumVersion);
            }
        }

        /// <summary>逐段比数字版本(不用 Version 类:微软这套版本号段数不固定)。</summary>
        public static bool VersionAtLeast(string actual, string minimum)
        {
            if (String.IsNullOrEmpty(actual) || String.IsNullOrEmpty(minimum))
            {
                return false;
            }

            string[] left = actual.Split('.');
            string[] right = minimum.Split('.');
            int count = left.Length > right.Length ? left.Length : right.Length;

            for (int i = 0; i < count; i++)
            {
                int a = 0;
                int b = 0;
                if (i < left.Length)
                {
                    Int32.TryParse(left[i], out a);
                }

                if (i < right.Length)
                {
                    Int32.TryParse(right[i], out b);
                }

                if (a != b)
                {
                    return a > b;
                }
            }

            return true;
        }

        /// <summary>缺任意一个就得走"下载 + 静默安装",而那是要管理员权限的。</summary>
        public static bool AnyMissing
        {
            get { return !DotNetDesktopPresent || !WindowsAppRuntimeOk; }
        }

        /// <summary>缺哪个说哪个,用来拼提示和日志(空列表 = 都齐了)。</summary>
        public static List<string> Missing()
        {
            List<string> missing = new List<string>();
            if (!DotNetDesktopPresent)
            {
                missing.Add(".NET " + WellKnown.DotNetDesktopMajor + SharedText.T(" 桌面运行时", " Desktop Runtime"));
            }

            if (!WindowsAppRuntimePresent)
            {
                missing.Add("Windows App Runtime 1.8");
            }
            else if (!WindowsAppRuntimeOk)
            {
                missing.Add("Windows App Runtime 1.8(当前 " + WindowsAppRuntimeVersion
                    + ",需要 " + WellKnown.WindowsAppRuntimeMinimumVersion + " 或更高)");
            }

            return missing;
        }

        public static string Describe()
        {
            string runtime = WindowsAppRuntimeVersion ?? "(缺)";
            if (!String.IsNullOrEmpty(WindowsAppRuntimeVersion) && !WindowsAppRuntimeOk)
            {
                runtime += "(版本过低,需要 " + WellKnown.WindowsAppRuntimeMinimumVersion + ")";
            }

            return ".NET " + WellKnown.DotNetDesktopMajor + "="
                + (DotNetDesktopVersion ?? "(缺)")
                + " / WinAppRuntime=" + runtime;
        }
    }
}
