using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DshInstaller.Shared.Install
{
    public static class DshVersionResolver
    {
        private static readonly Regex Exact = new Regex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);
        public static bool IsExact(string version) => version != null && version.Length <= 100 && Exact.IsMatch(version);
        public static string Resolve(string requested, Action<DownloadProgress> progress, Func<bool> cancellation)
        {
            string spec = string.IsNullOrWhiteSpace(requested) ? WellKnown.DshPackageVersion : requested.Trim();
            if (IsExact(spec)) return spec;
            if (spec.Length > 50 || !Regex.IsMatch(spec, @"^[a-zA-Z][a-zA-Z0-9._-]*$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException(SharedText.T("完整本地包需要具体版本号或 latest / next / alpha 标签，请修改版本选择。", "The complete bundle requires an exact version or a latest / next / alpha tag."));
            string url = "https://registry.npmjs.org/@deepseek-ai%2fdsh/" + Uri.EscapeDataString(spec);
            string text = DownloadEngine.DownloadText(new List<string> { url }, 8000, progress, cancellation);
            if (string.IsNullOrWhiteSpace(text)) throw new IOException("无法读取官方 DSH " + spec + " 版本，未回退旧版本。");
            return ParseVersion(text);
        }
        internal static string ParseVersion(string text)
        {
            using var json = JsonDocument.Parse(text);
            if (!json.RootElement.TryGetProperty("name", out var name) || name.GetString() != WellKnown.DshPackage
                || !json.RootElement.TryGetProperty("version", out var version) || !IsExact(version.GetString()))
                throw new InvalidDataException("官方 DSH 版本元数据无效。");
            return version.GetString();
        }
    }
}
