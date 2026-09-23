using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    class AppSettings
    {
        public string Theme = "pet";
        public bool DailyRandomTheme;
        public string RandomThemeDate;
        public double Scale = 1.0;
        public double Opacity = 1.0;
        public bool AlwaysOnTop = true;
        public bool ClickThrough;
        public bool LowPower;
        public bool HideOnFullscreen = true;  // step aside for full-screen videos / games
        public bool ClaudeEstimate = true;    // fill the gaps between desktop samples from Claude Code transcripts
        public bool Chatty = true;            // idle speech bubbles
        public bool Notifications = true;
        public int WarnAt = 80;               // used %
        public int CriticalAt = 95;           // used %
        public bool AutoStart = true;
        public double? X, Y;                  // window position (DIPs)
        public string Anchor = "br";          // which corner stays fixed when the widget resizes
        public int CodexLiveMinutes = 5;      // 0 = only local session logs
        public string ClaudeWeeklyReset = ""; // optional override, e.g. "Thu 23:00" / "週四 23:00"
        public Dictionary<string, bool> Enabled = new Dictionary<string, bool>();
        public List<string> Order = new List<string>();
        public bool FirstRunDone;
        public string TerminalColor = "green";

        public bool IsEnabled(string id)
        {
            bool v;
            return !Enabled.TryGetValue(id, out v) || v;
        }

        // ------------------------------------------------------------ persistence

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(AppPaths.SettingsFile)) return s;
                var o = Json.Obj(Json.Parse(File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8)));
                if (o == null) return s;
                s.Theme = Json.Str(Json.Get(o, "theme")) ?? s.Theme;
                s.DailyRandomTheme = Json.Bool(Json.Get(o, "dailyRandomTheme")) ?? false;
                s.RandomThemeDate = Json.Str(Json.Get(o, "randomThemeDate"));
                s.Scale = Clamp(Json.Num(Json.Get(o, "scale")) ?? 1.0, 0.5, 2.5);
                s.Opacity = Clamp(Json.Num(Json.Get(o, "opacity")) ?? 1.0, 0.2, 1.0);
                s.AlwaysOnTop = Json.Bool(Json.Get(o, "alwaysOnTop")) ?? true;
                s.ClickThrough = Json.Bool(Json.Get(o, "clickThrough")) ?? false;
                s.LowPower = Json.Bool(Json.Get(o, "lowPower")) ?? false;
                s.HideOnFullscreen = Json.Bool(Json.Get(o, "hideOnFullscreen")) ?? true;
                s.ClaudeEstimate = Json.Bool(Json.Get(o, "claudeEstimate")) ?? true;
                s.Chatty = Json.Bool(Json.Get(o, "chatty")) ?? true;
                s.Notifications = Json.Bool(Json.Get(o, "notifications")) ?? true;
                s.WarnAt = (int)Clamp(Json.Num(Json.Get(o, "warnAt")) ?? 80, 10, 99);
                s.CriticalAt = (int)Clamp(Json.Num(Json.Get(o, "criticalAt")) ?? 95, 10, 100);
                s.AutoStart = Json.Bool(Json.Get(o, "autoStart")) ?? true;
                s.X = Json.Num(Json.Get(o, "x"));
                s.Y = Json.Num(Json.Get(o, "y"));
                s.Anchor = Json.Str(Json.Get(o, "anchor")) ?? "br";
                s.CodexLiveMinutes = (int)Clamp(Json.Num(Json.Get(o, "codexLiveMinutes")) ?? 5, 0, 120);
                s.ClaudeWeeklyReset = Json.Str(Json.Get(o, "claudeWeeklyReset")) ?? "";
                s.FirstRunDone = Json.Bool(Json.Get(o, "firstRunDone")) ?? false;
                s.TerminalColor = Json.Str(Json.Get(o, "terminalColor")) ?? "green";
                var en = Json.Obj(Json.Get(o, "enabled"));
                if (en != null) foreach (var kv in en) s.Enabled[kv.Key] = Json.Bool(kv.Value) ?? true;
                var ord = Json.Arr(Json.Get(o, "order"));
                if (ord != null) s.Order = ord.Cast<object>().Select(x => Json.Str(x)).Where(x => x != null).ToList();
            }
            catch (Exception ex)
            {
                Log.Error("settings load failed", ex);
            }
            return s;
        }

        public void Save()
        {
            try
            {
                AppPaths.EnsureDataDirs();
                var o = new Dictionary<string, object>();
                o["theme"] = Theme;
                o["dailyRandomTheme"] = DailyRandomTheme;
                o["randomThemeDate"] = RandomThemeDate;
                o["scale"] = Math.Round(Scale, 3);
                o["opacity"] = Math.Round(Opacity, 3);
                o["alwaysOnTop"] = AlwaysOnTop;
                o["clickThrough"] = ClickThrough;
                o["lowPower"] = LowPower;
                o["hideOnFullscreen"] = HideOnFullscreen;
                o["claudeEstimate"] = ClaudeEstimate;
                o["chatty"] = Chatty;
                o["notifications"] = Notifications;
                o["warnAt"] = WarnAt;
                o["criticalAt"] = CriticalAt;
                o["autoStart"] = AutoStart;
                o["x"] = X.HasValue ? (object)Math.Round(X.Value, 1) : null;
                o["y"] = Y.HasValue ? (object)Math.Round(Y.Value, 1) : null;
                o["anchor"] = Anchor;
                o["codexLiveMinutes"] = CodexLiveMinutes;
                o["claudeWeeklyReset"] = ClaudeWeeklyReset;
                o["firstRunDone"] = FirstRunDone;
                o["terminalColor"] = TerminalColor;
                o["enabled"] = Enabled.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                o["order"] = Order.ToList();
                string tmp = AppPaths.SettingsFile + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(o, true), new UTF8Encoding(false));
                if (File.Exists(AppPaths.SettingsFile)) File.Replace(tmp, AppPaths.SettingsFile, null);
                else File.Move(tmp, AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Log.Error("settings save failed", ex);
            }
        }

        static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }
    }
}
