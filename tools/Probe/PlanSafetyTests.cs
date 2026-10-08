using System;
using System.Collections.Generic;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller.Probe
{
    // Offline pure logic only: no install, delete, process enumeration, network or registry access.
    internal static class PlanSafetyTests
    {
        public static int Run()
        {
            int count = 0;
            void Check(bool condition, string name)
            {
                if (!condition) throw new Exception("FAIL: " + name);
                count++;
            }
            const string dsh = @"C:\Apps\Dsh";
            Check(InstallPaths.Contains(@"C:\Apps\components", @"c:/apps/components/node/node.exe"), "case/slash boundary");
            Check(!InstallPaths.Contains(@"C:\Apps\components", @"C:\Apps\components-other\node.exe"), "sibling prefix");
            Check(!InstallPaths.Contains(@"C:\Apps\components", @"C:\Apps\components\..\other\node.exe"), "parent normalization");
            Check(!InstallPaths.Contains("relative", @"C:\Apps\node.exe"), "relative root rejected");
            Check(InstallPaths.Contains(@"\\server\share\components", @"\\server\share\components\node.exe"), "UNC child");
            Check(!InstallPaths.Contains(@"\\server\share\components", @"\\server\share\components2\node.exe"), "UNC sibling");
            Check(InstallPaths.ValidateLayout(dsh, dsh + @"\launcher", dsh + @"\components") == null, "default nesting");
            Check(InstallPaths.ValidateLayout(dsh, dsh, dsh + @"\components") != null, "equal roots");
            Check(InstallPaths.ValidateLayout(dsh, @"C:\Apps", dsh + @"\components") != null, "launcher ancestor");
            Check(InstallPaths.ValidateLayout(dsh, dsh + @"\launcher", @"C:\Apps") != null, "components ancestor");
            Check(InstallPaths.ValidateLayout(dsh, dsh + @"\components\launcher", dsh + @"\components") != null, "component launcher overlap");
            Check(InstallPaths.ValidateLayout(dsh, dsh + @"\.dsh\launcher", dsh + @"\components") != null, "data overlap");
            Check(InstallPaths.ValidateLayout(dsh, dsh + @"\plugins", dsh + @"\components") != null, "plugin overlap");
            Check(InstallPaths.ValidateLayout(@"C:\", @"D:\Launcher", @"E:\Components") != null, "drive root");
            var uninstall = new UninstallOptions { DshRoot = dsh, KeepUserData = true };
            var protectedPaths = InstallPaths.ProtectedPaths(uninstall);
            foreach (string target in new[] { dsh, @"C:\Apps", dsh + @"\.dsh", dsh + @"\plugins\nested" })
                Check(InstallPaths.KeepsForTarget(target, protectedPaths).Count > 0, "protected overlap " + target);
            Check(InstallPaths.KeepsForTarget(dsh + @"\launcher", protectedPaths).Count == 0, "separate target");
            Check(InstallPaths.KeepsForTarget(@"C:\Apps-other", protectedPaths).Count == 0, "separate sibling");
            Check(InstallPaths.KeepsForTarget(dsh, new[] { dsh + @"\plugins", dsh + @"\plugins" }).Count == 1, "deduplicate keeps");
            uninstall.KeepUserData = false;
            Check(InstallPaths.ProtectedPaths(uninstall).Count == 0, "explicit clear");
            var state = new InstallerState { Scope = "machine", DshRoot = dsh, LauncherRoot = @"D:\Custom\Launcher", ComponentsRoot = @"E:\Custom\Tools", Autostart = true, DesktopShortcut = true, StartMenuShortcut = false, InstallPython = true };
            var plan = new InstallOptions();
            EffectiveInstallPlan.Restore(plan, state);
            Check(plan.AllUsers, "machine repair scope");
            Check(plan.LauncherRoot == state.LauncherRoot && plan.ComponentsRoot == state.ComponentsRoot, "custom repair paths");
            Check(!plan.CreateStartMenuShortcut && plan.CreateDesktopShortcut && plan.InstallPython && plan.EnableAutostart, "recorded repair choices");
            foreach (string source in new[] { "china", "backend", "official" })
            {
                state.SourcePreference = source;
                EffectiveInstallPlan.Restore(plan, state);
                Check(plan.SourcePreference == source, "repair restores effective source " + source);
            }
            state.SourcePreference = "unknown";
            plan.SourcePreference = "china";
            EffectiveInstallPlan.Restore(plan, state);
            Check(plan.SourcePreference == "china", "unknown repair source keeps current default");
            state.StartMenuShortcut = null;
            EffectiveInstallPlan.Restore(plan, state);
            Check(plan.CreateStartMenuShortcut, "legacy shortcut fallback");
            Check(EffectiveInstallPlan.ElevationReasons(plan, true, _ => false).Count == 4, "all elevation reasons");
            plan.AllUsers = false; plan.EnableAutostart = false;
            plan.InstallDotNetRuntime = false; plan.InstallWindowsAppRuntime = false;
            Check(EffectiveInstallPlan.ElevationReasons(plan, true, _ => true).Count == 0, "runtime disabled");
            Check(EffectiveInstallPlan.ElevationReasons(plan, false, _ => false).Contains("directory"), "custom protected directory");
            plan.InstallDotNetRuntime = true;
            Check(EffectiveInstallPlan.ElevationReasons(plan, true, _ => true).Contains("runtime"), "runtime enabled");
            Console.WriteLine("PASS: " + count + " offline pure plan/path/state assertions");
            return 0;
        }
    }
}
