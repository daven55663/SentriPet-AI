using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentriPet
{
    /// <summary>The widget: hover-card placement, every theme, the pet's faces, the hover card, the speech bubble, lines.</summary>
    static class UiTests
    {
        public static void Run(TestKit t)
        {
            Placement(t);
            Themes(t);
            PetFaces(t);
            Card(t);
            Speech(t);
            Speaking(t);
        }

        // ------------------------------------------------------------------ hover card placement

        static void Placement(TestKit t)
        {
            t.Section("詳情卡擺放位置（三台 1920×1080 螢幕）");
            t.Run("Placement", () =>
            {
                var card = DetailCardView.Build(MockData.A()[0]);
                card.Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
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
            var visible = window;
            visible.Inflate(-margin, -margin);
            bool inside = window.Left >= work.Left - 0.5 && window.Right <= work.Right + 0.5 && window.Top >= work.Top - 0.5 && window.Bottom <= work.Bottom + 0.5;
            bool overlaps = visible.IntersectsWith(content);
            double arrowAt = r.X + r.PointerX, target = provider.Left + provider.Width / 2;
            bool pointsAt = r.Side == DetailPlacement.Side.Left || r.Side == DetailPlacement.Side.Right || Math.Abs(arrowAt - target) < 1 ||
                            r.PointerX <= margin + 18 || r.PointerX >= w - margin - 18;
            t.Check(name, inside && !overlaps && pointsAt, "side=" + r.Side + " window=(" + r.X + "," + r.Y + ") inWorkArea=" + inside + " overlapsPet=" + overlaps);
        }

        // ------------------------------------------------------------------ themes

        static int InkPixels(BitmapSource bmp)
        {
            int stride = bmp.PixelWidth * 4;
            var px = new byte[stride * bmp.PixelHeight];
            bmp.CopyPixels(px, stride, 0);
            int n = 0;
            for (int i = 3; i < px.Length; i += 4) if (px[i] > 16) n++;
            return n;
        }

        static void Themes(TestKit t)
        {
            t.Section("8 種造型（模擬資料 a/b/c、空的）");
            var sets = new List<KeyValuePair<string, List<ProviderView>>>
            {
                new KeyValuePair<string, List<ProviderView>>("a", MockData.A()),
                new KeyValuePair<string, List<ProviderView>>("b", MockData.B()),
                new KeyValuePair<string, List<ProviderView>>("c", MockData.C()),
                new KeyValuePair<string, List<ProviderView>>("空", new List<ProviderView>()),
            };
            string shots = t.TempDir("shots");
            foreach (var info in ThemeCatalog.All)
            {
                var ti = info;
                t.Run(ti.Name, () =>
                {
                    var results = new List<string>();
                    bool ok = true;
                    foreach (var set in sets)
                    {
                        var theme = ti.Create();
                        theme.Attach(new Snapshots.PreviewHost());
                        theme.Update(set.Value);
                        for (int i = 0; i < 40; i++) theme.Tick(1 / 30.0);
                        if (set.Value.Count > 0)
                        {
                            theme.Say(set.Value[0].Id, "測試一句話");
                            theme.Poke(set.Value[0].Id);
                            theme.Celebrate(set.Value[0].Id);
                        }
                        theme.Say(null, "大家好");
                        theme.Click(new Point(4, 4));
                        theme.Update(set.Value);   // data refresh without a layout change
                        for (int i = 0; i < 20; i++) theme.Tick(1 / 30.0);
                        var host = new Grid();
                        host.Children.Add(theme.Root);
                        var bmp = Snapshots.RenderToBitmap(host, 1.0);
                        var bounds = theme.ContentBounds(host);
                        int ink = InkPixels(bmp);
                        bool good = ink > 2000 && !bounds.IsEmpty && bounds.Width > 40 && bounds.Height > 30;
                        ok &= good;
                        results.Add(set.Key + ":" + bmp.PixelWidth + "×" + bmp.PixelHeight + (good ? "" : " 空白?"));
                        host.Children.Clear();
                        theme.Detach();
                    }
                    t.Check(ti.Name + "：畫得出來、互動不當機", ok, string.Join("  ", results));
                });
            }
            t.Run("截圖工具", () =>
            {
                var theme = ThemeCatalog.Get("pet").Create();
                theme.Attach(new Snapshots.PreviewHost());
                theme.Update(MockData.A());
                theme.Tick(0.1);
                string file = Path.Combine(shots, "pet.png");
                Snapshots.Render(theme.Root, file, 1.0, true);
                t.Check("截圖工具輸出 PNG", File.Exists(file) && new FileInfo(file).Length > 5000);
            });
        }

        // ------------------------------------------------------------------ the pet's faces

        static void PetFaces(TestKit t)
        {
            t.Section("果凍的表情、鬧鐘、閃爍的進度條");
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
                pet.Detach();
            });
        }

        // ------------------------------------------------------------------ hover card and speech bubble

        static void Card(TestKit t)
        {
            t.Section("詳情卡");
            t.Run("Card", () =>
            {
                var c = MockData.C()[0];
                var card = DetailCardView.Build(c);
                t.Check("快過期：顯示完整提醒", card.BannerShown && card.BannerText.Contains("Claude") && card.BannerText.Contains(Fmt.Pct(c.UseIt.Remaining)), card.BannerText);
                var a = MockData.A()[2];
                t.Check("不急的時候：沒有提醒", !DetailCardView.Build(a).BannerShown);
                c.UseIt = null;
                c.UseItLevel = 0;
                card.Update(c);
                t.Check("更新後提醒消失", !card.BannerShown);
                t.Check("同樣的額度組成：就地更新", card.Matches(c) && !card.Matches(a));
                var err = MockData.B()[3];
                var ec = DetailCardView.Build(err);
                ec.Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                t.Check("沒有資料的 AI 也畫得出卡片", ec.Root.DesiredSize.Height > 50);
            });
        }

        static void Speech(TestKit t)
        {
            t.Section("浮動泡泡");
            t.Run("Speech", () =>
            {
                var w = new SpeechWindow(null);
                w.SetText("claude", "一句很長很長的話，會自動換行，確認泡泡的大小算得出來。", Colors.Orange, 5);
                w.SetPointer(DetailPlacement.Side.Above, 40);
                var size = w.MeasurePx(1.5, 1.5);
                t.Check("泡泡的大小", size.Width > 100 && size.Height > 40, size.Width + "×" + size.Height);
                t.Check("顯示時間跟文字長度有關", w.Until > DateTime.UtcNow.AddSeconds(3) && w.ProviderId == "claude");
                w.Close();
            });
        }

        static void Speaking(TestKit t)
        {
            t.Section("桌寵說的話");
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
    }
}
