using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SentriPet
{
    /// <summary>System-tray icon: a tiny jelly whose fill shows the lowest remaining quota.</summary>
    class Tray : IDisposable
    {
        readonly Forms.NotifyIcon icon;
        IntPtr lastHicon;
        string lastSig;

        public Tray(Controller ctl)
        {
            icon = new Forms.NotifyIcon { Text = App.DisplayName, Visible = true };
            SetIcon(Draw(-1, false));
            icon.MouseUp += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Right) ctl.ShowMenu(true);
                else if (e.Button == Forms.MouseButtons.Left) ctl.ToggleWidget();
            };
        }

        public void Update(List<ProviderView> views)
        {
            var limited = views.Where(v => v.HasData && !v.Unlimited).ToList();
            double min = limited.Count > 0 ? limited.Min(v => v.Remaining) : -1;
            bool active = views.Any(v => v.Active);
            string sig = (int)Math.Round(min) + (active ? "a" : "");
            if (sig != lastSig)
            {
                lastSig = sig;
                SetIcon(Draw(min, active));
            }
            string tip = views.Count == 0 ? App.DisplayName : string.Join(" · ", views.Select(v => v.Summary));
            if (tip.Length > 63) tip = tip.Substring(0, 62) + "…";
            if (icon.Text != tip) icon.Text = tip;
        }

        public void Notify(string title, string text)
        {
            try { icon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.None); } catch { }
        }

        void SetIcon(Bitmap bmp)
        {
            IntPtr h = bmp.GetHicon();
            var old = icon.Icon;
            icon.Icon = Icon.FromHandle(h);
            if (old != null) old.Dispose();
            if (lastHicon != IntPtr.Zero) Native.DestroyIcon(lastHicon);
            lastHicon = h;
            bmp.Dispose();
        }

        /// <param name="remaining">lowest remaining %, or -1 when unknown</param>
        static Bitmap Draw(double remaining, bool active)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var body = new GraphicsPath();
                // gumdrop: flat bottom, dome top (same silhouette as the pet)
                body.AddBezier(5f, 29f, 1.5f, 29f, 1.5f, 25f, 1.5f, 21f);
                body.AddBezier(1.5f, 21f, 1.5f, 9f, 8f, 2.5f, 16f, 2.5f);
                body.AddBezier(16f, 2.5f, 24f, 2.5f, 30.5f, 9f, 30.5f, 21f);
                body.AddBezier(30.5f, 21f, 30.5f, 25f, 30.5f, 29f, 27f, 29f);
                body.CloseFigure();
                Color level = remaining < 0 ? Color.FromArgb(150, 160, 175) :
                              remaining >= 50 ? Color.FromArgb(52, 211, 153) :
                              remaining >= 20 ? Color.FromArgb(251, 191, 36) : Color.FromArgb(248, 113, 113);
                using (var b = new SolidBrush(Color.FromArgb(245, 246, 250))) g.FillPath(b, body);
                double frac = remaining < 0 ? 0.55 : Math.Max(0.06, remaining / 100);
                float top = (float)(29 - frac * 26.5);
                g.SetClip(body);
                using (var b = new SolidBrush(level)) g.FillRectangle(b, 0, top, 32, 32);
                g.ResetClip();
                using (var p = new Pen(Color.FromArgb(43, 33, 30), 2.2f)) g.DrawPath(p, body);
                using (var e = new SolidBrush(Color.FromArgb(43, 33, 30)))
                {
                    g.FillEllipse(e, 9.5f, 13f, 4f, 5f);
                    g.FillEllipse(e, 18.5f, 13f, 4f, 5f);
                }
                if (active)
                {
                    using (var a = new SolidBrush(Color.FromArgb(59, 130, 246))) g.FillEllipse(a, 22f, 0.5f, 9f, 9f);
                    using (var w = new Pen(Color.White, 1.4f)) g.DrawEllipse(w, 22f, 0.5f, 9f, 9f);
                }
            }
            return bmp;
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            if (lastHicon != IntPtr.Zero) Native.DestroyIcon(lastHicon);
        }
    }

    /// <summary>Per-user "run at sign-in" entry (HKCU\...\Run). Never touched by --dev builds.</summary>
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "SentriPet";
        const string LegacyValueName = "AIUsagePet";   // name used before 1.1

        static string Command
        {
            get { return "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" --autostart"; }
        }

        public static bool IsRegistered()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static void Set(bool enable)
        {
            if (AppPaths.Dev) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k.GetValue(LegacyValueName) != null) k.DeleteValue(LegacyValueName, false);
                    if (enable)
                    {
                        if (!string.Equals(k.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase))
                            k.SetValue(ValueName, Command);
                    }
                    else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex) { Log.Error("autostart", ex); }
        }
    }
}
