using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DshInstaller.Shared.Install
{
    public static class LauncherSelection
    {
        // Older launchers prefer the user's saved root over adjacent launcher.json.
        // Preserve a recoverable copy before applying the installer's explicit selection.
        public static string Apply(InstallOptions options, Func<bool> launcherIsRunning = null)
        {
            string path = options.LauncherSettingsFile;
            if (String.IsNullOrWhiteSpace(path)) return null;
            path = InstallPaths.Canonical(path);
            if (!String.Equals(Path.GetFileName(path), "LauncherSettings.json", StringComparison.OrdinalIgnoreCase)
                || InstallPaths.HasReparseAncestor(path))
                throw new InvalidDataException("启动器设置路径无效，未修改现有设置。");
            if (!File.Exists(path)) return null;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("启动器设置文件不能是符号链接。");
            var info = new FileInfo(path);
            if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("启动器设置文件过大，未修改现有设置。");
            byte[] original = File.ReadAllBytes(path);
            var settings = JsonNode.Parse(original) as JsonObject
                ?? throw new InvalidDataException("启动器设置文件无效，未修改现有设置。");
            string root = InstallPaths.Canonical(options.DshRoot);
            string previous = Value(settings, "dshRoot")?.GetValue<string>();
            bool rootChanged = String.IsNullOrWhiteSpace(previous) || !Path.IsPathFullyQualified(previous)
                || !String.Equals(InstallPaths.Canonical(previous), root, StringComparison.OrdinalIgnoreCase);
            string node = options.ReusePath("node") ?? Path.Combine(options.ComponentsRoot, "node", "node.exe");
            bool nodeChanged = File.Exists(node) && !String.Equals(Value(settings, "nodePath")?.GetValue<string>(), node, StringComparison.OrdinalIgnoreCase);
            if (!rootChanged && !nodeChanged) return null;
            if ((launcherIsRunning ?? HasRunningLauncher)())
                throw new IOException("请先退出正在运行的启动器，再切换 DSH。");
            Put(settings, "dshRoot", JsonValue.Create(root));
            if (File.Exists(node)) Put(settings, "nodePath", JsonValue.Create(node));
            if (rootChanged)
            {
                Put(settings, "apiKeyProtected", JsonValue.Create(String.Empty));
                Put(settings, "apiKeyValidatedUtc", null);
                Put(settings, "apiKeyLastValidationSucceeded", JsonValue.Create(false));
                Put(settings, "legacyApiKeyMigrationCompleted", JsonValue.Create(true));
            }
            string backup = path + ".before-selection-" + Guid.NewGuid().ToString("N") + ".bak";
            string temporary = path + ".selection-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(backup, original);
                File.WriteAllText(temporary, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                if (!File.ReadAllBytes(path).SequenceEqual(original))
                    throw new IOException("启动器正在更新设置，请退出启动器后重新选择 DSH。");
                File.Move(temporary, path, true);
                return backup;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static JsonNode Value(JsonObject settings, string name)
        {
            return settings.FirstOrDefault(pair => String.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        private static bool HasRunningLauncher()
        {
            bool running = false;
            foreach (string name in new[] { "DeepSeek Harness", "DeepSeek Harness.Core" })
                foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
                    using (process) { running |= !process.HasExited; }
            return running;
        }

        private static void Put(JsonObject settings, string name, JsonNode value)
        {
            foreach (string key in settings.Select(pair => pair.Key).Where(key => String.Equals(key, name, StringComparison.OrdinalIgnoreCase)).ToList())
                settings.Remove(key);
            settings[name] = value;
        }
    }
}
