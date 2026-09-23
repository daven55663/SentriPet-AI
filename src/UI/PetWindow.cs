using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SentriPet
{
    /// <summary>
    /// The transparent, borderless widget. Drag anywhere to move (across monitors), click to poke,
    /// hover for details, right-click for the menu, double-click for settings.
    /// </summary>
    class PetWindow : Window, IThemeHost
    {
        readonly Controller ctl;
        readonly AppSettings settings;
        readonly Random rng = new Random();
        readonly Grid host;
        readonly ScaleTransform zoom;
        readonly DispatcherTimer frame;
        Theme theme;
        DateTime lastFrame = DateTime.UtcNow;
        IntPtr hwnd;
        bool positioned, dragging, userHidden, fullscreenHidden;

        // floating speech bubble (themes without speech of their own)
        SpeechWindow speech;

        // hover card
        DetailWindow card;
        string hoverId, shownId, forcedId;
        DateTime hoverSince, lastInside, lastScan = DateTime.MinValue, forcedUntil, quietUntil;
        readonly List<KeyValuePair<string, FrameworkElement>> tagged = new List<KeyValuePair<string, FrameworkElement>>();
        const double ShowDelayMs = 300, SwitchDelayMs = 150, HideGraceMs = 400;

        public Theme Theme { get { return theme; } }
        public AppSettings Settings { get { return settings; } }
        public Random Rng { get { return rng; } }
        public IntPtr Handle { get { return hwnd; } }

        public PetWindow(Controller ctl, AppSettings settings)
        {
            this.ctl = ctl;
            this.settings = settings;
            Title = App.DisplayName;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;
            FontFamily = G.Ui;

            zoom = new ScaleTransform(settings.Scale, settings.Scale);
            host = new Grid { LayoutTransform = zoom };
            Content = host;

            frame = new DispatcherTimer(DispatcherPriority.Render);
            frame.Tick += OnFrame;

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                int ex = Native.GetExStyle(hwnd) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                Native.SetExStyle(hwnd, ex);
                ApplySettings();
            };
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(PlaceInitially), DispatcherPriority.Background);
            SizeChanged += (s, e) => { if (positioned && !dragging) PlaceAtAnchor(); };
            MouseLeftButtonDown += OnLeftDown;
            MouseRightButtonUp += (s, e) => { HideDetail(); ctl.ShowMenu(); e.Handled = true; };
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) { lastFrame = DateTime.UtcNow; frame.Start(); }
                else { frame.Stop(); HideDetail(); HideSpeech(); }
            };
        }

        // ------------------------------------------------------------------ theme & settings

        public void SetTheme(Theme t)
        {
            HideDetail();
            HideSpeech();
            hoverId = null;
            lastScan = DateTime.MinValue;
            if (theme != null)
            {
                try { theme.Detach(); } catch { }
                host.Children.Clear();
            }
            theme = t;
            t.Attach(this);
            host.Children.Add(t.Root);
            t.Update(ctl.Views);
        }

        public void ApplySettings()
        {
            zoom.ScaleX = zoom.ScaleY = settings.Scale;
            Opacity = settings.Opacity;
            Topmost = settings.AlwaysOnTop;
            frame.Interval = TimeSpan.FromMilliseconds(settings.LowPower ? 250 : 33);
            if (hwnd != IntPtr.Zero)
            {
                int ex = Native.GetExStyle(hwnd);
                ex = settings.ClickThrough ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
                Native.SetExStyle(hwnd, ex);
            }
        }

        public void SaveSettings() { settings.Save(); }

        public Point? CursorIn(FrameworkElement element)
        {
            try
            {
                Native.POINT p;
                if (!Native.GetCursorPos(out p) || PresentationSource.FromVisual(element) == null) return null;
                return element.PointFromScreen(new Point(p.X, p.Y));
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ visibility

        public bool UserHidden { get { return userHidden; } }

        public void ShowWidget()
        {
            userHidden = false;
            UpdateVisibility();
        }

        public void HideWidget()
        {
            userHidden = true;
            UpdateVisibility();
        }

        void UpdateVisibility()
        {
            bool show = !userHidden && !fullscreenHidden;
            if (show && !IsVisible) Show();
            else if (!show && IsVisible) Hide();
        }

        bool paused;

        /// <summary>No animation while the session is locked (nobody is looking).</summary>
        public void SetPaused(bool paused)
        {
            this.paused = paused;
            if (paused) { frame.Stop(); HideDetail(); HideSpeech(); }
            else if (IsVisible) { lastFrame = DateTime.UtcNow; frame.Start(); }
        }

        /// <summary>True when a line said now would be seen (the widget is on screen and the session unlocked).</summary>
        public bool CanTalk { get { return IsVisible && !paused && theme != null; } }

        int ticks;

        /// <summary>Called once a second by the controller.</summary>
        public void Periodic(bool menuOpen)
        {
            if (hwnd == IntPtr.Zero) return;
            bool fs = settings.HideOnFullscreen && Native.ForegroundIsFullscreen(hwnd);
            if (fs != fullscreenHidden)
            {
                fullscreenHidden = fs;
                UpdateVisibility();
            }
            // other always-on-top windows can push us down; step back up now and then (never over our own menu)
            if (++ticks % 15 == 0 && IsVisible && settings.AlwaysOnTop && !menuOpen)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (shownId != null)
            {
                // update the numbers in place and follow the widget if it moved or resized
                var v = ctl.Views.FirstOrDefault(x => x.Id == shownId);
                if (v == null) HideDetail();
                else
                {
                    card.SetView(v);
                    PlaceDetail();
                }
            }
            if (speech != null && speech.IsVisible) PlaceSpeech();
        }

        // ------------------------------------------------------------------ animation

        void OnFrame(object sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double dt = Math.Max(0, Math.Min(0.25, (now - lastFrame).TotalSeconds));
            lastFrame = now;
            if (theme != null)
            {
                try { theme.Tick(dt); }
                catch (Exception ex) { Log.Error("theme tick", ex); }
            }
            try { PollHover(now); }
            catch (Exception ex) { Log.Error("hover", ex); }
            if (speech != null && speech.IsVisible && now >= speech.Until) HideSpeech();
        }

        // ------------------------------------------------------------------ mouse

        string HitProvider(Point hostPoint, out FrameworkElement element)
        {
            element = null;
            var hit = host.InputHitTest(hostPoint) as DependencyObject;
            while (hit != null && hit != host)
            {
                var fe = hit as FrameworkElement;
                var tag = fe != null ? fe.Tag as string : null;
                if (tag != null && tag.StartsWith("pv:"))
                {
                    element = fe;
                    return tag.Substring(3);
                }
                hit = VisualTreeHelper.GetParent(hit) ?? LogicalTreeHelper.GetParent(hit);
            }
            return null;
        }

        void OnLeftDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.ClickCount >= 2)
            {
                ctl.OpenSettings();
                return;
            }
            HideDetail();
            HideSpeech();
            hoverId = null;
            var before = new Point(Left, Top);
            var hostPoint = e.GetPosition(host);
            var rootPoint = theme != null && theme.Root != null ? e.GetPosition(theme.Root) : new Point();
            dragging = true;
            try { DragMove(); }
            catch (InvalidOperationException) { }
            finally { dragging = false; }
            if (Math.Abs(Left - before.X) < 3 && Math.Abs(Top - before.Y) < 3)
            {
                // let the pet's reply bubble be seen before the card comes back
                quietUntil = DateTime.UtcNow.AddSeconds(3.5);
                if (theme != null && theme.Click(rootPoint)) return;
                FrameworkElement el;
                var id = HitProvider(hostPoint, out el);
                if (id != null && theme != null && !theme.Poke(id))
                {
                    // themes without a reaction of their own answer in a floating bubble
                    var v = ctl.Views.FirstOrDefault(x => x.Id == id);
                    if (v != null) ShowSpeech(id, Lines.Poke(v, rng));
                }
            }
            else AfterMove();
        }

        // ------------------------------------------------------------------ speech

        /// <summary>
        /// Says a line through the theme (bubble, dialog box, terminal comment…) or, when the theme has no speech of
        /// its own, in a floating bubble next to the widget.
        /// </summary>
        public void Say(string providerId, string text)
        {
            if (theme == null || string.IsNullOrEmpty(text)) return;
            bool handled = true;
            try { handled = theme.Say(providerId, text); }
            catch (Exception ex) { Log.Error("say", ex); }
            if (!handled) ShowSpeech(providerId, text);
            // the hover card would cover the line: step aside for a moment
            HideDetail();
            quietUntil = DateTime.UtcNow.AddSeconds(4);
        }

        void ShowSpeech(string providerId, string text)
        {
            if (!CanTalk || hwnd == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
            var v = providerId != null ? ctl.Views.FirstOrDefault(x => x.Id == providerId) : null;
            if (v == null) providerId = null;
            if (speech == null) speech = new SpeechWindow(this);
            speech.SetText(providerId, text, v != null ? v.Color : Palette.Hex("#94A3B8"), 3.5 + Math.Min(6, text.Length * 0.12));
            HideDetail();
            quietUntil = speech.Until.AddSeconds(-1);
            PlaceSpeech();
            if (!speech.IsVisible) speech.Show();
            PlaceSpeech();
        }

        /// <summary>Above the widget (or wherever there is room), the tail pointing at the speaker.</summary>
        void PlaceSpeech()
        {
            if (speech == null || hwnd == IntPtr.Zero) return;
            if (tagged.Count == 0 || (speech.ProviderId != null && ProviderScreenRect(speech.ProviderId).IsEmpty)) ScanTagged();
            var dpi = VisualTreeHelper.GetDpi(this);
            var size = speech.MeasurePx(dpi.DpiScaleX, dpi.DpiScaleY);
            var content = ContentScreenRect();
            var provider = speech.ProviderId != null ? ProviderScreenRect(speech.ProviderId) : Rect.Empty;
            if (provider.IsEmpty) provider = content;
            var wa = ScreenFor(PixelRect()).WorkingArea;
            double gap = 4 - SpeechWindow.Pad * dpi.DpiScaleY;
            var r = DetailPlacement.Compute(content, provider, new Rect(wa.Left, wa.Top, wa.Width, wa.Height), size.Width, size.Height, gap);
            speech.SetPointer(r.Side, r.PointerX / dpi.DpiScaleX);
            speech.MoveTo(r.X, r.Y);
        }

        void HideSpeech()
        {
            if (speech != null && speech.IsVisible) speech.Hide();
        }

        // ------------------------------------------------------------------ position (physical pixels)

        Native.RECT PixelRect()
        {
            Native.RECT r;
            Native.GetWindowRect(hwnd, out r);
            return r;
        }

        void MoveTo(int x, int y)
        {
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | 0x4 | Native.SWP_NOACTIVATE);
        }

        static Forms.Screen ScreenFor(Native.RECT r)
        {
            return Forms.Screen.FromRectangle(new System.Drawing.Rectangle(r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top)));
        }

        bool AnchorRight { get { return (settings.Anchor ?? "br").Contains("r"); } }
        bool AnchorBottom { get { return (settings.Anchor ?? "br").Contains("b"); } }

        void PlaceInitially()
        {
            if (hwnd == IntPtr.Zero) return;
            bool ok = false;
            if (settings.X.HasValue && settings.Y.HasValue)
            {
                var p = new System.Drawing.Point((int)settings.X.Value + (AnchorRight ? -20 : 20), (int)settings.Y.Value + (AnchorBottom ? -20 : 20));
                ok = Forms.Screen.AllScreens.Any(s => s.WorkingArea.Contains(p));
            }
            if (!ok) ResetToCorner(Forms.Screen.PrimaryScreen, "br");
            positioned = true;
            PlaceAtAnchor();
        }

        /// <summary>Size of the laid-out content in physical pixels (the HWND can lag behind while resizing).</summary>
        void PixelSize(out int w, out int h)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
            h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        }

        /// <summary>Puts the anchored corner of the widget on the saved anchor point.</summary>
        void PlaceAtAnchor()
        {
            if (hwnd == IntPtr.Zero || !settings.X.HasValue || !settings.Y.HasValue) return;
            var r = PixelRect();
            int w, h;
            PixelSize(out w, out h);
            int x = (int)settings.X.Value - (AnchorRight ? w : 0);
            int y = (int)settings.Y.Value - (AnchorBottom ? h : 0);
            if (x != r.Left || y != r.Top) MoveTo(x, y);
        }

        void ResetToCorner(Forms.Screen screen, string anchor)
        {
            var wa = screen.WorkingArea;
            settings.Anchor = anchor;
            settings.X = anchor.Contains("r") ? wa.Right - 16 : wa.Left + 16;
            settings.Y = anchor.Contains("b") ? wa.Bottom - 8 : wa.Top + 8;
        }

        void AfterMove()
        {
            var r = PixelRect();
            int w, h;
            PixelSize(out w, out h);
            var wa = ScreenFor(r).WorkingArea;
            int x = r.Left, y = r.Top;
            const int snap = 18;
            if (Math.Abs(x - wa.Left) < snap) x = wa.Left;
            if (Math.Abs(x + w - wa.Right) < snap) x = wa.Right - w;
            if (Math.Abs(y - wa.Top) < snap) y = wa.Top;
            if (Math.Abs(y + h - wa.Bottom) < snap) y = wa.Bottom - h;
            if (x != r.Left || y != r.Top) MoveTo(x, y);
            double cx = x + w / 2.0, cy = y + h / 2.0;
            settings.Anchor = (cy > wa.Top + wa.Height / 2.0 ? "b" : "t") + (cx > wa.Left + wa.Width / 2.0 ? "r" : "l");
            settings.X = AnchorRight ? x + w : x;
            settings.Y = AnchorBottom ? y + h : y;
            settings.Save();
        }

        public void MoveToScreen(Forms.Screen screen)
        {
            ResetToCorner(screen, settings.Anchor ?? "br");
            PlaceAtAnchor();
            settings.Save();
        }

        public void EnsureOnScreen()
        {
            if (hwnd == IntPtr.Zero) return;
            var r = PixelRect();
            var c = new System.Drawing.Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            if (!Forms.Screen.AllScreens.Any(s => s.WorkingArea.Contains(c)))
            {
                ResetToCorner(Forms.Screen.PrimaryScreen, "br");
                PlaceAtAnchor();
                settings.Save();
            }
        }

        public Forms.Screen CurrentScreen
        {
            get { return hwnd == IntPtr.Zero ? Forms.Screen.PrimaryScreen : ScreenFor(PixelRect()); }
        }

        // ------------------------------------------------------------------ hover card

        /// <summary>Keeps the card open for a while without hovering (used by --show-detail for testing).</summary>
        public void ForceDetail(string id, double seconds)
        {
            forcedId = id;
            forcedUntil = DateTime.UtcNow.AddSeconds(seconds);
        }

        /// <summary>
        /// Hover is decided from the global cursor position against each provider's bounding box, not from
        /// mouse events: a layered window lets the mouse fall through fully transparent pixels, which used to
        /// make the card flicker whenever the cursor crossed a gap in the pet.
        /// </summary>
        void PollHover(DateTime now)
        {
            bool forced = forcedId != null && now < forcedUntil;
            if (!forced && (dragging || ctl.MenuOpen || settings.ClickThrough || !IsVisible || theme == null || hwnd == IntPtr.Zero))
            {
                hoverId = null;
                HideDetail();
                return;
            }
            if ((now - lastScan).TotalMilliseconds > 250)
            {
                lastScan = now;
                ScanTagged();
            }

            string id = null;
            Native.POINT cp;
            if (forced) id = forcedId;
            else if (Native.GetCursorPos(out cp))
            {
                var cursor = host.PointFromScreen(new Point(cp.X, cp.Y));
                foreach (var kv in tagged)
                {
                    Rect b;
                    if (!TryBounds(kv.Value, out b)) continue;
                    b.Inflate(8, 8);
                    if (b.Contains(cursor)) { id = kv.Key; break; }
                }
                if (id == null && shownId != null && InCorridor(new Point(cp.X, cp.Y))) id = shownId;
            }

            if (id != null) lastInside = now;
            if (id != hoverId)
            {
                hoverId = id;
                hoverSince = now;
            }
            double held = (now - hoverSince).TotalMilliseconds;
            if (shownId == null)
            {
                if (hoverId != null && (forced || (held >= ShowDelayMs && now >= quietUntil))) ShowDetail(hoverId);
            }
            else if (hoverId == null)
            {
                if ((now - lastInside).TotalMilliseconds >= HideGraceMs) HideDetail();
            }
            else if (hoverId != shownId && held >= SwitchDelayMs)
            {
                ShowDetail(hoverId);   // switch straight to the other provider, no close/reopen
            }
        }

        void ScanTagged()
        {
            tagged.Clear();
            Collect(host);
        }

        void Collect(DependencyObject d)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                var fe = c as FrameworkElement;
                var tag = fe != null ? fe.Tag as string : null;
                if (tag != null && tag.StartsWith("pv:"))
                {
                    tagged.Add(new KeyValuePair<string, FrameworkElement>(tag.Substring(3), fe));
                    continue;
                }
                Collect(c);
            }
        }

        bool TryBounds(FrameworkElement el, out Rect r)
        {
            r = Rect.Empty;
            try
            {
                if (!el.IsVisible || el.ActualWidth <= 0 || el.ActualHeight <= 0 || !host.IsAncestorOf(el)) return false;
                r = el.TransformToAncestor(host).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                return true;
            }
            catch { return false; }
        }

        /// <summary>A rectangle in host coordinates → physical screen pixels.</summary>
        Rect ToScreen(Rect r)
        {
            var a = host.PointToScreen(r.TopLeft);
            var b = host.PointToScreen(r.BottomRight);
            return new Rect(a, b);
        }

        Rect ProviderScreenRect(string id)
        {
            var u = Rect.Empty;
            foreach (var kv in tagged)
            {
                if (kv.Key != id) continue;
                Rect b;
                if (TryBounds(kv.Value, out b)) u.Union(ToScreen(b));
            }
            return u;
        }

        Rect ContentScreenRect()
        {
            var c = theme != null ? theme.ContentBounds(host) : Rect.Empty;
            if (!c.IsEmpty) return ToScreen(c);
            var r = PixelRect();
            return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        /// <summary>While the card is up, the path from the pet to the card keeps it open.</summary>
        bool InCorridor(Point screen)
        {
            if (card == null || !card.IsVisible || card.Handle == IntPtr.Zero) return false;
            Native.RECT cr;
            if (!Native.GetWindowRect(card.Handle, out cr)) return false;
            var u = ProviderScreenRect(shownId);
            if (u.IsEmpty) return false;
            u.Union(new Rect(cr.Left, cr.Top, cr.Right - cr.Left, cr.Bottom - cr.Top));
            u.Inflate(10, 10);
            return u.Contains(screen);
        }

        void ShowDetail(string id)
        {
            var v = ctl.Views.FirstOrDefault(x => x.Id == id);
            if (v == null) { HideDetail(); return; }
            HideSpeech();
            if (card == null) card = new DetailWindow(this);
            card.SetView(v);
            shownId = id;
            PlaceDetail();
            if (!card.IsVisible) card.Show();
        }

        /// <summary>Puts the card outside the widget content, arrow pointing at the hovered provider.</summary>
        void PlaceDetail()
        {
            if (card == null || shownId == null || hwnd == IntPtr.Zero) return;
            if (ProviderScreenRect(shownId).IsEmpty) ScanTagged();
            var dpi = VisualTreeHelper.GetDpi(this);
            var size = card.MeasurePx(dpi.DpiScaleX, dpi.DpiScaleY);
            var content = ContentScreenRect();
            var provider = ProviderScreenRect(shownId);
            if (provider.IsEmpty) provider = content;
            var wa = ScreenFor(PixelRect()).WorkingArea;
            double gap = 6 - DetailCardView.Margin * dpi.DpiScaleY;   // 6px visible gap; the window margin is transparent
            var r = DetailPlacement.Compute(content, provider, new Rect(wa.Left, wa.Top, wa.Width, wa.Height), size.Width, size.Height, gap);
            card.SetPointer(r.Side, r.PointerX / dpi.DpiScaleX);
            card.MoveTo(r.X, r.Y);
        }

        public void HideDetail()
        {
            shownId = null;
            if (card != null && card.IsVisible) card.Hide();
        }
    }
}
