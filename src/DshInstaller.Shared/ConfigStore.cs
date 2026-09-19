using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DshInstaller.Shared
{
    /// <summary>安装器自己的状态文件,记住上次的选择和装到哪了。</summary>
    public sealed class InstallerState
    {
        public string DshRoot { get; set; }
        public string LauncherRoot { get; set; }
        public string ComponentsRoot { get; set; }
        public string NodeRoot { get; set; }
        public string Scope { get; set; }
        public bool Autostart { get; set; }
        public bool DesktopShortcut { get; set; }
        public bool InstallGit { get; set; }
        public bool InstallPnpm { get; set; }
        public bool InstallPython { get; set; }
        public string Language { get; set; }
        public string LastPage { get; set; }
        public string InstalledAt { get; set; }
        public string InstallerVersion { get; set; }

        /// <summary>
        /// 安装器自己留在哪(卸载时靠它再拉起来)。引导程序解压出来的那份,**装完不清理**。
        /// </summary>
        public string InstallerHome { get; set; }

        /// <summary>独立卸载程序的完整路径。卸载向导和被注册到"应用和功能"的都是它。</summary>
        public string UninstallerPath { get; set; }

        /// <summary>
        /// 安装时塞进 PATH 的目录。卸载器要按这份清单把 PATH 还原,
        /// 所以必须持久化 —— 不能靠"重新算一遍",用户可能改过组件目录。
        /// </summary>
        public List<string> PathEntries { get; set; } = new List<string>();
        public List<string> Steps { get; set; } = new List<string>();
    }

    /// <summary>状态文件的读写。放在 %LOCALAPPDATA%\DeepSeekHarness\installer-state.json。</summary>
    public static class ConfigStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string ConfigDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    WellKnown.ConfigFolderName);
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, WellKnown.ConfigFileName); }
        }

        public static string LogPath
        {
            get { return Path.Combine(ConfigDirectory, "installer.log"); }
        }

        public static InstallerState Load()
        {
            try
            {
                string path = ConfigPath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    InstallerState fromFile = JsonSerializer.Deserialize<InstallerState>(json, Options);
                    if (fromFile != null && !string.IsNullOrEmpty(fromFile.DshRoot))
                    {
                        return fromFile;
                    }
                }
            }
            catch
            {
            }

            // 状态文件不在(回滚清掉了 / 装到一半没写成)时,退到**注册表里那条卸载项**。
            //
            // 安装时会把三个目录连 PATH 条目一起写进注册表,而那条卸载项每次装都重写,
            // 比 %LOCALAPPDATA% 那份文件可靠得多。没有这道兜底,用户想卸载时会看到
            // "未找到本程序的安装记录"、一项都不让勾 —— 明明东西就在硬盘上(实测)。
            return LoadFromRegistry();
        }

        /// <summary>从注册表那条卸载项里把安装信息读回来(读不到返回 null)。</summary>
        private static InstallerState LoadFromRegistry()
        {
            Microsoft.Win32.RegistryKey[] roots = new Microsoft.Win32.RegistryKey[]
            {
                Microsoft.Win32.Registry.CurrentUser,
                Microsoft.Win32.Registry.LocalMachine,
            };

            for (int i = 0; i < roots.Length; i++)
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey key =
                        roots[i].OpenSubKey(WellKnown.RegistryUninstallKey))
                    {
                        if (key == null)
                        {
                            continue;
                        }

                        string dshRoot = key.GetValue("DshRoot") as string;
                        if (string.IsNullOrEmpty(dshRoot))
                        {
                            continue;
                        }

                        InstallerState state = new InstallerState
                        {
                            DshRoot = dshRoot,
                            LauncherRoot = key.GetValue("LauncherRoot") as string,
                            ComponentsRoot = key.GetValue("ComponentsRoot") as string,
                            InstallerHome = key.GetValue("InstallerHome") as string,
                            UninstallerPath = key.GetValue("UninstallString") as string,
                            Scope = i == 0 ? "user" : "machine",
                            InstallerVersion = key.GetValue("DisplayVersion") as string,
                        };

                        if (!string.IsNullOrEmpty(state.ComponentsRoot))
                        {
                            state.NodeRoot = Path.Combine(state.ComponentsRoot, "node");
                        }

                        string[] entries = key.GetValue("PathEntries") as string[];
                        if (entries != null)
                        {
                            state.PathEntries = new List<string>(entries);
                        }

                        return state;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        public static void Save(InstallerState state)
        {
            if (state == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                string json = JsonSerializer.Serialize(state, Options);
                File.WriteAllText(ConfigPath, json, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public static string ReadDshRoot()
        {
            InstallerState state = Load();
            return state == null ? null : state.DshRoot;
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    File.Delete(ConfigPath);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>安装日志。用户反馈问题时让他把这个文件发过来就行。</summary>
    public static class InstallLogger
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static string Path
        {
            get
            {
                if (_path == null)
                {
                    _path = ConfigStore.LogPath;
                }

                return _path;
            }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    string directory = System.IO.Path.GetDirectoryName(Path);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message;
                    File.AppendAllText(Path, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        public static void Section(string title)
        {
            Write("=== " + title + " ===");
        }
    }
}
