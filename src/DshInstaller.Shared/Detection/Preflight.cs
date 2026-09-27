using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using DshInstaller.Shared.Detection;

namespace DshInstaller.Shared
{
    /// <summary>体检里的一项。Risk=true 的项在界面上刷浅黄,并带一句「怎么办」。</summary>
    public sealed class PreflightItem
    {
        public string Id { get; set; }

        /// <summary>项目名,例如「磁盘可用空间」。</summary>
        public string Label { get; set; }

        /// <summary>查到的事实,例如「可用 42.1 GB」。只写事实,不写建议。</summary>
        public string Value { get; set; }

        /// <summary>有没有风险(要让用户先处理一下)。</summary>
        public bool Risk { get; set; }

        /// <summary>有风险时的一句话:怎么办。</summary>
        public string Advice { get; set; }
    }

    /// <summary>装前体检的结论。</summary>
    public sealed class PreflightReport
    {
        public List<PreflightItem> Items { get; } = new List<PreflightItem>();

        public bool HasRisk
        {
            get
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].Risk)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// 装前体检。
    ///
    /// 和「环境检测」的区别:检测回答"缺不缺组件",体检回答"现在装会不会出事" ——
    /// 磁盘够不够、系统版本能不能装、机器上是不是已经有一份 DSH、8787 被谁占着、
    /// 是不是在有代理的网络里。这五条都是**用户自己动手就能解决**的事,
    /// 所以每条风险项都必须带一句"怎么办",不能只标个黄。
    ///
    /// 探测结果主要复用 <see cref="EnvironmentProbe"/> 那份(它已经把 Windows 版本、
    /// DSH 安装位置都查过了),这里只补它没查的:磁盘、端口、代理。
    /// </summary>
    public static class Preflight
    {
        /// <summary>留多少空间才算够。装完大概 1 GB 出头,3 GB 是留了富余的数。</summary>
        public const long MinimumFreeBytes = 3L * 1024 * 1024 * 1024;

        public static PreflightReport Run(ProbeReport probe, string dshRootHint = null)
        {
            PreflightReport report = new PreflightReport();
            report.Items.Add(CheckDisk());
            report.Items.Add(CheckWindows(probe));
            report.Items.Add(CheckExistingDsh(probe, dshRootHint));
            report.Items.Add(CheckPort(probe));
            report.Items.Add(CheckProxy());
            return report;
        }

        // ---------------------------------------------------------------- 磁盘

        private static PreflightItem CheckDisk()
        {
            PreflightItem item = new PreflightItem
            {
                Id = "disk",
                Label = SharedText.T("磁盘可用空间", "Free disk space"),
            };

            try
            {
                // 用户范围和全局范围可能落在两个盘上,取更紧的那个报(报错的那条才有用)。
                List<string> roots = new List<string>();
                AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.Windows));

                long smallest = long.MaxValue;
                string tightestRoot = null;
                List<string> names = new List<string>();

                for (int i = 0; i < roots.Count; i++)
                {
                    try
                    {
                        DriveInfo drive = new DriveInfo(roots[i]);
                        if (!drive.IsReady)
                        {
                            continue;
                        }

                        if (!names.Contains(drive.Name))
                        {
                            names.Add(drive.Name);
                        }

                        if (drive.AvailableFreeSpace < smallest)
                        {
                            smallest = drive.AvailableFreeSpace;
                            tightestRoot = drive.Name;
                        }
                    }
                    catch
                    {
                    }
                }

                if (tightestRoot == null)
                {
                    item.Value = SharedText.T("未能读取", "Could not read");
                    return item;
                }

                item.Value = SharedText.T(
                    tightestRoot + " 可用 " + FormatSize(smallest),
                    FormatSize(smallest) + " free on " + tightestRoot);

                if (smallest < MinimumFreeBytes)
                {
                    item.Risk = true;
                    item.Advice = SharedText.T(
                        "安装大约需要 3 GB，请先清理磁盘再继续。",
                        "Setup needs about 3 GB. Please free up some space first.");
                }

