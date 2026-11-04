using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SentriPet
{
    /// <summary>
    /// "A new version is out" (#39): what changed, and Update now / Later / Skip this version. Updating depends on how
    /// this copy was installed (see Updater): replace its own files and start again, let Scoop / winget / Homebrew do
    /// it, or open the release page.
    /// </summary>
    class UpdateWindow : Window
    {
        static readonly Color Bg = Palette.Hex("#14161C");
        static readonly Color PanelBg = Palette.Hex("#1B1E26");
        static readonly Color LineC = Palette.Hex("#2A2E39");
        static readonly Color TextC = Palette.Hex("#E8EAF0");
        static readonly Color SubC = Palette.Hex("#8F98A8");
        static readonly Color Accent = Palette.Hex("#7C9CFF");
        static readonly Color ErrorC = Palette.Hex("#FF8A80");

        readonly Release release;
        readonly InstallKind kind;
        readonly Action<string> skip;
        readonly Action<ProcessStartInfo> restart;
        readonly TextBlock status;
        readonly ProgressBar bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
        readonly StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        readonly Button now, later, skipButton, page;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        bool failed;

        /// <summary>The window's content (the snapshot renders it).</summary>
        public Control Page { get; private set; }
        /// <summary>Downloading or installing.</summary>
        public bool Busy { get; private set; }
        /// <summary>The line under the notes: progress, or what went wrong.</summary>
        public string Message { get { return status.Text; } }

        /// <param name="skip">"Skip this version" (the version).</param>
        /// <param name="restart">Starts the process that finishes the update and quits SentriPet.</param>
        public UpdateWindow(Release release, InstallKind kind, Action<string> skip, Action<ProcessStartInfo> restart)
        {
            this.release = release;
            this.kind = kind;
            this.skip = skip;
            this.restart = restart;
            Title = AppInfo.Name + " · " + L.T("有新版本");
            Width = 540;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = G.B(Bg);
            Foreground = G.B(TextC);
            FontFamily = G.Ui;
            FontSize = 13;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://SentriPet/Assets/app.ico"))); } catch { }

            var root = new StackPanel();
            root.Children.Add(Txt(L.T("SentriPet 有新版本了"), 19, TextC, FontWeight.Bold));
            var sub = Txt(L.F("{0} 版已經推出，你現在用的是 {1} 版。", release.Version, AppInfo.Version), 13, SubC, FontWeight.Normal);
            sub.Margin = new Thickness(0, 4, 0, 14);
            root.Children.Add(sub);

            string changes = Updater.Changes(release.Notes);
            var notes = new StackPanel();
            notes.Children.Add(Txt(L.T("這一版的變化"), 13.5, TextC, FontWeight.SemiBold));
            var text = Txt(changes.Length > 0 ? changes : L.T("（這一版沒有附說明）"), 12.5, TextC, FontWeight.Normal);
            text.Margin = new Thickness(0, 6, 0, 0);
            text.LineHeight = 19;
            notes.Children.Add(text);
            root.Children.Add(new Border
            {
                Background = G.B(PanelBg),
                BorderBrush = G.B(LineC),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12),
                Child = new ScrollViewer { Content = notes, MaxHeight = 280, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            });

            var how = Txt(HowText(kind), 12, SubC, FontWeight.Normal);
            how.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(how);
            status = Txt("", 12, SubC, FontWeight.Normal);
            status.Margin = new Thickness(0, 8, 0, 0);
            status.IsVisible = false;
            root.Children.Add(status);
            root.Children.Add(bar);

            var foot = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
            page = Link(L.T("在 GitHub 上看完整說明"), () => Integration.OpenPath(release.Page));
            DockPanel.SetDock(page, Dock.Left);
            foot.Children.Add(page);
            bool download = kind == InstallKind.Download || kind == InstallKind.Development;
            now = Btn(download ? L.T("前往下載頁") : L.T("現在更新"), UpdateNow, true);
            later = Btn(L.T("稍後"), Close, false);
            skipButton = Btn(L.T("略過這個版本"), () =>
            {
                if (skip != null) skip(release.Version);
                Close();
            }, false);
            buttons.Children.Add(skipButton);
            buttons.Children.Add(later);
            buttons.Children.Add(now);
            DockPanel.SetDock(buttons, Dock.Right);
            foot.Children.Add(buttons);
            root.Children.Add(foot);
            Page = new Border { Padding = new Thickness(24, 20, 24, 18), Background = G.B(Bg), Child = root };
            Content = Page;
            Closed += (s, e) => cancel.Cancel();
        }

        /// <summary>What "Update now" will do with this copy.</summary>
        public static string HowText(InstallKind kind)
        {
            switch (kind)
            {
                case InstallKind.SelfUpdate: return L.T("按「現在更新」會下載新版，確認檔案和 GitHub 公布的 SHA-256 相符後替換，再自動重新開啟；設定、造型和紀錄都會保留。");
                case InstallKind.Scoop: return L.T("這份是用 Scoop 安裝的：按「現在更新」會關閉 SentriPet，在命令視窗執行 scoop update sentripet，完成後自動重新開啟。");
                case InstallKind.Winget: return L.T("這份是用 winget 安裝的：按「現在更新」會關閉 SentriPet，在命令視窗執行 winget upgrade，完成後自動重新開啟。winget 的套件庫通常要幾天後才會收錄新版本。");
                case InstallKind.Homebrew: return L.T("這份是用 Homebrew 安裝的：按「現在更新」會關閉 SentriPet，執行 brew upgrade --cask sentripet，完成後自動重新開啟。Homebrew 若要求先信任這個 tap，請先在終端機執行一次 brew trust daven55663/sentripet。");
                case InstallKind.Development: return L.T("這是從原始碼建置的版本，不會自動替換；按「前往下載頁」可以看新版本。");
                default: return L.T("這份要自己下載安裝：按「前往下載頁」會打開 GitHub 的發佈頁，下載後覆蓋舊版即可，設定都會保留。");
            }
        }

        async void UpdateNow()
        {
            if (Busy) return;
            if (failed || kind == InstallKind.Download || kind == InstallKind.Development)
            {
                Integration.OpenPath(release.Page);
                Close();
                return;
            }
            SetBusy(true);
            try
            {
                if (kind == InstallKind.SelfUpdate)
                {
                    bar.IsVisible = true;
                    var psi = await Updater.InstallOver(release, Environment.ProcessPath, (text, part) => Dispatcher.UIThread.Post(() =>
                    {
                        if (cancel.IsCancellationRequested) return;
                        Say(text, false);
                        bar.IsIndeterminate = part < 0;
                        if (part >= 0) bar.Value = part;
                    }), cancel.Token);
                    Say(L.T("完成！正在重新開啟…"), false);
                    if (restart != null) restart(psi);
                }
                else
                {
                    string brew = kind == InstallKind.Homebrew ? Updater.Brew : null;
                    if (kind == InstallKind.Homebrew && brew == null) throw new UpdateException(L.T("找不到 Homebrew（brew）"));
                    var psi = Updater.PackageUpdate(kind, Environment.ProcessId, Environment.ProcessPath, brew, Path.Combine(AppPaths.LogDir, "update.log"));
                    Log.Info("update: " + AppInfo.Version + " to " + release.Version + " through " + kind);
                    if (restart != null) restart(psi);
                }
            }
            catch (Exception ex)
            {
                if (cancel.IsCancellationRequested) return;
                Log.Error("update to " + release.Version, ex);
                SetBusy(false);
                bar.IsVisible = false;
                Say((ex is UpdateException ? ex.Message : L.F("更新失敗：{0}", (ex.InnerException ?? ex).Message)) + " " +
                    L.T("可以改從 GitHub 的發佈頁下載。"), true);
                failed = true;
                now.Content = L.T("前往下載頁");
            }
        }

        void SetBusy(bool on)
        {
            Busy = on;
            now.IsEnabled = later.IsEnabled = skipButton.IsEnabled = !on;
        }

        void Say(string text, bool error)
        {
            status.Text = L.Finish(text);
            status.Foreground = G.B(error ? ErrorC : SubC);
            status.IsVisible = text.Length > 0;
        }

        // ------------------------------------------------------------------ layout helpers

        static TextBlock Txt(string s, double size, Color c, FontWeight w)
        {
            return new TextBlock { Text = L.Finish(s), FontSize = size, Foreground = G.B(c), FontWeight = w, TextWrapping = TextWrapping.Wrap };
        }

        Button Btn(string text, Action click, bool primary)
        {
            var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(9), Padding = new Thickness(16, 8) };
            if (primary)
            {
                b.Background = G.B(Accent);
                b.Foreground = G.B(Palette.Hex("#0E1220"));
                b.FontWeight = FontWeight.SemiBold;
            }
            b.Click += (s, e) =>
            {
                try { click(); }
                catch (Exception ex) { Log.Error("button " + text, ex); }
            };
            return b;
        }

        static Button Link(string text, Action click)
        {
            var b = new Button
            {
                Content = text,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = G.B(Accent),
                Padding = new Thickness(0, 8),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            b.Click += (s, e) =>
            {
                try { click(); }
                catch (Exception ex) { Log.Error("link " + text, ex); }
            };
            return b;
        }
    }
}
