using System;
using System.Linq;
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
            Closed += (s, e) => frame.Stop();
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

        /// <summary>The mouse position is only known while it is over the widget (no global cursor API on every system).</summary>
        public Point? CursorIn(Visual element)
        {
            if (pointer == null) return null;
            return this.TranslatePoint(pointer.Value, element);
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
}
