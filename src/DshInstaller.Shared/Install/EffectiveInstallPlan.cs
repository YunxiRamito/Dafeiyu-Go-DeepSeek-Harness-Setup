using System;
using System.Collections.Generic;
using System.IO;

namespace DshInstaller.Shared.Install
{
    public static class EffectiveInstallPlan
    {
        /// <summary>Restore recorded choices; callers apply only explicit overrides afterwards.</summary>
        public static void Restore(InstallOptions options, InstallerState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.DshRoot)) return;
            options.AllUsers = string.Equals(state.Scope, "machine", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Scope, "all", StringComparison.OrdinalIgnoreCase);
            options.DshRoot = state.DshRoot;
            options.LauncherRoot = string.IsNullOrWhiteSpace(state.LauncherRoot)
                ? Path.Combine(state.DshRoot, WellKnown.LauncherFolder) : state.LauncherRoot;
            options.ComponentsRoot = string.IsNullOrWhiteSpace(state.ComponentsRoot)
                ? Path.Combine(state.DshRoot, "components") : state.ComponentsRoot;
            string source = ConfigStore.NormalizeSourcePreference(state.SourcePreference);
            if (source != null) options.SourcePreference = source;
            options.InstallGit = state.InstallGit;
            options.InstallPnpm = state.InstallPnpm;
            options.InstallPython = state.InstallPython;
            options.EnableAutostart = state.Autostart;
            options.CreateDesktopShortcut = state.DesktopShortcut;
            options.CreateStartMenuShortcut = state.StartMenuShortcut ?? state.DesktopShortcut;
        }

        /// <summary>Pure decision with injected read-only runtime and directory assessment.</summary>
        public static List<string> ElevationReasons(InstallOptions options, bool runtimeMissing, Func<string, bool> canWrite)
        {
            var reasons = new List<string>();
            if (options.AllUsers) reasons.Add("scope");
            if (options.EnableAutostart) reasons.Add("autostart");
            if (runtimeMissing && (options.InstallDotNetRuntime || options.InstallWindowsAppRuntime)) reasons.Add("runtime");
            if (!canWrite(options.DshRoot) || !canWrite(options.LauncherRoot) || !canWrite(options.ComponentsRoot)) reasons.Add("directory");
            return reasons;
        }
    }
}
