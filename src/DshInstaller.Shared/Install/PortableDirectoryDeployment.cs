using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace DshInstaller.Shared.Install
{
    internal static class PortableDirectoryDeployment
    {
        internal static void MoveToTarget(string source, string target, CancellationToken cancellation)
        {
            if (!Path.IsPathFullyQualified(source) || !Path.IsPathFullyQualified(target))
                throw new ArgumentException("Deployment directories must be absolute.");
            source = Path.GetFullPath(source);
            target = Path.GetFullPath(target);
            if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Deployment target already exists.");
            cancellation.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(source, target);
                return;
            }

            string parent = Path.GetDirectoryName(target);
            Directory.CreateDirectory(parent);
            string staging = Path.Combine(parent, ".node-stage-" + Guid.NewGuid().ToString("N"));
            try
            {
                var pending = new Stack<(string Source, string Target)>();
                pending.Push((source, staging));
                while (pending.Count > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var directory = pending.Pop();
                    RejectLink(directory.Source);
                    Directory.CreateDirectory(directory.Target);
                    foreach (string file in Directory.EnumerateFiles(directory.Source))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        RejectLink(file);
                        File.Copy(file, Path.Combine(directory.Target, Path.GetFileName(file)), false);
                    }
                    foreach (string child in Directory.EnumerateDirectories(directory.Source))
                        pending.Push((child, Path.Combine(directory.Target, Path.GetFileName(child))));
                }
                cancellation.ThrowIfCancellationRequested();
                Directory.Move(staging, target);
                Directory.Delete(source, true);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Portable component archive contains a directory or file link.");
        }
    }
}