                return item;
            }
            catch
            {
                item.Value = SharedText.T("未能读取", "Could not read");
                return item;
            }
        }

        private static void AddRoot(List<string> roots, string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                string root = Path.GetPathRoot(path);
                if (!string.IsNullOrWhiteSpace(root) && !roots.Contains(root))
                {
                    roots.Add(root);
                }
            }
            catch
            {
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
            {
                return (bytes / 1024.0 / 1024 / 1024).ToString("0.#") + " GB";
            }

            if (bytes >= 1024L * 1024)
            {
                return (bytes / 1024.0 / 1024).ToString("0.#") + " MB";
            }

            return (bytes / 1024.0).ToString("0.#") + " KB";
        }

        // ---------------------------------------------------------------- Windows

        private static PreflightItem CheckWindows(ProbeReport probe)
        {
            PreflightItem item = new PreflightItem
            {
                Id = "windows",
                Label = SharedText.T("系统版本", "Windows version"),
            };

            if (probe == null)
            {
                item.Value = SharedText.T("未能读取", "Could not read");
                return item;
            }

            item.Value = (probe.WindowsName ?? SharedText.T("未知", "Unknown"))
                + " (build " + probe.WindowsBuild + ")";

            if (!probe.IsWindowsSupported)
            {
                item.Risk = true;
                item.Advice = SharedText.T(
                    "需要 Windows 10 1809 或更高版本，请先升级系统。",
                    "Windows 10 (1809) or later is required. Please update Windows first.");
            }

            return item;
        }

        // ---------------------------------------------------------------- 已装 DSH

        private static PreflightItem CheckExistingDsh(ProbeReport probe, string dshRootHint)
        {
            PreflightItem item = new PreflightItem
            {
                Id = "dsh",
                Label = SharedText.T("已安装的 DSH", "Existing DSH"),
            };

            ComponentStatus dsh = probe == null ? null : probe["dsh"];
            if (dsh == null || dsh.State != DetectState.Ready)
            {
                item.Value = SharedText.T("未发现，本次会装上", "Not found; it will be installed");
                return item;
            }

            string version = string.IsNullOrWhiteSpace(dsh.DetectedVersion)
                ? SharedText.T("版本未知", "version unknown")
                : dsh.DetectedVersion;

            item.Value = version + " · " + dsh.Path;

            if (string.IsNullOrWhiteSpace(dsh.DetectedVersion))
            {
                // 装是装在硬盘上,却读不出包版本 —— 多半是装了一半或者被手动改过。
                item.Risk = true;
                item.Advice = SharedText.T(
                    "这一份可能不完整，建议先卸载再重新安装。",
                    "This copy may be incomplete. Uninstall it first, then install again.");
            }

            return item;
        }

        // ---------------------------------------------------------------- 端口

        private static PreflightItem CheckPort(ProbeReport probe)
        {
            PreflightItem item = new PreflightItem
            {
                Id = "port",
                Label = SharedText.T("端口 " + WellKnown.ServicePort, "Port " + WellKnown.ServicePort),
            };

            bool busy = IsPortBusy(WellKnown.ServicePort);
            if (!busy)
            {
                item.Value = SharedText.T("空闲", "Free");
                return item;
            }

            // 已经装了 DSH 又有东西占着 8787,最可能就是它自己在跑 —— 那是正常现象,
            // 标黄只会让用户白紧张。真占用的判定留给"没装 DSH 却占着"这种可疑情况。
            ComponentStatus dsh = probe == null ? null : probe["dsh"];
            bool dshInstalled = dsh != null && dsh.State == DetectState.Ready;

            if (dshInstalled)
            {
                item.Value = SharedText.T("已被占用（可能是 DSH 正在运行）", "In use (DSH may be running)");
                return item;
            }

            item.Value = SharedText.T("已被其他程序占用", "In use by another program");
            item.Risk = true;
            item.Advice = SharedText.T(
                "请先关掉占用该端口的程序；启动器也会自动换一个可用端口。",
                "Close the program using this port first; the launcher can also pick another free port.");
            return item;
        }

        /// <summary>真实占一下试试。只试回环地址,不碰外部网卡。</summary>
        public static bool IsPortBusy(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return false;
            }
            catch (SocketException)
            {
                return true;
            }
            catch
            {
                // 查不出来就说"不占",免得无凭无据地吓用户
                return false;
            }
            finally
            {
                try
                {
                    if (listener != null)
                    {
                        listener.Stop();
                    }
                }
                catch
                {
                }
            }
        }

        // ---------------------------------------------------------------- 代理

        private static PreflightItem CheckProxy()
        {
            PreflightItem item = new PreflightItem
            {
                Id = "proxy",
                Label = SharedText.T("网络代理", "Network proxy"),
            };

            string system = ReadSystemProxy();
            if (string.IsNullOrWhiteSpace(system))
            {
                item.Value = SharedText.T("未检测到系统代理", "No system proxy detected");
                return item;
            }

            item.Value = SharedText.T("检测到 " + system, "Detected " + system);

            // 代理本身不是问题,只是要提醒一句:装不动的时候知道去哪儿改。
            item.Advice = SharedText.T(
                "下载会跟随系统代理；如果一直失败，可在下载前改用自定义代理。",
                "Downloads will follow the system proxy. If they keep failing, set a custom proxy before downloading.");
            return item;
        }

        /// <summary>
        /// 读系统代理。先看 Internet 选项(Windows 上真正生效的那份),
        /// 没有再退回 .NET 的解析(它还能看到环境变量里的代理)。
        /// </summary>
        public static string ReadSystemProxy()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (key != null)
                    {
                        object enabled = key.GetValue("ProxyEnable");
                        bool on = enabled != null && Convert.ToInt32(enabled) != 0;
                        if (on)
                        {
                            object server = key.GetValue("ProxyServer");
                            if (server != null && !string.IsNullOrWhiteSpace(server.ToString()))
                            {
                                return server.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                Uri resolved = ProxySupport.ResolveSystemProxy();
                if (resolved != null)
                {
                    return resolved.Authority;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
