using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentriPet
{
    /// <summary>
    /// Jelly pets: every AI is a little gumdrop creature whose belly is filled with jelly up to the
    /// remaining quota. Eyes follow the mouse, it blinks, breathes, sweats when low, sleeps when empty.
    /// </summary>
    class PetTheme : Theme
    {
        const double CardW = 120, StageH = 96, BodyW = 80, BodyH = 66, BubbleZone = 56;
        static readonly Color Ink = Palette.Hex("#2B211E");
        static readonly Geometry BodyGeo = Frozen("M 14,66 C 4,66 0,58 0,48 C 0,20 18,0 40,0 C 62,0 80,20 80,48 C 80,58 76,66 66,66 Z");

        static Geometry Frozen(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }

        class MeterRow
        {
            public string Key;
            public Border Fill;
            public TextBlock Pct;
            public Anim Width = new Anim(0);
        }

        class Card
        {
            public string Id;
            public ProviderView V;
            public StackPanel Panel;
            public Canvas Stage, Body, Fx;
            public ScaleTransform BodyScale, ShadowScale;
            public TranslateTransform BodyMove;
            public Ellipse Shadow;
            public Path BaseFill, Liquid, Surface, Outline;
            public Canvas EyeL, EyeR;
            public ScaleTransform EyeLScale, EyeRScale;
            public TranslateTransform EyeLMove, EyeRMove;
            public Path HappyL, HappyR, SleepL, SleepR, Mouth, BrowL, BrowR, Sweat;
            public Ellipse BlushL, BlushR;
            public TextBlock Question;
            public TextBlock[] Zzz;
            public Ellipse[] Dots;
            public Path[] Sparkles;
            public RotateTransform AccRotate;
            public UIElement AccBlink, AccGlow;
            public TextBlock NameText, PctText, ResetText, ErrorText;
            public List<MeterRow> Rows = new List<MeterRow>();
            public Border Bubble;
            public TextBlock BubbleText;
            public Path BubbleTail;
            public double BubbleUntil = -1, BubbleAlpha;
            public Anim Level = new Anim(0);
            public Anim LookX = new Anim(0), LookY = new Anim(0);
            public double Phase, JumpT = -1, LandT = -1, BlinkT = -1, NextBlink = 2, NextHop = 20, SquintUntil = -1, CelebrateUntil = -1;
            public string Expr;
            public Color Color;
        }

        Grid root;
        StackPanel row;
        Canvas bubbleLayer;
        readonly List<Card> cards = new List<Card>();
        double nextChat = 25, chatCooldown;

        public override string Id { get { return "pet"; } }
        public override string Name { get { return "果凍桌寵"; } }
        public override string Mood { get { return "元氣滿滿"; } }
        public override string Blurb { get { return "果凍小怪獸，額度越多肚子越滿"; } }

        protected override FrameworkElement CreateRoot()
        {
            root = new Grid { Margin = new Thickness(6, 0, 6, 8) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BubbleZone) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetRow(row, 1);
            bubbleLayer = new Canvas { IsHitTestVisible = false };
            Grid.SetRowSpan(bubbleLayer, 2);
            root.Children.Add(row);
            root.Children.Add(bubbleLayer);
            return root;
        }

        /// <summary>Only the pets and their plates; the speech-bubble zone above them is empty most of the time.</summary>
        public override Rect ContentBounds(Visual relativeTo)
        {
            var r = BoundsOf(row, relativeTo);
            return r.IsEmpty ? base.ContentBounds(relativeTo) : r;
        }

        List<ProviderView> Shown
        {
            get
            {
                if (Views.Count > 0) return Views;
                return new List<ProviderView>
                {
                    new ProviderView { Id = "_", Name = "偵測中", Mascot = "antenna", Color = Palette.Hex("#94A3B8"), Error = "正在尋找電腦上的 AI…", Mood = SentriPet.Mood.Unknown, Remaining = 50 }
                };
            }
        }

        protected override void Rebuild()
        {
            row.Children.Clear();
            bubbleLayer.Children.Clear();
            cards.Clear();
            foreach (var v in Shown)
            {
                var c = MakeCard(v);
                cards.Add(c);
                row.Children.Add(c.Panel);
                bubbleLayer.Children.Add(c.Bubble);
                bubbleLayer.Children.Add(c.BubbleTail);
            }
        }

        // ------------------------------------------------------------------ building

        Card MakeCard(ProviderView v)
        {
            var c = new Card { Id = v.Id, V = v, Phase = Rng.NextDouble() * 10, NextBlink = 1 + Rng.NextDouble() * 3, NextHop = 15 + Rng.NextDouble() * 20 };
            c.Color = v.HasData ? v.Color : G.Desaturate(v.Color, 0.6);
            var color = c.Color;
            var dark = Palette.Darken(color, 0.4);

            c.Panel = new StackPanel { Width = CardW, Tag = "pv:" + v.Id };

            // ---------------- stage (the pet itself)
            c.Stage = new Canvas { Width = CardW, Height = StageH };
            c.Shadow = G.Oval(CardW / 2, StageH - 7, 64, 10, G.B(Colors.Black, 0.2));
            c.ShadowScale = new ScaleTransform(1, 1, 32, 5);
            c.Shadow.RenderTransform = c.ShadowScale;
            c.Stage.Children.Add(c.Shadow);

            c.Body = new Canvas { Width = BodyW, Height = BodyH };
            G.Place(c.Body, (CardW - BodyW) / 2, StageH - 10 - BodyH);
            c.BodyScale = new ScaleTransform(1, 1, BodyW / 2, BodyH);
            c.BodyMove = new TranslateTransform();
            var tg = new TransformGroup();
            tg.Children.Add(c.BodyScale);
            tg.Children.Add(c.BodyMove);
            c.Body.RenderTransform = tg;
            c.Stage.Children.Add(c.Body);

            var acc = MakeAccessory(c, v.Mascot, color, dark);
            if (acc != null) c.Body.Children.Add(acc);

            c.BaseFill = new Path { Data = BodyGeo, Fill = G.Vertical(Palette.Lighten(color, 0.9), Palette.Lighten(color, 0.74)) };
            c.Liquid = new Path { Fill = G.Vertical(Palette.Lighten(color, 0.3), color, Palette.Darken(color, 0.12)), Clip = BodyGeo };
            c.Surface = new Path { Stroke = G.B(Colors.White, 0.6), StrokeThickness = 1.6, Clip = BodyGeo };
            c.Outline = new Path { Data = BodyGeo, Stroke = G.B(dark), StrokeThickness = 2.4, StrokeLineJoin = PenLineJoin.Round };
            c.Body.Children.Add(c.BaseFill);
            c.Body.Children.Add(c.Liquid);
            c.Body.Children.Add(c.Surface);

            // glossy highlights
            var gloss = new Ellipse { Width = 20, Height = 9, Fill = G.B(Colors.White, 0.7), RenderTransform = new RotateTransform(-28, 10, 4.5) };
            G.Place(gloss, 11, 13);
            c.Body.Children.Add(gloss);
            c.Body.Children.Add(G.Oval(31, 8.5, 5, 4, G.B(Colors.White, 0.75)));
            c.Body.Children.Add(c.Outline);

            // ---------------- face
            c.BlushL = G.Oval(17, 43, 11, 5.5, G.B(Palette.Hex("#FF6F91"), 0.42));
            c.BlushR = G.Oval(63, 43, 11, 5.5, G.B(Palette.Hex("#FF6F91"), 0.42));
            c.Body.Children.Add(c.BlushL);
            c.Body.Children.Add(c.BlushR);

            c.EyeL = MakeEye(27, 33, out c.EyeLScale, out c.EyeLMove);
            c.EyeR = MakeEye(53, 33, out c.EyeRScale, out c.EyeRMove);
            c.Body.Children.Add(c.EyeL);
            c.Body.Children.Add(c.EyeR);

            c.HappyL = G.P("M 22,34.5 Q 27,28.5 32,34.5", null, G.B(Ink), 2.2);
            c.HappyR = G.P("M 48,34.5 Q 53,28.5 58,34.5", null, G.B(Ink), 2.2);
            c.SleepL = G.P("M 22,32 Q 27,37 32,32", null, G.B(Ink), 2.2);
            c.SleepR = G.P("M 48,32 Q 53,37 58,32", null, G.B(Ink), 2.2);
            c.BrowL = G.P("M 20.5,24.5 L 30,21.5", null, G.B(Ink), 1.9);
            c.BrowR = G.P("M 50,21.5 L 59.5,24.5", null, G.B(Ink), 1.9);
            c.Mouth = G.P("M 35.5,43 Q 40,47.5 44.5,43", null, G.B(Ink), 1.8);
            foreach (var e in new UIElement[] { c.HappyL, c.HappyR, c.SleepL, c.SleepR, c.BrowL, c.BrowR, c.Mouth })
                c.Body.Children.Add(e);

            c.Sweat = G.P("M 0,0 C 3,5 5,8 5,10.5 A 5,5 0 0 1 -5,10.5 C -5,8 -3,5 0,0 Z", G.Vertical(Palette.Hex("#DDF3FF"), Palette.Hex("#7CC8FF")), G.B(Palette.Hex("#3B82C4")), 1);
            G.Place(c.Sweat, 70, 14);
            c.Body.Children.Add(c.Sweat);

            // ---------------- effects (not squashed with the body)
            c.Fx = new Canvas { Width = CardW, Height = StageH, IsHitTestVisible = false };
            c.Stage.Children.Add(c.Fx);
            c.Question = G.T("?", 20, dark, FontWeights.Black, G.Num);
            G.Place(c.Question, CardW / 2 + 22, 4);
            c.Fx.Children.Add(c.Question);
            c.Zzz = new TextBlock[3];
            for (int i = 0; i < 3; i++)
            {
                c.Zzz[i] = G.T("z", 10 + i * 3, Palette.Hex("#6B7FD7"), FontWeights.Bold, G.Num);
                c.Fx.Children.Add(c.Zzz[i]);
            }
            c.Dots = new Ellipse[3];
            for (int i = 0; i < 3; i++)
            {
                c.Dots[i] = new Ellipse { Width = 6, Height = 6, Fill = G.B(dark) };
                c.Fx.Children.Add(c.Dots[i]);
            }
            c.Sparkles = new Path[4];
            for (int i = 0; i < 4; i++)
            {
                c.Sparkles[i] = new Path { Data = G.Star(new Point(0, 0), 5, 1.6, 4, 0), Fill = G.B(Palette.Hex("#FFD166")), Stroke = G.B(Palette.Hex("#E09F00")), StrokeThickness = 0.8, RenderTransform = new ScaleTransform(1, 1) };
                c.Fx.Children.Add(c.Sparkles[i]);
            }
            c.Panel.Children.Add(c.Stage);

            // ---------------- info plate
            c.Panel.Children.Add(MakePlate(c, v));

            // ---------------- speech bubble (lives in the overlay layer)
            c.BubbleText = new TextBlock { FontFamily = G.Ui, FontSize = 11, Foreground = G.B(Palette.Hex("#2B2B35")), TextWrapping = TextWrapping.Wrap, MaxWidth = 172 };
            c.Bubble = new Border
            {
                Child = c.BubbleText,
                Background = G.B(Color.FromArgb(0xF7, 0xFF, 0xFF, 0xFF)),
                BorderBrush = G.B(Palette.Lighten(color, 0.25)),
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(9, 5, 9, 6),
                Visibility = Visibility.Collapsed,
                Effect = G.Shadow(8, 1.5, 0.25, Colors.Black),
            };
            c.BubbleTail = new Path
            {
                Data = Geometry.Parse("M 0,0 L 12,0 L 4,9 Z"),
                Fill = G.B(Color.FromArgb(0xF7, 0xFF, 0xFF, 0xFF)),
                Stroke = G.B(Palette.Lighten(color, 0.25)),
                StrokeThickness = 1.4,
                Visibility = Visibility.Collapsed,
            };
            c.Level.Value = c.Level.Target = v.HasData ? v.Remaining : 0;
            return c;
        }

        static Canvas MakeEye(double cx, double cy, out ScaleTransform scale, out TranslateTransform move)
        {
            var eye = new Canvas();
            G.Place(eye, cx, cy);
            eye.Children.Add(G.Oval(0, 0, 8.6, 10.6, G.B(Ink)));
            eye.Children.Add(G.Oval(-1.6, -2.4, 3.2, 3.4, G.B(Colors.White)));
            eye.Children.Add(G.Oval(1.8, 2.2, 1.6, 1.6, G.B(Colors.White, 0.8)));
            scale = new ScaleTransform(1, 1);
            move = new TranslateTransform();
            var tg = new TransformGroup();
            tg.Children.Add(scale);
            tg.Children.Add(move);
            eye.RenderTransform = tg;
            return eye;
        }

        UIElement MakeAccessory(Card c, string mascot, Color color, Color dark)
        {
            var acc = new Canvas();
            var stroke = G.B(dark);
            switch (mascot)
            {
                case "sparkle":
                {
                    acc.Children.Add(G.P("M 40,2 Q 37,-6 40,-11", null, stroke, 2.2));
                    var star = new Path
                    {
                        Data = G.Star(new Point(0, 0), 10, 3.6, 8, 0),
                        Fill = G.Vertical(Palette.Lighten(color, 0.35), color),
                        Stroke = stroke,
                        StrokeThickness = 1.4,
                        StrokeLineJoin = PenLineJoin.Round,
                    };
                    c.AccRotate = new RotateTransform(0);
                    star.RenderTransform = c.AccRotate;
                    G.Place(star, 40, -19);
                    acc.Children.Add(star);
                    break;
                }
                case "prompt":
                {
                    acc.Children.Add(G.P("M 40,2 L 40,-6", null, stroke, 2.2));
                    var screen = new Border
                    {
                        Width = 24, Height = 16,
                        CornerRadius = new CornerRadius(4),
                        Background = G.Vertical(Palette.Hex("#2A2F45"), Palette.Hex("#171A26")),
                        BorderBrush = stroke,
                        BorderThickness = new Thickness(1.6),
                    };
                    G.Place(screen, 28, -21);
                    acc.Children.Add(screen);
                    var gt = G.T(">", 10, Palette.Hex("#6EF2A6"), FontWeights.Bold, G.Mono);
                    G.Place(gt, 32, -21.5);
                    acc.Children.Add(gt);
                    var cursor = new Rectangle { Width = 6, Height = 2, Fill = G.B(Palette.Hex("#6EF2A6")) };
                    G.Place(cursor, 40, -10.5);
                    acc.Children.Add(cursor);
                    c.AccBlink = cursor;
                    break;
                }
                case "goggles":
                {
                    acc.Children.Add(G.P("M 5,21 Q 40,4 75,21", null, G.B(Palette.Darken(color, 0.55)), 3.4));
                    var glass = G.Vertical(Palette.Hex("#E8F7FF"), Palette.Hex("#8EC9F5"));
                    acc.Children.Add(G.Circle(29, 12.5, 7.5, glass, stroke, 2));
                    acc.Children.Add(G.Circle(51, 12.5, 7.5, glass, stroke, 2));
                    acc.Children.Add(G.Oval(27, 10, 3.5, 2.5, G.B(Colors.White, 0.9)));
                    acc.Children.Add(G.Oval(49, 10, 3.5, 2.5, G.B(Colors.White, 0.9)));
                    break;
                }
                case "llama":
                {
                    var fill = G.B(Palette.Lighten(color, 0.8));
                    var l = new Ellipse { Width = 11, Height = 24, Fill = fill, Stroke = stroke, StrokeThickness = 2, RenderTransform = new RotateTransform(-18, 5.5, 20) };
                    var r = new Ellipse { Width = 11, Height = 24, Fill = fill, Stroke = stroke, StrokeThickness = 2, RenderTransform = new RotateTransform(18, 5.5, 20) };
                    G.Place(l, 17, -12);
                    G.Place(r, 52, -12);
                    acc.Children.Add(l);
                    acc.Children.Add(r);
                    break;
                }
                case "cat":
                {
                    var fill = G.B(Palette.Lighten(color, 0.8));
                    acc.Children.Add(G.P("M 9,22 L 12,-4 L 30,8 Z", fill, stroke, 2));
                    acc.Children.Add(G.P("M 71,22 L 68,-4 L 50,8 Z", fill, stroke, 2));
                    acc.Children.Add(G.P("M 14,14 L 15,3 L 23,9 Z", G.B(Palette.Hex("#FFB3C6")), null, 0));
                    acc.Children.Add(G.P("M 66,14 L 65,3 L 57,9 Z", G.B(Palette.Hex("#FFB3C6")), null, 0));
                    break;
                }
                default:
                {
                    acc.Children.Add(G.P("M 40,2 Q 44,-6 40,-12", null, stroke, 2.2));
                    var ball = G.Circle(40, -16, 5, G.Vertical(Palette.Lighten(color, 0.45), color), stroke, 1.6);
                    acc.Children.Add(ball);
                    c.AccGlow = ball;
                    break;
                }
            }
            return acc;
        }

        UIElement MakePlate(Card c, ProviderView v)
        {
            var plate = new Border
            {
                Width = CardW - 6,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 5, 9, 6),
                Background = G.Vertical(Color.FromArgb(0xF4, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xF0, 0xF6, 0xF6, 0xFA)),
                BorderBrush = G.B(Palette.Lighten(c.Color, 0.35)),
                BorderThickness = new Thickness(1.3),
                Effect = G.Shadow(10, 2, 0.22, Colors.Black),
                Margin = new Thickness(0, -6, 0, 0),
            };
            var sp = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            c.NameText = G.T(v.Name, 12, Palette.Hex("#2B2B35"), FontWeights.Bold, G.Ui);
            c.NameText.VerticalAlignment = VerticalAlignment.Center;
            c.PctText = G.T("", 14, Palette.Hex("#16A34A"), FontWeights.Bold, G.Num);
            Grid.SetColumn(c.PctText, 1);
            head.Children.Add(c.NameText);
            head.Children.Add(c.PctText);
            sp.Children.Add(head);

            if (v.HasData)
            {
                foreach (var m in v.Meters.Take(3))
                {
                    var r = new MeterRow { Key = m.Key };
                    var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                    var lbl = G.T(m.ShortLabel ?? "", 9, Palette.Hex("#6B7280"), FontWeights.SemiBold, G.Ui);
                    lbl.VerticalAlignment = VerticalAlignment.Center;
                    Border fill;
                    var bar = G.Bar(50, 6, G.B(Palette.Hex("#E6E8EE")), out fill);
                    fill.Background = G.Lg(Palette.Lighten(c.Color, 0.25), c.Color, 0);
                    bar.VerticalAlignment = VerticalAlignment.Center;
                    bar.HorizontalAlignment = HorizontalAlignment.Left;
                    Grid.SetColumn(bar, 1);
                    r.Fill = fill;
                    r.Pct = G.T("", 9, Palette.Hex("#4B5563"), FontWeights.SemiBold, G.Num);
                    r.Pct.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetColumn(r.Pct, 2);
                    g.Children.Add(lbl);
                    g.Children.Add(bar);
                    g.Children.Add(r.Pct);
                    sp.Children.Add(g);
                    r.Width.Value = r.Width.Target = 50 * m.Remaining / 100;
                    c.Rows.Add(r);
                }
                c.ResetText = G.T("", 9, Palette.Hex("#6B7280"), FontWeights.Normal, G.Ui);
                c.ResetText.Margin = new Thickness(0, 3, 0, 0);
                c.ResetText.TextTrimming = TextTrimming.CharacterEllipsis;
                sp.Children.Add(c.ResetText);
            }
            else
            {
                c.ErrorText = G.T(v.Error ?? "沒有資料", 9.5, Palette.Hex("#6B7280"), FontWeights.Normal, G.Ui);
                c.ErrorText.TextWrapping = TextWrapping.Wrap;
                c.ErrorText.Margin = new Thickness(0, 2, 0, 0);
                sp.Children.Add(c.ErrorText);
            }
            plate.Child = sp;
            return plate;
        }

        // ------------------------------------------------------------------ data → visuals

        static Color LevelInk(double remaining)
        {
            if (remaining >= 50) return Palette.Hex("#16A34A");
            if (remaining >= 20) return Palette.Hex("#D97706");
            return Palette.Hex("#DC2626");
        }

        protected override void Refresh()
        {
            var shown = Shown;
            foreach (var c in cards)
            {
                var v = shown.FirstOrDefault(x => x.Id == c.Id);
                if (v == null) continue;
                c.V = v;
                c.Level.Target = v.HasData ? (v.Unlimited ? 100 : v.Remaining) : 0;
                c.NameText.Text = v.Name;
                c.NameText.Foreground = G.B(v.Stale ? Palette.Hex("#8A8F9C") : Palette.Hex("#2B2B35"));
                if (!v.HasData)
                {
                    c.PctText.Text = "?";
                    c.PctText.Foreground = G.B(Palette.Hex("#9CA3AF"));
                    if (c.ErrorText != null) c.ErrorText.Text = v.Error ?? "沒有資料";
                    continue;
                }
                c.PctText.Text = v.Unlimited ? "∞" : (v.Primary != null && v.Primary.UsedApprox ? "≈" : "") + Fmt.Pct(v.Remaining);
                c.PctText.Foreground = G.B(v.Unlimited ? Palette.Hex("#7C3AED") : LevelInk(v.Remaining));
                foreach (var r in c.Rows)
                {
                    var m = v.Meters.FirstOrDefault(x => x.Key == r.Key);
                    if (m == null) continue;
                    r.Width.Target = 50 * m.Remaining / 100;
                    r.Pct.Text = m.Unlimited ? "∞" : (m.UsedApprox ? "≈" : "") + Fmt.Pct(m.Remaining);
                    r.Pct.Foreground = G.B(m.Unlimited ? Palette.Hex("#7C3AED") : LevelInk(m.Remaining));
                }
                if (c.ResetText != null)
                {
                    var p = v.Primary;
                    if (v.Unlimited) c.ResetText.Text = v.Snap != null && v.Snap.Note != null ? v.Snap.Note : "沒有額度限制";
                    else if (p != null && p.ResetsAt.HasValue) c.ResetText.Text = (v.Stale ? "舊資料 · ↻ " + G.ResetText(p) : "↻ " + G.ResetText(p) + " 後重置");
                    else c.ResetText.Text = p != null && p.Used <= 0 ? "閒置中，用了才開始計時" : "重置時間未知";
                }
            }
        }

        string ExpressionFor(Card c)
        {
            var v = c.V;
            if (c.SquintUntil > Time || c.CelebrateUntil > Time) return "squint";
            if (v == null || !v.HasData) return "unknown";
            switch (v.Mood)
            {
                case SentriPet.Mood.Great: return "happy";
                case SentriPet.Mood.Good: return "content";
                case SentriPet.Mood.Worried: return "worried";
                case SentriPet.Mood.Critical: return "critical";
                case SentriPet.Mood.Empty: return "sleep";
            }
            return "content";
        }

        static readonly Geometry MouthOpen = Frozen("M 34,42 Q 40,51.5 46,42 Q 40,44.5 34,42 Z");
        static readonly Geometry MouthSmile = Frozen("M 35.5,43 Q 40,47.5 44.5,43");
        static readonly Geometry MouthFlat = Frozen("M 36,45 L 44,45");
        static readonly Geometry MouthWavy = Frozen("M 34.5,45.5 Q 37,43 39.5,45.5 T 44.5,45.5");
        static readonly Geometry MouthO = Frozen("M 37.9,45 A 2.1,2.4 0 1 1 42.1,45 A 2.1,2.4 0 1 1 37.9,45 Z");

        void ApplyExpression(Card c, string expr)
        {
            c.Expr = expr;
            bool open = expr != "sleep" && expr != "squint";
            c.EyeL.Visibility = c.EyeR.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            c.HappyL.Visibility = c.HappyR.Visibility = expr == "squint" ? Visibility.Visible : Visibility.Collapsed;
            c.SleepL.Visibility = c.SleepR.Visibility = expr == "sleep" ? Visibility.Visible : Visibility.Collapsed;
            bool brows = expr == "worried" || expr == "critical";
            c.BrowL.Visibility = c.BrowR.Visibility = brows ? Visibility.Visible : Visibility.Collapsed;
            c.Sweat.Visibility = brows ? Visibility.Visible : Visibility.Collapsed;
            c.BlushL.Opacity = c.BlushR.Opacity = (expr == "happy" || expr == "squint") ? 1 : expr == "content" ? 0.55 : 0.2;
            c.Question.Visibility = expr == "unknown" ? Visibility.Visible : Visibility.Collapsed;
            foreach (var z in c.Zzz) z.Visibility = expr == "sleep" ? Visibility.Visible : Visibility.Collapsed;

            Geometry mouth;
            Brush fill = null;
            switch (expr)
            {
                case "happy":
                case "squint": mouth = MouthOpen; fill = G.B(Palette.Hex("#7A2B35")); break;
                case "content": mouth = MouthSmile; break;
                case "worried":
                case "critical": mouth = MouthWavy; break;
                case "sleep": mouth = MouthO; fill = G.B(Palette.Hex("#7A2B35")); break;
                default: mouth = MouthFlat; break;
            }
            c.Mouth.Data = mouth;
            c.Mouth.Fill = fill;
        }

        // ------------------------------------------------------------------ animation

        public override void Tick(double dt)
        {
            base.Tick(dt);
            chatCooldown -= dt;
            foreach (var c in cards) TickCard(c, dt);
            if (Host != null && Host.Settings.Chatty && Views.Count > 0)
            {
                nextChat -= dt;
                if (nextChat <= 0)
                {
                    nextChat = 420 + Rng.NextDouble() * 480;
                    var v = Views[Rng.Next(Views.Count)];
                    Say(v.Id, Lines.Idle(v, Views, Rng));
                }
            }
        }

        void TickCard(Card c, double dt)
        {
            var v = c.V;
            string expr = ExpressionFor(c);
            if (expr != c.Expr) ApplyExpression(c, expr);
            bool active = v != null && v.Active;
            bool sleeping = expr == "sleep";

            // ---- body motion
            c.Level.Step(dt, 2.5);
            double t = Time + c.Phase;
            double breathe = Math.Sin(t * (active ? 7.5 : 2.1));
            double sx = 1 - 0.025 * breathe, sy = 1 + 0.03 * breathe;
            if (sleeping) { sx = 1.04 + 0.02 * Math.Sin(t * 1.3); sy = 0.9 - 0.025 * Math.Sin(t * 1.3); }
            double jy = 0;
            if (c.JumpT >= 0)
            {
                c.JumpT += dt;
                double u = c.JumpT / 0.5;
                if (u >= 1) { c.JumpT = -1; c.LandT = 0; }
                else
                {
                    double s = Math.Sin(Math.PI * u);
                    jy = -20 * s;
                    sy *= 1 + 0.1 * s;
                    sx *= 1 - 0.06 * s;
                }
            }
            if (c.LandT >= 0)
            {
                c.LandT += dt;
                double u = c.LandT / 0.22;
                if (u >= 1) c.LandT = -1;
                else
                {
                    double s = Math.Sin(Math.PI * u);
                    sy *= 1 - 0.14 * s;
                    sx *= 1 + 0.1 * s;
                }
            }
            if (active && c.JumpT < 0 && c.LandT < 0) jy = -3.5 * Math.Abs(Math.Sin(t * 7));
            double tx = expr == "critical" ? Math.Sin(Time * 38) * 0.7 : 0;
            c.BodyScale.ScaleX = sx;
            c.BodyScale.ScaleY = sy;
            c.BodyMove.X = tx;
            c.BodyMove.Y = jy;
            double lift = Math.Min(1, -jy / 24);
            c.ShadowScale.ScaleX = c.ShadowScale.ScaleY = 1 - 0.35 * lift;
            c.Shadow.Opacity = 1 - 0.5 * lift;

            // idle hop
            if (!sleeping && v != null && v.HasData && (v.Mood == SentriPet.Mood.Great || v.Mood == SentriPet.Mood.Good))
            {
                c.NextHop -= dt;
                if (c.NextHop <= 0 && c.JumpT < 0) { c.JumpT = 0; c.NextHop = 18 + Rng.NextDouble() * 30; }
            }

            // ---- jelly
            double amp = c.Level.Value <= 0.5 ? 0 : (active ? 2.6 : 1.5);
            Geometry fill, surf;
            Wave(c.Level.Value, Time, c.Phase, amp, out fill, out surf);
            c.Liquid.Data = fill;
            c.Surface.Data = surf;
            foreach (var r in c.Rows)
            {
                r.Width.Step(dt, 4);
                double w = Math.Max(0, Math.Min(50, r.Width.Value));
                r.Fill.Width = r.Width.Target > 0.25 ? Math.Max(5, w) : w;
            }

            // ---- eyes
            c.NextBlink -= dt;
            if (c.NextBlink <= 0) { c.BlinkT = 0; c.NextBlink = 2.2 + Rng.NextDouble() * 4.5; }
            double open = 1;
            if (c.BlinkT >= 0)
            {
                c.BlinkT += dt;
                double u = c.BlinkT / 0.16;
                if (u >= 1) c.BlinkT = -1;
                else open = 0.1 + 0.9 * Math.Abs(1 - 2 * u);
            }
            if (expr == "critical") open *= 0.55;
            c.EyeLScale.ScaleY = c.EyeRScale.ScaleY = open;

            Point? cur = Host != null ? Host.CursorIn(c.Body) : null;
            double lx, ly;
            if (cur.HasValue)
            {
                double dx = cur.Value.X - 40, dy = cur.Value.Y - 33;
                double dist = Math.Sqrt(dx * dx + dy * dy) + 0.001;
                double k = Math.Min(1, dist / 60);
                lx = dx / dist * 2.8 * k;
                ly = dy / dist * 2.0 * k;
                if (dist > 900) { lx = Math.Sin(t * 0.7) * 2.2; ly = Math.Sin(t * 0.43) * 1.2; }
            }
            else { lx = Math.Sin(t * 0.7) * 2.2; ly = Math.Sin(t * 0.43) * 1.2; }
            if (active) ly = 1.6;
            c.LookX.Target = lx;
            c.LookY.Target = ly;
            c.LookX.Step(dt, 9);
            c.LookY.Step(dt, 9);
            c.EyeLMove.X = c.EyeRMove.X = c.LookX.Value;
            c.EyeLMove.Y = c.EyeRMove.Y = c.LookY.Value;

            // ---- accessory
            if (c.AccRotate != null) c.AccRotate.Angle = (Time * (active ? 160 : 25)) % 360;
            if (c.AccBlink != null) c.AccBlink.Opacity = ((int)(Time * 2.2) % 2 == 0) ? 1 : 0;
            if (c.AccGlow != null) c.AccGlow.Opacity = active ? 0.6 + 0.4 * Math.Sin(Time * 9) : 1;

            // ---- effects
            double headX = CardW / 2, headY = StageH - 10 - BodyH + jy;
            if (c.Sweat.Visibility == Visibility.Visible)
            {
                double u = ((Time + c.Phase) % 1.8) / 1.8;
                Canvas.SetTop(c.Sweat, 12 + u * 12);
                c.Sweat.Opacity = u < 0.8 ? 1 : (1 - u) * 5;
            }
            if (sleeping)
            {
                for (int i = 0; i < 3; i++)
                {
                    double u = ((Time * 0.45 + i / 3.0) % 1.0);
                    G.Place(c.Zzz[i], headX + 20 + u * 22 + Math.Sin(u * 6) * 3, headY + 6 - u * 30);
                    c.Zzz[i].Opacity = Math.Sin(u * Math.PI);
                }
            }
            bool dots = active && !sleeping;
            for (int i = 0; i < 3; i++)
            {
                c.Dots[i].Visibility = dots ? Visibility.Visible : Visibility.Collapsed;
                if (dots)
                {
                    double b = Math.Max(0, Math.Sin(Time * 8 - i * 0.9));
                    G.Place(c.Dots[i], headX + 26 + i * 9, headY - 6 - b * 5);
                }
            }
            if (c.Question.Visibility == Visibility.Visible)
                Canvas.SetTop(c.Question, headY - 18 + Math.Sin(Time * 3) * 3);

            bool sparkle = c.CelebrateUntil > Time || (expr == "happy" && v != null && v.Remaining >= 90);
            for (int i = 0; i < c.Sparkles.Length; i++)
            {
                var sp = c.Sparkles[i];
                if (!sparkle) { sp.Visibility = Visibility.Collapsed; continue; }
                sp.Visibility = Visibility.Visible;
                double speed = c.CelebrateUntil > Time ? 3 : 0.9;
                double u = ((Time * speed * 0.5 + i * 0.27 + c.Phase) % 1.0);
                double ang = i * 90 + 45 + Time * (c.CelebrateUntil > Time ? 120 : 15);
                double rad = 42 + (c.CelebrateUntil > Time ? u * 16 : 4 * Math.Sin(u * 6.28));
                var p = G.Polar(new Point(headX, headY + 34), rad, ang);
                G.Place(sp, p.X, p.Y);
                double sc = Math.Sin(u * Math.PI);
                ((ScaleTransform)sp.RenderTransform).ScaleX = ((ScaleTransform)sp.RenderTransform).ScaleY = 0.4 + sc * 0.8;
                sp.Opacity = sc;
            }

            // ---- speech bubble
            TickBubble(c, dt);
        }

        static void Wave(double level, double time, double phase, double amp, out Geometry fill, out Geometry surface)
        {
            double y0 = BodyH - (Math.Max(0, Math.Min(100, level)) / 100.0) * (BodyH - 3);
            var f = new StreamGeometry();
            var s = new StreamGeometry();
            using (var fc = f.Open())
            using (var sc = s.Open())
            {
                fc.BeginFigure(new Point(-2, BodyH + 2), true, true);
                const int n = 22;
                for (int i = 0; i <= n; i++)
                {
                    double x = -2 + (BodyW + 4) * i / (double)n;
                    double y = y0 + amp * Math.Sin(x / 10.5 + time * 2.6 + phase) + amp * 0.45 * Math.Sin(x / 5.7 - time * 1.9);
                    var pt = new Point(x, y);
                    fc.LineTo(pt, true, true);
                    if (i == 0) sc.BeginFigure(pt, false, false);
                    else sc.LineTo(pt, true, true);
                }
                fc.LineTo(new Point(BodyW + 2, BodyH + 2), true, true);
            }
            f.Freeze();
            s.Freeze();
            fill = f;
            surface = s;
        }

        void TickBubble(Card c, double dt)
        {
            bool want = c.BubbleUntil > Time;
            c.BubbleAlpha = Math.Max(0, Math.Min(1, c.BubbleAlpha + (want ? dt * 6 : -dt * 4)));
            if (c.BubbleAlpha <= 0)
            {
                if (c.Bubble.Visibility != Visibility.Collapsed)
                {
                    c.Bubble.Visibility = Visibility.Collapsed;
                    c.BubbleTail.Visibility = Visibility.Collapsed;
                }
                return;
            }
            c.Bubble.Visibility = Visibility.Visible;
            c.BubbleTail.Visibility = Visibility.Visible;
            c.Bubble.Opacity = c.BubbleTail.Opacity = c.BubbleAlpha;
            c.Bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = c.Bubble.DesiredSize;
            Point origin;
            try { origin = c.Panel.TranslatePoint(new Point(CardW / 2, 0), bubbleLayer); }
            catch { return; }
            double layerW = Math.Max(bubbleLayer.ActualWidth, size.Width);
            double left = Math.Max(0, Math.Min(layerW - size.Width, origin.X - size.Width / 2));
            double rise = (1 - c.BubbleAlpha) * 6;
            double top = origin.Y + 10 - size.Height + rise;
            G.Place(c.Bubble, left, top);
            G.Place(c.BubbleTail, origin.X - 4, top + size.Height - 1.6);
        }

        // ------------------------------------------------------------------ interactions

        public override void Say(string providerId, string text)
        {
            if (string.IsNullOrEmpty(text) || cards.Count == 0) return;
            var c = cards.FirstOrDefault(x => x.Id == providerId) ?? cards[0];
            foreach (var o in cards) if (o != c) o.BubbleUntil = Math.Min(o.BubbleUntil, Time);
            c.BubbleText.Text = text;
            c.BubbleUntil = Time + 3.5 + Math.Min(6, text.Length * 0.12);
        }

        public override void Poke(string providerId)
        {
            var c = cards.FirstOrDefault(x => x.Id == providerId);
            if (c == null) return;
            if (c.JumpT < 0) c.JumpT = 0;
            c.SquintUntil = Time + 1.1;
            if (c.V != null) Say(c.Id, Lines.Poke(c.V, Rng));
        }

        public override void Celebrate(string providerId)
        {
            var c = cards.FirstOrDefault(x => x.Id == providerId);
            if (c == null) return;
            c.JumpT = 0;
            c.CelebrateUntil = Time + 3;
        }
    }
}
