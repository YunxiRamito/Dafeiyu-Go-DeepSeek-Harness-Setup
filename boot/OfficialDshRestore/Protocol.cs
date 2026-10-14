using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DshInstaller.Shared.Backup.Official
{
    [DataContract]
    public sealed class PayloadFile
    {
        [DataMember] public string Path;
        [DataMember] public string Target;
        [DataMember] public string Group;
        [DataMember] public string Hash;
        [DataMember] public long Length;
    }
    [DataContract]
    public sealed class PayloadManifest
    {
        [DataMember] public int Version = 1;
        [DataMember] public string ProfileMapping = "web -> desktop; existing desktop official bundles preserved";
        [DataMember] public List<PayloadFile> Files = new List<PayloadFile>();
        [DataMember] public List<string> Warnings = new List<string>();
    }
    public sealed class Outcome
    {
        public bool Ok, Canceled;
        public string OutputPath, Error, Summary;
        public int Imported, Skipped;
        public List<string> Warnings = new List<string>();
    }
    public static class Protocol
    {
        static Protocol()
        {
#if NETFRAMEWORK
            // Framework's default legacy path policy would break otherwise valid migrations from long G: project paths.
            Type context = typeof(object).Assembly.GetType("System.AppContext");
            if (context != null)
            {
                var method = context.GetMethod("SetSwitch", new[] { typeof(string), typeof(bool) });
                if (method != null)
                    foreach (string name in new[] { "Switch.System.IO.UseLegacyPathHandling", "Switch.System.IO.BlockLongPaths" })
                        method.Invoke(null, new object[] { name, false });
            }
#endif
        }
        public const int FooterSize = 64, MaxFiles = 250000;
        public const long MaxBytes = 20L * 1024 * 1024 * 1024;
        public const string Magic = "DSH-OFFICIAL-SFX";
        public static string Full(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("A directory or file path is required.");
            string full = System.IO.Path.GetFullPath(path.Trim());
            return full.Length > System.IO.Path.GetPathRoot(full).Length ? full.TrimEnd('\\', '/') : full;
        }
        public static bool Contains(string root, string path)
        {
            if (String.IsNullOrWhiteSpace(root)) return false;
            root = Full(root); path = Full(path);
            return String.Equals(root, path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        public static void Plain(string path)
        {
            string current = Full(path);
            while (!String.IsNullOrEmpty(current))
            {
                try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point is not allowed: " + current); }
                catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
                current = System.IO.Path.GetDirectoryName(current);
            }
        }
        public static void Relative(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length > 230 || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0)
                throw new InvalidDataException("Unsafe relative path: " + path);
            foreach (string part in path.Split('/'))
            {
                string stem = part.Split('.')[0];
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ")
                    || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || part.Any(c => c < 32)
                    || new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
                    || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && Char.IsDigit(stem[3])))
                    throw new InvalidDataException("Unsafe path component: " + part);
            }
        }
        public static string Within(string root, string relative)
        {
            Relative(relative); string path = Full(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            if (!Contains(root, path) || String.Equals(Full(root), path, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escaped its root.");
            Plain(path); return path;
        }
        public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public static byte[] CopyHash(Stream input, Stream output, long maximum, CancellationToken token, Action<long> onBytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] buffer = new byte[81920]; int read; long bytes = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested(); bytes = checked(bytes + read);
                    if (bytes > maximum) throw new InvalidDataException("File exceeds its declared or allowed size.");
                    if (output != null) output.Write(buffer, 0, read);
                    hash.TransformBlock(buffer, 0, read, buffer, 0); if (onBytes != null) onBytes(read);
                }
                hash.TransformFinalBlock(new byte[0], 0, 0); return hash.Hash;
            }
        }
        public static void Footer(Stream output, long length, byte[] hash)
        {
            byte[] bytes = new byte[FooterSize]; Encoding.ASCII.GetBytes(Magic).CopyTo(bytes, 0);
            BitConverter.GetBytes(1).CopyTo(bytes, 16); BitConverter.GetBytes(length).CopyTo(bytes, 20); hash.CopyTo(bytes, 28); output.Write(bytes, 0, bytes.Length);
        }
        public static void ManifestWrite(Stream output, PayloadManifest manifest)
        { new DataContractJsonSerializer(typeof(PayloadManifest)).WriteObject(output, manifest); }
        public static PayloadManifest Validate(string exe, CancellationToken token)
        {
            using (FileStream file = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (Stream segment = Segment(file, token))
            using (ZipArchive zip = new ZipArchive(segment, ZipArchiveMode.Read, false, Encoding.UTF8))
                return ValidateZip(zip, token);
        }
        public static Stream Segment(FileStream file, CancellationToken token)
        {
            if (file.Length < FooterSize + 2) throw new InvalidDataException("Incomplete SFX.");
            byte[] footer = new byte[FooterSize]; file.Position = file.Length - FooterSize; ReadExact(file, footer);
            long length = BitConverter.ToInt64(footer, 20), start = file.Length - FooterSize - length;
            if (Encoding.ASCII.GetString(footer, 0, Magic.Length) != Magic || BitConverter.ToInt32(footer, 16) != 1
                || length < 22 || length > MaxBytes || start < 2 || footer.Skip(60).Any(b => b != 0)) throw new InvalidDataException("Invalid SFX footer.");
            file.Position = 0; if (file.ReadByte() != 'M' || file.ReadByte() != 'Z') throw new InvalidDataException("Invalid executable header.");
            var segment = new Slice(file, start, length);
            byte[] hash = CopyHash(segment, null, length, token, null);
            if (!hash.SequenceEqual(footer.Skip(28).Take(32))) throw new InvalidDataException("Payload SHA-256 mismatch.");
            segment.Position = 0; return segment;
        }
        public static PayloadManifest ValidateZip(ZipArchive zip, CancellationToken token)
        {
            if (zip.Entries.Count < 2 || zip.Entries.Count > MaxFiles + 1) throw new InvalidDataException("Invalid archive entry count.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested(); Relative(entry.FullName);
                if (entries.ContainsKey(entry.FullName)) throw new InvalidDataException("Duplicate ZIP path: " + entry.FullName);
                entries.Add(entry.FullName, entry);
                int kind = (entry.ExternalAttributes >> 16) & 0xf000;
                if (kind == 0xa000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Archive links are not allowed.");
                if (entry.Length < 0 || entry.Length > MaxBytes || entry.CompressedLength < 0
                    || entry.Length > Math.Max(1024 * 1024, entry.CompressedLength * 1000L)) throw new InvalidDataException("Archive size or compression ratio limit exceeded.");
                total = checked(total + entry.Length); if (total > MaxBytes) throw new InvalidDataException("Archive exceeds 20 GiB.");
            }
            ZipArchiveEntry manifestEntry;
            if (!entries.TryGetValue("manifest.json", out manifestEntry) || manifestEntry.Length > 64 * 1024 * 1024)
                throw new InvalidDataException("Missing or oversized manifest.");
            PayloadManifest manifest;
            using (Stream stream = manifestEntry.Open())
            using (MemoryStream bounded = new MemoryStream())
            {
                CopyHash(stream, bounded, 64 * 1024 * 1024, token, null);
                if (bounded.Length != manifestEntry.Length) throw new InvalidDataException("Manifest uncompressed length mismatch.");
                bounded.Position = 0;
                manifest = (PayloadManifest)new DataContractJsonSerializer(typeof(PayloadManifest)).ReadObject(bounded);
            }
            if (manifest == null || manifest.Version != 1 || manifest.Files == null || manifest.Files.Count < 1 || manifest.Files.Count > MaxFiles)
                throw new InvalidDataException("Invalid manifest.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PayloadFile item in manifest.Files)
            {
                token.ThrowIfCancellationRequested(); Relative(item.Target); Relative(item.Path); Migration.ValidateTarget(item.Target, item.Group);
                if (item.Path != "data/" + item.Target || !seen.Add(item.Path) || item.Length < 0 || item.Hash == null || item.Hash.Length != 64 || item.Hash.Any(c => !Uri.IsHexDigit(c)))
                    throw new InvalidDataException("Invalid manifest path, length or hash.");
                ZipArchiveEntry entry;
                if (!entries.TryGetValue(item.Path, out entry) || entry.Length != item.Length) throw new InvalidDataException("Manifest entry missing or length mismatch.");
                using (Stream source = entry.Open())
                    if (Hex(CopyHash(source, null, item.Length, token, null)) != item.Hash.ToLowerInvariant()) throw new InvalidDataException("File SHA-256 mismatch: " + item.Path);
            }
            if (entries.Count != seen.Count + 1) throw new InvalidDataException("Unlisted archive data.");
            foreach (string name in seen)
            {
                int slash = name.LastIndexOf('/');
                while (slash > 0) { if (seen.Contains(name.Substring(0, slash))) throw new InvalidDataException("File/directory collision."); slash = name.LastIndexOf('/', slash - 1); }
            }
            return manifest;
        }
        private static void ReadExact(Stream input, byte[] bytes)
        { int offset = 0; while (offset < bytes.Length) { int read = input.Read(bytes, offset, bytes.Length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; } }
        private sealed class Slice : Stream
        {
            private readonly Stream stream; private readonly long start, length; private long position;
            public Slice(Stream stream, long start, long length) { this.stream = stream; this.start = start; this.length = length; }
            public override bool CanRead { get { return true; } } public override bool CanSeek { get { return true; } } public override bool CanWrite { get { return false; } }
            public override long Length { get { return length; } }
            public override long Position { get { return position; } set { if (value < 0 || value > length) throw new IOException("Seek outside payload."); position = value; } }
            public override int Read(byte[] buffer, int offset, int count) { stream.Position = start + position; int read = stream.Read(buffer, offset, (int)Math.Min(count, length - position)); position += read; return read; }
            public override long Seek(long offset, SeekOrigin origin) { Position = (origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? position : length) + offset; return position; }
            public override void Flush() { } public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
        }
    }
}
