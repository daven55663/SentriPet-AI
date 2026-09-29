using System;
using System.Collections.Generic;
using System.Globalization;
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
        public bool TrayNumber;               // the tray icon shows the lowest remaining % (#21)
        public bool TrayOnly;                 // no pet on the desktop, only the tray icon (#21)
        public bool ExportUsage;              // keep usage.json in the settings folder up to date (#22, see UsageExport)
        public bool UsageServer;              // the local web page for OBS / scripts on 127.0.0.1 (#22)
        public int UsagePort = SentriPet.UsageServer.DefaultPort;
        public bool HideOnFullscreen = true;  // step aside for full-screen videos / games
        public bool ClaudeEstimate = true;    // fill the gaps between desktop samples from Claude Code transcripts
        public bool Chatty = true;            // idle speech bubbles
        public bool Notifications = true;
        public bool UseItReminder = true;     // nag to spend a weekly/monthly quota before it resets unused
        public Dictionary<string, string> UseItNotified = new Dictionary<string, string>();   // "provider|meter" → "resetUtc#level" already announced
        public bool QuietHours;               // quiet time (#17): no notifications, no speech of its own
        public string QuietFrom = "22:00", QuietTo = "08:00";
        public string QuietDays = "0123456";  // weekdays with quiet hours (Sunday = 0)
        public DateTime? PausedUntil;         // "pause reminders" from the menu (UTC)
        public bool WeeklyReport = true;      // a notification with the summary when a weekly/monthly window ends (#16)
        public int WarnAt = 80;               // used %
        public int CriticalAt = 95;           // used %
        public bool AutoStart = true;
        public double? X, Y;                  // window position (DIPs)
        public string Anchor = "br";          // which corner stays fixed when the widget resizes
        public int CodexLiveMinutes = 5;      // 0 = only local session logs
        public string ClaudeWeeklyReset = ""; // optional override, e.g. "Thu 23:00" / "週四 23:00"
        public bool ClaudeStatusBridge;       // SentriPet is Claude Code's status-line command (official usage, see ClaudeStatusLine)
        public bool AgentHooks;               // "done / waiting for you" from Claude Code's hooks and Codex's notify (#14, see AgentHooks)
        public bool AgentHookNotify;          // …also as a notification
        public string CodexNotifyChain;       // the notify array the user had in Codex's config.toml (still run)
        public string ClaudeStatusLineChain;  // the user's own statusLine object (JSON), shown through SentriPet and restored when turned off
        public Dictionary<string, bool> Enabled = new Dictionary<string, bool>();
        public List<string> Order = new List<string>();
        public bool FirstRunDone;
        public string TerminalColor = "green";
        public string Language = "auto";         // "auto" (the system's language) or zh-TW, zh-CN, en, ja, ko

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
                s.TrayNumber = Json.Bool(Json.Get(o, "trayNumber")) ?? false;
                s.TrayOnly = Json.Bool(Json.Get(o, "trayOnly")) ?? false;
                s.ExportUsage = Json.Bool(Json.Get(o, "exportUsage")) ?? false;
                s.UsageServer = Json.Bool(Json.Get(o, "usageServer")) ?? false;
                s.UsagePort = (int)Clamp(Json.Num(Json.Get(o, "usagePort")) ?? SentriPet.UsageServer.DefaultPort, 1024, 65535);
                s.HideOnFullscreen = Json.Bool(Json.Get(o, "hideOnFullscreen")) ?? true;
                s.ClaudeEstimate = Json.Bool(Json.Get(o, "claudeEstimate")) ?? true;
                s.Chatty = Json.Bool(Json.Get(o, "chatty")) ?? true;
                s.Notifications = Json.Bool(Json.Get(o, "notifications")) ?? true;
                s.UseItReminder = Json.Bool(Json.Get(o, "useItReminder")) ?? true;
                var un = Json.Obj(Json.Get(o, "useItNotified"));
                if (un != null) foreach (var kv in un) { var str = Json.Str(kv.Value); if (str != null) s.UseItNotified[kv.Key] = str; }
                s.WeeklyReport = Json.Bool(Json.Get(o, "weeklyReport")) ?? true;
                s.QuietHours = Json.Bool(Json.Get(o, "quietHours")) ?? false;
                s.QuietFrom = Json.Str(Json.Get(o, "quietFrom")) ?? s.QuietFrom;
                s.QuietTo = Json.Str(Json.Get(o, "quietTo")) ?? s.QuietTo;
                s.QuietDays = Json.Str(Json.Get(o, "quietDays")) ?? s.QuietDays;
                DateTime paused;
                if (DateTime.TryParse(Json.Str(Json.Get(o, "pausedUntil")) ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out paused))
                    s.PausedUntil = DateTime.SpecifyKind(paused, DateTimeKind.Utc);
                s.WarnAt = (int)Clamp(Json.Num(Json.Get(o, "warnAt")) ?? 80, 10, 99);
                s.CriticalAt = (int)Clamp(Json.Num(Json.Get(o, "criticalAt")) ?? 95, 10, 100);
                s.AutoStart = Json.Bool(Json.Get(o, "autoStart")) ?? true;
                s.X = Json.Num(Json.Get(o, "x"));
                s.Y = Json.Num(Json.Get(o, "y"));
                s.Anchor = Json.Str(Json.Get(o, "anchor")) ?? "br";
                s.CodexLiveMinutes = (int)Clamp(Json.Num(Json.Get(o, "codexLiveMinutes")) ?? 5, 0, 120);
                s.ClaudeWeeklyReset = Json.Str(Json.Get(o, "claudeWeeklyReset")) ?? "";
                s.ClaudeStatusBridge = Json.Bool(Json.Get(o, "claudeStatusBridge")) ?? false;
                s.AgentHooks = Json.Bool(Json.Get(o, "agentHooks")) ?? false;
                s.AgentHookNotify = Json.Bool(Json.Get(o, "agentHookNotify")) ?? false;
                s.CodexNotifyChain = Json.Str(Json.Get(o, "codexNotifyChain"));
                s.ClaudeStatusLineChain = Json.Str(Json.Get(o, "claudeStatusLineChain"));
                s.FirstRunDone = Json.Bool(Json.Get(o, "firstRunDone")) ?? false;
                s.TerminalColor = Json.Str(Json.Get(o, "terminalColor")) ?? "green";
                s.Language = Json.Str(Json.Get(o, "language")) ?? "auto";
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
                o["trayNumber"] = TrayNumber;
                o["trayOnly"] = TrayOnly;
                o["exportUsage"] = ExportUsage;
                o["usageServer"] = UsageServer;
                o["usagePort"] = UsagePort;
                o["hideOnFullscreen"] = HideOnFullscreen;
                o["claudeEstimate"] = ClaudeEstimate;
                o["chatty"] = Chatty;
                o["notifications"] = Notifications;
                o["useItReminder"] = UseItReminder;
                o["useItNotified"] = UseItNotified.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                o["weeklyReport"] = WeeklyReport;
                o["quietHours"] = QuietHours;
                o["quietFrom"] = QuietFrom;
                o["quietTo"] = QuietTo;
                o["quietDays"] = QuietDays;
                if (PausedUntil.HasValue && PausedUntil.Value > DateTime.UtcNow) o["pausedUntil"] = PausedUntil.Value.ToString("o", CultureInfo.InvariantCulture);
                o["warnAt"] = WarnAt;
                o["criticalAt"] = CriticalAt;
                o["autoStart"] = AutoStart;
                o["x"] = X.HasValue ? (object)Math.Round(X.Value, 1) : null;
                o["y"] = Y.HasValue ? (object)Math.Round(Y.Value, 1) : null;
                o["anchor"] = Anchor;
                o["codexLiveMinutes"] = CodexLiveMinutes;
                o["claudeWeeklyReset"] = ClaudeWeeklyReset;
                o["claudeStatusBridge"] = ClaudeStatusBridge;
                o["agentHooks"] = AgentHooks;
                o["agentHookNotify"] = AgentHookNotify;
                if (CodexNotifyChain != null) o["codexNotifyChain"] = CodexNotifyChain;
                if (ClaudeStatusLineChain != null) o["claudeStatusLineChain"] = ClaudeStatusLineChain;
                o["firstRunDone"] = FirstRunDone;
                o["terminalColor"] = TerminalColor;
                o["language"] = Language;
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
