using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DshInstaller.Shared.Backup.Official
{
    internal static class Json
    {
        public static Dictionary<string, object> Object(string text)
        {
#if NETFRAMEWORK
            object value = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 64 }.DeserializeObject(text);
#else
            object value;
            using (var document = System.Text.Json.JsonDocument.Parse(text)) value = Convert(document.RootElement);
#endif
            var result = value as Dictionary<string, object>; if (result == null) throw new System.IO.InvalidDataException("JSON object required."); return Normalize(result);
        }
#if !NETFRAMEWORK
        private static object Convert(System.Text.Json.JsonElement value)
        {
            switch (value.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object: return value.EnumerateObject().ToDictionary(p => p.Name, p => Convert(p.Value));
                case System.Text.Json.JsonValueKind.Array: return value.EnumerateArray().Select(Convert).ToList();
                case System.Text.Json.JsonValueKind.String: return value.GetString();
                case System.Text.Json.JsonValueKind.True: return true;
                case System.Text.Json.JsonValueKind.False: return false;
                case System.Text.Json.JsonValueKind.Number: return value.GetDouble();
                default: return null;
            }
        }
#endif
        private static Dictionary<string, object> Normalize(Dictionary<string, object> dict)
        {
            foreach (string key in dict.Keys.ToList())
            { var child = dict[key] as Dictionary<string, object>; if (child != null) dict[key] = Normalize(child); else if (dict[key] is object[]) dict[key] = ((object[])dict[key]).ToList(); }
            return dict;
        }
        public static string Serialize(Dictionary<string, object> value)
        {
#if NETFRAMEWORK
            return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 64 }.Serialize(value);
#else
            return System.Text.Json.JsonSerializer.Serialize(value);
#endif
        }
        public static string String(Dictionary<string, object> value, string name)
        { object child; return value.TryGetValue(name, out child) ? child as string : null; }
        public static Dictionary<string, object> Child(Dictionary<string, object> value, string name, bool create)
        {
            object child; if (value.TryGetValue(name, out child)) { var result = child as Dictionary<string, object>; if (result == null) throw new System.IO.InvalidDataException("Invalid JSON object: " + name); return result; }
            var fresh = new Dictionary<string, object>(); if (create) value.Add(name, fresh); return fresh;
        }
        public static List<object> Array(Dictionary<string, object> value, string name, bool create)
        {
            object child; if (value.TryGetValue(name, out child)) { var result = child as List<object>; if (result == null) throw new System.IO.InvalidDataException("Invalid JSON array: " + name); return result; }
            var fresh = new List<object>(); if (create) value.Add(name, fresh); return fresh;
        }
    }
}
