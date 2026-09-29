using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace SentriPet
{
    /// <summary>The numbers for other programs (#22): usage.json and the local web page.</summary>
    static class ExportTests
    {
        public static void Run(TestKit t)
        {
            t.Section("給其他程式用（#22）：usage.json 與本機網頁");
            t.Run("Format", () => Format(t));
            t.Run("File", () => FileWrites(t));
            t.Run("Server routes", () => Routes(t));
            t.Run("Server socket", () => Socket(t));
        }

        static readonly DateTime Now = new DateTime(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc);

        static List<ProviderView> Views(double claudeLeft)
        {
            var five = new Meter { Key = "five_hour", Label = "5 小時", ShortLabel = "5h", Used = 100 - claudeLeft, WindowMinutes = 300, ResetsAt = Now.AddMinutes(133), UsedApprox = true };
            var week = new Meter { Key = "seven_day", Label = "每週", ShortLabel = "週", Used = 38.04, WindowMinutes = 10080, ResetsAt = Now.AddDays(3), ResetApprox = true };
            var claude = new ProviderView
            {
                Id = "claude", Name = "Claude", Color = Rgba.Hex("#D97757"), Plan = "Max", HasData = true, Active = true,
                Meters = new List<Meter> { five, week }, Remaining = Math.Min(five.Remaining, week.Remaining), Mood = ProviderView.MoodFor(claudeLeft),
                Snap = new Snapshot { ObservedAt = Now.AddMinutes(-3) },
            };
            var ollama = new ProviderView
            {
                Id = "ollama", Name = "Ollama", Color = Rgba.Hex("#9CA3AF"), HasData = true, Unlimited = true, Remaining = 100,
                Meters = new List<Meter> { new Meter { Key = "local", Label = "本機", Unlimited = true } },
            };
            var copilot = new ProviderView { Id = "copilot", Name = "Copilot", Color = Rgba.Hex("#8B5CF6"), Error = "not signed in", Mood = Mood.Unknown };
            return new List<ProviderView> { claude, ollama, copilot };
        }

        static void Format(TestKit t)
        {
            string text = Json.Serialize(UsageExport.Build(Views(58.26), Now), true);
            var o = Json.Obj(Json.Parse(text));
            t.Equal("有版本號（之後改格式才不會弄壞別人的腳本）", 1.0, Json.Num(Json.Get(o, "version")));
            t.Equal("寫出更新時間（UTC）", "2026-09-29T08:00:00Z", Json.Str(Json.Get(o, "updatedAt")));
            t.Equal("最低的剩餘 %（不算無上限的、沒資料的）", 58.3, Json.Num(Json.Get(o, "lowestRemaining")));
            var ps = Json.Arr(Json.Get(o, "providers"));
            t.Equal("每個 AI 一筆", 3, ps.Count);
            var c = Json.Obj(ps[0]);
            t.Equal("id、名稱、顏色", "claude|Claude|#D97757", Json.Str(Json.Get(c, "id")) + "|" + Json.Str(Json.Get(c, "name")) + "|" + Json.Str(Json.Get(c, "color")));
            t.Check("正在工作", Json.Bool(Json.Get(c, "active")) == true);
            t.Equal("心情是英文代號", "good", Json.Str(Json.Get(c, "mood")));
            t.Equal("量到的時間", "2026-09-29T07:57:00Z", Json.Str(Json.Get(c, "observedAt")));
            var ms = Json.Arr(Json.Get(c, "meters"));
            var five = Json.Obj(ms[0]);
            t.Equal("剩餘 % 四捨五入到小數一位", 58.3, Json.Num(Json.Get(five, "remaining")));
            t.Equal("用掉的 %", 41.7, Json.Num(Json.Get(five, "used")));
            t.Equal("重置時間（UTC）", "2026-09-29T10:13:00Z", Json.Str(Json.Get(five, "resetsAt")));
            t.Check("標出估計的數字與推算的重置時間", Json.Bool(Json.Get(five, "estimated")) == true && Json.Bool(Json.Get(five, "resetEstimated")) == false &&
                                                   Json.Bool(Json.Get(Json.Obj(ms[1]), "resetEstimated")) == true);
            t.Equal("視窗長度（分鐘）", 300.0, Json.Num(Json.Get(five, "windowMinutes")));
            var o2 = Json.Obj(ps[1]);
            var local = Json.Obj(Json.Arr(Json.Get(o2, "meters"))[0]);
            t.Check("無上限：remaining 是 null", Json.Bool(Json.Get(o2, "unlimited")) == true && Json.Get(o2, "remaining") == null && Json.Get(local, "remaining") == null &&
                                                Json.Get(local, "resetsAt") == null);
            var cp = Json.Obj(ps[2]);
            t.Check("沒資料：hasData false、心情 unknown、附上原因", Json.Bool(Json.Get(cp, "hasData")) == false && Json.Str(Json.Get(cp, "mood")) == "unknown" &&
                                                         Json.Str(Json.Get(cp, "error")) == "not signed in" && Json.Get(cp, "remaining") == null);
            var empty = Json.Obj(Json.Parse(Json.Serialize(UsageExport.Build(new List<ProviderView>(), Now), false)));
            t.Check("還沒有任何 AI：providers 是空陣列、lowestRemaining 是 null", Json.Arr(Json.Get(empty, "providers")).Count == 0 && Json.Get(empty, "lowestRemaining") == null);
        }

        static void FileWrites(TestKit t)
        {
            string file = Path.Combine(t.TempDir("export"), "sub", "usage.json");
            var ex = new UsageExporter(file);
            ex.Update(Views(58), Now, false);
            t.Check("只開網頁：有最新的 JSON，但不寫檔", ex.Latest != null && ex.Writes == 0 && !File.Exists(file));
            ex.Update(Views(58), Now, true);
            t.Check("開啟後寫出 usage.json（資料夾不存在也會建立）", ex.Writes == 1 && File.Exists(file) && Json.Obj(Json.TryParse(File.ReadAllText(file))) != null);
            ex.Update(Views(58), Now.AddSeconds(10), true);
            t.Equal("數字沒變：不重寫", 1, ex.Writes);
            ex.Update(Views(57), Now.AddSeconds(11), true);
            t.Equal("數字變了：馬上寫", 2, ex.Writes);
            ex.Update(Views(57), Now.AddSeconds(11 + 61), true);
            t.Equal("沒變也每分鐘寫一次（腳本看 updatedAt 就知道桌寵還在跑）", 3, ex.Writes);
            t.Contains("檔案裡是最新的時間", File.ReadAllText(file), "\"2026-09-29T08:01:12Z\"");
            t.Check("不留下暫存檔", !File.Exists(file + ".tmp"));
            ex.Remove();
            t.Check("關掉：刪除檔案，免得腳本一直讀到舊數字", !File.Exists(file));
            ex.Update(Views(57), Now.AddSeconds(80), true);
            t.Check("再打開：馬上重寫", ex.Writes == 4 && File.Exists(file));
        }

        static void Routes(TestKit t)
        {
            string json = null;
            byte[] png = null;
            var s = new UsageServer(() => json, () => png);
            t.Check("先開 port 0（系統挑一個空的）", s.Start(0) && s.Port > 0);
            try
            {
                string host = "Host: 127.0.0.1:" + s.Port;
                int status = 0;
                string type = null;
                Func<string, string> get = head => Encoding.UTF8.GetString(s.Respond(head, out status, out type));

                t.Check("本機網址可以", s.HostAllowed("127.0.0.1:" + s.Port) && s.HostAllowed("localhost:" + s.Port) && s.HostAllowed("[::1]:" + s.Port) &&
                                      s.HostAllowed("LOCALHOST:" + s.Port) && s.HostAllowed("127.0.0.1"));
                t.Check("別的主機名稱（DNS rebinding）、別的連接埠、沒有 Host：拒絕",
                        !s.HostAllowed("evil.example:" + s.Port) && !s.HostAllowed("127.0.0.1:" + (s.Port + 1)) && !s.HostAllowed(null) &&
                        !s.HostAllowed("127.0.0.1.evil.example") && !s.HostAllowed("[::1]x") && !s.HostAllowed("0.0.0.0:" + s.Port));

                get("GET /usage.json HTTP/1.1\r\n" + host);
                t.Equal("還沒有資料：503", 503, status);
                json = "{\"version\":1}";
                string body = get("GET /usage.json?x=1 HTTP/1.1\r\n" + host);
                t.Check("/usage.json：200 + JSON", status == 200 && type.StartsWith("application/json") && body == json, status + " " + type);
                body = get("GET / HTTP/1.1\r\n" + host);
                t.Check("/：OBS 用的網頁", status == 200 && type.StartsWith("text/html") && body.Contains("/usage.json") && body.Contains("/pet.png"), status + " " + type);
                get("GET /overlay?view=pet&scale=2 HTTP/1.1\r\n" + host);
                t.Equal("/overlay 也是那個網頁", 200, status);
                get("GET /pet.png HTTP/1.1\r\n" + host);
                t.Equal("還沒有桌寵畫面：404", 404, status);
                png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
                var bytes = s.Respond("GET /pet.png HTTP/1.1\r\n" + host, out status, out type);
                t.Check("/pet.png：200 + PNG", status == 200 && type == "image/png" && bytes.Length == 4);
                get("GET /usage.json HTTP/1.1\r\nHost: evil.example:" + s.Port);
                t.Equal("別的網站借本機連進來：403", 403, status);
                get("POST /usage.json HTTP/1.1\r\n" + host);
                t.Equal("只收 GET：405", 405, status);
                get("GET /../settings.json HTTP/1.1\r\n" + host);
                t.Equal("其他路徑一律 404（不會讀到檔案）", 404, status);
                get("garbage");
                t.Equal("看不懂的請求：400", 400, status);
            }
            finally { s.Stop(); }
            t.Check("停止後不再執行", !s.Running);
        }

        static void Socket(TestKit t)
        {
            var s = new UsageServer(() => "{\"version\":1,\"providers\":[]}", null);
            if (!s.Start(0)) { t.Check("開伺服器", false); return; }
            try
            {
                string reply = Ask(s.Port, "GET /usage.json HTTP/1.1\r\nHost: 127.0.0.1:" + s.Port + "\r\n\r\n");
                t.Check("真的連線：回 200 與 JSON", reply.StartsWith("HTTP/1.1 200") && reply.EndsWith("{\"version\":1,\"providers\":[]}"), reply.Split('\r')[0]);
                t.Contains("不讓瀏覽器快取", reply, "Cache-Control: no-store");
                t.Check("不給其他網站讀（沒有 CORS 標頭）", reply.IndexOf("Access-Control-Allow-Origin", StringComparison.OrdinalIgnoreCase) < 0);
                reply = Ask(s.Port, "HEAD /usage.json HTTP/1.1\r\nHost: localhost:" + s.Port + "\r\n\r\n");
                t.Check("HEAD：只有標頭", reply.StartsWith("HTTP/1.1 200") && reply.EndsWith("\r\n\r\n"));
                reply = Ask(s.Port, "GET / HTTP/1.1\r\nHost: attacker.example\r\n\r\n");
                t.Check("真的連線：別的 Host 回 403", reply.StartsWith("HTTP/1.1 403"), reply.Split('\r')[0]);
                var second = new UsageServer(() => null, null);
                t.Check("連接埠被占用：開不起來，但不會當掉", !second.Start(s.Port) && !second.Running);
            }
            finally { s.Stop(); }
        }

        static string Ask(int port, string request)
        {
            using (var c = new TcpClient())
            {
                c.ReceiveTimeout = 5000;
                c.Connect(System.Net.IPAddress.Loopback, port);
                var st = c.GetStream();
                var b = Encoding.ASCII.GetBytes(request);
                st.Write(b, 0, b.Length);
                var ms = new MemoryStream();
                st.CopyTo(ms);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
