using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DshInstaller.Shared.Install
{
    public sealed class RecommendedPluginItem
    {
        public string Owner { get; set; } = String.Empty;
        public string Repository { get; set; } = String.Empty;
        public string Description { get; set; } = String.Empty;
        public string Note { get; set; } = String.Empty;
        public string ImageUrl { get; set; } = String.Empty;
        public string InstallSpecifier { get; set; } = String.Empty;

        public string DisplayName
        {
            get
            {
                return String.IsNullOrWhiteSpace(Owner)
                    ? Repository
                    : Owner + "/" + Repository;
            }
        }
    }

    public static class RecommendedPluginFeed
    {
        private const string Repository =
            "YunxiRamito/Dafeiyu-Go-DeepSeek-Harness-Click-To-Run";

        public static async Task<List<RecommendedPluginItem>> LoadAsync(
            string sourcePreference)
        {
            string raw = "https://raw.githubusercontent.com/"
                + Repository + "/main/featured-plugins.json";
            string jsdelivr = "https://cdn.jsdelivr.net/gh/"
                + Repository + "@main/featured-plugins.json";
            string[] urls = String.Equals(
                    sourcePreference,
                    MirrorSource.Official,
                    StringComparison.OrdinalIgnoreCase)
                ? new[] { raw }
                : new[] { jsdelivr };

            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Dafeiyu-Go-Installer");
                for (int index = 0; index < urls.Length; index++)
                {
                    try
                    {
                        string json = await client.GetStringAsync(
                            urls[index]).ConfigureAwait(false);
                        List<RecommendedPluginItem> items =
                            Parse(json);
                        if (items.Count > 0)
                        {
                            return items;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return BuiltIn();
        }

        public static List<RecommendedPluginItem> Parse(string json)
        {
            List<RecommendedPluginItem> result =
                new List<RecommendedPluginItem>();
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement items;
                if (!document.RootElement.TryGetProperty(
                        "items",
                        out items)
                    || items.ValueKind != JsonValueKind.Array)
                {
                    return result;
                }

                foreach (JsonElement entry in items.EnumerateArray())
                {
                    RecommendedPluginItem item =
                        new RecommendedPluginItem
                        {
                            Owner = ReadString(entry, "owner"),
                            Repository = ReadString(entry, "repository"),
                            Description = ReadString(entry, "description"),
                            Note = ReadString(entry, "note"),
                            ImageUrl = ReadString(entry, "imageUrl"),
                            InstallSpecifier = ReadString(
                                entry,
                                "installSpecifier")
                        };
                    if (!String.IsNullOrWhiteSpace(item.Repository)
                        && !String.IsNullOrWhiteSpace(
                            item.InstallSpecifier))
                    {
                        result.Add(item);
                    }
                }
            }

            return result;
        }

        private static string ReadString(
            JsonElement element,
            string name)
        {
            JsonElement value;
            return element.TryGetProperty(name, out value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? String.Empty
                    : String.Empty;
        }

        private static List<RecommendedPluginItem> BuiltIn()
        {
            return new List<RecommendedPluginItem>
            {
                new RecommendedPluginItem
                {
                    Owner = "MeteorNOX",
                    Repository = "DeepSeek-Balance-Whale-Widget",
                    Description = "右下角余额挂件，支持拖拽吸附。",
                    ImageUrl = "https://github.com/MeteorNOX.png?size=80",
                    InstallSpecifier = "github:MeteorNOX/DeepSeek-Balance-Whale-Widget#54d56d552608c430c5e8c79d3e93314695ea25b0"
                },
                new RecommendedPluginItem
                {
                    Owner = "wenbin-wb",
                    Repository = "dsh-bridge",
                    Description = "多通道远程访问与安全守护插件。",
                    ImageUrl = "https://github.com/wenbin-wb.png?size=80",
                    InstallSpecifier = "github:wenbin-wb/dsh-bridge#c18bdd83fb494c64ea10ce3fca49f5fbf897c545"
                },
                new RecommendedPluginItem
                {
                    Owner = "yyh-001",
                    Repository = "dsh-meme",
                    Description = "表情包插件，支持文本斗图与情绪发图。",
                    ImageUrl = "https://github.com/yyh-001.png?size=80",
                    InstallSpecifier = "github:yyh-001/dsh-meme#8ddc253c8165a123a632d7e7f73917705b7d306c"
                },
                new RecommendedPluginItem
                {
                    Owner = "xmanrui",
                    Repository = "dsh-im",
                    Description = "接入飞书、微信、钉钉等 IM 机器人。",
                    ImageUrl = "https://github.com/xmanrui.png?size=80",
                    InstallSpecifier = "github:xmanrui/dsh-im#42776b5beb4b304afbca0c8703d647cb59614df0"
                }
            };
        }
    }
}
