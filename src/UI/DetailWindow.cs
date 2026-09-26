using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentriPet
{
    /// <summary>Where the hover card goes: always outside the widget's content, pointing at the hovered provider.</summary>
    static class DetailPlacement
    {
        public enum Side { Above, Below, Left, Right }

        public struct Result
        {
            public int X, Y;          // card window top-left (px)
            public Side Side;
            public double PointerX;   // px from the card window's left edge (Above/Below only)
        }

        /// <summary>
        /// All rectangles are in physical pixels. <paramref name="gap"/> is the distance between the content and
        /// the card window (negative when the window's transparent margin may overlap the content).
        /// </summary>
        public static Result Compute(Rect content, Rect provider, Rect work, double w, double h, double gap)
        {
            var r = new Result();
            double cx = provider.Left + provider.Width / 2;
            double cy = provider.Top + provider.Height / 2;
            double x = Clamp(cx - w / 2, work.Left, work.Right - w);
            if (content.Top - gap - h >= work.Top)
            {
                r.Side = Side.Above;
                r.X = (int)Math.Round(x);
                r.Y = (int)Math.Round(content.Top - gap - h);
            }
            else if (content.Bottom + gap + h <= work.Bottom)
            {
                r.Side = Side.Below;
                r.X = (int)Math.Round(x);
                r.Y = (int)Math.Round(content.Bottom + gap);
            }
            else
            {
                double spaceLeft = content.Left - work.Left, spaceRight = work.Right - content.Right;
                if (spaceRight >= w + gap || spaceRight >= spaceLeft)
                {
                    r.Side = Side.Right;
                    r.X = (int)Math.Round(Math.Min(content.Right + gap, work.Right - w));
                }
                else
                {
                    r.Side = Side.Left;
                    r.X = (int)Math.Round(Math.Max(content.Left - gap - w, work.Left));
                }
                r.Y = (int)Math.Round(Clamp(cy - h / 2, work.Top, work.Bottom - h));
            }
            r.PointerX = Clamp(cx - r.X, 0, w);
            return r;
        }

        static double Clamp(double v, double lo, double hi)
        {
            if (hi < lo) return lo;
            return Math.Max(lo, Math.Min(hi, v));
        }
    }

    /// <summary>The hover card content. Built once per provider layout, then updated in place.</summary>
    class DetailCardView
    {
        public const double Margin = 14;      // transparent space around the card (room for the shadow)
        const double ContentWidth = 292;
        const double ArrowW = 16, ArrowH = 9;
        static readonly Color Text = Palette.Hex("#F2F4F8");
        static readonly Color Sub = Palette.Hex("#9AA3B2");
        static readonly Color CardBg = Color.FromArgb(0xF6, 0x1C, 0x1E, 0x27);
        static readonly Color Edge = Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF);

        class MeterUi
        {
            public string Key;
            public TextBlock Pct, Info;
            public Border Fill;
        }

        public FrameworkElement Root { get; private set; }
        string signature;
        Canvas arrowUp, arrowDown;
        Border busy, useIt, useItIcon;
        TextBlock error, status, note, useItText;
        int useItLevel;
        readonly List<MeterUi> meters = new List<MeterUi>();

        public static string SignatureOf(ProviderView v)
        {
            return v.Id + "|" + v.HasData + "|" + (v.Plan ?? "") + "|" +
                   string.Join(",", v.Meters.Select(m => m.Key + (m.Unlimited ? "*" : "")));
        }

        public bool Matches(ProviderView v) { return signature == SignatureOf(v); }

        /// <summary>For the self-test: the "use it before it resets" banner.</summary>
        internal bool BannerShown { get { return useIt.Visibility == Visibility.Visible; } }
        internal string BannerText { get { return useItText.Text; } }

        public static DetailCardView Build(ProviderView v)
        {
            var d = new DetailCardView { signature = SignatureOf(v) };
            var accent = G.Vivid(v.Color);
            var sp = new StackPanel { Width = ContentWidth };

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = G.B(accent), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            name.Children.Add(G.T(v.Name, 14.5, Text, FontWeights.Bold, G.Ui));
            d.busy = G.Pill(G.T(L.T("工作中"), 9.5, Palette.Hex("#93C5FD"), FontWeights.SemiBold, G.Ui), G.B(Palette.Hex("#3B82F6"), 0.2), 6, new Thickness(6, 1, 6, 1));
            d.busy.Margin = new Thickness(8, 1, 0, 0);
            d.busy.VerticalAlignment = VerticalAlignment.Center;
            name.Children.Add(d.busy);
            head.Children.Add(name);
            if (!string.IsNullOrEmpty(v.Plan))
            {
                var plan = G.Pill(G.T(v.Plan, 10.5, Palette.Lighten(accent, 0.35), FontWeights.SemiBold, G.Ui), G.B(accent, 0.18), 7, new Thickness(8, 2, 8, 2));
                plan.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(plan, 1);
                head.Children.Add(plan);
            }
            sp.Children.Add(head);

            // "use it before it resets" banner (shown by Update when a weekly/monthly window is about to expire unused)
            var ug = new Grid();
            ug.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ug.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            d.useItIcon = new Border { Width = 20, Height = 20, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            d.useItText = G.T("", 11.5, Text, FontWeights.SemiBold, G.Ui);
            d.useItText.TextWrapping = TextWrapping.Wrap;
            d.useItText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(d.useItText, 1);
            ug.Children.Add(d.useItIcon);
            ug.Children.Add(d.useItText);
            d.useIt = new Border
            {
                Child = ug,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 10, 7),
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            sp.Children.Add(d.useIt);

            if (!v.HasData)
            {
                d.error = G.T("", 12, Palette.Hex("#FCA5A5"));
                d.error.TextWrapping = TextWrapping.Wrap;
                d.error.Margin = new Thickness(0, 8, 0, 0);
                sp.Children.Add(d.error);
            }
            foreach (var m in v.Meters)
            {
                var ui = new MeterUi { Key = m.Key };
                var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(G.T(m.Label, 12.5, Text, FontWeights.SemiBold, G.Ui));
                ui.Pct = G.T("", 12.5, Text, FontWeights.Bold, G.Ui);
                Grid.SetColumn(ui.Pct, 1);
                g.Children.Add(ui.Pct);
                sp.Children.Add(g);
                if (!m.Unlimited)
                {
                    Border fill;
                    var bar = G.Bar(ContentWidth, 6, G.B(Colors.White, 0.1), out fill);
                    bar.Margin = new Thickness(0, 5, 0, 0);
                    fill.Background = G.Lg(Palette.Lighten(accent, 0.3), accent, 0);
                    ui.Fill = fill;
                    sp.Children.Add(bar);
                }
                ui.Info = G.T("", 11, Sub);
                ui.Info.TextWrapping = TextWrapping.Wrap;
                ui.Info.Margin = new Thickness(0, 4, 0, 0);
                sp.Children.Add(ui.Info);
                d.meters.Add(ui);
            }
            sp.Children.Add(new Border { Height = 1, Background = G.B(Colors.White, 0.1), Margin = new Thickness(0, 10, 0, 8) });
            d.status = Wrap(Sub);
            d.note = Wrap(Sub);
            sp.Children.Add(d.status);
            sp.Children.Add(d.note);
            var hint = Wrap(Palette.Hex("#6B7280"));
            hint.Text = L.T("點一下互動 · 拖曳移動 · 右鍵選單 · 雙擊開設定");
            hint.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(hint);

            var card = new Border
            {
                Child = sp,
                Padding = new Thickness(15, 12, 15, 12),
                CornerRadius = new CornerRadius(14),
                Background = G.B(CardBg),
                BorderBrush = G.B(Edge),
                BorderThickness = new Thickness(1),
            };
            d.arrowUp = Arrow(true);
            d.arrowDown = Arrow(false);
            var column = new StackPanel { Margin = new Thickness(Margin), Effect = G.Shadow(18, 4, 0.45, Colors.Black) };
            column.Children.Add(d.arrowUp);
            column.Children.Add(card);
            column.Children.Add(d.arrowDown);
            TextOptions.SetTextFormattingMode(column, TextFormattingMode.Display);
            d.Root = column;
            d.Update(v);
            d.SetPointer(DetailPlacement.Side.Above, Margin + 40);
            return d;
        }

        /// <summary>A small triangle that sits on the card edge (overlapping the border by one pixel).</summary>
        static Canvas Arrow(bool up)
        {
            var c = new Canvas { Height = ArrowH, HorizontalAlignment = HorizontalAlignment.Stretch };
            var fill = new Path
            {
                Data = Geometry.Parse(up ? "M 0,9.8 L 8,0 L 16,9.8 Z" : "M 0,-0.8 L 16,-0.8 L 8,9 Z"),
                Fill = G.B(CardBg),
            };
            var edge = new Path
            {
                Data = Geometry.Parse(up ? "M 0,9 L 8,0 L 16,9" : "M 0,0 L 8,9 L 16,0"),
                Stroke = G.B(Edge),
                StrokeThickness = 1,
            };
            c.Children.Add(fill);
            c.Children.Add(edge);
            return c;
        }

        static TextBlock Wrap(Color c)
        {
            var t = G.T("", 11, c);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 2, 0, 0);
            return t;
        }

        public void Update(ProviderView v)
        {
            busy.Visibility = v.Active ? Visibility.Visible : Visibility.Collapsed;
            if (error != null) error.Text = v.Error ?? L.T("沒有資料");
            int level = v.HasData && v.UseIt != null ? v.UseItLevel : 0;
            useIt.Visibility = level > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (level > 0)
            {
                useItText.Text = Lines.UseItAlert(v);
                if (level != useItLevel)
                {
                    var accent = G.UseItAccent(level);
                    useIt.Background = G.B(accent, 0.15);
                    useIt.BorderBrush = G.B(accent, 0.55);
                    useItText.Foreground = G.B(Palette.Lighten(accent, 0.5));
                    useItIcon.Child = G.AlarmClock(20, accent);
                }
            }
            useItLevel = level;
            foreach (var ui in meters)
            {
                var m = v.Meters.FirstOrDefault(x => x.Key == ui.Key);
                if (m == null) continue;
                ui.Pct.Text = m.Unlimited ? L.T("無限制") : L.F("剩 {0}", (m.UsedApprox ? "≈" : "") + Fmt.Pct(m.Remaining));
                ui.Pct.Foreground = G.B(m.Unlimited ? Palette.Hex("#C4B5FD") : Palette.Level(m.Remaining));
                if (ui.Fill != null)
                    ui.Fill.Width = m.Remaining > 0.5 ? Math.Max(6, ContentWidth * m.Remaining / 100) : 0;
                string info;
                if (m.Unlimited) info = m.ValueText ?? "";
                else if (m.ResetsAt.HasValue)
                    info = "↻ " + L.F("{0} 重置（{1}後）", Fmt.When(m.ResetsAt), Fmt.Countdown(m.ResetsAt)) + (m.ResetApprox ? " · " + L.T("推算值") : "");
                else info = m.Used <= 0 ? L.T("閒置中：下次使用時才開始計時") : L.T("重置時間未知");
                if (m.ValueText != null && !m.Unlimited) info = m.ValueText + " · " + info;
                if (m.WasReset) info += " · " + L.T("已自動重置");
                ui.Info.Text = info;
            }
            status.Text = v.HasData ? (v.StatusText ?? "") : "";
            status.Visibility = string.IsNullOrEmpty(status.Text) ? Visibility.Collapsed : Visibility.Visible;
            note.Text = v.Snap != null && !string.IsNullOrEmpty(v.Snap.Note) ? v.Snap.Note : "";
            note.Visibility = string.IsNullOrEmpty(note.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Shows the arrow on the edge facing the widget. <paramref name="x"/> is in DIPs from the window's left.</summary>
        public void SetPointer(DetailPlacement.Side side, double x)
        {
            // both arrow rows always keep their height, so the card never changes size when it flips sides
            arrowUp.Visibility = side == DetailPlacement.Side.Below ? Visibility.Visible : Visibility.Hidden;
            arrowDown.Visibility = side == DetailPlacement.Side.Above ? Visibility.Visible : Visibility.Hidden;
            double cardW = ContentWidth + 32;
            double left = Math.Max(18, Math.Min(cardW - 18 - ArrowW, x - Margin - ArrowW / 2));
            foreach (Path p in arrowUp.Children) Canvas.SetLeft(p, left);
            foreach (Path p in arrowDown.Children) Canvas.SetLeft(p, left);
        }
    }

    /// <summary>
    /// A window that floats next to the widget: owned by it (so Windows always keeps it above the widget),
    /// click-through and never activated (so it can not steal the mouse from the widget).
    /// </summary>
    class OverlayWindow : Window
    {
        IntPtr hwnd;

        public OverlayWindow(Window owner)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;
            FontFamily = G.Ui;
            Title = App.DisplayName;
            Owner = owner;
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Native.SetExStyle(hwnd, Native.GetExStyle(hwnd) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
            };
            new WindowInteropHelper(this).EnsureHandle();
        }

        public IntPtr Handle { get { return hwnd; } }

        /// <summary>Size the window will take for its current content, in physical pixels.</summary>
        public Size MeasurePx(double dpiX, double dpiY)
        {
            var root = Content as FrameworkElement;
            if (root == null) return new Size(0, 0);
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = root.DesiredSize;
            return new Size(Math.Ceiling(d.Width * dpiX), Math.Ceiling(d.Height * dpiY));
        }

        public void MoveTo(int x, int y)
        {
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | 0x4 | Native.SWP_NOACTIVATE);
        }
    }

    /// <summary>The hover card as its own window.</summary>
    class DetailWindow : OverlayWindow
    {
        DetailCardView view;

        public DetailWindow(Window owner) : base(owner) { }

        public void SetView(ProviderView v)
        {
            if (view == null || !view.Matches(v))
            {
                view = DetailCardView.Build(v);
                Content = view.Root;
            }
            else view.Update(v);
        }

        public void SetPointer(DetailPlacement.Side side, double xDip)
        {
            if (view != null) view.SetPointer(side, xDip);
        }
    }

    /// <summary>
    /// A floating speech bubble next to the widget, for themes that have no speech of their own
    /// (reminders, pokes, greetings).
    /// </summary>
    class SpeechWindow : OverlayWindow
    {
        public const double Pad = 10;                 // transparent room around the bubble (for the shadow)
        const double TailW = 14, TailH = 8, MaxText = 230;
        static readonly Color Paper = Color.FromArgb(0xF8, 0xFF, 0xFF, 0xFF);
        readonly TextBlock text;
        readonly Border bubble;
        readonly Canvas tailUp, tailDown;
        readonly StackPanel column;

        public string ProviderId { get; private set; }
        public DateTime Until { get; private set; }

        public SpeechWindow(Window owner) : base(owner)
        {
            text = new TextBlock { FontFamily = G.Ui, FontSize = 12, Foreground = G.B(Palette.Hex("#2B2B35")), TextWrapping = TextWrapping.Wrap, MaxWidth = MaxText };
            bubble = new Border { Child = text, Background = G.B(Paper), BorderThickness = new Thickness(1.4), CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 7, 11, 8) };
            tailUp = Tail(true);
            tailDown = Tail(false);
            column = new StackPanel { Margin = new Thickness(Pad), Effect = G.Shadow(10, 2, 0.28, Colors.Black) };
            column.Children.Add(tailUp);
            column.Children.Add(bubble);
            column.Children.Add(tailDown);
            TextOptions.SetTextFormattingMode(column, TextFormattingMode.Display);
            Content = column;
        }

        static Canvas Tail(bool up)
        {
            var c = new Canvas { Height = TailH, HorizontalAlignment = HorizontalAlignment.Stretch };
            c.Children.Add(new Path { Data = Geometry.Parse(up ? "M 0,8.9 L 7,0 L 14,8.9 Z" : "M 0,-0.9 L 14,-0.9 L 7,8 Z"), Fill = G.B(Paper) });
            c.Children.Add(new Path { Data = Geometry.Parse(up ? "M 0,8 L 7,0 L 14,8" : "M 0,0 L 7,8 L 14,0"), StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round });
            return c;
        }

        public void SetText(string providerId, string s, Color accent, double seconds)
        {
            ProviderId = providerId;
            text.Text = s;
            var edge = G.B(Palette.Lighten(accent, 0.2));
            bubble.BorderBrush = edge;
            ((Path)tailUp.Children[1]).Stroke = edge;
            ((Path)tailDown.Children[1]).Stroke = edge;
            Until = DateTime.UtcNow.AddSeconds(seconds);
        }

        /// <summary>Points the tail at the speaker. <paramref name="x"/> is in DIPs from the window's left.</summary>
        public void SetPointer(DetailPlacement.Side side, double x)
        {
            tailUp.Visibility = side == DetailPlacement.Side.Below ? Visibility.Visible : Visibility.Hidden;
            tailDown.Visibility = side == DetailPlacement.Side.Above ? Visibility.Visible : Visibility.Hidden;
            column.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = column.DesiredSize.Width - 2 * Pad;
            double left = Math.Max(12, Math.Min(w - 12 - TailW, x - Pad - TailW / 2));
            foreach (Path p in tailUp.Children) Canvas.SetLeft(p, left);
            foreach (Path p in tailDown.Children) Canvas.SetLeft(p, left);
        }
    }
}
