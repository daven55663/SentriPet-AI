using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SentriPet
{
    /// <summary>New versions (#39): the daily look at GitHub, the release notes, how each kind of copy is updated, and the self-update's files.</summary>
    static class UpdaterTests
    {
        const string Notes = "SentriPet 是放在桌面上的 AI 用量監控桌寵。\n\n## 安裝\n\n| 系統 | 檔案 |\n|---|---|\n| Windows | `SentriPet-2.7.0-win-x64.zip` |\n\n" +
                             "## 這一版的變化\n\n- **自動更新**（#39）：每天看一次有沒有新版，見 [README](https://github.com/x#readme)。\n- 修正 `theme.json` 的 GIF。\n\n" +
                             "## 開發與測試環境\n\n| CPU | Intel |\n";

        public static void Run(TestKit t)
        {
            t.Section("新版本與自動更新（#39）");
            t.Run("Versions", () =>
            {
                t.Check("2.7.0 比 2.6.0 新", Updater.IsNewer("2.7.0", "2.6.0"));
                t.Check("v2.10.0 比 2.9.9 新（數字比，不是字串比）", Updater.IsNewer("v2.10.0", "2.9.9"));
                t.Check("2.6.1 比 2.6.0 新", Updater.IsNewer("2.6.1", "2.6.0"));
                t.Check("一樣的版本不算新", !Updater.IsNewer("2.6.0", "2.6.0") && !Updater.IsNewer("v2.6", "2.6.0"));
                t.Check("比較舊的不算新", !Updater.IsNewer("2.5.1", "2.6.0"));
                t.Check("看不懂的版本不算新", !Updater.IsNewer("latest", "2.6.0") && !Updater.IsNewer(null, "2.6.0"));
                t.Equal("2.7.0-beta 讀成 2.7.0", new Version(2, 7, 0, 0), Updater.ParseVersion("2.7.0-beta"));

                var s = new AppSettings();
                var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
                t.Check("預設會檢查，從沒查過就該查", s.CheckUpdates && Updater.Due(s, now));
                s.LastUpdateCheck = now.AddHours(-3);
                t.Check("3 小時前查過：先不查", !Updater.Due(s, now));
                s.LastUpdateCheck = now.AddHours(-25);
                t.Check("超過一天：再查一次", Updater.Due(s, now));
                s.LastUpdateCheck = now.AddDays(2);
                t.Check("時鐘被往回調（上次在「未來」）：再查一次", Updater.Due(s, now));
                s.CheckUpdates = false;
                s.LastUpdateCheck = null;
                t.Check("關掉自動檢查：不查", !Updater.Due(s, now));

                var newer = new Release { Tag = "v99.0.0", Version = "99.0.0" };
                var older = new Release { Tag = "v0.9.0", Version = "0.9.0" };
                var settings = new AppSettings();
                t.Check("有新版本：問使用者", Updater.Offer(newer, settings, false));
                t.Check("沒有比較新：不問", !Updater.Offer(older, settings, true) && !Updater.Offer(null, settings, true));
                settings.SkippedVersion = "99.0.0";
                t.Check("略過的版本：每天的檢查不再問", !Updater.Offer(newer, settings, false));
                t.Check("略過的版本：自己按「檢查新版本」時還是會問", Updater.Offer(newer, settings, true));
                t.Check("略過的是舊的那版：更新的版本照問", Updater.Offer(new Release { Tag = "v99.1.0", Version = "99.1.0" }, settings, false));
            });

            t.Run("Release", () =>
            {
                string json = "{\"tag_name\":\"v2.7.0\",\"name\":\"SentriPet 2.7.0\",\"draft\":false,\"prerelease\":false," +
                              "\"html_url\":\"https://github.com/daven55663/SentriPet-AI/releases/tag/v2.7.0\",\"published_at\":\"2026-11-05T08:00:00Z\"," +
                              "\"body\":" + Json.Serialize(Notes, false) + ",\"assets\":[" +
                              Asset("SentriPet-2.7.0-win-x64.zip", 43138805, "sha256:60510F61F600C0F3AD7BAF9CF389B5FF797C6A8E402A4D81DC5B22EFDE4A403B") + "," +
                              Asset("SentriPet-2.7.0-osx-arm64.zip", 43078598, "sha256:0737") + "," +
                              Asset("SentriPet-2.7.0-osx-x64.zip", 45560053, null) + "," +
                              Asset("SentriPet-2.7.0-linux-x64.tar.gz", 42348216, "sha256:925f") + "]}";
                var r = Updater.Parse(json);
                t.Check("讀得懂 GitHub 的 releases/latest", r != null && r.Version == "2.7.0" && r.Tag == "v2.7.0" && r.Assets.Count == 4);
                if (r == null) return;
                t.Check("發佈時間", r.Published == new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc));
                var win = Updater.AssetFor(r, "win-x64");
                t.Check("Windows 用的檔案、大小與 SHA-256（小寫）", win != null && win.Size == 43138805 && win.Sha256 == "60510f61f600c0f3ad7baf9cf389b5ff797c6a8e402a4d81dc5b22efde4a403b");
                t.Equal("Apple 晶片用的檔案", "SentriPet-2.7.0-osx-arm64.zip", Updater.AssetFor(r, "osx-arm64").Name);
                t.Equal("Linux 用的是 .tar.gz", "SentriPet-2.7.0-linux-x64.tar.gz", Updater.AssetFor(r, "linux-x64").Name);
                t.Check("沒有公布 SHA-256 的檔案記成 null", Updater.AssetFor(r, "osx-x64").Sha256 == null);
                t.Check("沒有這個系統的檔案：null", Updater.AssetFor(r, "linux-arm64") == null);
                t.Check("草稿或預先發佈版不算", Updater.Parse(json.Replace("\"draft\":false", "\"draft\":true")) == null &&
                                               Updater.Parse(json.Replace("\"prerelease\":false", "\"prerelease\":true")) == null);
                t.Check("版本號看不懂：不算", Updater.Parse(json.Replace("v2.7.0\",\"name", "nightly\",\"name")) == null);
                var other = Updater.Parse(json.Replace("https://github.com/daven55663/SentriPet-AI/releases/tag/v2.7.0", "https://example.com/x"));
                t.Equal("說明頁不是這個專案的網址：改用 GitHub 上的發佈頁", "https://github.com/daven55663/SentriPet-AI/releases/tag/v2.7.0", other.Page);
                t.Check("不是 JSON：null", Updater.Parse("<html>") == null && Updater.Parse(null) == null);

                string changes = Updater.Changes(Notes);
                t.Contains("說明只取「這一版的變化」那一段", changes, "自動更新（#39）：每天看一次有沒有新版");
                t.Check("不含安裝表與開發環境", !changes.Contains("CPU") && !changes.Contains("win-x64.zip") && !changes.Contains("開發與測試環境"));
                t.Check("Markdown 記號拿掉，連結只留文字", !changes.Contains("**") && !changes.Contains("`") && changes.Contains("見 README") && !changes.Contains("https://"));
                t.Check("清單用 •", changes.StartsWith("• "));
                t.Equal("沒有那一段：整份說明（純文字）", "• 第一點\n• 第二點", Updater.Changes("- **第一點**\n- 第二點"));
                t.Equal("英文、韓文（含單字連接符號）的標題也認得", "• a|• b", Updater.Changes("## What's new\n- a\n## Install\nx") + "|" + Updater.Changes("## 변⁠경 사항\n- b\n## x\ny"));
            });

            t.Run("Kinds", () =>
            {
                Func<string, bool> none = p => false;
                Func<string, bool> cask = p => p == "/opt/homebrew/Caskroom/sentripet";
                t.Equal("install.cmd 的資料夾：自己替換", InstallKind.SelfUpdate, Updater.KindOf(@"C:\Users\x\AppData\Local\Programs\SentriPet\SentriPet.exe", true, false, none));
                t.Equal("解壓縮在任何地方：自己替換", InstallKind.SelfUpdate, Updater.KindOf(@"D:\Tools\SentriPet\SentriPet.exe", true, false, none));
                t.Equal("Scoop（current）", InstallKind.Scoop, Updater.KindOf(@"C:\Users\x\scoop\apps\sentripet\current\SentriPet.exe", true, false, none));
                t.Equal("Scoop（版本資料夾、自訂位置）", InstallKind.Scoop, Updater.KindOf(@"D:\Tools\apps\sentripet\2.6.0\SentriPet.exe", true, false, none));
                t.Equal("winget", InstallKind.Winget, Updater.KindOf(@"C:\Users\x\AppData\Local\Microsoft\WinGet\Packages\daven55663.SentriPet_Microsoft.Winget.Source_8wekyb3d8bbwe\SentriPet.exe", true, false, none));
                t.Equal("從原始碼建置：不替換", InstallKind.Development, Updater.KindOf(@"D:\AI-Usage-Pet\xplat\SentriPet.Desktop\bin\Release\net10.0\SentriPet.exe", true, false, none));
                t.Equal("macOS + Homebrew", InstallKind.Homebrew, Updater.KindOf("/Applications/SentriPet.app/Contents/MacOS/SentriPet", false, true, cask));
                t.Equal("macOS 自己下載的：開下載頁", InstallKind.Download, Updater.KindOf("/Applications/SentriPet.app/Contents/MacOS/SentriPet", false, true, none));
                t.Equal("Linux：開下載頁", InstallKind.Download, Updater.KindOf("/home/x/.local/share/sentripet/SentriPet", false, false, none));
                t.Equal("Scoop 從版本資料夾啟動時，重新開啟用 current", @"C:\Users\x\scoop\apps\sentripet\current\SentriPet.exe",
                        Updater.ScoopCurrent(@"C:\Users\x\scoop\apps\sentripet\2.6.0\SentriPet.exe"));

                var scoop = Updater.PackageUpdate(InstallKind.Scoop, 4321, @"C:\Users\o'neil\scoop\apps\sentripet\2.6.0\SentriPet.exe");
                string script = scoop.ArgumentList.Last();
                t.Check("Scoop：PowerShell 等 SentriPet 結束後執行 scoop update sentripet", scoop.FileName == "powershell.exe" && script.Contains("Get-Process -Id 4321") && script.Contains("scoop update sentripet"));
                t.Contains("Scoop：路徑裡的單引號有跳脫、重新開啟 current", script, @"'C:\Users\o''neil\scoop\apps\sentripet\current\SentriPet.exe'");
                t.Check("Scoop：命令視窗看得到（套件管理器可能會問問題）", !scoop.CreateNoWindow && !scoop.UseShellExecute);
                var winget = Updater.PackageUpdate(InstallKind.Winget, 4321, @"C:\w\SentriPet.exe");
                t.Contains("winget：照套件 ID 更新", winget.ArgumentList.Last(), "winget upgrade --id daven55663.SentriPet --exact");
                var brew = Updater.PackageUpdate(InstallKind.Homebrew, 77, "/Applications/SentriPet.app/Contents/MacOS/SentriPet", "/opt/homebrew/bin/brew", "/tmp/u.log");
                string sh = brew.ArgumentList.Last();
                t.Check("Homebrew：等結束、brew upgrade --cask、再打開 App", brew.FileName == "/bin/sh" && sh.Contains("kill -0 77") &&
                        sh.Contains("'/opt/homebrew/bin/brew' upgrade --cask sentripet") && sh.Contains("/usr/bin/open '/Applications/SentriPet.app'"));
                t.Check("自己替換或開下載頁：不經套件管理器", Updater.PackageUpdate(InstallKind.SelfUpdate, 1, "x") == null && Updater.PackageUpdate(InstallKind.Download, 1, "x") == null);

                var restart = Updater.Restart(@"C:\p\SentriPet.exe", 555, "--smoke-test", "r.txt");
                var args = restart.ArgumentList.ToList();
                t.Check("重新開啟：帶舊版本號、等舊的結束", args.Contains("--updated-from") && args[args.IndexOf("--updated-from") + 1] == AppInfo.Version &&
                        args.Contains("--wait-pid") && args[args.IndexOf("--wait-pid") + 1] == "555" && args.Last() == "r.txt");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Updater.WaitFor("2147480000", 5000);
                Updater.WaitFor("not a pid", 5000);
                t.Check("等一個早就不在的程式：馬上回來", sw.ElapsedMilliseconds < 2000, sw.ElapsedMilliseconds + " ms");
            });

            t.Run("SelfUpdate", () =>
            {
                string dir = t.TempDir("update");
                // a release: a zip with the program and an example folder, served by a local stand-in for GitHub
                string pkg = Path.Combine(dir, "pkg");
                Directory.CreateDirectory(Path.Combine(pkg, "examples", "themes"));
                File.WriteAllText(Path.Combine(pkg, Updater.ExeName), "new program");
                File.WriteAllText(Path.Combine(pkg, "examples", "themes", "cloud.txt"), "example");
                string zip = Path.Combine(dir, "release.zip");
                ZipFile.CreateFromDirectory(pkg, zip);
                byte[] bytes = File.ReadAllBytes(zip);
                string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                string name = "SentriPet-99.0.0-" + Updater.Rid + (Updater.Rid.StartsWith("linux") ? ".tar.gz" : ".zip");
                using (var server = new FakeHttpServer(0))
                {
                    string url = "http://127.0.0.1:" + server.Port;
                    server.Route("/pkg", 200, bytes);
                    server.Route("/latest", 200, "{\"tag_name\":\"v99.0.0\",\"body\":\"## What's new\\n- everything\",\"assets\":[" + Asset(name, bytes.Length, "sha256:" + sha, url + "/pkg") + "]}");
                    server.Route("/missing", 404, "{}");
                    var r = Updater.Fetch(url + "/latest");
                    t.Check("向（假的）GitHub 查到 99.0.0", r != null && r.Version == "99.0.0" && Updater.IsNewer(r.Version, AppInfo.Version));
                    t.Check("還沒有任何發佈（404）：null", Updater.Fetch(url + "/missing") == null);
                    var ua = server.Requests.FirstOrDefault(q => q.StartsWith("GET /latest"));
                    t.Check("只送出版本查詢：User-Agent 是 SentriPet/版本，不帶其他資料", ua != null && ua.Contains("User-Agent: SentriPet/" + AppInfo.Version) && !ua.Contains("Cookie") && !ua.Contains("Authorization"));
                    if (r == null) return;
                    var asset = Updater.AssetFor(r, Updater.Rid);
                    long last = 0;
                    string file = Updater.Download(asset, Path.Combine(dir, "dl"), (got, total) => last = got, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    t.Check("下載完成，進度到最後一個位元組", File.Exists(file) && last == bytes.Length && !File.Exists(file + ".part"));
                    t.Check("大小與 SHA-256 都對：通過", Updater.Problem(file, asset) == null);
                    t.Check("SHA-256 不符：停止", Updater.Problem(file, new ReleaseAsset { Name = name, Size = bytes.Length, Sha256 = new string('0', 64) }) != null);
                    t.Check("大小不符：停止", Updater.Problem(file, new ReleaseAsset { Name = name, Size = bytes.Length + 1, Sha256 = sha }) != null);
                    t.Check("沒有公布 SHA-256：不敢裝", Updater.Problem(file, new ReleaseAsset { Name = name, Size = bytes.Length }) != null);

                    // this copy, "installed": the old program and a file of the user's next to it
                    string install = Path.Combine(dir, "install");
                    Directory.CreateDirectory(install);
                    string exe = Path.Combine(install, Updater.ExeName);
                    File.WriteAllText(exe, "old program");
                    File.WriteAllText(Path.Combine(install, "notes.txt"), "mine");
                    string from = Updater.Unpack(file, Path.Combine(dir, "dl"));
                    t.Check("解壓縮後找得到程式", File.Exists(Path.Combine(from, Updater.ExeName)));
                    Updater.Replace(from, exe);
                    t.Check("新程式放上去了，舊的改名成 .old", File.ReadAllText(exe) == "new program" && File.ReadAllText(exe + ".old") == "old program");
                    t.Check("其他檔案也一起更新，自己的檔案不動", File.Exists(Path.Combine(install, "examples", "themes", "cloud.txt")) && File.ReadAllText(Path.Combine(install, "notes.txt")) == "mine");

                    // a broken package: the old program is put back
                    File.WriteAllText(exe, "old program");
                    File.Delete(exe + ".old");
                    string broken = Path.Combine(dir, "broken");
                    Directory.CreateDirectory(broken);
                    File.WriteAllText(Path.Combine(broken, "readme.txt"), "no program here");
                    bool threw = false;
                    try { Updater.Replace(broken, exe); }
                    catch (UpdateException) { threw = true; }
                    t.Check("新檔案裡沒有程式：停止並換回舊的", threw && File.ReadAllText(exe) == "old program" && !File.Exists(exe + ".old"));

                    // the whole thing, as "Update now" does it
                    File.Delete(Path.Combine(install, "examples", "themes", "cloud.txt"));
                    var steps = new List<string>();
                    var psi = Updater.InstallOver(r, exe, (text, part) => { lock (steps) if (part <= 0) steps.Add(text); }, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    t.Check("下載、檢查、安裝，依序報告", steps.Count >= 3, string.Join(" / ", steps));
                    t.Check("裝好後由新程式重新開啟", psi.FileName == exe && psi.ArgumentList.Contains("--updated-from") && File.ReadAllText(exe) == "new program");
                    Updater.CleanUp(exe);
                    t.Check("下次啟動時清掉 .old 和下載的檔案", !File.Exists(exe + ".old") && !Directory.Exists(Updater.WorkDir));
                }
            });

            t.Run("UpdateSettings", () =>
            {
                if (File.Exists(AppPaths.SettingsFile)) File.Delete(AppPaths.SettingsFile);
                var s = AppSettings.Load();
                t.Check("預設：每天檢查新版本，沒有略過的版本", s.CheckUpdates && s.SkippedVersion == null && s.LastUpdateCheck == null);
                s.CheckUpdates = false;
                s.SkippedVersion = "2.7.0";
                s.LastUpdateCheck = new DateTime(2026, 11, 4, 3, 4, 5, DateTimeKind.Utc);
                s.Save();
                var r = AppSettings.Load();
                t.Check("讀回：關掉檢查、略過的版本、上次檢查的時間", !r.CheckUpdates && r.SkippedVersion == "2.7.0" && r.LastUpdateCheck == s.LastUpdateCheck);
                File.Delete(AppPaths.SettingsFile);
            });
        }

        static string Asset(string name, long size, string digest, string url = null)
        {
            return "{\"name\":\"" + name + "\",\"size\":" + size + ",\"browser_download_url\":\"" + (url ?? "https://github.com/daven55663/SentriPet-AI/releases/download/v2.7.0/" + name) + "\"" +
                   (digest != null ? ",\"digest\":\"" + digest + "\"" : "") + "}";
        }
    }
}
