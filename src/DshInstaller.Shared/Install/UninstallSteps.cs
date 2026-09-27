using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 内置的卸载步骤。
    ///
    /// 直接复用安装那套 <see cref="InstallStep"/> / <see cref="InstallRunner"/> ——
    /// 执行器本来就是个"顺序跑一串动作 + 上报进度"的东西,跟装还是卸无关。
    /// 卸载选项通过闭包带进各步骤,不去污染通用的 <see cref="InstallContext"/>。
    /// </summary>
    public static class UninstallSteps
    {
        public const string IdStop = "stop";
        public const string IdAutostart = "autostart";
        public const string IdShortcut = "shortcut";
        public const string IdLauncher = "launcher";
        public const string IdDsh = "dsh";
        public const string IdPath = "path";
        public const string IdUserData = "userdata";
        public const string IdRegistry = "registry";
        public const string IdState = "state";
        public const string IdVerify = "verify";

        /// <summary>按选项挑出要跑的卸载步骤,顺序就是执行顺序。</summary>
        public static List<InstallStep> BuildPlan(UninstallOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            List<InstallStep> plan = new List<InstallStep>();

            // 先停进程,否则文件被占用删不掉
            plan.Add(Step(IdStop,
                SharedText.T("结束相关进程", "Stop running processes"),
                delegate(InstallContext context, CancellationToken token)
                {
                    return Task.Run(delegate { StopProcesses(context, options); }, token);
                }));

            if (options.RemoveAutostart)
            {
                plan.Add(Step(IdAutostart,
                    SharedText.T("移除开机自启", "Remove startup on boot"),
                    delegate(InstallContext context, CancellationToken token)
                    {
                        return Task.Run(delegate { RemoveAutostart(context, options); }, token);
                    }));
            }

            if (options.RemoveShortcuts)
            {
                plan.Add(Step(IdShortcut,
                    SharedText.T("删除快捷方式", "Delete shortcuts"),
                    delegate(InstallContext context, CancellationToken token)
                    {
                        return Task.Run(delegate { RemoveShortcuts(context, options); }, token);
                    }));
            }

            if (options.RemoveLauncher)
            {
                plan.Add(Step(IdLauncher,
                    SharedText.T("删除启动器", "Delete the launcher"),
                    delegate(InstallContext context, CancellationToken token)
                    {
                        return Task.Run(delegate
                        {
                            DeleteDirectory(context, options, options.LauncherRoot,
                                SharedText.T("启动器", "launcher"));
                        }, token);
                    }));
            }

            if (options.RemoveDshCore || options.RemoveComponents)
            {
                plan.Add(Step(IdDsh,
                    SharedText.T("删除 DSH 本体与组件", "Delete the DSH core and components"),
                    delegate(InstallContext context, CancellationToken token)
                    {
                        return Task.Run(delegate
                        {
                            if (options.RemoveDshCore)
                            {
                                // 用户数据就在本体目录里(便携安装):.dsh 是技能/会话,plugins 是插件。
                                // 默认这两个留着 —— 卸载的是程序,不是人家攒下来的东西。
                                List<string> keep = new List<string>();
                                if (options.KeepUserData && !string.IsNullOrWhiteSpace(options.DshRoot))
                                {
                                    keep.Add(Path.Combine(options.DshRoot, ".dsh"));
                                    keep.Add(Path.Combine(options.DshRoot, "plugins"));
                                }

                                DeleteDirectory(context, options, options.DshRoot,
                                    SharedText.T("DSH 本体", "DSH core"), keep);
                            }

                            if (options.RemoveComponents)
                            {
                                DeleteDirectory(context, options, options.ComponentsRoot,
                                    SharedText.T("便携组件", "portable components"));
                            }
                        }, token);
                    }));
            }

            if (options.CleanPath)
            {
                plan.Add(Step(IdPath,
                    SharedText.T("清理环境变量 PATH", "Clean the PATH environment variable"),
                    delegate(InstallContext context, CancellationToken token)
                    {
                        return Task.Run(delegate { CleanPath(context, options); }, token);
                    }));
            }

            if (options.RemoveUserData)
            {
                plan.Add(Step(IdUserData,
                    SharedText.T("删除用户数据(.dsh)", "Delete user data (.dsh)"),
                    delegate(InstallContext context, CancellationToken token)
                    {
                        return Task.Run(delegate
                        {
                            DeleteDirectory(context, options, options.UserDataRoot,
                                SharedText.T("用户数据", "user data"));
                        }, token);
                    }));
            }

            plan.Add(Step(IdRegistry,
                SharedText.T("移除卸载入口", "Remove the uninstall entry"),
                delegate(InstallContext context, CancellationToken token)
                {
                    return Task.Run(delegate { RemoveUninstallEntry(context, options); }, token);
                }));

            plan.Add(Step(IdState,
                SharedText.T("清理安装记录", "Clear install records"),
                delegate(InstallContext context, CancellationToken token)
                {
                    return Task.Run(delegate
                    {
                        if (options.DryRun)
                        {
                            context.Report(SharedText.T("演练模式,不落盘", "Dry run, nothing written"), 100);
                            return;
                        }

                        try
                        {
                            ConfigStore.Clear();
                            context.Log("已清理安装状态文件");
                        }
                        catch (Exception exception)
                        {
                            context.Log("清理状态文件失败:" + exception.Message);
                        }

                        try
                        {
                            string bootRoot = Path.Combine(
                                Environment.GetFolderPath(
                                    Environment.SpecialFolder.LocalApplicationData),
                                "DeepSeekHarness",
                                "Boot");
                            if (Directory.Exists(bootRoot))
                            {
                                foreach (string file in Directory.GetFiles(
                                    bootRoot,
                                    "*",
                                    SearchOption.AllDirectories))
                                {
                                    try { File.Delete(file); } catch { }
                                }

                                foreach (string directory in Directory.GetDirectories(
                                    bootRoot,
                                    "*",
                                    SearchOption.AllDirectories))
                                {
                                    try { Directory.Delete(directory, false); } catch { }
                                }

                                Directory.Delete(bootRoot, false);
                                context.Log("已清理引导临时目录:" + bootRoot);
                            }
                        }
                        catch (Exception exception)
                        {
                            context.Log("引导临时目录未完全清理:" + exception.Message);
                        }

                        context.Report(SharedText.T("完成", "Done"), 100);
                    }, token);
                }));

            plan.Add(Step(IdVerify,
                SharedText.T("确认清理结果", "Verify the removal"),
                delegate(InstallContext context, CancellationToken token)
                {
                    return Task.Run(delegate { Verify(context, options); }, token);
                }));

            return plan;
        }

        private static InstallStep Step(string id, string title,
            Func<InstallContext, CancellationToken, Task> run)
        {
            return new InstallStep
            {
                Id = id,
                Title = title,

                // 卸载一律不设"必需":某一项删不掉(例如文件被占)不该让整条流程中断,
                // 用户要的是"能清的都清掉",残留由最后一步报出来。
                Required = false,
                Run = run,
            };
        }

        // ---------------------------------------------------------------- 各步实现

        /// <summary>
        /// 把"怎么卸载"这条线索全部抹掉:注册表里的卸载项、启动器目录里的卸载程序、
        /// 以及安装器自己保留在 %LOCALAPPDATA% 的那份副本。
        ///
        /// 顺序上排在删目录之后 —— 前面几步可能已经把启动器目录整个删了,
        /// 这里对不存在的路径静默跳过即可。
        /// </summary>
        private static void RemoveUninstallEntry(InstallContext context, UninstallOptions options)
        {
            if (options.DryRun)
            {
                context.Report(SharedText.T("演练模式,不动注册表", "Dry run, the registry is left alone"), 100);
                return;
            }

            // 1) 注册表卸载项。安装时按范围写进 HKCU 或 HKLM,卸载时两边都试,
            //    免得"换了范围重装"之后留下孤儿项。
            Microsoft.Win32.RegistryKey[] roots = new Microsoft.Win32.RegistryKey[]
            {
                Microsoft.Win32.Registry.CurrentUser,
                Microsoft.Win32.Registry.LocalMachine,
            };

            for (int i = 0; i < roots.Length; i++)
            {
                try
                {
                    // throwOnMissingSubKey=false:不存在就当"本来就没有",不报错
                    roots[i].DeleteSubKeyTree(WellKnown.RegistryUninstallKey, false);
                    context.Log("已删除卸载项(" + (i == 0 ? "HKCU" : "HKLM") + ")");
                }
                catch (Exception exception)
                {
                    context.Log("删除卸载项失败:" + exception.Message);
                }
            }

            // 2) DSH 根目录里那份独立卸载程序(新版本放这儿;老版本在启动器目录,一并试)
            foreach (string place in new string[] { options.DshRoot, options.LauncherRoot })
            {
                if (string.IsNullOrWhiteSpace(place))
                {
                    continue;
                }

                try
                {
                    string shortcut = Path.Combine(place, WellKnown.UninstallerExe);
                    if (File.Exists(shortcut))
                    {
                        File.Delete(shortcut);
                        context.Log("已删除 " + shortcut);
                    }
                }
                catch (Exception exception)
                {
                    context.Log("删除卸载程序失败:" + exception.Message);
                }
            }

            // 3) 安装器自己的副本。卸载程序已经把自己复制到 %TEMP% 里跑,
            //    所以这里删的目录不会被"正在运行"锁住(锁住就只记一条日志,不当作失败)。
            string home = BuiltInSteps.InstallerHomeFor(options.DshRoot);
            try
            {
                if (Directory.Exists(home))
                {
                    Directory.Delete(home, true);
                    context.Log("已删除安装器副本:" + home);
                }
            }
            catch (Exception exception)
            {
                context.Log("安装器副本没删掉(重启后可手动删除):" + exception.Message);
            }

            context.Report(SharedText.T("完成", "Done"), 100);
        }

        private static void StopProcesses(InstallContext context, UninstallOptions options)
        {
            if (options.DryRun)
            {
                context.Report(SharedText.T("演练模式,不动进程", "Dry run, processes left alone"), 100);
                return;
            }

            // 只结束"要被卸载的东西",别把自己也算了。
            //
            // 血泪教训:这个列表以前含 "DSH-Installer",而卸载器和回滚都跑在这个进程里 ——
            // 于是第一步就把自己杀了,后面全不执行,报告也永远写不出来(实测"闪退"就是这么来的)。
            string[] names = new string[]
            {
                "DeepSeek Harness",
                "DeepSeek Harness.Core",
            };

            int stopped = 0;
            for (int i = 0; i < names.Length; i++)
            {
                System.Diagnostics.Process[] found =
                    System.Diagnostics.Process.GetProcessesByName(names[i]);

                for (int j = 0; j < found.Length; j++)
                {
                    // 双重保险:无论如何都不碰当前进程
                    try
                    {
                        if (found[j].Id == Environment.ProcessId)
                        {
                            continue;
                        }

                        found[j].Kill();
                        stopped++;
                        context.Log("已结束 " + names[i]);
                    }
                    catch (Exception exception)
                    {
                        context.Log("结束 " + names[i] + " 失败:" + exception.Message);
                    }
                    finally
                    {
                        found[j].Dispose();
                    }
                }
            }

            // DSH 本体是拿 node.exe 跑的,光杀启动器不够 ——
            // 文件被 node 占着,目录就删不掉(实测报 Access denied)。
            //
            // 但**不能无差别杀 node**:用户机器上可能跑着别的 Node 程序。
            // 只杀"可执行文件位于我们的组件目录下"的那些 —— 那才是我们装的。
            stopped += StopOurNodeProcesses(context, options.ComponentsRoot);

            if (stopped == 0)
            {
                context.Log("没有需要结束的进程");
            }

            // 给文件句柄一点时间释放,不然后面删目录会失败
            Thread.Sleep(1500);
            context.Report(
                SharedText.T("已结束 " + stopped + " 个进程", "Stopped " + stopped + " process(es)"),
                100);
        }

        /// <summary>
        /// 结束属于我们这次安装的 node 进程。
        ///
        /// 判断依据是可执行文件路径落在我们的组件目录下 ——
        /// 这样既能把占着 DSH 文件的 node 干掉,又不会误杀用户自己跑的 Node 程序。
        /// </summary>
        private static int StopOurNodeProcesses(InstallContext context, string componentsRoot)
        {
            if (string.IsNullOrWhiteSpace(componentsRoot))
            {
                return 0;
            }

            string root = componentsRoot.TrimEnd('\\');
            int stopped = 0;

            System.Diagnostics.Process[] found;
            try
            {
                found = System.Diagnostics.Process.GetProcessesByName("node");
            }
            catch
            {
                return 0;
            }

            for (int i = 0; i < found.Length; i++)
            {
                try
                {
                    if (found[i].Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    string exe = null;
                    try
                    {
                        exe = found[i].MainModule == null ? null : found[i].MainModule.FileName;
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(exe)
                        || exe.IndexOf(root, StringComparison.OrdinalIgnoreCase) != 0)
                    {
                        continue;
                    }

                    found[i].Kill();
                    stopped++;
                    context.Log("已结束装在我们组件目录里的 node: " + exe);
                }
                catch (Exception exception)
                {
                    context.Log("结束 node 失败: " + exception.Message);
                }
                finally
                {
                    found[i].Dispose();
                }
            }

            return stopped;
        }

        private static void RemoveAutostart(InstallContext context, UninstallOptions options)
        {
            if (options.DryRun)
            {
                context.Report(SharedText.T("演练模式,不动计划任务", "Dry run, tasks left alone"), 100);
                return;
            }

            string script =
                "$ErrorActionPreference='SilentlyContinue';" +
                "Unregister-ScheduledTask -TaskName " + Quote(WellKnown.AutostartTaskName) + " -Confirm:$false;" +
                "Write-Output 'OK'";

            try
            {
                string output = PowerShellScript.Run(script, 60000);
                context.Log("计划任务清理:" + (string.IsNullOrWhiteSpace(output) ? "(无输出)" : output.Trim()));
            }
            catch (Exception exception)
            {
                context.Log("清理计划任务失败:" + exception.Message);
            }

            // 顺手清掉旧式自启(Run 键 / 启动文件夹),早期版本用过这种写法
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    WellKnown.RegistryRunKey, true))
                {
                    if (key != null && key.GetValue(WellKnown.RunValueName) != null)
                    {
                        key.DeleteValue(WellKnown.RunValueName, false);
                        context.Log("已删除 Run 键里的自启项");
                    }
                }
            }
            catch (Exception exception)
            {
                context.Log("清理 Run 键失败:" + exception.Message);
            }

            try
            {
                string startup = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    WellKnown.ProductName + ".lnk");
                if (File.Exists(startup))
                {
                    File.Delete(startup);
                    context.Log("已删除启动文件夹快捷方式");
                }
            }
            catch
            {
            }

            context.Report(SharedText.T("完成", "Done"), 100);
        }

        private static void RemoveShortcuts(InstallContext context, UninstallOptions options)
        {
            if (options.DryRun)
            {
                context.Report(SharedText.T("演练模式,不删快捷方式", "Dry run, shortcuts left alone"), 100);
                return;
            }

            List<string> targets = new List<string>();
            string[] shortcutNames = new string[]
            {
                WellKnown.ProductName,
                WellKnown.LegacyProductName
            };

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            for (int index = 0; index < shortcutNames.Length; index++)
            {
                targets.Add(Path.Combine(desktop, shortcutNames[index] + ".lnk"));
            }

            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(commonDesktop))
            {
                for (int index = 0; index < shortcutNames.Length; index++)
                {
                    targets.Add(Path.Combine(commonDesktop, shortcutNames[index] + ".lnk"));
                }
            }

            string startMenuUser = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            for (int index = 0; index < shortcutNames.Length; index++)
            {
                targets.Add(Path.Combine(startMenuUser, shortcutNames[index] + ".lnk"));
            }

            // 开始菜单那份是装在**同名子文件夹**里的
            // 过渡期同时清理新旧产品名的子目录。
            // 机器范围则在 %PROGRAMDATA% 那套下面 —— 两边都要清,子文件夹也要一起收掉。
            if (!string.IsNullOrEmpty(startMenuUser))
            {
                for (int index = 0; index < shortcutNames.Length; index++)
                {
                    targets.Add(Path.Combine(
                        startMenuUser,
                        shortcutNames[index],
                        shortcutNames[index] + ".lnk"));
                }
            }

            string startMenuMachine = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            if (!string.IsNullOrEmpty(startMenuMachine))
            {
                for (int index = 0; index < shortcutNames.Length; index++)
                {
                    targets.Add(Path.Combine(
                        startMenuMachine,
                        shortcutNames[index],
                        shortcutNames[index] + ".lnk"));
                }
            }

            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            for (int index = 0; index < shortcutNames.Length; index++)
            {
                targets.Add(Path.Combine(startup, shortcutNames[index] + ".lnk"));
            }

            int deleted = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                try
                {
                    if (File.Exists(targets[i]))
                    {
                        File.Delete(targets[i]);
                        deleted++;
                        context.Log("已删除 " + targets[i]);
                    }
                }
                catch (Exception exception)
                {
                    context.Log("删除失败 " + targets[i] + ":" + exception.Message);
                }
            }

            // 快捷方式删了,那个只剩空壳的子文件夹也顺手收掉
            foreach (string root in new string[] { startMenuUser, startMenuMachine })
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                for (int index = 0; index < shortcutNames.Length; index++)
                {
                    try
                    {
                        string folder = Path.Combine(root, shortcutNames[index]);
                        if (Directory.Exists(folder) && Directory.GetFiles(folder).Length == 0)
                        {
                            Directory.Delete(folder);
                            context.Log("已删除空目录 " + folder);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            context.Report(SharedText.T("已删除 " + deleted + " 个", "Deleted " + deleted), 100);
        }

        private static void CleanPath(InstallContext context, UninstallOptions options)
        {
            if (options.DryRun)
            {
                context.Report(SharedText.T("演练模式,不改 PATH", "Dry run, PATH left unchanged"), 100);
                return;
            }

            List<string> entries = options.PathEntries;
            if (entries == null || entries.Count == 0)
            {
                // 状态文件里没记录时,按默认布局兜底推一遍
                entries = new List<string>();
                if (!string.IsNullOrWhiteSpace(options.ComponentsRoot))
                {
                    entries.Add(Path.Combine(options.ComponentsRoot, "node"));
                    entries.Add(Path.Combine(options.ComponentsRoot, "git", "cmd"));
                    entries.Add(Path.Combine(options.ComponentsRoot, "pnpm"));
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                PathChangeResult result = options.AllUsers
                    ? PathEditor.RemoveFromMachinePath(entries[i])
                    : PathEditor.RemoveFromUserPath(entries[i]);
                context.Log(result.Message);
            }

            context.Report(SharedText.T("完成", "Done"), 100);
        }

        /// <summary>
        /// 把"要保留的路径"收拾干净:只认**真实存在**、而且在 root 里面的那些。
        /// 不存在的、跑到 root 外面的(状态文件被手改过)一律丢掉 ——
        /// 这种输入拿去拼路径很容易变成"删了不该删的地方"。
        /// </summary>
        /// <remarks>public 是为了能单独跑测试:这段逻辑一旦出错,删掉的是用户的数据。</remarks>
        public static List<string> NormalizeKeeps(string root, List<string> keepPaths)
        {
            List<string> keeps = new List<string>();
            if (keepPaths == null || keepPaths.Count == 0)
            {
                return keeps;
            }

            string prefix = root.TrimEnd('\\') + "\\";

            for (int i = 0; i < keepPaths.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(keepPaths[i]))
                {
                    continue;
                }

                try
                {
                    string full = Path.GetFullPath(keepPaths[i]).TrimEnd('\\');

                    // 必须在 root 里面(不是 root 自己),否则不认
                    if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (Directory.Exists(full) && !keeps.Contains(full))
                    {
                        keeps.Add(full);
                    }
                }
                catch
                {
                }
            }

            return keeps;
        }

        /// <summary>
        /// 删 root 里的东西,但 <paramref name="keep"/> 那些目录整个留下。
        ///
        /// 递归规则:遇到要保留的目录 —— 跳过;保留项在更深处 —— 进这一层继续。
        /// 返回值是没删掉的项数,和 <see cref="DeleteTree"/> 语义一致。
        /// </summary>
        /// <remarks>public 是为了能单独跑测试(见 NormalizeKeeps 的说明)。</remarks>
        public static int DeleteTreeKeeping(InstallContext context, string root, List<string> keep)
        {
            int failed = 0;

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(root);
            }
            catch (Exception exception)
            {
                context.Log("读取 " + root + " 出错:" + exception.Message);
                return 1;
            }

            for (int i = 0; i < directories.Length; i++)
            {
                string child;

                try
                {
                    child = Path.GetFullPath(directories[i]).TrimEnd('\\');
                }
                catch
                {
                    continue;
                }

                bool keepWhole = false;
                bool keepInside = false;

                for (int k = 0; k < keep.Count; k++)
                {
                    if (string.Equals(child, keep[k], StringComparison.OrdinalIgnoreCase))
                    {
                        keepWhole = true;
                        break;
                    }

                    if (keep[k].StartsWith(child + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        keepInside = true;
                    }
                }

                if (keepWhole)
                {
                    context.Log("保留 " + child);
                    continue;
                }

                failed += keepInside
                    ? DeleteTreeKeeping(context, child, keep)
                    : DeleteTree(context, child);
            }

            // 这一层的散文件:没有"保留"的说法,全部删掉
            try
            {
                foreach (string file in Directory.GetFiles(root))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception exception)
                    {
                        context.Log("删除文件失败 " + file + ":" + exception.Message);
                        failed++;
                    }
                }
            }
            catch (Exception exception)
            {
                context.Log("读取文件列表出错 " + root + ":" + exception.Message);
                failed++;
            }

            // 注意:**这里不能删 root 自己**。
            // 保留项还挂在 root 底下,顺手来一发递归删除就把人家删了 ——
            // 整个"保留用户数据"的意义正好反过来了。root 连同里面的保留项一起留着。
            return failed;
        }

        private static void DeleteDirectory(
            InstallContext context, UninstallOptions options, string directory, string label,
            List<string> keepPaths = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                context.Log("跳过删除:没记录这个目录(" + label + ")");
                context.Report(label + SharedText.T("目录未记录,跳过", " directory not recorded, skipping"), 100);
                return;
            }

            // 安全闸:绝不允许删盘根或系统目录。
            // 状态文件理论上能被手改,这里不能盲信它。
            string full;
            try
            {
                full = Path.GetFullPath(directory).TrimEnd('\\');
            }
            catch
            {
                context.Report(label + SharedText.T("路径无效,跳过", " path invalid, skipping"), 100);
                return;
            }

            if (full.Length <= 3 || IsUnsafePath(full))
            {
                context.Log("拒绝删除(看着像系统目录):" + full);
                context.Report(label + SharedText.T("路径不安全,已跳过", " path looks unsafe, skipped"), 100);
                return;
            }

            if (!Directory.Exists(full))
            {
                // 这条一定要落盘。以前只 Report,日志里什么都看不到,
                // 结果"卸载完了目录还在"查了半天才发现是路径压根没传进来(实测踩过)。
                context.Log("跳过删除:" + full + " 不存在(记的是这个路径)");
                context.Report(label + SharedText.T("原本不存在,跳过", " not present, skipping"), 100);
                return;
            }

            if (options.DryRun)
            {
                context.Report(label + SharedText.T("演练模式,不删", " dry run, not deleting"), 100);
                return;
            }

            try
            {
                // 有要保留的子目录时走"选择性删除":程序文件清掉,用户数据原样留着。
                List<string> keep = NormalizeKeeps(full, keepPaths);
                int failed = keep.Count == 0
                    ? DeleteTree(context, full)
                    : DeleteTreeKeeping(context, full, keep);

                if (failed > 0)
                {
                    // 再给一次机会:前一步刚结束进程,句柄释放可能要几百毫秒
                    // (实测就是"杀完进程立刻删,还剩一项")。
                    context.Log("有 " + failed + " 项没删掉,等一秒再试一次");
                    System.Threading.Thread.Sleep(1000);
                    failed = keep.Count == 0
                        ? DeleteTree(context, full)
                        : DeleteTreeKeeping(context, full, keep);
                }

                if (failed > 0)
                {
                    // **不抛**。卸载的语义是"能清多少清多少":杀完进程、该删的都删了,
                    // 剩下一项(多半被占着)不该让整趟显示成"卸载未完成" ——
                    // 那页面会让用户以为卸载失败了,其实东西早就不起作用了(实测反馈)。
                    // 残留会记进日志,并出现在最后的"确认清理结果"里。
                    context.Log("有 " + failed + " 项残留(多半被占用),已记日志;不影响卸载结果");
                    context.Report(
                        label + SharedText.T("已删除(有残留,见日志)", " deleted (some items left, see log)"), 100);
                }
                else if (keep.Count > 0)
                {
                    context.Log("已删除 " + full + " ,保留:" + string.Join(" ; ", keep.ToArray()));
                    context.Report(
                        label + SharedText.T("已删除,已保留用户数据", " deleted; your data was kept"), 100);
                }
                else
                {
                    context.Log("已删除 " + full);
                    context.Report(label + SharedText.T("已删除", " deleted"), 100);
                }
            }
            catch (Exception exception)
            {
                context.Log("删除 " + full + " 出错:" + exception.Message);
                context.Report(
                    label + SharedText.T("删除出错,见日志", " deletion error, see log"), 100);
            }
        }

        /// <summary>
        /// 删一整棵目录树,**逐项删**。
        ///
        /// 为什么不用 Directory.Delete(path, true):
        ///   · 它是一锤子买卖 —— 树里任何一项删不掉(被占用、名字怪、路径超长),
        ///     整棵树就原地留着。实测 Node 的 node_modules 里就有这种项,
        ///     报出来是"参数错误。: 'sdk'"这种看不懂的话,而结果是**整个安装目录还在**;
        ///   · 逐项删的话,最坏情况只是少删几个文件,其余照删不误。
        /// 删不掉的会记进日志,最后由"确认清理结果"那步统一报出来。
        /// </summary>
        private static int DeleteTree(InstallContext context, string root)
        {
            int failed = 0;

            string[] files;
            try
            {
                files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            }
            catch (Exception exception)
            {
                context.Log("枚举文件失败(改用顶层):" + exception.Message);
                try
                {
                    files = Directory.GetFiles(root);
                }
                catch
                {
                    files = new string[0];
                }
            }

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    // 只读属性会让 Delete 直接失败,先摘掉
                    File.SetAttributes(files[i], FileAttributes.Normal);
                    File.Delete(files[i]);
                }
                catch (Exception exception)
                {
                    failed++;
                    context.Log("删不掉(先跳过):" + files[i] + " -> " + exception.Message);
                }
            }

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
            }
            catch
            {
                dirs = new string[0];
            }

            // 深的先删,不然父目录被非空卡住
            Array.Sort(dirs, delegate(string a, string b)
            {
                return b.Length.CompareTo(a.Length);
            });

            for (int i = 0; i < dirs.Length; i++)
            {
                try
                {
                    Directory.Delete(dirs[i], false);
                }
                catch
                {
                    // 子目录里还有删不掉的文件,留着让它报在最后
                }
            }

            try
            {
                Directory.Delete(root, false);
            }
            catch (Exception exception)
            {
                failed++;
                context.Log("最后那层目录没删掉:" + root + " -> " + exception.Message);
            }

            return failed;
        }

        private static void Verify(InstallContext context, UninstallOptions options)
        {
            if (options.DryRun)
            {
                context.Report(SharedText.T("演练模式,跳过检查", "Dry run, skipping verification"), 100);
                return;
            }

            List<string> left = new List<string>();

            if (!string.IsNullOrWhiteSpace(options.DshRoot) && Directory.Exists(options.DshRoot))
            {
                left.Add(options.DshRoot);
            }

            if (!string.IsNullOrWhiteSpace(options.LauncherRoot) && Directory.Exists(options.LauncherRoot))
            {
                left.Add(options.LauncherRoot);
            }

            if (!string.IsNullOrWhiteSpace(options.ComponentsRoot) && Directory.Exists(options.ComponentsRoot))
            {
                left.Add(options.ComponentsRoot);
            }

            if (left.Count > 0)
            {
                // 用户选择"保留数据"时,DSH 本体目录会被故意留下(里面是技能/会话/插件),
                // 这时不能报"存在残留项" —— 用户明明自己选的保留,却看到一句像出了错的提示。
                bool intentional = options.KeepUserData
                    && left.Count == 1
                    && !string.IsNullOrWhiteSpace(options.DshRoot)
                    && string.Equals(left[0], options.DshRoot, StringComparison.OrdinalIgnoreCase);

                if (intentional)
                {
                    context.Log("按你的选择保留了 " + left[0] + " 里的用户数据");
                    context.Report(
                        SharedText.T("已按你的选择保留用户数据", "Your data was kept, as you chose"), 100);
                    return;
                }

                context.Log("这些目录还在(可能被占用):" + string.Join("; ", left.ToArray()));
                context.Report(SharedText.T("存在残留项,详情见日志", "Some items remain; see the log"), 100);
            }
            else
            {
                context.Report(SharedText.T("无残留", "Nothing left behind"), 100);
            }
        }

        /// <summary>路径看着像不该删的东西(Windows / Program Files 根 / 用户目录根)。</summary>
        private static bool IsUnsafePath(string full)
        {
            string lower = full.ToLowerInvariant();

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                .TrimEnd('\\').ToLowerInvariant();
            if (!string.IsNullOrEmpty(windows) && (lower == windows || lower.StartsWith(windows + "\\")))
            {
                return true;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                .TrimEnd('\\').ToLowerInvariant();
            if (!string.IsNullOrEmpty(programFiles) && lower == programFiles)
            {
                return true;
            }

            string users = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                .TrimEnd('\\').ToLowerInvariant();
            if (!string.IsNullOrEmpty(users) && lower == users)
            {
                return true;
            }

            return false;
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "''";
            }

            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
