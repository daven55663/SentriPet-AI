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
using Avalonia.LogicalTree;
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
                PetFaces(t);
                CustomThemes(t);
                Placement(t);
                Windows(t);
                Speaking(t);
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

        // ------------------------------------------------------------------ the pet's faces (ported from the WPF self-test)

        static void PetFaces(TestKit t)
        {
            t.Section("果凍的表情、鬧鐘、閃爍的進度條");
            Progress = "pet faces";
            t.Run("PetFaces", () =>
            {
                var pet = new PetTheme();
                pet.Attach(new Snapshots.PreviewHost());
                Action<List<ProviderView>> show = views =>
                {
                    pet.Update(views);
                    for (int i = 0; i < 3; i++) pet.Tick(1 / 30.0);
                };
                show(MockData.C());
                t.Equal("剩 2 天、還有不少：有點著急", "hurry1", pet.ExpressionOf("claude"));
                t.Equal("最後一天：更著急", "hurry2", pet.ExpressionOf("codex"));
                t.Equal("最後 6 小時：慌張", "hurry3", pet.ExpressionOf("copilot"));
                t.Equal("快過期的那條在閃（等級 1）", "sd#1", pet.UrgentBarOf("claude"));
                t.Equal("快過期的那條在閃（等級 2）", "codex:10080#2", pet.UrgentBarOf("codex"));
                t.Equal("快過期的那條在閃（等級 3）", "premium_interactions#3", pet.UrgentBarOf("copilot"));
                t.Check("旁邊有鬧鐘", pet.HasClock("claude") && pet.HasClock("codex") && pet.HasClock("copilot"));
                pet.Poke("claude");
                pet.Tick(1 / 30.0);
                t.Equal("戳一下：瞇眼", "squint", pet.ExpressionOf("claude"));
                show(MockData.A());
                t.Check("正在工作：開心，不著急（鬧鐘還在）", !pet.ExpressionOf("codex").StartsWith("hurry") && pet.HasClock("codex"), pet.ExpressionOf("codex"));
                show(MockData.B());
                t.Equal("5 小時快用完：維持快沒力的表情", "critical", pet.ExpressionOf("claude"));
                t.Equal("5 小時用完：睡覺", "sleep", pet.ExpressionOf("codex"));
                var quiet = MockData.C();
                foreach (var v in quiet) { v.UseIt = null; v.UseItLevel = 0; }
                show(quiet);
                t.Check("關掉提醒：沒有鬧鐘、不閃、不著急",
                    !pet.HasClock("claude") && pet.UrgentBarOf("claude") == null && !pet.ExpressionOf("claude").StartsWith("hurry"), pet.ExpressionOf("claude"));
                // growing pets (#23): one more accessory at levels 2, 4, 6, 8 and 10, on every pet, changed in place
                var worn = new[] { 1, 2, 4, 6, 8, 10, 12, 0 }.Select(lv => { pet.SetGrowth(lv); return pet.GearOf("claude") + "/" + pet.GearOf("copilot"); });
                t.Equal("升級配件：Lv 1、2、4、6、8、10、12、關掉", "0/0 1/1 2/2 3/3 4/4 5/5 5/5 0/0", string.Join(" ", worn));
                pet.SetGrowth(8);
                show(MockData.A());
                t.Equal("換資料重建後配件還在", 4, pet.GearOf("claude"));
                pet.Detach();
            });
        }

        static void CustomThemes(TestKit t)
        {
            t.Section("自訂造型（#24）");
            Progress = "custom themes";
            t.Run("custom themes", () =>
            {
                string dir = t.TempDir("themes");
                ExampleTheme.Install(System.IO.Path.Combine(dir, ExampleTheme.Folder));
                // two broken ones next to it: nothing may crash, each says why
                Directory.CreateDirectory(System.IO.Path.Combine(dir, "broken"));
                File.WriteAllText(System.IO.Path.Combine(dir, "broken", "theme.json"), "{ \"card\": ");
                Directory.CreateDirectory(System.IO.Path.Combine(dir, "fake-picture"));
                File.WriteAllText(System.IO.Path.Combine(dir, "fake-picture", "theme.json"),
                    "{ \"card\": { \"elements\": [ { \"type\": \"image\", \"width\": 20, \"height\": 20, \"image\": \"face.png\" }, { \"type\": \"text\", \"text\": \"{name}\" } ] } }");
                File.WriteAllText(System.IO.Path.Combine(dir, "fake-picture", "face.png"), "not a picture");
                // a text per state (#28): texts, not picture files
                Directory.CreateDirectory(System.IO.Path.Combine(dir, "words"));
                File.WriteAllText(System.IO.Path.Combine(dir, "words", "theme.json"),
                    "{ \"card\": { \"elements\": [ { \"type\": \"text\", \"text\": \"{name}\", \"states\": { \"great\": \"好耶\", \"low\": { \"en\": \"low\", \"zh-TW\": \"快沒了\" } } } ] } }");
                try
                {
                    ThemeCatalog.LoadCustom(dir);
                    var cloud = ThemeCatalog.CustomSpecs.FirstOrDefault(s => s.Id == "custom:cloud");
                    t.Check("範例造型（內建在程式裡）：寫得出來、讀得懂、沒有問題", cloud != null && cloud.Usable && cloud.Problems.Count == 0,
                            cloud == null ? "missing" : string.Join(" | ", cloud.Problems));
                    var broken = ThemeCatalog.CustomSpecs.FirstOrDefault(s => s.Id == "custom:broken");
                    t.Check("壞掉的造型：不能用、說出原因、不在選單裡", broken != null && !broken.Usable && broken.Problems.Count > 0 && ThemeCatalog.Choices.All(c => c.Id != broken.Id));
                    var fake = ThemeCatalog.CustomSpecs.FirstOrDefault(s => s.Id == "custom:fake-picture");
                    t.Check("不是圖片的圖片：說出來、其他照樣用", fake != null && fake.Usable && fake.Problems.Any(p => p.Contains("face.png")) && fake.Elements.Count == 1,
                            fake == null ? "missing" : string.Join(" | ", fake.Problems));
                    var texts = ThemeCatalog.CustomSpecs.FirstOrDefault(s => s.Id == "custom:words");
                    t.Check("依狀態換的文字不會被當成圖片檢查", texts != null && texts.Usable && texts.Problems.Count == 0 && texts.Elements[0].States.Count == 2,
                            texts == null ? "missing" : string.Join(" | ", texts.Problems));
                    t.Equal("選單：8 種內建＋可以用的自訂造型", 11, ThemeCatalog.Choices.Count);

                    var theme = ThemeCatalog.Get("custom:cloud").Create();
                    theme.Attach(new Snapshots.PreviewHost());
                    theme.Update(MockData.A());
                    for (int i = 0; i < 30; i++) theme.Tick(1 / 30.0);
                    var bmp = Snapshots.RenderToBitmap(theme.Root, 1.0);
                    t.Check("範例造型畫得出來（三張卡片、有圖）", bmp.PixelSize.Width > 300 && InkPixels(bmp) > 20000, bmp.PixelSize + ", " + InkPixels(bmp) + " px");
                    var tags = theme.Root.GetLogicalDescendants().OfType<Control>().Select(c => c.Tag as string).Where(x => x != null && x.StartsWith("pv:")).ToList();
                    t.Equal("每個 AI 一張卡片（懸停、點擊找得到）", "pv:claude,pv:codex,pv:copilot", string.Join(",", tags));
                    var words = theme.Root.GetLogicalDescendants().OfType<TextBlock>().Where(x => x.IsVisible).Select(x => x.Text ?? "").ToList();
                    t.Check("卡片上有名稱和剩餘 %", words.Contains("Claude") && words.Any(x => x.EndsWith("%")), string.Join(" | ", words.Take(6)));
                    theme.Update(MockData.B());
                    theme.Tick(1 / 30.0);
                    t.Check("換資料也畫得出來（沒資料、用完）", InkPixels(Snapshots.RenderToBitmap(theme.Root, 1.0)) > 20000);
                    theme.Detach();
                    // its own lines (#28): said while the theme is on the widget
                    var pwSettings = new AppSettings { Theme = "custom:cloud" };
                    var pw = new PetWindow(DesktopController.ForSnapshot(pwSettings, MockData.A()), pwSettings);
                    pw.SetTheme(ThemeCatalog.Get("custom:cloud").Create());
                    t.Check("範例造型附帶的台詞：換上造型就會說", CustomLines.Theme != null && CustomLines.Theme == cloud.Lines && CustomLines.For("idleGreat") != null);
                    pw.SetTheme(ThemeCatalog.Get("pet").Create());
                    t.Check("換回內建造型：不再說那個造型的台詞", CustomLines.Theme == null);
                    pw.Close();
                    var fakeTheme = ThemeCatalog.Get("custom:fake-picture").Create();
                    fakeTheme.Attach(new Snapshots.PreviewHost());
                    fakeTheme.Update(MockData.A());
                    t.Check("有問題的自訂造型也不會當掉", Snapshots.RenderToBitmap(fakeTheme.Root, 1.0).PixelSize.Width > 0);
                    t.Equal("找不到的造型（資料夾被刪了）：用果凍桌寵", "pet", ThemeCatalog.Get("custom:gone").Id);
                }
                finally { ThemeCatalog.LoadCustom(System.IO.Path.Combine(dir, "none")); }
            });
        }

        // ------------------------------------------------------------------ hover card placement (ported from the WPF self-test)

        static void Placement(TestKit t)
        {
            t.Section("詳情卡擺放位置（三台 1920×1080 螢幕）");
            Progress = "placement";
            t.Run("Placement", () =>
            {
                var card = DetailCardView.Build(MockData.A()[0]);
                card.Root.Measure(Size.Infinity);
                double w = Math.Ceiling(card.Root.DesiredSize.Width), h = Math.Ceiling(card.Root.DesiredSize.Height);
                double margin = DetailCardView.Margin, gap = 6 - margin;
                var left = new Rect(-1920, 0, 1920, 1032);
                var primary = new Rect(0, 0, 1920, 1032);
                var right = new Rect(1920, 0, 1920, 1032);
                Case(t, "主螢幕右下角（預設）", primary, new Rect(1538, 852, 360, 164), new Rect(1538, 852, 120, 164), w, h, gap, margin);
                Case(t, "主螢幕左上角", primary, new Rect(22, 64, 360, 164), new Rect(22, 64, 120, 164), w, h, gap, margin);
                Case(t, "主螢幕正中央", primary, new Rect(780, 450, 360, 164), new Rect(900, 450, 120, 164), w, h, gap, margin);
                Case(t, "左螢幕貼左邊", left, new Rect(-1920, 852, 360, 164), new Rect(-1920, 852, 120, 164), w, h, gap, margin);
                Case(t, "右螢幕貼右邊", right, new Rect(3480, 852, 360, 164), new Rect(3720, 852, 120, 164), w, h, gap, margin);
                Case(t, "高清單（上下都放不下）", primary, new Rect(800, 40, 360, 950), new Rect(800, 500, 360, 120), w, h, gap, margin);
                Case(t, "高清單貼右邊", primary, new Rect(1560, 40, 360, 950), new Rect(1560, 500, 360, 120), w, h, gap, margin);
            });
        }

        static void Case(TestKit t, string name, Rect work, Rect content, Rect provider, double w, double h, double gap, double margin)
        {
            var r = DetailPlacement.Compute(content, provider, work, w, h, gap);
            var window = new Rect(r.X, r.Y, w, h);
            var visible = window.Deflate(margin);
            bool inside = window.Left >= work.Left - 0.5 && window.Right <= work.Right + 0.5 && window.Top >= work.Top - 0.5 && window.Bottom <= work.Bottom + 0.5;
            bool overlaps = visible.Intersects(content);
            double arrowAt = r.X + r.PointerX, target = provider.Left + provider.Width / 2;
            bool pointsAt = r.Side == DetailPlacement.Side.Left || r.Side == DetailPlacement.Side.Right || Math.Abs(arrowAt - target) < 1 ||
                            r.PointerX <= margin + 18 || r.PointerX >= w - margin - 18;
            t.Check(name, inside && !overlaps && pointsAt, "side=" + r.Side + " window=(" + r.X + "," + r.Y + ") inWorkArea=" + inside + " overlapsPet=" + overlaps);
        }

        // ------------------------------------------------------------------ what the pets say (ported from the WPF self-test)

        static void Speaking(TestKit t)
        {
            t.Section("桌寵說的話");
            Progress = "lines";
            t.Run("Lines", () =>
            {
                var all = MockData.A().Concat(MockData.B()).Concat(MockData.C()).ToList();
                var problems = new List<string>();
                for (int seed = 0; seed < 40; seed++)
                {
                    var rng = new Random(seed);
                    foreach (var v in all)
                    {
                        foreach (var line in new[] { Lines.Idle(v, all, rng), Lines.Poke(v, rng) })
                            if (string.IsNullOrEmpty(line) || line.Contains("{") || line.Contains("}")) problems.Add(v.Id + ": " + line);
                        if (v.UseItLevel > 0)
                        {
                            string u = Lines.UseIt(v, rng);
                            if (string.IsNullOrEmpty(u) || u.Contains("{")) problems.Add("use-it " + v.Id + ": " + u);
                        }
                    }
                }
                t.Check("閒聊、戳一下、催促：每句都完整（沒有 {placeholder}）", problems.Count == 0, problems.Count == 0 ? "40 輪 × " + all.Count + " 隻" : problems.First());
                var c = MockData.C();
                t.Check("三個等級的通知文字", c.All(v => Lines.UseItAlert(v).Length > 10), string.Join(" / ", c.Select(v => Lines.UseItAlert(v))));
                t.Equal("不急的時候沒有通知文字", "", Lines.UseItAlert(MockData.A()[2]));
                var claude = all[0];
                t.Check("提醒、緊急、重置都提到名字", Lines.Warn(claude, claude.Primary).Contains("Claude") && Lines.Critical(claude, claude.Primary).Contains("Claude") && Lines.Reset(claude, claude.Primary).Contains("Claude"));
                t.Contains("第一次見面的招呼", Lines.Greeting(MockData.A()), "Claude、Codex、Copilot");
            });
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
                var c = MockData.C()[0];
                var cv = DetailCardView.Build(c);
                t.Check("快過期：卡片上有完整提醒", cv.BannerShown && cv.BannerText.Contains("Claude") && cv.BannerText.Contains(Fmt.Pct(c.UseIt.Remaining)), cv.BannerText);
                var a = MockData.A()[2];
                t.Check("不急的時候：沒有提醒", !DetailCardView.Build(a).BannerShown);
                c.UseIt = null;
                c.UseItLevel = 0;
                cv.Update(c);
                t.Check("更新後提醒消失", !cv.BannerShown);
                t.Check("同樣的額度組成：就地更新", cv.Matches(c) && !cv.Matches(a));
                t.Equal("沒有預測：不顯示用完時間", "", cv.Forecasts);
                c.Meters[0].RunsOutAt = DateTime.UtcNow.AddMinutes(40);
                cv.Update(c);
                t.Contains("照目前速度會用完：卡片上顯示時間（#15）", cv.Forecasts, Fmt.When(c.Meters[0].RunsOutAt));
                c.Meters[0].RunsOutAt = null;
                cv.Update(c);
                t.Equal("預測消失：那一行也消失", "", cv.Forecasts);
                // growing pets (#23): the level next to the name, and the card is built again when it changes
                DetailCardView.GrowthLevel = 3;
                t.Check("升級後卡片會重建", !cv.Matches(c));
                var lvTexts = DetailCardView.Build(c).Root.GetLogicalDescendants().OfType<TextBlock>().Select(x => x.Text).ToList();
                DetailCardView.GrowthLevel = 0;
                t.Check("卡片上顯示 Lv 3", lvTexts.Contains("Lv 3"));
                t.Check("關掉養成：不顯示等級", !DetailCardView.Build(c).Root.GetLogicalDescendants().OfType<TextBlock>().Any(x => (x.Text ?? "").StartsWith("Lv ")));
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
            Progress = "scaled drawing";
            t.Run("scaled drawing", () =>
            {
                // a card with a BoxShadow (the themes' shadows since #8) drawn at 2×, twice: the card must fill the
                // bitmap both times (at a higher dpi Avalonia drew such a border at 1×; the scale transform used
                // instead must not stay on the control)
                var card = new Border
                {
                    Width = 50, Height = 30,
                    Background = Avalonia.Media.Brushes.Red,
                    BoxShadow = G.Shadow(8, 2, 0.5, Avalonia.Media.Colors.Black),
                    // a 10 × 10 blue square in the top-left corner of the red card
                    Child = new Border { Background = Avalonia.Media.Brushes.Blue, Width = 10, Height = 10,
                                         HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top },
                };
                Func<RenderTargetBitmap, int, int, byte[]> pixel = (bmp, x, y) => Snapshots.Bgra(bmp, new PixelRect(x, y, 1, 1));   // (macOS gives RGBA)
                Func<byte[], bool> red = px => px[2] > 200 && px[0] < 60;
                Func<byte[], bool> blue = px => px[0] > 200 && px[2] < 60;
                for (int pass = 1; pass <= 2; pass++)
                {
                    using (var bmp = Snapshots.Draw(card, 2))
                    {
                        t.Equal("放大 2 倍的截圖尺寸（第 " + pass + " 次）", "100x60", bmp.PixelSize.Width + "x" + bmp.PixelSize.Height);
                        var corner = pixel(bmp, 96, 56);
                        var outside = pixel(bmp, 30, 30);
                        var inside = pixel(bmp, 10, 10);
                        t.Check("卡片畫滿到右下角（不是只畫 1 倍）（第 " + pass + " 次）", red(corner), "BGRA " + string.Join(",", corner));
                        t.Check("左上角的小方塊是 2 倍大、沒有被放大兩次（第 " + pass + " 次）", blue(inside) && red(outside),
                                "BGRA " + string.Join(",", inside) + " / " + string.Join(",", outside));
                    }
                }
            });
            Progress = "demo gif";
            t.Run("demo gif", () =>
            {
                // frames with colours 32 apart (the palette holds them exactly, the dithering never flips one),
                // noisy enough that the LZW table fills up and is cleared
                int w = 160, h = 120;
                var rng = new Random(11);
                var colours = Enumerable.Range(0, 200).Select(i => (rng.Next(8) * 32 << 16) | (rng.Next(8) * 32 << 8) | rng.Next(8) * 32).ToArray();
                Func<int, int[]> noise = seed =>
                {
                    var r = new Random(seed);
                    return Enumerable.Range(0, w * h).Select(i => colours[r.Next(colours.Length)]).ToArray();
                };
                Func<int[], int, int[]> square = (bg, x0) =>
                {
                    var px = (int[])bg.Clone();
                    for (int y = 30; y < 60; y++) for (int x = x0; x < x0 + 30; x++) px[y * w + x] = 0xE0E0E0;
                    return px;
                };
                var bgA = noise(1);
                var scene1 = new List<int[]> { square(bgA, 10), square(bgA, 10), square(bgA, 40) };   // the second one is unchanged
                var scene2 = new List<int[]> { noise(2), square(noise(2), 90) };
                Func<int[], byte[]> bgra = px =>
                {
                    var b = new byte[px.Length * 4];
                    for (int i = 0; i < px.Length; i++) { b[i * 4] = (byte)px[i]; b[i * 4 + 1] = (byte)(px[i] >> 8); b[i * 4 + 2] = (byte)(px[i] >> 16); b[i * 4 + 3] = 255; }
                    return b;
                };
                var gif = new GifWriter(w, h);
                gif.AddScene(scene1.Select(bgra).ToList(), 10);
                gif.AddScene(scene2.Select(bgra).ToList(), 10);
                string file = Path.Combine(Path.GetTempPath(), "sentripet-selftest.gif");
                gif.Save(file);
                List<int> delays;
                var shown = DecodeGif(File.ReadAllBytes(file), out delays);
                try { File.Delete(file); } catch { }
                var expected = new List<int[]> { scene1[0], scene1[2], scene2[0], scene2[1] };
                t.Equal("GIF：沒變的畫格併進上一格（4 格）", 4, shown.Count);
                t.Equal("GIF：總長度不變（5 × 0.1 秒）", 50, delays.Sum());
                t.Equal("GIF：沒變的那格讓上一格停兩倍久", 20, delays.Count > 0 ? delays[0] : -1);
                for (int i = 0; i < Math.Min(shown.Count, expected.Count); i++)
                    t.Check("GIF：解碼後第 " + (i + 1) + " 格的每個像素都對（只存變動區域、換調色盤、LZW 清表）",
                        shown[i].SequenceEqual(expected[i]));
            });
            Progress = "menu";
            t.Run("menu", () =>
            {
                var ctl = DesktopController.ForSnapshot(new AppSettings { Theme = "glass" }, MockData.A());
                var items = ctl.BuildMenuItems();
                var subs = items.OfType<MenuItem>().Where(m => m.ItemsSource != null).ToList();
                t.Equal("右鍵選單：只有一個換造型的子選單", 1, subs.Count(m => Equals(m.Header, L.T("換造型"))));
                t.Check("右鍵選單：沒有重複的「今天心情如何？」", !items.OfType<MenuItem>().Any(m => Equals(m.Header, "今天心情如何？")));
                var looks = ((IEnumerable<object>)subs.First(m => Equals(m.Header, L.T("換造型"))).ItemsSource).ToList();
                var radios = looks.OfType<MenuItem>().Where(m => m.ToggleType == MenuItemToggleType.Radio).ToList();
                t.Equal("換造型：8 種造型", 8, radios.Count);
                t.Check("換造型：每一項都標出心情", radios.All(m => m.Header is Grid && ((Grid)m.Header).Children.OfType<TextBlock>().Count() == 2));
                t.Equal("換造型：目前的造型打勾", 1, radios.Count(m => m.IsChecked));
                t.Check("換造型：隨機與每天隨機換都在同一個選單", looks.OfType<MenuItem>().Any(m => Equals(m.Header, L.T("交給命運吧（隨機）"))) &&
                                                               looks.OfType<MenuItem>().Any(m => Equals(m.Header, L.T("每天隨機換一個"))));
                // the switches that are rarely changed are on the settings page only (the user's request)
                var rare = new[] { L.T("永遠在最上層"), L.T("滑鼠穿透（不擋點擊）"), L.T("會說話"), L.T("額度提醒通知"), L.T("催我用完週額度（重置前提醒）"), L.T("開機自動啟動") };
                t.Check("右鍵選單：不常用的開關只放在設定頁", !items.OfType<MenuItem>().Any(m => rare.Contains(m.Header as string)),
                    string.Join(", ", items.OfType<MenuItem>().Where(m => rare.Contains(m.Header as string)).Select(m => m.Header)));
                var report = subs.FirstOrDefault(m => Equals(m.Header, L.T("額度利用率（週報／月報）")));
                t.Check("右鍵選單：額度利用率緊接在換造型後面", report != null && items.IndexOf(report) == items.IndexOf(subs.First(m => Equals(m.Header, L.T("換造型")))) + 1);
                var entries = report != null ? ((IEnumerable<object>)report.ItemsSource).OfType<MenuItem>().ToList() : new List<MenuItem>();
                Func<object, List<Control>> all = null;
                all = o =>
                {
                    var list = new List<Control>();
                    var c = o as Control;
                    if (c == null) return list;
                    list.Add(c);
                    var panel = c as Panel;
                    if (panel != null) foreach (var ch in panel.Children) list.AddRange(all(ch));
                    var border = c as Border;
                    if (border != null) list.AddRange(all(border.Child));
                    return list;
                };
                Func<MenuItem, string> text = m => m.Header as string ?? string.Join(" ", all(m.Header).OfType<TextBlock>().Select(x => x.Text));
                var claude = entries.FirstOrDefault(m => text(m).StartsWith("Claude · "));
                // the sample report has 8 past Claude weeks: 8 bars and the week in progress
                var claudeBars = claude != null ? all(claude.Header).OfType<StackPanel>().FirstOrDefault(sp => sp.Orientation == Avalonia.Layout.Orientation.Horizontal) : null;
                t.Check("額度利用率：選單裡直接有圖表（過去 8 期＋這期）與數字（上期 88%）",
                    claude != null && claudeBars != null && claudeBars.Children.Count == 9 && text(claude).Contains(Fmt.Pct(88)),
                    claude == null ? "no Claude entry" : text(claude) + " / " + (claudeBars == null ? "no bars" : claudeBars.Children.Count + " bars"));
                t.Check("額度利用率：每個週／月額度一項，最後是完整報告",
                    entries.Any(m => text(m).StartsWith("Codex · ")) && text(entries.Last()) == L.T("查看完整報告…"), string.Join(" | ", entries.Select(text)));
            });
            Progress = "report window";
            t.Run("report window", () =>
            {
                var win = new ReportWindow(new Snapshots.SnapshotSettingsHost(), false) { Width = 760, Height = 900 };
                win.Show();
                for (int i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
                t.Equal("用量報告：預設最近 7 天，每天一根", 7, win.DayColumns);
                win.ShowDays(30);
                t.Equal("用量報告：切到最近 30 天", 30, win.DayColumns);
                t.Check("用量報告：專案排行（最多 8 個）", win.ProjectNames.Contains("SentriPet") && win.ProjectNames.Count <= 8, string.Join(", ", win.ProjectNames));
                t.Check("用量報告：API 等值費用有算出來（範例資料）", win.CostTotal.HasValue && win.CostTotal.Value > 1, win.CostTotal.HasValue ? Fmt.Usd(win.CostTotal.Value) : "null");
                win.Close();
                // the shareable weekly summary (#20): a 1200 × 675 PNG
                string card = Path.Combine(Path.GetTempPath(), "sentripet-share-test.png");
                try { File.Delete(card); } catch { }
                ShareCard.Save(new Snapshots.SnapshotSettingsHost(), card, DateTime.Now);
                var head = File.Exists(card) ? File.ReadAllBytes(card).Take(24).ToArray() : new byte[0];
                int w = head.Length == 24 ? head[16] << 24 | head[17] << 16 | head[18] << 8 | head[19] : 0, h = head.Length == 24 ? head[20] << 24 | head[21] << 16 | head[22] << 8 | head[23] : 0;
                t.Equal("週報圖：1200 × 675 的 PNG", "1200x675", w + "x" + h);
                try { File.Delete(card); } catch { }
            });
            Progress = "settings page";
            t.Run("settings", () =>
            {
                var host = new TestHost();
                // another account (#26): its own card with name, folder, colours and its status line
                host.Settings.Accounts.Add(new Account { Id = "claude-2", Kind = "claude", Folder = t.TempDir("account"), Name = "Work", Color = AccountSetup.Colors[0] });
                // a lines.json with a mistake (#25): the page says which line is skipped
                CustomLines.Override = "{ \"zh-TW\": { \"poke\": [\"好\", \"{bad} 壞掉\"] } }";
                CustomLines.Reload();
                var win = new SettingsWindow(host) { Width = 700, Height = 900 };
                win.Show();
                var linesText = win.GetVisualDescendants().OfType<TextBlock>().Select(tb => tb.Text ?? "").FirstOrDefault(s => s.Contains("{bad}"));
                t.Check("設定頁：自訂台詞寫錯時說出是哪一句", linesText != null && linesText.Contains(L.F("目前的語言有 {0} 句自訂台詞", 1)), linesText ?? "(none)");
                CustomLines.Override = null;
                CustomLines.Reload();
                // growing pets (#23): the level and the ten achievements (the sample report unlocks some)
                var stars = win.GetVisualDescendants().OfType<TextBlock>().Where(tb => tb.Text == "★" || tb.Text == "☆").ToList();
                t.Check("設定頁：10 個成就，有的已解鎖", stars.Count == 10 && stars.Any(x => x.Text == "★") && stars.Any(x => x.Text == "☆"),
                        stars.Count(x => x.Text == "★") + " / " + stars.Count);
                t.Check("設定頁：顯示等級", win.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text == "Lv " + host.Progress.Level));
                t.Check("設定頁：其他帳號有名稱和狀態列開關", win.GetVisualDescendants().OfType<TextBox>().Any(b => b.Text == "Work") &&
                        win.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text == L.T("連接這個帳號的 Claude Code 狀態列")));
                var removeBtn = win.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, L.T("移除")));
                if (removeBtn != null) removeBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                for (int i = 0; i < 2; i++) Dispatcher.UIThread.RunJobs();
                t.Check("設定頁：移除帳號", removeBtn != null && host.Settings.Accounts.Count == 0 && !win.GetVisualDescendants().OfType<TextBox>().Any(b => b.Text == "Work"));
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
                // for other programs (#22): the OBS switch applies at once and shows the address, or how to fix a taken port
                host.UsageServerUrl = "http://127.0.0.1:47291/";
                var obsLabel = win.GetVisualDescendants().OfType<TextBlock>().First(tb => tb.Text == L.T("直播畫面（OBS）"));
                var obs = obsLabel.FindAncestorOfType<Grid>().GetVisualDescendants().OfType<ToggleSwitch>().First();
                int changes = host.Changed;
                obs.IsChecked = true;
                for (int i = 0; i < 2; i++) Dispatcher.UIThread.RunJobs();
                t.Check("設定頁：打開直播畫面 → 馬上套用、顯示網址", host.Settings.UsageServer && host.Changed > changes &&
                        win.GetVisualDescendants().OfType<TextBox>().Any(b => b.Text == host.UsageServerUrl));
                host.UsageServerUrl = null;
                obs.IsChecked = false;
                obs.IsChecked = true;
                for (int i = 0; i < 2; i++) Dispatcher.UIThread.RunJobs();
                t.Check("設定頁：連接埠被占用 → 說明要改哪裡", win.GetVisualDescendants().OfType<TextBlock>().Any(tb => tb.Text != null && tb.Text.Contains("usagePort")));
                win.Close();
            });
            Progress = "pet picture";
            t.Run("pet picture", () =>
            {
                // the OBS page's picture of the widget (#22), also while the widget is hidden (only in the tray)
                var settings = new AppSettings { Theme = "pet" };
                var pw = new PetWindow(DesktopController.ForSnapshot(settings, MockData.A()), settings);
                pw.SetTheme(ThemeCatalog.Get("pet").Create());
                var png = pw.Picture();
                using (var bmp = png != null ? new Bitmap(new MemoryStream(png)) : null)
                    t.Check("OBS 的桌寵畫面：隱藏時也畫得出來", bmp != null && bmp.PixelSize.Width > 80 && bmp.PixelSize.Height > 60 && InkPixels(bmp) > 3000,
                            bmp == null ? "null" : bmp.PixelSize + ", " + InkPixels(bmp) + " px");
                pw.Close();
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
            readonly UsageHistory history = MockData.History();
            public UsageHistory History { get { return history; } }
            public void ApplyWidgetSettings() { Changed++; }
            public void ChangeTheme(string id) { settings.Theme = id; Changed++; }
            public void ChangeLanguage(string code) { settings.Language = code; Changed++; }
            public void RefreshViews() { Changed++; }
            public void OpenReportWindow() { Changed++; }
            public void ApplyExportSettings() { Changed++; }
            Progress progress;
            public Progress Progress { get { return progress ?? (progress = MockData.Growth(history)); } }
            public void ApplyGrowth() { Changed++; }
            public void OpenThemesFolder() { Changed++; }
            public void ReloadThemes() { Changed++; }
            public string AddAccount(string kind, string folder) { Changed++; return AccountSetup.Problem(settings, kind, folder) ?? (AccountSetup.Add(settings, kind, folder) == null ? "?" : null); }
            public void RemoveAccount(Account a) { settings.Accounts.Remove(a); Changed++; }
            public void AccountChanged() { Changed++; }
            public string SetAccountStatusLine(Account a, bool on) { Changed++; return null; }
            public string UsageServerUrl { get; set; }
        }

        // ------------------------------------------------------------------ languages

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        static void ShortcutChecks(TestKit t)
        {
            t.Run("start menu shortcut", () =>
            {
                string lnk = Path.Combine(t.TempDir("lnk"), "SentriPet test.lnk");
                string target = Environment.ProcessPath;
                CreateShortcut(lnk, target);
                t.Check("開始選單捷徑：建立得出來", File.Exists(lnk));
                t.Equal("開始選單捷徑：指向程式", target, ReadTarget(lnk));
                t.Equal("開始選單捷徑：帶著通知用的 AppUserModelID", Integration.AppUserModelId, ReadAppId(lnk));
            });
        }

        static bool SamePixels(Bitmap a, Bitmap b)
        {
            int w = a.PixelSize.Width, h = a.PixelSize.Height;
            var pa = new byte[w * h * 4];
            var pb = new byte[w * h * 4];
            foreach (var pair in new[] { Tuple.Create(a, pa), Tuple.Create(b, pb) })
            {
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(pair.Item2, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { pair.Item1.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pair.Item2.Length, w * 4); }
                finally { handle.Free(); }
            }
            return pa.SequenceEqual(pb);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        static void CreateShortcut(string lnk, string target) { Integration.ShellLink.Create(lnk, target, "", Integration.AppUserModelId, AppInfo.Name); }
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        static string ReadTarget(string lnk) { return Integration.ShellLink.ReadTarget(lnk); }
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        static string ReadAppId(string lnk) { return Integration.ShellLink.ReadAppId(lnk); }

        static bool HasHan(string s) { return s.Any(c => c >= '一' && c <= '鿿'); }

        static void Languages(TestKit t)
        {
            t.Section("介面語言（Avalonia）");
            try
            {
                // the hover card is built once and then only updated: the words written when it was built (quota names,
                // the hint at the bottom) stayed Chinese after switching the language (user report)
                var zhCard = DetailCardView.Build(MockData.A()[0]);
                Program.UseLanguage("en");
                t.Equal("英文：造型名稱", "Jelly Pet", ThemeCatalog.Get("pet").Name);
                var enView = MockData.A()[0];
                t.Check("換語言後懸停卡片會重建，不會只更新數字", !zhCard.Matches(enView));
                var cardTexts = DetailCardView.Build(enView).Root.GetLogicalDescendants().OfType<TextBlock>().Select(x => x.Text ?? "").ToList();
                var cardHan = cardTexts.Where(HasHan).ToList();
                t.Check("英文的懸停卡片沒有中文（額度名稱、最下面的提示）", cardHan.Count == 0 && cardTexts.Any(x => x.StartsWith("Click to interact")),
                        string.Join(" | ", cardHan.Take(5)));
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
            t.Run("statusline command", () =>
            {
                // the program as Claude Code runs it: JSON in, one line out, quickly
                var now = DateTime.UtcNow;
                string input = "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":25,\"resets_at\":" + (Json.ToUnixMs(now.AddHours(2)) / 1000) +
                               "},\"seven_day\":{\"used_percentage\":40,\"resets_at\":" + (Json.ToUnixMs(now.AddDays(3)) / 1000) + "}}}";
                var psi = new ProcessStartInfo(Environment.ProcessPath)
                {
                    UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8,
                };
                psi.ArgumentList.Add("--statusline");
                psi.ArgumentList.Add("--dev");
                var sw = Stopwatch.StartNew();
                using (var p = Process.Start(psi))
                {
                    var w = new StreamWriter(p.StandardInput.BaseStream, new System.Text.UTF8Encoding(false));
                    w.Write(input);
                    w.Close();
                    string output = p.StandardOutput.ReadToEnd();
                    bool exited = p.WaitForExit(20000);
                    t.Check("狀態列指令：讀得到輸入、印出剩餘額度", exited && p.ExitCode == 0 && output.Contains("5h") && output.Contains("75%"), output.Trim());
                    t.Check("狀態列指令：夠快（Claude Code 每次回覆都會執行）", sw.ElapsedMilliseconds < 5000, sw.ElapsedMilliseconds + " ms");
                }
                // the dev-profile file it wrote next to Claude's settings
                string devFile = Path.Combine(AppPaths.Home, ".claude", "sentripet-status-dev.json");
                t.Check("狀態列指令：開發模式寫到另一個檔案（不碰正式的數字）", File.Exists(devFile));
                try { File.Delete(devFile); } catch { }
            });
            t.Run("hook command", () =>
            {
                // the program as Claude Code's hook (JSON on stdin) and as Codex's notify program (JSON as the last argument):
                // prints nothing (a Stop hook's output can stop Claude from stopping), exit code 0, the event written (#14)
                foreach (var source in new[] { "claude", "codex" })
                {
                    string events = source == "claude" ? AgentHooks.ClaudeEventsFile : AgentHooks.CodexEventsFile;
                    try { File.Delete(events); } catch { }
                    var psi = new ProcessStartInfo(Environment.ProcessPath)
                    {
                        UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8,
                    };
                    psi.ArgumentList.Add("--dev");
                    psi.ArgumentList.Add("--hook");
                    psi.ArgumentList.Add(source);
                    if (source == "codex") psi.ArgumentList.Add("{\"type\":\"agent-turn-complete\",\"cwd\":\"/work/demo-app\"}");
                    var sw = Stopwatch.StartNew();
                    using (var p = Process.Start(psi))
                    {
                        var w = new StreamWriter(p.StandardInput.BaseStream, new System.Text.UTF8Encoding(false));
                        if (source == "claude") w.Write("{\"hook_event_name\":\"Notification\",\"notification_type\":\"permission_prompt\",\"cwd\":\"/work/demo-app\"}");
                        w.Close();
                        string output = p.StandardOutput.ReadToEnd();
                        bool exited = p.WaitForExit(20000);
                        t.Check(source + " hook：不印任何東西、結束碼 0、夠快", exited && p.ExitCode == 0 && output.Length == 0 && sw.ElapsedMilliseconds < 5000,
                            "exit " + (exited ? p.ExitCode.ToString() : "-") + ", " + output.Length + " chars, " + sw.ElapsedMilliseconds + " ms");
                    }
                    var e = File.Exists(events) ? AgentHooks.ParseLine(File.ReadAllLines(events).LastOrDefault()) : null;
                    t.Check(source + " hook：事件寫到開發模式的檔案", e != null && e.Source == source && e.Project == "demo-app" &&
                                                                  e.Kind == (source == "claude" ? AgentEvent.Permission : AgentEvent.Done), events);
                    try { File.Delete(events); } catch { }
                }
            });
            t.Check("全螢幕偵測：Windows 與 Linux（X11）", Integration.CanDetectFullscreen == (Os.Windows || Os.Linux));
            t.Run("tray icon", () =>
            {
                int full = InkPixels(TrayArt.Draw(90, false, 64)), empty = InkPixels(TrayArt.Draw(10, true, 64));
                t.Check("系統匣圖示畫得出來（果凍＋工作中的藍點）", full > 1500 && empty > 1500, full + " / " + empty + " px");
                t.Check("不同剩餘額度畫出不同的圖", !SamePixels(TrayArt.Draw(90, false, 32), TrayArt.Draw(10, false, 32)));
                // the number icon (#21): a full badge, a different picture for each number, "100" and "?" drawn too
                int badge = InkPixels(TrayArt.DrawNumber(42, false, 64));
                t.Check("數字圖示：整個方塊都畫出來", badge > 3000, badge + " px");
                t.Check("數字圖示：不同數字畫出不同的圖", !SamePixels(TrayArt.DrawNumber(42, false, 32), TrayArt.DrawNumber(87, false, 32)) &&
                                                       !SamePixels(TrayArt.DrawNumber(100, false, 32), TrayArt.DrawNumber(-1, false, 32)));
            });
            if (OperatingSystem.IsWindows()) ShortcutChecks(t);
        }

        /// <summary>Decodes a GIF (for the demo writer's check): the screen after each frame, as 0xRRGGBB.</summary>
        static List<int[]> DecodeGif(byte[] g, out List<int> delays)
        {
            delays = new List<int>();
            var frames = new List<int[]>();
            int w = g[6] | g[7] << 8, h = g[8] | g[9] << 8, pos = 13;
            int[] global = null;
            if ((g[10] & 0x80) != 0) { global = Colours(g, pos, 2 << (g[10] & 7)); pos += 3 * (2 << (g[10] & 7)); }
            var screen = new int[w * h];
            int transparent = -1, delay = 0;
            while (g[pos] != 0x3B)
            {
                if (g[pos] == 0x21)
                {
                    if (g[pos + 1] == 0xF9) { delay = g[pos + 4] | g[pos + 5] << 8; transparent = (g[pos + 3] & 1) != 0 ? g[pos + 6] : -1; }
                    pos += 2;
                    while (g[pos] != 0) pos += g[pos] + 1;
                    pos++;
                    continue;
                }
                int fx = g[pos + 1] | g[pos + 2] << 8, fy = g[pos + 3] | g[pos + 4] << 8, fw = g[pos + 5] | g[pos + 6] << 8, fh = g[pos + 7] | g[pos + 8] << 8;
                int flags = g[pos + 9];
                pos += 10;
                var pal = global;
                if ((flags & 0x80) != 0) { pal = Colours(g, pos, 2 << (flags & 7)); pos += 3 * (2 << (flags & 7)); }
                int min = g[pos++];
                var data = new List<byte>();
                while (g[pos] != 0) { for (int i = 1; i <= g[pos]; i++) data.Add(g[pos + i]); pos += g[pos] + 1; }
                pos++;
                var indices = Unlzw(data, min, fw * fh);
                for (int i = 0; i < indices.Count && i < fw * fh; i++)
                    if (indices[i] != transparent) screen[(fy + i / fw) * w + fx + i % fw] = pal[indices[i]];
                frames.Add((int[])screen.Clone());
                delays.Add(delay);
            }
            return frames;
        }

        static int[] Colours(byte[] g, int pos, int n)
        {
            var c = new int[n];
            for (int i = 0; i < n; i++) c[i] = g[pos + i * 3] << 16 | g[pos + i * 3 + 1] << 8 | g[pos + i * 3 + 2];
            return c;
        }

        static List<int> Unlzw(List<byte> data, int min, int count)
        {
            var output = new List<int>(count);
            int clear = 1 << min, end = clear + 1, size = min + 1, bit = 0;
            var table = new List<int[]>();
            Action reset = () =>
            {
                table.Clear();
                for (int i = 0; i < clear; i++) table.Add(new[] { i });
                table.Add(null);
                table.Add(null);
                size = min + 1;
            };
            reset();
            int[] prev = null;
            while (bit + size <= data.Count * 8)
            {
                int code = 0;
                for (int i = 0; i < size; i++, bit++) if ((data[bit >> 3] >> (bit & 7) & 1) != 0) code |= 1 << i;
                if (code == clear) { reset(); prev = null; continue; }
                if (code == end) break;
                int[] entry;
                if (code < table.Count && table[code] != null) entry = table[code];
                else if (code == table.Count && prev != null) entry = prev.Concat(new[] { prev[0] }).ToArray();
                else throw new InvalidDataException("bad LZW code " + code);
                output.AddRange(entry);
                if (prev != null && table.Count < 4096) table.Add(prev.Concat(new[] { entry[0] }).ToArray());
                prev = entry;
                if (table.Count == 1 << size && size < 12) size++;
            }
            return output;
        }
    }
}
