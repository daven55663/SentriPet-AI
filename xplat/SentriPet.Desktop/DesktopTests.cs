using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SentriPet
{
    /// <summary>
    /// --selftest FILE: the checks of the cross-platform desk pet that need Avalonia (off-screen, no display needed):
    /// every theme, the hover card, the speech bubble, the settings page, languages, and the per-system integration.
    /// The core checks run in SentriPet.Tests. Exit code = failures.
    /// </summary>
    static class DesktopTests
    {
        public static int Run(string outFile)
        {
            AppBuilder.Configure<DesktopApp>()
                .UseSkia()
                .UseHarfBuzz()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            var t = new TestKit();
            var sw = Stopwatch.StartNew();
            Watchdog(t, outFile, 300);
            AppPaths.DataDirOverride = t.TempDir("profile");
            try
            {
                Themes(t);
                Windows(t);
                Languages(t);
                IntegrationChecks(t);
            }
            finally
            {
                L.Use(L.Source);
                G.UseFonts();
                AppPaths.DataDirOverride = null;
                t.Cleanup();
            }
            string summary = (t.Failed == 0 ? "ALL PASS" : t.Failed + " FAILED") + "  (" + t.Passed + " passed, " + t.Failed + " failed, " + t.Skipped + " skipped, " +
                             sw.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s)";
            string text = AppInfo.Name + " " + AppInfo.Version + " desktop self-test (Avalonia) on " + Os.Name + Environment.NewLine + t.Report + Environment.NewLine + summary + Environment.NewLine;
            Console.OutputEncoding = Encoding.UTF8;
            if (outFile == null) Console.Write(text);
            else File.WriteAllText(outFile, text, new UTF8Encoding(false));
            return t.Failed;
        }

        /// <summary>What the test is doing (printed when the watchdog fires).</summary>
        static volatile string Progress = "start";

        /// <summary>Ends the run with the partial report when it takes far too long (a hang in CI then shows where).</summary>
        static void Watchdog(TestKit t, string outFile, int seconds)
        {
            var th = new Thread(() =>
            {
                Thread.Sleep(seconds * 1000);
                string text = "TIMEOUT after " + seconds + " s while: " + Progress + Environment.NewLine + t.Report;
                Console.WriteLine(text);
                try { if (outFile != null) File.WriteAllText(outFile, text + Environment.NewLine + "1 FAILED (timeout)" + Environment.NewLine); } catch { }
                Environment.Exit(99);
            }) { IsBackground = true };
            th.Start();
        }

        // ------------------------------------------------------------------ themes

        static int InkPixels(Bitmap bmp)
        {
            int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
            var px = new byte[w * h * 4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { bmp.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), px.Length, w * 4); }
            finally { handle.Free(); }
            int n = 0;
            for (int i = 3; i < px.Length; i += 4) if (px[i] > 16) n++;
            return n;
        }

        static List<string> Texts(Visual root)
        {
            var list = new List<string>();
            foreach (var v in root.GetVisualDescendants().Concat(new[] { root }))
            {
                var tb = v as TextBlock;
                if (tb != null && tb.IsEffectivelyVisible)
                {
                    string s = tb.Inlines != null && tb.Inlines.Count > 0
                        ? string.Concat(tb.Inlines.Select(i => i is Avalonia.Controls.Documents.LineBreak ? Environment.NewLine : i is Avalonia.Controls.Documents.Run ? ((Avalonia.Controls.Documents.Run)i).Text : ""))
                        : tb.Text;
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
            }
            return list;
        }

        static readonly List<KeyValuePair<string, Func<List<ProviderView>>>> Sets = new List<KeyValuePair<string, Func<List<ProviderView>>>>
        {
            new KeyValuePair<string, Func<List<ProviderView>>>("a", MockData.A),
            new KeyValuePair<string, Func<List<ProviderView>>>("b", MockData.B),
            new KeyValuePair<string, Func<List<ProviderView>>>("c", MockData.C),
            new KeyValuePair<string, Func<List<ProviderView>>>("空", () => new List<ProviderView>()),
        };

        const string TestLine = "(a test line)";

        /// <summary>Draws a theme with every sample set (and pokes it a little); returns the texts on screen.</summary>
        static List<string> Exercise(ThemeInfo info, List<string> problems)
        {
            Progress = "theme " + info.Id + " (" + L.Current + ")";
            Console.WriteLine(Progress);
            var texts = new List<string>();
            foreach (var set in Sets)
            {
                var data = set.Value();
                var theme = info.Create();
                theme.Attach(new Snapshots.PreviewHost());
                theme.Update(data);
                for (int i = 0; i < 60; i++) theme.Tick(1 / 10.0);
                if (data.Count > 0)
                {
                    theme.Say(data[0].Id, TestLine);
                    theme.Poke(data[0].Id);
                    theme.Celebrate(data[0].Id);
                }
                theme.Click(new Point(4, 4));
                theme.Update(data);
                for (int i = 0; i < 20; i++) theme.Tick(1 / 30.0);
                var bmp = Snapshots.RenderToBitmap(theme.Root, 1.0);
                int ink = InkPixels(bmp);
                if (ink < 2000) problems.Add(set.Key + ": blank? (" + ink + " px)");
                texts.AddRange(Texts(theme.Root).Select(s => s.Replace(TestLine, "")));
                theme.Detach();
            }
            return texts;
        }

        static void Themes(TestKit t)
        {
            t.Section("8 種造型（Avalonia，模擬資料 a/b/c、空的）");
            t.Equal("造型清單和 Windows 版一樣有 8 種", "pet,glass,pixel,terminal,gauge,potion,neon,note", string.Join(",", ThemeCatalog.All.Select(x => x.Id)));
            foreach (var info in ThemeCatalog.All)
            {
                var ti = info;
                t.Run(ti.Id, () =>
                {
                    var problems = new List<string>();
                    var texts = Exercise(ti, problems);
                    t.Check(ti.Name + "：畫得出來、互動不當機", problems.Count == 0 && texts.Count > 3, texts.Count + " 段文字 " + string.Join(" ", problems));
                });
            }
        }

        // ------------------------------------------------------------------ hover card, bubble, settings page

        static void Windows(TestKit t)
        {
            t.Section("詳情卡、泡泡、設定頁");
            Progress = "hover card";
            t.Run("card", () =>
            {
                foreach (var v in MockData.C().Concat(MockData.B()))
                {
                    var card = DetailCardView.Build(v);
                    card.SetPointer(DetailPlacement.Side.Above, 90);
                    var bmp = Snapshots.RenderToBitmap(card.Root, 1.0);
                    if (bmp.PixelSize.Width < 200 || InkPixels(bmp) < 5000) { t.Check("詳情卡畫得出來（" + v.Name + "）", false, bmp.PixelSize.ToString()); return; }
                }
                t.Check("詳情卡畫得出來（每個模擬 AI）", true);
            });
            Progress = "speech bubble";
            t.Run("speech", () =>
            {
                var v = MockData.A()[0];
                var bubble = new SpeechWindow();
                bubble.SetText(v.Id, Lines.Poke(v, new Random(5)), v.Color.ToColor(), 5);
                bubble.SetPointer(DetailPlacement.Side.Below, 40);
                var size = bubble.MeasurePx(1.0);
                t.Check("浮動泡泡量得出大小、不超過最大寬度", size.Width > 60 && size.Width < 300 && size.Height > 30, size.ToString());
                t.Check("泡泡在秒數到了之前不會消失", bubble.Until > DateTime.UtcNow.AddSeconds(3));
            });
            Progress = "settings page";
            t.Run("settings", () =>
            {
                var host = new TestHost();
                var win = new SettingsWindow(host) { Width = 700, Height = 900 };
                win.Show();
                for (int i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
                var switches = win.GetVisualDescendants().OfType<ToggleSwitch>().ToList();
                var sliders = win.GetVisualDescendants().OfType<Slider>().ToList();
                t.Check("設定頁：開關和滑桿都在", switches.Count >= 8 && sliders.Count >= 5, switches.Count + " 個開關、" + sliders.Count + " 個滑桿");
                var chips = win.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string && L.Languages.Any(l => ((string)b.Content).EndsWith(l.Native))).ToList();
                t.Equal("設定頁：5 種語言按鈕", 5, chips.Count);
                // flipping the "talks" switch changes the setting
                var label = win.GetVisualDescendants().OfType<TextBlock>().First(tb => tb.Text == L.T("會說話"));
                var talk = label.FindAncestorOfType<Grid>().GetVisualDescendants().OfType<ToggleSwitch>().First();
                bool before = host.Settings.Chatty;
                talk.IsChecked = !talk.IsChecked;
                t.Check("設定頁：切換「會說話」會改到設定", host.Settings.Chatty != before, "Chatty=" + host.Settings.Chatty);
                var gallery = win.GetVisualDescendants().OfType<Button>().Count(b => b.Content is StackPanel);
                t.Equal("設定頁：造型卡片 8 張", 8, gallery);
                win.Close();
            });
        }

        class TestHost : ISettingsHost
        {
            readonly AppSettings settings = new AppSettings();
            UsageService service;
            public int Changed;
            public AppSettings Settings { get { return settings; } }
            public UsageService Service { get { return service ?? (service = new UsageService(settings)); } }
            public List<ProviderView> Views { get { return MockData.A(); } }
            public void ApplyWidgetSettings() { Changed++; }
            public void ChangeTheme(string id) { settings.Theme = id; Changed++; }
            public void ChangeLanguage(string code) { settings.Language = code; Changed++; }
            public void RefreshViews() { Changed++; }
        }

        // ------------------------------------------------------------------ languages

        static bool HasHan(string s) { return s.Any(c => c >= '一' && c <= '鿿'); }

        static void Languages(TestKit t)
        {
            t.Section("介面語言（Avalonia）");
            try
            {
                Program.UseLanguage("en");
                t.Equal("英文：造型名稱", "Jelly Pet", ThemeCatalog.Get("pet").Name);
                foreach (var info in ThemeCatalog.All)
                {
                    var ti = info;
                    t.Run("en " + ti.Id, () =>
                    {
                        var texts = Exercise(ti, new List<string>());
                        var han = texts.Where(HasHan).Distinct().ToList();
                        t.Check(ti.Name + "：英文畫面沒有中文", han.Count == 0, string.Join(" | ", han.Take(5)));
                    });
                }
                Program.UseLanguage("ko");
                t.Run("ko", () =>
                {
                    var texts = new List<string>();
                    foreach (var info in ThemeCatalog.All) texts.AddRange(Exercise(info, new List<string>()));
                    var split = texts.Where(x => x != L.KeepWords(x)).Distinct().ToList();
                    t.Check("韓文畫面：8 種造型的每個詞都不會被拆到兩行", split.Count == 0 && texts.Count > 100, texts.Count + " 段文字 " + string.Join(" | ", split.Take(5)));
                });
            }
            finally { Program.UseLanguage(L.Source); }
            t.Equal("切回繁中：造型名稱", "果凍桌寵", ThemeCatalog.Get("pet").Name);
        }

        // ------------------------------------------------------------------ per-system integration

        static void IntegrationChecks(TestKit t)
        {
            t.Section("各系統整合（" + Os.Name + "）");
            t.Run("autostart", () =>
            {
                string entry = Integration.DesktopEntry("/opt/Senti Pet/SentriPet");
                t.Contains("Linux 開機啟動：Exec 有引號和 --autostart", entry, "Exec=\"/opt/Senti Pet/SentriPet\" --autostart");
                t.Contains("Linux 開機啟動：是 Application", entry, "Type=Application");
                foreach (var bundle in new[] { "/Applications/SentriPet.app", null })
                {
                    string plist = Integration.LaunchAgent(bundle, "/Applications/SentriPet.app/Contents/MacOS/SentriPet");
                    bool xmlOk;
                    try
                    {
                        var rs = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
                        using (var r = XmlReader.Create(new StringReader(plist), rs)) while (r.Read()) { }
                        xmlOk = true;
                    }
                    catch { xmlOk = false; }
                    t.Check("macOS 開機啟動：plist 是正確的 XML" + (bundle != null ? "（.app）" : "（執行檔）"), xmlOk && plist.Contains("<key>RunAtLoad</key>") && plist.Contains("--autostart"));
                }
                t.Contains("macOS 開機啟動：.app 用 open 開啟", Integration.LaunchAgent("/Applications/SentriPet.app", "x"), "<string>/usr/bin/open</string>");
            });
            t.Run("notify", () =>
            {
                string exe; string[] args;
                bool ok = Integration.NotifyCommand("SentriPet", "Claude 快用完了 <&>", out exe, out args);
                if (Os.Windows) t.Check("Windows 通知：用 PowerShell 顯示 toast", ok && exe == "powershell.exe" && args.Last().Contains("ToastNotificationManager"));
                else if (Os.Mac) t.Check("macOS 通知：用 osascript，文字當參數傳（不拼進指令）", ok && exe.EndsWith("osascript") && args.Contains("Claude 快用完了 <&>"));
                else if (ok) t.Check("Linux 通知：用 notify-send", exe.EndsWith("notify-send") && args.Contains("Claude 快用完了 <&>"));
                else t.Skip("Linux 通知", "這台機器沒有 notify-send");
            });
            Progress = "single instance";
            t.Run("single instance", () =>
            {
                string name = "SentriPet.test." + Guid.NewGuid().ToString("N").Substring(0, 8);
                var shown = new ManualResetEventSlim(false);
                bool first = Integration.ClaimSingleInstance(() => shown.Set(), name);
                bool second = Integration.ClaimSingleInstance(() => { }, name);
                t.Check("第一個執行的取得執行權", first);
                t.Check("第二個執行的會退出", !second);
                t.Check("第二個執行的會請第一個顯示出來", shown.Wait(5000));
            });
            t.Check("全螢幕偵測：只在 Windows 開放", Integration.CanDetectFullscreen == Os.Windows);
        }
    }
}
