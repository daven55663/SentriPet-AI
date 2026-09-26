using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SentriPet
{
    /// <summary>Copilot's cache, the local Ollama service and JSON plugins — on sample files and a local fake server.</summary>
    static class ProviderTests
    {
        public static void Run(TestKit t)
        {
            try
            {
                Copilot(t);
                Ollama(t);
                Plugins(t);
            }
            finally
            {
                CopilotProvider.CacheOverride = null;
            }
        }

        static void Copilot(TestKit t)
        {
            t.Section("Copilot 額度快取");
            t.Run("Copilot", () =>
            {
                var now = DateTime.UtcNow;
                string dir = t.TempDir("copilot");
                string cache = Path.Combine(dir, "copilot-user-cache.json");
                CopilotProvider.CacheOverride = cache;
                var s = new AppSettings();

                TestKit.WriteFile(cache, "{\"user\":{\"access_type_sku\":\"copilot_pro\",\"copilot_plan\":\"individual\",\"quota_reset_date_utc\":\"" + TestKit.Iso(now.AddDays(7)) + "\"," +
                    "\"quota_snapshots\":{\"chat\":{\"unlimited\":true,\"entitlement\":0},\"completions\":{\"unlimited\":true}," +
                    "\"premium_interactions\":{\"entitlement\":300,\"remaining\":210.5,\"percent_remaining\":70.2,\"unlimited\":false,\"timestamp_utc\":\"" + TestKit.Iso(now.AddHours(-1)) + "\"}}}}");
                var p = new CopilotProvider().Fetch(false, s);
                var pr = p.Meters.FirstOrDefault();
                t.Check("Pro：只有進階請求有上限", p.Meters.Count == 1 && pr.Key == "premium_interactions" && pr.Label == "進階請求" && pr.ShortLabel == "PR", p.Error);
                t.Near("用掉 29.8%", 29.8, pr.Used, 0.001);
                t.Equal("剩餘次數", "210 / 300", pr.ValueText);
                t.Close("每月重置時間", now.AddDays(7), pr.ResetsAt, TimeSpan.FromSeconds(1));
                t.Check("方案與新鮮度", p.Plan == "Pro" && !p.Stale);

                TestKit.WriteFile(cache, "{\"access_type_sku\":\"copilot_pro_plus\",\"quota_snapshots\":{\"premium_interactions\":{\"entitlement\":1500,\"remaining\":1500}}}");
                t.Equal("Pro+ 方案", "Pro+", new CopilotProvider().Fetch(false, s).Plan);

                TestKit.WriteFile(cache, "{\"access_type_sku\":\"free_limited_copilot\",\"limited_user_quotas\":{\"chat\":400,\"completions\":1500}," +
                    "\"monthly_quotas\":{\"chat\":500,\"completions\":4000},\"limited_user_reset_date\":\"" + TestKit.Iso(now.AddDays(10)) + "\"}");
                var f = new CopilotProvider().Fetch(false, s);
                var chat = f.Meters.FirstOrDefault(m => m.Key == "chat");
                var comp = f.Meters.FirstOrDefault(m => m.Key == "completions");
                t.Check("Free：聊天與補全各一條", f.Plan == "Free" && chat != null && comp != null && chat.ValueText == "400 / 500", f.Error);
                t.Check("Free：用量換算", chat != null && Math.Abs(chat.Used - 20) < 0.001 && comp != null && Math.Abs(comp.Used - 62.5) < 0.001);

                TestKit.WriteFile(cache, "{\"quota_snapshots\":{\"chat\":{\"unlimited\":true},\"premium_interactions\":{\"unlimited\":true}}}");
                var u = new CopilotProvider().Fetch(false, s);
                t.Check("全部無限制：顯示 ∞", u.Meters.Count == 1 && u.Meters[0].Unlimited);

                TestKit.WriteFile(cache, "{\"quota_reset_date_utc\":\"" + TestKit.Iso(now.AddDays(-1)) + "\",\"quota_snapshots\":{\"premium_interactions\":{\"entitlement\":300,\"remaining\":3}}}");
                var r = new CopilotProvider().Fetch(false, s).Meters.First();
                t.Check("過了每月重置日：歸零、改成下個月 1 號", r.WasReset && r.Used == 0 && r.ResetsAt.HasValue && r.ResetsAt.Value.Day == 1 && r.ResetsAt > now);

                File.SetLastWriteTimeUtc(cache, now.AddDays(-5));
                var old = new CopilotProvider().Fetch(false, s);
                t.Check("快取 5 天沒更新：標成舊資料", old.Stale && old.Note != null && old.Note.Contains("快取來自"), old.Note);

                TestKit.WriteFile(cache, "{ broken");
                t.Check("快取壞掉：說明原因", new CopilotProvider().Fetch(false, s).Error.Contains("無法解析"));
                TestKit.WriteFile(cache, "{\"other\":1}");
                t.Check("快取沒有額度：說明原因", new CopilotProvider().Fetch(false, s).Error.Contains("沒有額度資訊"));
                File.Delete(cache);
                t.Check("找不到快取：說明原因", new CopilotProvider().Fetch(false, s).Error.Contains("找不到 Copilot 額度快取"));
            });
        }

        static void Ollama(TestKit t)
        {
            t.Section("Ollama 本機服務（假伺服器，port 11434）");
            FakeHttpServer server;
            try { server = new FakeHttpServer(11434); }
            catch (SocketException)
            {
                t.Skip("Ollama", "port 11434 已被使用（這台電腦有在跑 Ollama），略過");
                return;
            }
            t.Run("Ollama", () =>
            {
                using (server)
                {
                    server.Route("/api/ps", 200, "{\"models\":[{\"name\":\"llama3:8b\",\"size\":1}]}");
                    server.Route("/api/tags", 200, "{\"models\":[{\"name\":\"a\"},{\"name\":\"b\"},{\"name\":\"c\"}]}");
                    var s = new OllamaProvider().Fetch(false, new AppSettings());
                    t.Check("有模型載入中", s.Meters.Count == 1 && s.Meters[0].Unlimited && s.Active && s.Plan == "本機", s.Error);
                    t.Equal("說明：模型數與載入中的模型", "3 個模型，載入中：llama3:8b", s.Note);
                    server.Route("/api/ps", 200, "{\"models\":[]}");
                    var idle = new OllamaProvider().Fetch(false, new AppSettings());
                    t.Check("沒有模型載入：閒置", !idle.Active && idle.Note == "3 個模型，目前閒置", idle.Note);
                }
                var off = new OllamaProvider().Fetch(false, new AppSettings());
                t.Check("服務沒開：從桌寵上隱藏", off.Offline && off.Error == "Ollama 沒有在執行");
            });
        }

        static void Plugins(TestKit t)
        {
            t.Section("JSON 外掛（檔案、指令、HTTP）");
            t.Run("Plugins", () =>
            {
                var now = DateTime.UtcNow;
                string dir = t.TempDir("plugins");
                string data = Path.Combine(dir, "data");
                Environment.SetEnvironmentVariable("SENTRIPET_TEST_TOKEN", "secret-123");
                Environment.SetEnvironmentVariable("SENTRIPET_TEST_ENV", "1");
                using (var web = new FakeHttpServer(0))
                {
                    TestKit.WriteFile(Path.Combine(data, "acme.json"), "{\"plan\":\"Team\",\"meters\":[{\"label\":\"每日\",\"used\":42,\"windowMinutes\":1440,\"resetsAt\":\"" + TestKit.Iso(now.AddHours(5)) + "\"}," +
                        "{\"label\":\"每月\",\"remaining\":80,\"windowMinutes\":43200}]}");
                    TestKit.WriteFile(Path.Combine(data, "mapped.json"), "{\"account\":{\"tier\":\"Enterprise\"},\"usage\":{\"requests\":250,\"reset\":" + TestKit.Unix(now.AddDays(2)) + "},\"limits\":{\"requests\":1000}}");
                    TestKit.WriteFile(Path.Combine(data, "dict.json"), "{\"quotas\":{\"a\":{\"pct\":10},\"b\":{\"pct\":20},\"c\":{\"pct\":30}}}");
                    TestKit.WriteFile(Path.Combine(data, "list.json"), "{\"items\":[{\"model\":\"m1\",\"left\":0.75},{\"model\":\"m2\",\"left\":0.5}]}");
                    // a script that prints a log line before the JSON (cmd on Windows, sh elsewhere)
                    string script = Path.Combine(data, Os.Windows ? "print.cmd" : "print.sh");
                    TestKit.WriteFile(script, Os.Windows ? "@echo off\r\necho starting up...\r\ntype \"%~dp0acme.json\"\r\n"
                                                         : "echo starting up...\ncat \"$(dirname \"$0\")/acme.json\"\n");
                    web.Route("/usage", 200, "{\"meters\":[{\"label\":\"API\",\"used\":12.5,\"windowMinutes\":43200}]}");
                    web.Route("/down", 500, "{\"error\":\"boom\"}");

                    Func<string, string> esc = x => x.Replace("\\", "\\\\");
                    Action<string, string> plugin = (name, json) => TestKit.WriteFile(Path.Combine(dir, name + ".json"), json);
                    plugin("acme", "{\"name\":\"Acme AI\",\"color\":\"#FF8800\",\"mascot\":\"cat\",\"intervalSeconds\":5,\"source\":{\"type\":\"file\",\"path\":\"" + esc(Path.Combine(data, "acme.json")) + "\"}}");
                    plugin("mapped", "{\"id\":\"mapped\",\"source\":{\"type\":\"file\",\"path\":\"" + esc(Path.Combine(data, "mapped.json")) + "\"},\"plan\":\"$.account.tier\",\"note\":\"固定的說明\"," +
                        "\"meters\":[{\"key\":\"req\",\"label\":\"Requests\",\"used\":\"$.usage.requests\",\"total\":\"$.limits.requests\",\"resetsAt\":\"$.usage.reset\",\"valueText\":\"{used} / {total}\"}]}");
                    plugin("dict", "{\"source\":{\"type\":\"file\",\"path\":\"" + esc(Path.Combine(data, "d*.json")) + "\"}," +
                        "\"metersFrom\":{\"path\":\"$.quotas\",\"used\":\"pct\",\"labels\":{\"a\":\"Alpha\",\"b\":\"Beta\"},\"windowMinutes\":{\"a\":300},\"onlyLabeled\":true}}");
                    plugin("list", "{\"source\":{\"type\":\"file\",\"path\":\"" + esc(Path.Combine(data, "list.json")) + "\"}," +
                        "\"metersFrom\":{\"path\":\"$.items\",\"keyField\":\"model\",\"remaining\":\"left\",\"scale\":100}}");
                    plugin("cmd", "{\"source\":{\"type\":\"command\",\"command\":\"" + esc(script) + "\",\"timeoutSeconds\":20}}");
                    plugin("cmdargs", Os.Windows
                        ? "{\"source\":{\"type\":\"command\",\"command\":\"cmd.exe\",\"args\":[\"/d\",\"/c\",\"type\",\"" + esc(Path.Combine(data, "acme.json")) + "\"]}}"
                        : "{\"source\":{\"type\":\"command\",\"command\":\"/bin/sh\",\"args\":[\"-c\",\"cat '" + Path.Combine(data, "acme.json") + "'\"]}}");
                    plugin("web", "{\"source\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:" + web.Port + "/usage\",\"headers\":{\"X-Token\":\"${env:SENTRIPET_TEST_TOKEN}\"}}}");
                    plugin("down", "{\"source\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:" + web.Port + "/down\"}}");
                    plugin("weird", "{\"source\":{\"type\":\"ftp\"}}");
                    plugin("nofile", "{\"source\":{\"type\":\"file\",\"path\":\"" + esc(Path.Combine(data, "missing.json")) + "\"}}");
                    plugin("nometers", "{\"source\":{\"type\":\"file\",\"path\":\"" + esc(Path.Combine(data, "mapped.json")) + "\"}}");
                    plugin("off", "{\"enabled\":false,\"source\":{\"type\":\"file\",\"path\":\"x\"}}");
                    plugin("detectno", "{\"detect\":{\"paths\":[\"" + esc(Path.Combine(dir, "nope")) + "\"],\"commands\":[\"sentripet-no-such-command\"]}}");
                    plugin("detectenv", "{\"detect\":{\"paths\":[\"" + esc(Path.Combine(dir, "nope")) + "\"],\"env\":[\"SENTRIPET_TEST_ENV\"]}}");
                    plugin("detectpath", "{\"detect\":{\"paths\":[\"" + esc(Path.Combine(data, "*" + Path.GetExtension(script))) + "\"]}}");
                    TestKit.WriteFile(Path.Combine(dir, "broken.json"), "{ this is broken");

                    var all = CustomProvider.LoadFrom(dir);
                    Func<string, CustomProvider> get = id => all.FirstOrDefault(p => p.Id == id);
                    t.Check("載入外掛：停用的和壞掉的跳過，其他照常", all.Count == 14 && get("off") == null && get("broken") == null, all.Count + " 個：" + string.Join(",", all.Select(p => p.Id)));
                    t.Equal("網址裡的 / 不會被改成 \\", "https://example.com/v1/secret-123", AppPaths.ExpandVars("https://example.com/v1/${env:SENTRIPET_TEST_TOKEN}"));
                    t.Equal("指令參數 /c 保持原樣", "/c", AppPaths.ExpandArg("/c"));
                    t.Equal("指令參數的 ~ 是家目錄", AppPaths.Home + "/tools/usage.py", AppPaths.ExpandArg("~/tools/usage.py"));
                    t.Equal("檔案路徑的 ~ 與 /", Path.Combine(AppPaths.Home, "a", "b.json"), AppPaths.Expand("~/a/b.json"));
                    var acme = get("acme");
                    t.Check("名稱、顏色、造型、最短 10 秒更新", acme != null && acme.Name == "Acme AI" && acme.Mascot == "cat" && acme.IntervalSeconds == 10 && acme.Color == Rgba.Hex("#FF8800") && !acme.BuiltIn);
                    t.Check("沒有 detect：一律視為已安裝", acme != null && acme.Detect().Installed);

                    var s = new AppSettings();
                    var a = acme.Fetch(false, s);
                    t.Check("標準格式：兩個額度、方案", a.Meters.Count == 2 && a.Plan == "Team" && a.Meters[0].Used == 42 && a.Meters[1].Used == 20 && a.Meters[0].ShortLabel == "日" && a.Meters[1].ShortLabel == "月", a.Error);

                    var m = get("mapped").Fetch(false, s);
                    var req = m.Meters.FirstOrDefault();
                    t.Check("對應規則：250 / 1000 → 25%", req != null && req.Key == "req" && Math.Abs(req.Used - 25) < 0.001 && req.ValueText == "250 / 1000", m.Error);
                    t.Check("對應規則：方案用路徑、說明用固定文字、重置時間", m.Plan == "Enterprise" && m.Note == "固定的說明" && req != null && req.ResetsAt.HasValue && Math.Abs((req.ResetsAt.Value - now.AddDays(2)).TotalSeconds) < 2);

                    var d = get("dict").Fetch(false, s);
                    t.Check("metersFrom（物件）：只取有名字的、帶視窗長度", d.Meters.Count == 2 && d.Meters[0].Label == "Alpha" && d.Meters[0].ShortLabel == "5h" && d.Meters[1].Label == "Beta" && d.Meters[1].Used == 20, d.Error);
                    var l = get("list").Fetch(false, s);
                    t.Check("metersFrom（陣列）：用欄位當名字、乘上 scale", l.Meters.Count == 2 && l.Meters[0].Key == "m1" && Math.Abs(l.Meters[0].Used - 25) < 0.001 && Math.Abs(l.Meters[1].Used - 50) < 0.001, l.Error);

                    var c = get("cmd").Fetch(false, s);
                    t.Check("指令：略過前面的訊息，讀 JSON", c.Meters.Count == 2 && c.Plan == "Team", c.Error);
                    var ca = get("cmdargs").Fetch(false, s);
                    t.Check("指令：帶 /c 之類的參數", ca.Meters.Count == 2, ca.Error);

                    var w = get("web").Fetch(false, s);
                    t.Check("HTTP：讀到用量", w.Meters.Count == 1 && w.Meters[0].Used == 12.5, w.Error);
                    t.Check("HTTP：標頭裡的 ${env:…} 會展開", web.Requests.Any(r => r.Contains("X-Token: secret-123")));
                    t.Equal("HTTP：伺服器錯誤", "HTTP 500", get("down").Fetch(false, s).Error);
                    t.Contains("不支援的來源", get("weird").Fetch(false, s).Error, "不支援的 source.type");
                    t.Contains("找不到檔案", get("nofile").Fetch(false, s).Error, "找不到檔案");
                    t.Contains("沒有用量資料", get("nometers").Fetch(false, s).Error, "沒有產生任何用量資料");

                    t.Check("detect：路徑和指令都不在 → 沒安裝", !get("detectno").Detect().Installed);
                    t.Check("detect：環境變數", get("detectenv").Detect().Installed);
                    t.Check("detect：萬用字元路徑", get("detectpath").Detect().Installed);
                }
                Environment.SetEnvironmentVariable("SENTRIPET_TEST_TOKEN", null);
                Environment.SetEnvironmentVariable("SENTRIPET_TEST_ENV", null);
            });
        }
    }

    /// <summary>The polling service: detection, fetching on worker threads, ordering and hiding.</summary>
    static class ServiceTests
    {
        public static void Run(TestKit t)
        {
            t.Section("背景更新服務");
            t.Run("Service", () =>
            {
                Func<string, string, double, Snapshot> ok = (key, label, used) =>
                {
                    var s = new Snapshot { ObservedAt = DateTime.UtcNow, Source = "stub" };
                    s.Meters.Add(new Meter { Key = key, Label = label, ShortLabel = label, Used = used, WindowMinutes = 300 });
                    return s;
                };
                var alpha = new StubProvider("alpha", "Alpha") { OnFetch = (f, st) => ok("q", "Q", 10) };
                var beta = new StubProvider("beta", "Beta") { OnFetch = (f, st) => { throw new InvalidOperationException("來源壞了"); } };
                var gamma = new StubProvider("gamma", "Gamma") { OnDetect = () => new Detection { Installed = false } };
                var delta = new StubProvider("delta", "Delta") { OnFetch = (f, st) => new Snapshot { Offline = true, Error = "沒開" } };
                var eps = new StubProvider("eps", "Eps") { OnFetch = (f, st) => ok("q", "Q", 1) };
                var nul = new StubProvider("nul", "Nul");
                var settings = new AppSettings();
                settings.Enabled["eps"] = false;
                int changes = 0;
                using (var svc = new UsageService(settings))
                {
                    svc.Factory = () => new List<Provider> { alpha, beta, gamma, delta, eps, nul };
                    svc.Changed += () => Interlocked.Increment(ref changes);
                    svc.Start();
                    bool all = TestKit.WaitUntil(() => svc.SnapshotFor("alpha") != null && svc.SnapshotFor("beta") != null && svc.SnapshotFor("delta") != null && svc.SnapshotFor("nul") != null, 15000);
                    t.Check("偵測後在背景抓資料", all);
                    t.Check("沒安裝的不抓、停用的不抓", gamma.Fetches == 0 && eps.Fetches == 0 && svc.SnapshotFor("gamma") == null && svc.SnapshotFor("eps") == null);
                    t.Check("有通知畫面更新", changes > 0);

                    var views = svc.BuildViews(settings, false);
                    t.Equal("畫面上：有資料的、出錯的都顯示，離線的隱藏", "alpha,beta,nul", string.Join(",", views.Select(v => v.Id)));
                    var b = views.FirstOrDefault(v => v.Id == "beta");
                    t.Check("抓資料丟出例外：顯示錯誤訊息", b != null && !b.HasData && b.Error == "來源壞了");
                    var n = views.FirstOrDefault(v => v.Id == "nul");
                    t.Check("沒有回傳資料：顯示「沒有資料」", n != null && n.Error == "沒有資料");
                    settings.Order = new List<string> { "nul", "alpha" };
                    t.Equal("依使用者排序", "nul,alpha,beta", string.Join(",", svc.BuildViews(settings, false).Select(v => v.Id)));
                    t.Check("設定頁看得到離線的", svc.BuildViews(settings, true).Any(v => v.Id == "delta"));
                    settings.UseItReminder = false;
                    t.Check("關掉快用掉提醒：畫面資料不帶等級", svc.BuildViews(settings, false).All(v => v.UseItLevel == 0 && v.UseIt == null));

                    int before = alpha.Fetches;
                    svc.RefreshNow("alpha");
                    t.Check("立即更新", TestKit.WaitUntil(() => alpha.Fetches > before, 5000));
                }
            });
        }
    }
}
