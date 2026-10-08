using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading;

namespace DeepSeekHarnessLauncher.Backup
{
    internal sealed class DshDataDiscoveryEntry
    {
        internal const string DymKind = "Dym";
        internal const string DshDirectoryKind = "DshDirectory";

        internal string Kind { get; set; }
        internal string Path { get; set; }
        internal string Display { get; set; }
    }

    internal sealed class DshDataDiscoveryResult
    {
        internal List<DshDataDiscoveryEntry> Entries { get; } = new List<DshDataDiscoveryEntry>();
        internal bool Truncated { get; set; }
        internal int DirectoriesVisited { get; set; }
        internal int FileSystemEntriesVisited { get; set; }
    }

    internal static class DshDataDiscoveryService
    {
        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private static readonly HashSet<string> IgnoredDirectoryNames = new HashSet<string>(
            new[] { ".git", ".hg", ".svn", "node_modules", ".pnpm", "bin", "obj", "dist",
                "preview-artifacts", "packages", "cache", "caches", ".cache", "temp", "tmp", "runtime",
                "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin",
                "System Volume Information", "Recovery", "WinSxS" }, StringComparer.OrdinalIgnoreCase);

        internal static DshDataDiscoveryResult Discover(
            IEnumerable<string> searchRoots,
            IEnumerable<string> excludedRoots,
            CancellationToken token,
            Action<string, int> progress = null,
            int maximumDepth = 4,
            int maximumDirectories = 4096,
            int maximumFileSystemEntries = 100000,
            int maximumResults = 256)
        {
            if (maximumDepth < 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            if (maximumDirectories < 1) throw new ArgumentOutOfRangeException(nameof(maximumDirectories));
            if (maximumFileSystemEntries < 1) throw new ArgumentOutOfRangeException(nameof(maximumFileSystemEntries));
            if (maximumResults < 1) throw new ArgumentOutOfRangeException(nameof(maximumResults));
            token.ThrowIfCancellationRequested();

            var result = new DshDataDiscoveryResult();
            var excluded = NormalizeRoots(excludedRoots, token, maximumFileSystemEntries, result);
            if (result.Truncated) return result;
            var roots = NormalizeRoots(searchRoots, token, maximumFileSystemEntries, result);
            var queued = new HashSet<string>(PathComparer);
            var visited = new HashSet<string>(PathComparer);
            var archives = new HashSet<string>(PathComparer);
            var homes = new Dictionary<string, DshDataDiscoveryEntry>(PathComparer);
            var queue = new Queue<(string Path, int Depth, bool IsDirectory)>();
            foreach (string root in roots)
            {
                token.ThrowIfCancellationRequested();
                if (IsExcluded(root, excluded) || !TryGetAttributes(root, out FileAttributes attributes)
                    || (attributes & FileAttributes.ReparsePoint) != 0) continue;
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (isDirectory && IsIgnoredDirectory(root)) continue;
                if (queued.Add(root)) queue.Enqueue((root, 0, isDirectory));
            }

            while (queue.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                if (!visited.Add(current.Path) || IsExcluded(current.Path, excluded)) continue;
                if (!current.IsDirectory)
                {
                    if (!VisitEntry(result, maximumFileSystemEntries)) return result;
                    if (File.Exists(current.Path) && IsDym(current.Path)
                        && !AddArchive(result, archives, current.Path, maximumResults)) return result;
                    continue;
                }
                if (IsInsideHome(current.Path, homes) || IsIgnoredDirectory(current.Path)) continue;
                if (result.DirectoriesVisited >= maximumDirectories)
                {
                    result.Truncated = true;
                    continue;
                }
                result.DirectoriesVisited++;
                progress?.Invoke(current.Path, result.DirectoriesVisited);
                token.ThrowIfCancellationRequested();
                if (TryResolveDshDirectory(current.Path, result, token, maximumFileSystemEntries, out string home)
                    && !IsExcluded(home, excluded))
                {
                    if (!AddHome(result, homes, current.Path, home, maximumResults)) return result;
                    // Only the actual data home is opaque: an install root can also contain backups.
                    if (PathComparer.Equals(current.Path, home)) continue;
                }
                if (result.FileSystemEntriesVisited >= maximumFileSystemEntries)
                {
                    result.Truncated = true;
                    return result;
                }
                foreach (var entry in EnumerateEntries(current.Path, result))
                {
                    token.ThrowIfCancellationRequested();
                    if (!VisitEntry(result, maximumFileSystemEntries)) return result;
                    string child = entry.Path;
                    if (IsExcluded(child, excluded) || entry.IsReparsePoint) continue;
                    if (!entry.IsDirectory)
                    {
                        if (IsDym(child) && !AddArchive(result, archives, child, maximumResults)) return result;
                        continue;
                    }
                    if (IsIgnoredDirectoryName(child) || IsInsideHome(child, homes)) continue;
                    if (current.Depth >= maximumDepth)
                    {
                        result.Truncated = true;
                        continue;
                    }
                    if (queued.Add(child)) queue.Enqueue((child, current.Depth + 1, true));
                }
            }
            token.ThrowIfCancellationRequested();
            return result;
        }

        private static List<string> NormalizeRoots(IEnumerable<string> paths, CancellationToken token,
            int maximumRoots, DshDataDiscoveryResult result)
        {
            var normalized = new List<string>();
            var seen = new HashSet<string>(PathComparer);
            if (paths == null) return normalized;
            int count = 0;
            foreach (string value in paths)
            {
                token.ThrowIfCancellationRequested();
                if (++count > maximumRoots) { result.Truncated = true; break; }
                if (String.IsNullOrWhiteSpace(value)) continue;
                try
                {
                    string path = Normalize(value);
                    if (seen.Add(path)) normalized.Add(path);
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (IOException) { }
            }
            return normalized;
        }

        private static string Normalize(string path)
            => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path.Trim()));

