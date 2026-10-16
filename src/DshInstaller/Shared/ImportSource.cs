using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using DeepSeekHarnessLauncher.Backup;
using DshInstaller.Shared.Backup.Official;

namespace DshInstaller
{
    /// <summary>Read-only source adaptation. A selected migration executable is data, never executed.</summary>
    internal sealed class ImportSource : IDisposable
    {
        internal string Home { get; private set; }
        internal string DisplayPath { get; private set; }
        internal string ImportRoot { get; private set; }
        internal List<string> Warnings { get; } = new List<string>();
        private string _staging;

        internal static ImportSource Open(string selected, string temporaryRoot, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string path = Protocol.Full(selected);
            Protocol.Plain(path);
            if (Directory.Exists(path)) return OpenFolder(path);
            if (!File.Exists(path)) throw new FileNotFoundException("所选来源不存在。 / The selected source does not exist.", path);
            if (!String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择 DSH 数据文件夹，或由 Dafeiyu-Go 导出的官方迁移 EXE。 / Choose a DSH data folder or an official migration EXE exported by Dafeiyu-Go.");

            string root = Protocol.Full(temporaryRoot);
            Protocol.Plain(root);
            if (Protocol.Contains(root, path)) throw new InvalidDataException("迁移包必须位于临时导入目录之外。 / The archive must be outside the import staging directory.");
            var source = new ImportSource { DisplayPath = path };
            try
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var segment = Protocol.Segment(input, token);
                using var zip = new ZipArchive(segment, ZipArchiveMode.Read, false, Encoding.UTF8);
                PayloadManifest manifest = Protocol.ValidateZip(zip, token);
                if (manifest.Warnings != null) source.Warnings.AddRange(manifest.Warnings);
                if (manifest.Files.Any(item => item.Target.StartsWith("home/.credentials", StringComparison.Ordinal) || item.Target == "home/.env" || item.Target.StartsWith("compat/", StringComparison.Ordinal)))
                    source.Warnings.Add("迁移包中的账号凭据、环境配置和兼容偏好不会导入 Dafeiyu-Go。 / Account credentials, environment settings and compatibility preferences are not imported into Dafeiyu-Go.");
                token.ThrowIfCancellationRequested();
                source._staging = Path.Combine(root, "import-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(source._staging);
                Protocol.Plain(source._staging);
                source.Home = Path.Combine(source._staging, ".dsh");
                source.ImportRoot = source.Home;
                foreach (PayloadFile item in manifest.Files)
                {
                    token.ThrowIfCancellationRequested();
                    string relative = item.Target.StartsWith("home/", StringComparison.Ordinal) ? item.Target.Substring(5)
                        : item.Target.StartsWith("agents/skills/", StringComparison.Ordinal) ? item.Target.Substring(7) : null;
                    // The import core separately filters profile settings, credentials, and application bundles.
                    if (relative == null || !new[] { "sessions/", "storages/", "attachments/", "skills/", "plugins/", "profiles/" }
                        .Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal))) continue;
                    string destination = Protocol.Within(source.Home, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using var entry = zip.GetEntry(item.Path).Open();
                    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    byte[] hash = Protocol.CopyHash(entry, output, item.Length, token, null);
                    if (output.Length != item.Length || !String.Equals(Protocol.Hex(hash), item.Hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("迁移包在预览期间发生变化。 / Migration archive changed during preview.");
                }
                source.Home = DshDataImportService.ResolveSourceHome(source.Home);
                return source;
            }
            catch { source.Dispose(); throw; }
        }

        private static ImportSource OpenFolder(string path)
        {
            string[] candidates = { path, Path.Combine(path, "home"), Path.Combine(path, "data", "home") };
            foreach (string candidate in candidates)
            {
                if (!Directory.Exists(candidate)) continue;
                try
                {
                    string home = DshDataImportService.ResolveSourceHome(candidate);
                    return new ImportSource { Home = home, DisplayPath = path, ImportRoot = candidate };
                }
                catch (InvalidDataException) { }
            }
            throw new InvalidDataException("没有找到可导入的 DSH 数据。请选择 .dsh / DSH_HOME，或包含 home、data/home 的导出目录；如果只有迁移 EXE，请使用“从官方迁移 EXE 导入”。 / No DSH data found. Choose .dsh / DSH_HOME or an exported home / data/home folder, or select the official migration EXE.");
        }

        public void Dispose()
        {
            string staging = _staging;
            _staging = null;
            if (staging != null && Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
}
