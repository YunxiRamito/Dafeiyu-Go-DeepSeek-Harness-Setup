using System;
using System.Collections.Generic;
using System.IO;

namespace DshInstaller.Shared.Install
{
    /// <summary>Canonical, lexical path decisions. Never probes or modifies the filesystem.</summary>
    public static class InstallPaths
    {
        public static string Canonical(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("An absolute directory is required.");
            string full = Path.GetFullPath(path);
            return Path.TrimEndingDirectorySeparator(full);
        }

        public static bool Contains(string root, string path)
        {
            try
            {
                string parent = Canonical(root);
                string child = Canonical(path);
                return string.Equals(parent, child, StringComparison.OrdinalIgnoreCase)
                    || child.StartsWith(Path.EndsInDirectorySeparator(parent)
                        ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Read-only guard against a recorded path traversing an existing junction.</summary>
        public static bool HasReparseAncestor(string path)
        {
            string current = Canonical(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
                current = Path.GetDirectoryName(current);
            }
            return false;
        }

        public static bool Overlaps(string first, string second)
        {
            return Contains(first, second) || Contains(second, first);
        }

        public static string ValidateLayout(string dsh, string launcher, string components)
        {
            try
            {
                dsh = Canonical(dsh);
                launcher = Canonical(launcher);
                components = Canonical(components);
                foreach (string root in new[] { dsh, launcher, components })
                    if (string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
                        return "Install directories cannot be drive or share roots.";
                // The default layout has two separate children beneath the DSH root.
                if (Contains(launcher, dsh) || Contains(components, dsh) || Overlaps(launcher, components))
                    return "Launcher and component directories must be separate and cannot contain the DSH root.";
                foreach (string data in new[] { Path.Combine(dsh, ".dsh"), Path.Combine(dsh, "plugins") })
                    if (Overlaps(data, launcher) || Overlaps(data, components))
                        return "Program directories cannot overlap user data (.dsh or plugins).";
                return null;
            }
            catch (Exception exception) { return "Invalid install directory: " + exception.Message; }
        }

        public static List<string> ProtectedPaths(UninstallOptions options)
        {
            var paths = new List<string>();
            if (!options.KeepUserData) return paths;
            // Fail closed for malformed recorded paths: callers must not turn this into full deletion.
            if (!string.IsNullOrWhiteSpace(options.DshRoot))
            {
                string root = Canonical(options.DshRoot);
                paths.Add(Path.Combine(root, ".dsh"));
                paths.Add(Path.Combine(root, "plugins"));
            }
            if (!string.IsNullOrWhiteSpace(options.UserDataRoot))
                paths.Add(Canonical(options.UserDataRoot));
            return paths;
        }

        /// <summary>Keep the whole target if it is itself inside a protected subtree.</summary>
        public static List<string> KeepsForTarget(string target, IEnumerable<string> protectedPaths)
        {
            var keeps = new List<string>();
            target = Canonical(target);
            if (protectedPaths == null) return keeps;
            foreach (string path in protectedPaths)
            {
                string keep = Canonical(path);
                if (Contains(keep, target)) return new List<string> { target };
                if (Contains(target, keep) && !keeps.Exists(p => string.Equals(p, keep, StringComparison.OrdinalIgnoreCase)))
                    keeps.Add(keep);
            }
            return keeps;
        }
    }
}
