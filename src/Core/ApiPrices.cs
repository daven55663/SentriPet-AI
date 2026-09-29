using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>
    /// API prices (#19): what the tokens would cost through the pay-as-you-go API, to compare with a subscription.
    /// The table (prices.json, with the date it was checked and its sources) is built in; a prices.json in the settings
    /// folder replaces it, so newer prices need no new version. Only an estimate: plans and the API count differently.
    /// </summary>
    static class ApiPrices
    {
        public class Price { public double Input, CacheWrite, CacheRead, Output; }

        static Dictionary<string, Price> table;
        static DateTime checkedOn;
        static List<string> sources = new List<string>();

        /// <summary>The day the prices were checked (from the table).</summary>
        public static DateTime CheckedOn { get { Ensure(); return checkedOn; } }
        public static List<string> Sources { get { Ensure(); return sources; } }

        /// <summary>Test hook: a table's JSON instead of the files.</summary>
        internal static string Override;

        public static string UserFile { get { return Path.Combine(AppPaths.DataDir, "prices.json"); } }

        static void Ensure()
        {
            if (table != null) return;
            string json = Override;
            if (json == null && File.Exists(UserFile))
            {
                try { json = File.ReadAllText(UserFile, Encoding.UTF8); if (Parse(json) == null) json = null; }
                catch (Exception ex) { Log.Warn("prices.json: " + ex.Message); json = null; }
            }
            if (json == null)
            {
                using (var s = typeof(ApiPrices).Assembly.GetManifestResourceStream("SentriPet.prices.json"))
                    json = s == null ? "{}" : new StreamReader(s, Encoding.UTF8).ReadToEnd();
            }
            table = Parse(json) ?? new Dictionary<string, Price>();
        }

        /// <summary>Forgets the loaded table (tests, or after the user's file changed).</summary>
        public static void Reload() { table = null; }

        static Dictionary<string, Price> Parse(string json)
        {
            var root = Json.Obj(Json.TryParse(json));
            var models = Json.Obj(Json.Get(root, "models"));
            if (models == null) return null;
            var t = new Dictionary<string, Price>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in models)
            {
                var a = Json.Arr(kv.Value);
                if (a == null || a.Count < 4) continue;
                var n = a.Cast<object>().Select(x => Json.Num(x) ?? -1).ToList();
                if (n.Any(x => x < 0)) continue;
                t[kv.Key] = new Price { Input = n[0], CacheWrite = n[1], CacheRead = n[2], Output = n[3] };
            }
            DateTime d;
            checkedOn = DateTime.TryParse(Json.Str(Json.Get(root, "checked")) ?? "", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
            var src = Json.Arr(Json.Get(root, "sources"));
            sources = src == null ? new List<string>() : src.Cast<object>().Select(x => Json.Str(x)).Where(x => x != null).ToList();
            return t;
        }

        /// <summary>The price of a model: the longest key its id starts with ("claude-opus-5-5-20260801" → "claude-opus-5-5"), or null.</summary>
        public static Price For(string model)
        {
            Ensure();
            if (string.IsNullOrEmpty(model)) return null;
            string best = null;
            foreach (var k in table.Keys)
                if (model.StartsWith(k, StringComparison.OrdinalIgnoreCase) && (best == null || k.Length > best.Length)) best = k;
            return best == null ? null : table[best];
        }

        /// <summary>What these tokens would cost through the API (USD), or null when the model's price is not known.</summary>
        public static double? Cost(TokenEntry e)
        {
            var p = For(e.Model);
            if (p == null) return null;
            return (e.Input * p.Input + e.CacheWrite * p.CacheWrite + e.CacheRead * p.CacheRead + e.Output * p.Output) / 1e6;
        }
    }
}
