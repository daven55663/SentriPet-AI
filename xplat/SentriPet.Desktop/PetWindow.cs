using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SentriPet
{
    /// <summary>
    /// The transparent, borderless widget (Avalonia port of src/UI/PetWindow.cs). Drag anywhere to move (across
    /// monitors, snaps to screen edges), click to poke, right-click for the menu.
    /// </summary>
    class PetWindow : Window, IThemeHost
    {
        readonly DesktopController ctl;
        readonly AppSettings settings;
        readonly Random rng = new Random();
        readonly Panel host;
        readonly LayoutTransformControl zoom;
        readonly DispatcherTimer frame;
        Theme theme;
        DateTime lastFrame = DateTime.UtcNow;
        Point? pointer;
        bool positioned, pressed, dragging;
        PixelPoint pressScreen, pressPos;

        // hover card
        DetailWindow card;
        string hoverId, shownId, forcedId;
        DateTime forcedUntil;
        bool cardHovered;
        DateTime hoverSince, lastInside, lastScan = DateTime.MinValue, quietUntil;
        readonly List<KeyValuePair<string, Control>> tagged = new List<KeyValuePair<string, Control>>();
        const double ShowDelayMs = 300, SwitchDelayMs = 150, HideGraceMs = 400;

        public Theme CurrentTheme { get { return theme; } }
        public AppSettings Settings { get { return settings; } }
        public Random Rng { get { return rng; } }

        public PetWindow(DesktopController ctl, AppSettings settings)
        {
            this.ctl = ctl;
            this.settings = settings;
            Title = AppInfo.Name;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            CanResize = false;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Topmost = settings.AlwaysOnTop;
            FontFamily = G.Ui;

            host = new Panel();
            zoom = new LayoutTransformControl { LayoutTransform = new ScaleTransform(settings.Scale, settings.Scale), Child = host };
            Content = zoom;

            frame = new DispatcherTimer(TimeSpan.FromMilliseconds(settings.LowPower ? 250 : 33), DispatcherPriority.Render, OnFrame);
            Opened += (s, e) =>
            {
                PlaceInitially();
                lastFrame = DateTime.UtcNow;
                frame.Start();
            };
            Closed += (s, e) => { frame.Stop(); if (card != null) card.Close(); };
            SizeChanged += (s, e) => { if (positioned && !dragging) PlaceAtAnchor(); };
            PointerPressed += OnPressed;
            PointerMoved += OnMoved;
            PointerReleased += OnReleased;
            PointerExited += (s, e) => pointer = null;
        }

        // ------------------------------------------------------------------ theme & settings

        public void SetTheme(Theme t)
        {
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
            zoom.LayoutTransform = new ScaleTransform(settings.Scale, settings.Scale);
            Opacity = settings.Opacity;
            Topmost = settings.AlwaysOnTop;
            frame.Interval = TimeSpan.FromMilliseconds(settings.LowPower ? 250 : 33);
        }

        public void SaveSettings() { settings.Save(); }

        /// <summary>
        /// Where the mouse is, in screen pixels. Windows can tell anywhere on screen; elsewhere it is only known while
        /// the pointer is over the widget.
        /// </summary>
        PixelPoint? CursorScreen()
        {
            if (Os.Windows)
            {
                NativeMethods.POINT p;
                if (NativeMethods.GetCursorPos(out p)) return new PixelPoint(p.X, p.Y);
                return null;
            }
            if (pointer != null) return this.PointToScreen(pointer.Value);
            return null;
        }

        public Point? CursorIn(Visual element)
        {
            var screen = CursorScreen();
            if (screen == null) return null;
            try { return element.PointToClient(screen.Value); }
            catch { return null; }
        }

        public void Say(string providerId, string text)
        {
            if (theme == null || string.IsNullOrEmpty(text)) return;
            try { theme.Say(providerId, text); }
            catch (Exception ex) { Log.Error("say", ex); }
        }

        void OnFrame(object sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double dt = Math.Max(0, Math.Min(0.25, (now - lastFrame).TotalSeconds));
            lastFrame = now;
            if (theme == null) return;
            try { theme.Tick(dt); }
            catch (Exception ex) { Log.Error("theme tick", ex); }
            try { PollHover(now); }
            catch (Exception ex) { Log.Error("hover", ex); }
        }

        /// <summary>Called once a second: keep the card's numbers current and follow the widget.</summary>
        public void Periodic()
        {
            if (shownId == null) return;
            var v = ctl.Views.FirstOrDefault(x => x.Id == shownId);
            if (v == null) { HideDetail(); return; }
            card.SetView(v);
            PlaceDetail();
        }

        // ------------------------------------------------------------------ hover card

        /// <summary>
        /// Hover is decided from the cursor position against each provider's bounding box (not from enter/leave events),
        /// with a short delay before showing, a grace period before hiding and a "corridor" from the pet to the card.
        /// </summary>
        /// <summary>Keeps one card open for a while without hovering (--show-detail, for testing).</summary>
        public void ForceDetail(string id, double seconds)
        {
            forcedId = id;
            forcedUntil = DateTime.UtcNow.AddSeconds(seconds);
        }

        void PollHover(DateTime now)
        {
            bool forced = forcedId != null && now < forcedUntil;
            if (!forced && (pressed || dragging || ctl.MenuOpen || !IsVisible || theme == null))
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
            var cursor = CursorScreen();
            if (forced) id = forcedId;
            else if (cursor != null)
            {
                var pt = new Point(cursor.Value.X, cursor.Value.Y);
                foreach (var kv in tagged)
                {
                    var b = ScreenRect(kv.Value);
                    if (b != null && b.Value.Inflate(8).Contains(pt)) { id = kv.Key; break; }
                }
                if (id == null && shownId != null && InCorridor(pt)) id = shownId;
            }
            if (id == null && shownId != null && cardHovered) id = shownId;

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
            else if (hoverId != shownId && held >= SwitchDelayMs) ShowDetail(hoverId);
        }

        void ScanTagged()
        {
            tagged.Clear();
            foreach (var v in host.GetVisualDescendants())
            {
                var c = v as Control;
                var tag = c != null ? c.Tag as string : null;
                if (tag != null && tag.StartsWith("pv:")) tagged.Add(new KeyValuePair<string, Control>(tag.Substring(3), c));
            }
        }

        /// <summary>An element's bounds in screen pixels.</summary>
        Rect? ScreenRect(Control el)
        {
            if (el == null || !el.IsEffectivelyVisible || el.Bounds.Width <= 0) return null;
            var m = el.TransformToVisual(this);
            if (m == null) return null;
            return ToScreen(new Rect(el.Bounds.Size).TransformToAABB(m.Value));
        }

        Rect ToScreen(Rect r)
        {
            var a = this.PointToScreen(r.TopLeft);
            var b = this.PointToScreen(r.BottomRight);
            return new Rect(new Point(a.X, a.Y), new Point(b.X, b.Y));
        }

        Rect? ProviderScreenRect(string id)
        {
            Rect? u = null;
            foreach (var kv in tagged)
            {
                if (kv.Key != id) continue;
                var b = ScreenRect(kv.Value);
                if (b != null) u = u == null ? b : u.Value.Union(b.Value);
            }
            return u;
        }

        Rect ContentScreenRect()
        {
            var c = theme != null ? theme.ContentBounds(this) : null;
            if (c != null) return ToScreen(c.Value);
            int w, h;
            PixelSize(out w, out h);
            return new Rect(Position.X, Position.Y, w, h);
        }

        /// <summary>While the card is up, the path from the pet to the card keeps it open.</summary>
        bool InCorridor(Point screen)
        {
            if (card == null || !card.IsVisible) return false;
            var u = ProviderScreenRect(shownId);
            if (u == null) return false;
            var cs = card.MeasurePx(DesktopScaling);
            var cr = new Rect(card.Position.X, card.Position.Y, cs.Width, cs.Height);
            return u.Value.Union(cr).Inflate(10).Contains(screen);
        }

        void ShowDetail(string id)
        {
            var v = ctl.Views.FirstOrDefault(x => x.Id == id);
            if (v == null) { HideDetail(); return; }
            if (card == null)
            {
                card = new DetailWindow();
                card.PointerEntered += (s, e) => cardHovered = true;
                card.PointerExited += (s, e) => cardHovered = false;
            }
            card.SetView(v);
            shownId = id;
            PlaceDetail();
            if (!card.IsVisible) card.Show(this);
            PlaceDetail();
        }

        /// <summary>Puts the card outside the widget content, arrow pointing at the hovered provider.</summary>
        void PlaceDetail()
        {
            if (card == null || shownId == null) return;
            if (ProviderScreenRect(shownId) == null) ScanTagged();
            double scaling = DesktopScaling;
            var size = card.MeasurePx(scaling);
            var content = ContentScreenRect();
            var provider = ProviderScreenRect(shownId) ?? content;
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null) return;
            var wa = screen.WorkingArea;
            double gap = 6 - DetailCardView.Margin * scaling;   // 6px visible gap; the window margin is transparent
            var r = DetailPlacement.Compute(content, provider, new Rect(wa.X, wa.Y, wa.Width, wa.Height), size.Width, size.Height, gap);
            card.SetPointer(r.Side, r.PointerX / scaling);
            card.MoveTo(r.X, r.Y);
        }

        public void HideDetail()
        {
            shownId = null;
            cardHovered = false;
            if (card != null && card.IsVisible) card.Hide();
        }

        // ------------------------------------------------------------------ mouse: drag, click, menu

        void OnPressed(object sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed)
            {
                ctl.ShowMenu(this);
                e.Handled = true;
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            HideDetail();
            hoverId = null;
            pressed = true;
            dragging = false;
            pressScreen = this.PointToScreen(point.Position);
            pressPos = Position;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        void OnMoved(object sender, PointerEventArgs e)
        {
            pointer = e.GetPosition(this);
            if (!pressed) return;
            var now = this.PointToScreen(e.GetPosition(this));
            int dx = now.X - pressScreen.X, dy = now.Y - pressScreen.Y;
            if (!dragging && Math.Abs(dx) + Math.Abs(dy) > 4) dragging = true;
            if (dragging) Position = new PixelPoint(pressPos.X + dx, pressPos.Y + dy);
        }

        void OnReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!pressed) return;
            pressed = false;
            e.Pointer.Capture(null);
            if (dragging)
            {
                dragging = false;
                AfterMove();
                return;
            }
            if (theme == null) return;
            quietUntil = DateTime.UtcNow.AddSeconds(3.5);   // let the reply bubble be seen before the card comes back
            var rootPoint = e.GetPosition(theme.Root);
            if (theme.Click(rootPoint)) return;
            string id = HitProvider(e.GetPosition(host));
            if (id != null) theme.Poke(id);
        }

        string HitProvider(Point hostPoint)
        {
            var hit = host.InputHitTest(hostPoint) as Visual;
            while (hit != null && hit != host)
            {
                var tag = (hit as Control) != null ? ((Control)hit).Tag as string : null;
                if (tag != null && tag.StartsWith("pv:")) return tag.Substring(3);
                hit = hit.GetVisualParent();
            }
            return null;
        }

        // ------------------------------------------------------------------ position (physical pixels, like the WPF version)

        bool AnchorRight { get { return (settings.Anchor ?? "br").Contains("r"); } }
        bool AnchorBottom { get { return (settings.Anchor ?? "br").Contains("b"); } }

        void PixelSize(out int w, out int h)
        {
            double s = DesktopScaling;
            w = (int)Math.Round(Bounds.Width * s);
            h = (int)Math.Round(Bounds.Height * s);
        }

        void PlaceInitially()
        {
            bool ok = false;
            if (settings.X.HasValue && settings.Y.HasValue)
            {
                var p = new PixelPoint((int)settings.X.Value + (AnchorRight ? -20 : 20), (int)settings.Y.Value + (AnchorBottom ? -20 : 20));
                ok = Screens.All.Any(s => s.WorkingArea.Contains(p));
            }
            if (!ok && Screens.Primary != null) ResetToCorner(Screens.Primary, "br");
            positioned = true;
            PlaceAtAnchor();
        }

        /// <summary>Puts the anchored corner of the widget on the saved anchor point.</summary>
        void PlaceAtAnchor()
        {
            if (!settings.X.HasValue || !settings.Y.HasValue) return;
            int w, h;
            PixelSize(out w, out h);
            var target = new PixelPoint((int)settings.X.Value - (AnchorRight ? w : 0), (int)settings.Y.Value - (AnchorBottom ? h : 0));
            if (target != Position) Position = target;
        }

        void ResetToCorner(Screen screen, string anchor)
        {
            var wa = screen.WorkingArea;
            settings.Anchor = anchor;
            settings.X = anchor.Contains("r") ? wa.Right - 16 : wa.X + 16;
            settings.Y = anchor.Contains("b") ? wa.Bottom - 8 : wa.Y + 8;
        }

        void AfterMove()
        {
            int w, h;
            PixelSize(out w, out h);
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null) return;
            var wa = screen.WorkingArea;
            int x = Position.X, y = Position.Y;
            const int snap = 18;
            if (Math.Abs(x - wa.X) < snap) x = wa.X;
            if (Math.Abs(x + w - wa.Right) < snap) x = wa.Right - w;
            if (Math.Abs(y - wa.Y) < snap) y = wa.Y;
            if (Math.Abs(y + h - wa.Bottom) < snap) y = wa.Bottom - h;
            if (x != Position.X || y != Position.Y) Position = new PixelPoint(x, y);
            double cx = x + w / 2.0, cy = y + h / 2.0;
            settings.Anchor = (cy > wa.Y + wa.Height / 2.0 ? "b" : "t") + (cx > wa.X + wa.Width / 2.0 ? "r" : "l");
            settings.X = AnchorRight ? x + w : x;
            settings.Y = AnchorBottom ? y + h : y;
            settings.Save();
        }

        public void MoveToScreen(Screen screen)
        {
            ResetToCorner(screen, settings.Anchor ?? "br");
            PlaceAtAnchor();
            settings.Save();
        }
    }

    static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT p);
    }
}