        private static bool Contains(string root, string path)
        {
            if (PathComparer.Equals(root, path)) return true;
            string prefix = System.IO.Path.EndsInDirectorySeparator(root) ? root : root + System.IO.Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, PathComparison);
        }

        private static bool IsExcluded(string path, List<string> excluded)
        {
            foreach (string root in excluded) if (Contains(root, path)) return true;
            return false;
        }

        private static bool IsInsideHome(string path, Dictionary<string, DshDataDiscoveryEntry> homes)
        {
            foreach (string home in homes.Keys) if (Contains(home, path)) return true;
            return false;
        }

        private static bool IsIgnoredDirectory(string path)
            => IsIgnoredDirectoryName(path)
                || File.Exists(System.IO.Path.Combine(path, "DeepSeek Harness.Core.dll"))
                || File.Exists(System.IO.Path.Combine(path, "DeepSeek Harness.Core.exe"));

        private static bool IsIgnoredDirectoryName(string path)
        {
            string name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
            return IgnoredDirectoryNames.Contains(name)
                || name.IndexOf("Dafeiyu-Go-DeepSeek-Harness-Click-To-Run", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsReparsePoint(string path)
            => !TryGetAttributes(path, out FileAttributes attributes)
                || (attributes & FileAttributes.ReparsePoint) != 0;

        private static bool TryGetAttributes(string path, out FileAttributes attributes)
        {
            attributes = default;
            try { attributes = File.GetAttributes(path); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }

        private static bool IsDym(string path)
            => String.Equals(System.IO.Path.GetExtension(path), ".dym", StringComparison.OrdinalIgnoreCase);

        private readonly struct DiscoveryFileSystemEntry
        {
            internal DiscoveryFileSystemEntry(string path, FileAttributes attributes)
            {
                Path = path;
                IsDirectory = (attributes & FileAttributes.Directory) != 0;
                IsReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
            }

            internal string Path { get; }
            internal bool IsDirectory { get; }
            internal bool IsReparsePoint { get; }
        }

        // Reuse metadata returned by directory enumeration instead of querying every path again.
        private static IEnumerable<DiscoveryFileSystemEntry> EnumerateEntries(string directory, DshDataDiscoveryResult result)
        {
            IEnumerator<DiscoveryFileSystemEntry> entries;
            try
            {
                entries = new FileSystemEnumerable<DiscoveryFileSystemEntry>(directory,
                    (ref FileSystemEntry entry) => new DiscoveryFileSystemEntry(entry.ToFullPath(), entry.Attributes),
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = false, ReturnSpecialDirectories = false,
                        AttributesToSkip = 0, IgnoreInaccessible = false
                    }).GetEnumerator();
            }
            catch (IOException) { result.Truncated = true; yield break; }
            catch (UnauthorizedAccessException) { result.Truncated = true; yield break; }
            catch (System.Security.SecurityException) { result.Truncated = true; yield break; }
            using (entries)
            {
                while (TryMoveNext(entries, result, out DiscoveryFileSystemEntry entry)) yield return entry;
            }
        }

        private static bool TryMoveNext(IEnumerator<DiscoveryFileSystemEntry> entries, DshDataDiscoveryResult result,
            out DiscoveryFileSystemEntry entry)
        {
            entry = default;
            try
            {
                if (!entries.MoveNext()) return false;
                entry = entries.Current;
                return true;
            }
            catch (IOException) { result.Truncated = true; return false; }
            catch (UnauthorizedAccessException) { result.Truncated = true; return false; }
            catch (System.Security.SecurityException) { result.Truncated = true; return false; }
        }

        private static bool VisitEntry(DshDataDiscoveryResult result, int maximumEntries)
        {
            if (result.FileSystemEntriesVisited >= maximumEntries) { result.Truncated = true; return false; }
            result.FileSystemEntriesVisited++;
            return true;
        }

        private static bool TryResolveDshDirectory(string path, DshDataDiscoveryResult result,
            CancellationToken token, int maximumEntries, out string home)
        {
            home = null;
            if (!HasDshEvidence(path, result, token, maximumEntries)) return false;
            try { home = Normalize(DshDataImportService.ResolveSourceHome(path)); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static bool HasDshEvidence(string path, DshDataDiscoveryResult result, CancellationToken token, int maximumEntries)
        {
            if (String.Equals(System.IO.Path.GetFileName(path), ".dsh", StringComparison.OrdinalIgnoreCase)
                || Directory.Exists(System.IO.Path.Combine(path, ".dsh"))) return true;
            string profiles = System.IO.Path.Combine(path, "profiles");
            if (Directory.Exists(profiles) && !IsReparsePoint(profiles))
            {
                int inspected = 0;
                foreach (var profile in EnumerateEntries(profiles, result))
                {
                    token.ThrowIfCancellationRequested();
                    if (!VisitEntry(result, maximumEntries)) return false;
                    if (profile.IsDirectory && !profile.IsReparsePoint
                        && File.Exists(System.IO.Path.Combine(profile.Path, "package.json"))) return true;
                    if (++inspected >= 128) { result.Truncated = true; break; }
                }
            }
            string storages = System.IO.Path.Combine(path, "storages");
            if (Directory.Exists(storages) && !IsReparsePoint(storages))
                foreach (string name in new[] { "metadata", "session-meta", "sessions" })
                {
                    string marker = System.IO.Path.Combine(storages, name);
                    if (Directory.Exists(marker) && !IsReparsePoint(marker)) return true;
                }
            string sessions = System.IO.Path.Combine(path, "sessions");
            if (!Directory.Exists(sessions) || IsReparsePoint(sessions)) return false;
            int probed = 0;
            foreach (var session in EnumerateEntries(sessions, result))
            {
                token.ThrowIfCancellationRequested();
                if (!VisitEntry(result, maximumEntries)) return false;
                if (IsSessionFile(session)) return true;
                if (session.IsDirectory && !session.IsReparsePoint)
                {
                    foreach (var file in EnumerateEntries(session.Path, result))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!VisitEntry(result, maximumEntries)) return false;
                        if (IsSessionFile(file)) return true;
                        if (++probed >= 128) { result.Truncated = true; return false; }
                    }
                }
                if (++probed >= 128) { result.Truncated = true; return false; }
            }
            return false;
        }

        private static bool IsSessionFile(DiscoveryFileSystemEntry entry)
            => !entry.IsDirectory && !entry.IsReparsePoint
                && (String.Equals(System.IO.Path.GetExtension(entry.Path), ".json", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(System.IO.Path.GetExtension(entry.Path), ".jsonl", StringComparison.OrdinalIgnoreCase));

        private static bool AddArchive(DshDataDiscoveryResult result, HashSet<string> archives, string path, int maximumResults)
        {
            if (!archives.Add(path)) return true;
            if (result.Entries.Count >= maximumResults) { result.Truncated = true; return false; }
            result.Entries.Add(new DshDataDiscoveryEntry
            {
                Kind = DshDataDiscoveryEntry.DymKind, Path = path, Display = System.IO.Path.GetFileName(path)
            });
            return true;
        }

        private static bool AddHome(DshDataDiscoveryResult result, Dictionary<string, DshDataDiscoveryEntry> homes,
            string path, string home, int maximumResults)
        {
            if (homes.TryGetValue(home, out DshDataDiscoveryEntry existing))
            {
                if (PathComparer.Equals(existing.Path, home) && !PathComparer.Equals(path, home))
                {
                    existing.Path = path;
                    existing.Display = path + " (DSH_HOME: " + home + ")";
                }
                return true;
            }
            if (result.Entries.Count >= maximumResults) { result.Truncated = true; return false; }
            var entry = new DshDataDiscoveryEntry
            {
                Kind = DshDataDiscoveryEntry.DshDirectoryKind, Path = path,
                Display = PathComparer.Equals(path, home) ? path : path + " (DSH_HOME: " + home + ")"
            };
            homes.Add(home, entry);
            result.Entries.Add(entry);
            return true;
        }
    }
}
