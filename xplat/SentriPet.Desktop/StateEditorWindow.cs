using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace SentriPet
{
    /// <summary>
    /// The state editor (#34): a custom theme's folder is a library of pictures (its sub-folders are groups). Drag a
    /// picture onto a state — or drop a picture file from the file manager, which is copied into the folder — and the
    /// pet shows it in that state. The choices are saved right away in the theme's states.json; theme.json stays as its
    /// author wrote it.
    /// </summary>
    class StateEditorWindow : Window
    {
        static readonly Color Bg = Palette.Hex("#14161C");
        static readonly Color PanelBg = Palette.Hex("#1B1E26");
        static readonly Color TileBg = Palette.Hex("#20242E");
        static readonly Color LineC = Palette.Hex("#2A2E39");
        static readonly Color TextC = Palette.Hex("#E8EAF0");
        static readonly Color SubC = Palette.Hex("#8F98A8");
        static readonly Color Accent = Palette.Hex("#7C9CFF");
        const string DragPrefix = "sentripet-picture:";

        /// <summary>The states as they are listed: the ones that win over the others first.</summary>
        public static readonly string[] Order = { "working", "done", "great", "good", "worried", "low", "empty", "unknown" };

        readonly Action<string> changed, use;
        readonly List<ThemeSpec> themes;
        readonly ComboBox themeBox = new ComboBox { MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
        readonly StackPanel library = new StackPanel();
        readonly StackPanel stateList = new StackPanel();
        readonly TextBlock status;
        readonly List<Button> themeButtons = new List<Button>();
        readonly Dictionary<string, Bitmap> thumbs = new Dictionary<string, Bitmap>();
        readonly Dictionary<string, Border> tiles = new Dictionary<string, Border>();
        ThemeSpec spec;
        string selected;

        /// <summary>The state → picture choices of the theme being edited (its states.json).</summary>
        public readonly Dictionary<string, string> Picks = new Dictionary<string, string>();
        /// <summary>The theme being edited (null when there is none).</summary>
        public ThemeSpec Theme { get { return spec; } }
        /// <summary>The window's content (the snapshot renders it).</summary>
        public Control Page { get; private set; }

        /// <param name="themeId">The theme to start with (null: the first one).</param>
        /// <param name="changed">A theme's pictures were saved (its id).</param>
        /// <param name="use">"Use this theme" (its id).</param>
        public StateEditorWindow(string themeId, Action<string> changed, Action<string> use)
        {
            this.changed = changed;
            this.use = use;
            Title = AppInfo.Name + " · " + L.T("編輯狀態圖片");
            Width = 920;
            Height = 680;
            MinWidth = 720;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = G.B(Bg);
            Foreground = G.B(TextC);
            FontFamily = G.Ui;
            FontSize = 13;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://SentriPet/Assets/app.ico"))); } catch { }
            themes = ThemeCatalog.CustomSpecs.Where(s => s.Usable && s.Folder != null).ToList();

            var root = new Grid { Margin = new Thickness(22, 18, 22, 14), Background = G.B(Bg), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
            var head = new StackPanel();
            head.Children.Add(Txt(L.T("編輯狀態圖片"), 19, TextC, FontWeight.Bold));
            var sub = Txt(L.T("把左邊的圖片拖到右邊的狀態上，桌寵在那個狀態就會換成那張圖；也可以從檔案總管把圖片直接拖進來。選好就會存起來。"), 12, SubC, FontWeight.Normal);
            sub.Margin = new Thickness(0, 4, 0, 12);
            head.Children.Add(sub);
            root.Children.Add(head);

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var label = Txt(L.T("造型"), 13.5, TextC, FontWeight.SemiBold);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 10, 0);
            bar.Children.Add(label);
            themeBox.Margin = new Thickness(0, 0, 14, 0);
            themeBox.ItemsSource = themes.Select(t => t.Name).ToList();
            themeBox.SelectionChanged += (s, e) => { if (themeBox.SelectedIndex >= 0 && themeBox.SelectedIndex < themes.Count) SelectTheme(themes[themeBox.SelectedIndex]); };
            bar.Children.Add(themeBox);
            themeButtons.Add(Btn(L.T("打開資料夾"), () => { if (spec != null) Integration.OpenPath(spec.Folder); }));
            themeButtons.Add(Btn(L.T("加入圖片…"), AddPictures));
            themeButtons.Add(Btn(L.T("用這個造型"), () =>
            {
                if (spec == null || use == null) return;
                use(spec.Id);
                Status(L.F("桌寵已換成「{0}」", spec.Name));
            }));
            foreach (var b in themeButtons) bar.Children.Add(b);
            Grid.SetRow(bar, 1);
            root.Children.Add(bar);

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,400") };
            var left = Panel(L.T("圖庫"), L.T("這個造型資料夾裡的圖片，子資料夾是一個分類。從檔案總管拖進來的圖片會複製到這裡。"), library);
            DragDrop.SetAllowDrop(left, true);
            DragDrop.AddDragOverHandler(left, (s, e) => e.DragEffects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None);
            DragDrop.AddDropHandler(left, (s, e) => { foreach (var f in DroppedFiles(e)) Import(f); });
            body.Children.Add(left);
            var right = Panel(L.T("狀態"), L.T("由上往下，先符合的先用。"), stateList);
            Grid.SetColumn(right, 2);
            body.Children.Add(right);
            Grid.SetRow(body, 2);
            root.Children.Add(body);

            status = Txt("", 12, SubC, FontWeight.Normal);
            status.Margin = new Thickness(2, 10, 0, 0);
            Grid.SetRow(status, 3);
            root.Children.Add(status);
            Page = root;
            Content = root;
            Closed += (s, e) => ClearThumbs();

            int start = themes.FindIndex(t => t.Id == themeId);
            if (themes.Count > 0) themeBox.SelectedIndex = Math.Max(0, start);
            else
            {
                foreach (var b in themeButtons) b.IsEnabled = false;
                library.Children.Add(Txt(L.T("還沒有可以用的自訂造型：到設定頁的「造型」按「打開造型資料夾」，會放進一個範例。"), 12.5, SubC, FontWeight.Normal));
            }
        }

        // ------------------------------------------------------------------ what the buttons and drops do

        /// <summary>Edits another theme (a fresh read of its folder, with its states.json).</summary>
        public void SelectTheme(ThemeSpec s)
        {
            spec = ThemeSpec.Load(s.Folder);
            selected = null;
            Picks.Clear();
            foreach (var kv in spec.Picks) Picks[kv.Key] = kv.Value;
            ClearThumbs();
            BuildLibrary();
            BuildStates();
            Status(spec.MainPicture == null ? L.T("這個造型沒有圖片元素，沒有可以換的圖。") : "");
        }

        /// <summary>Puts a picture of the library in a state and saves.</summary>
        public void Assign(string state, string file)
        {
            if (spec == null || spec.MainPicture == null || file == null || Array.IndexOf(ThemeSpec.StateNames, state) < 0) return;
            Picks[state] = file;
            Save(L.F("「{0}」換成 {1}", StateName(state), Path.GetFileNameWithoutExtension(file)));
        }

        /// <summary>Back to the theme's own picture for a state.</summary>
        public void Clear(string state)
        {
            if (spec == null || !Picks.Remove(state)) return;
            Save(L.F("「{0}」改回造型原本的圖", StateName(state)));
        }

        /// <summary>Copies a picture file into the theme's folder (a new name when one is taken); its name there, or null.</summary>
        public string Import(string path)
        {
            if (spec == null || path == null || !File.Exists(path)) return null;
            if (Array.IndexOf(ThemeSpec.ImageTypes, Path.GetExtension(path).ToLowerInvariant()) < 0)
            {
                Status(L.F("{0} 不是這個造型能用的圖片", Path.GetFileName(path)));
                return null;
            }
            string folder = Path.GetFullPath(spec.Folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return full.Substring(folder.Length).Replace('\\', '/');   // already in the library
            try
            {
                string name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
                string dest = Path.Combine(spec.Folder, name + ext);
                for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(spec.Folder, name + " (" + i + ")" + ext);
                File.Copy(path, dest);
                BuildLibrary();
                Status(L.F("已加入 {0}", Path.GetFileName(dest)));
                return Path.GetFileName(dest);
            }
            catch (Exception ex)
            {
                Log.Warn("state editor import: " + ex.Message);
                Status(L.F("加不進來：{0}", ex.Message));
                return null;
            }
        }

        void Save(string what)
        {
            try
            {
                ThemeSpec.SavePicks(spec.Folder, Picks);
                if (changed != null) changed(spec.Id);
                Status(what + L.T("（已存好；正在用這個造型時，桌寵會馬上換上）"));
            }
            catch (Exception ex)
            {
                Log.Error("state editor save", ex);
                Status(L.F("存不起來：{0}", ex.Message));
            }
            BuildStates();
        }

        async void AddPictures()
        {
            if (spec == null) return;
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = L.T("加入圖片…"),
                    AllowMultiple = true,
                    FileTypeFilter = new[] { new FilePickerFileType(L.T("圖片")) { Patterns = ThemeSpec.ImageTypes.Select(x => "*" + x).ToList() } },
                });
                foreach (var f in files)
                {
                    string path = f.TryGetLocalPath();
                    if (path != null) Import(path);
                }
            }
            catch (Exception ex) { Log.Warn("state editor picker: " + ex.Message); }
        }

        void Select(string file)
        {
            selected = file;
            foreach (var kv in tiles) kv.Value.BorderBrush = G.B(kv.Key == file ? Accent : Colors.Transparent);
            Status(L.F("選了 {0}：拖到右邊的狀態上，或點一下那個狀態", Path.GetFileNameWithoutExtension(file)));
        }

        void Status(string text)
        {
            status.Text = L.Finish(text ?? "");
        }

        // ------------------------------------------------------------------ the library

        void BuildLibrary()
        {
            library.Children.Clear();
            tiles.Clear();
            var files = ThemeSpec.Pictures(spec.Folder);
            if (files.Count == 0)
            {
                library.Children.Add(Txt(L.T("這個資料夾裡還沒有圖片：把圖片拖進來，或按「加入圖片…」。"), 12.5, SubC, FontWeight.Normal));
                return;
            }
            foreach (var group in files.GroupBy(f => f.Contains("/") ? f.Substring(0, f.IndexOf('/')) : ""))
            {
                var title = Txt(group.Key.Length == 0 ? L.T("這個資料夾") : group.Key, 12.5, SubC, FontWeight.SemiBold);
                title.Margin = new Thickness(2, library.Children.Count == 0 ? 0 : 10, 0, 6);
                library.Children.Add(title);
                var wrap = new WrapPanel();
                foreach (var f in group) wrap.Children.Add(Tile(f));
                library.Children.Add(wrap);
            }
        }

        Border Tile(string file)
        {
            var pic = new Image { Source = Thumb(file), Width = 84, Height = 84, Stretch = Stretch.Uniform };
            var name = new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(file), FontSize = 11, Foreground = G.B(TextC), TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 92, Margin = new Thickness(0, 3, 0, 0),
            };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(pic);
            sp.Children.Add(name);
            var tile = new Border
            {
                Width = 100, Height = 116, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(4, 6, 4, 4),
                CornerRadius = new CornerRadius(10), Background = G.B(TileBg), BorderThickness = new Thickness(2),
                BorderBrush = G.B(file == selected ? Accent : Colors.Transparent), Child = sp, Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(tile, file);
            tile.PointerPressed += async (s, e) =>
            {
                if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return;
                Select(file);
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText(DragPrefix + file));
                try { await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy); }
                catch (Exception ex) { Log.Warn("state editor drag: " + ex.Message); }
            };
            tiles[file] = tile;
            return tile;
        }

        /// <summary>A small copy of a picture (the first frame of an animation).</summary>
        Bitmap Thumb(string file)
        {
            Bitmap b;
            if (file == null || spec == null) return null;
            if (thumbs.TryGetValue(file, out b)) return b;
            try
            {
                using (var fs = File.OpenRead(Path.Combine(spec.Folder, file)))
                    b = Bitmap.DecodeToWidth(fs, 168, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception ex) { Log.Warn("state editor thumbnail " + file + ": " + ex.Message); b = null; }
            thumbs[file] = b;
            return b;
        }

        void ClearThumbs()
        {
            foreach (var b in thumbs.Values) if (b != null) b.Dispose();
            thumbs.Clear();
        }

        // ------------------------------------------------------------------ the states

        /// <summary>A state's name on screen.</summary>
        public static string StateName(string state)
        {
            switch (state)
            {
                case "working": return L.T("工作中");
                case "done": return L.T("AI 做完了");
                case "great": return L.T("額度很多");
                case "good": return L.T("還不錯");
                case "worried": return L.T("有點擔心");
                case "low": return L.T("快沒了");
                case "empty": return L.T("用完了");
                default: return L.T("沒資料");
            }
        }

        static string StateHint(string state)
        {
            switch (state)
            {
                case "working": return L.T("AI 正在回覆時，優先於下面的狀態");
                case "done": return L.T("Claude Code／Codex 做完比較久的事後 30 秒");
                case "great": return L.T("剩 60% 以上");
                case "good": return L.T("剩 30～60%");
                case "worried": return L.T("剩 12～30%");
                case "low": return L.T("剩不到 12%");
                case "empty": return L.T("額度用完了");
                default: return L.T("抓不到用量時");
            }
        }

        void BuildStates()
        {
            stateList.Children.Clear();
            if (spec == null) return;
            foreach (var state in Order)
            {
                string file, source;
                if (Picks.TryGetValue(state, out file)) source = L.F("你選的：{0}", Path.GetFileNameWithoutExtension(file));
                else if (spec.Authored.TryGetValue(state, out file)) source = L.F("造型原本的：{0}", Path.GetFileNameWithoutExtension(file));
                else source = state == "done" ? L.T("沒有圖：照工作中或心情顯示") : L.T("沒有圖：用造型的預設圖");

                var slot = new Border
                {
                    Width = 72, Height = 72, CornerRadius = new CornerRadius(10), Background = G.B(TileBg), BorderThickness = new Thickness(2),
                    BorderBrush = G.B(LineC), Cursor = new Cursor(StandardCursorType.Hand),
                    Child = file != null ? new Image { Source = Thumb(file), Margin = new Thickness(4), Stretch = Stretch.Uniform }
                                         : (Control)new TextBlock { Text = "+", FontSize = 22, Foreground = G.B(SubC), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                string st = state;
                DragDrop.SetAllowDrop(slot, true);
                DragDrop.AddDragOverHandler(slot, (s, e) =>
                {
                    e.DragEffects = DraggedPicture(e) != null || HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
                    slot.BorderBrush = G.B(Accent);
                    e.Handled = true;
                });
                DragDrop.AddDragLeaveHandler(slot, (s, e) => slot.BorderBrush = G.B(LineC));
                DragDrop.AddDropHandler(slot, (s, e) =>
                {
                    e.Handled = true;
                    string pic = DraggedPicture(e) ?? DroppedFiles(e).Select(Import).FirstOrDefault(x => x != null);
                    if (pic != null) Assign(st, pic);
                    else slot.BorderBrush = G.B(LineC);
                });
                slot.PointerPressed += (s, e) => { if (selected != null) Assign(st, selected); };

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
                text.Children.Add(Txt(StateName(state), 13.5, TextC, FontWeight.SemiBold));
                var hint = Txt(StateHint(state), 11.5, SubC, FontWeight.Normal);
                hint.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(hint);
                var src = Txt(source, 11.5, Picks.ContainsKey(state) ? Accent : SubC, FontWeight.Normal);
                src.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(src);

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 6, 0, 6) };
                row.Children.Add(slot);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                if (Picks.ContainsKey(state))
                {
                    var undo = Btn(L.T("還原"), () => Clear(st));
                    undo.Margin = new Thickness(0);
                    undo.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(undo, 2);
                    row.Children.Add(undo);
                }
                if (stateList.Children.Count > 0) stateList.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
                stateList.Children.Add(row);
            }
        }

        // ------------------------------------------------------------------ drag and drop

        static string DraggedPicture(DragEventArgs e)
        {
            var text = e.DataTransfer.TryGetText();
            return text != null && text.StartsWith(DragPrefix) ? text.Substring(DragPrefix.Length) : null;
        }

        static bool HasFiles(DragEventArgs e)
        {
            return e.DataTransfer.Contains(DataFormat.File);
        }

        static List<string> DroppedFiles(DragEventArgs e)
        {
            var items = e.DataTransfer.TryGetFiles();
            return items == null ? new List<string>() : items.Select(i => i.TryGetLocalPath()).Where(p => p != null).ToList();
        }

        // ------------------------------------------------------------------ layout helpers

        static TextBlock Txt(string s, double size, Color c, FontWeight w)
        {
            return new TextBlock { Text = L.Finish(s), FontSize = size, Foreground = G.B(c), FontWeight = w, TextWrapping = TextWrapping.Wrap };
        }

        static Button Btn(string text, Action click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 7) };
            b.Click += (s, e) =>
            {
                try { click(); }
                catch (Exception ex) { Log.Error("button " + text, ex); }
            };
            return b;
        }

        static Border Panel(string title, string hint, Control content)
        {
            var sp = new DockPanel();
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(Txt(title, 15, TextC, FontWeight.Bold));
            var h = Txt(hint, 11.5, SubC, FontWeight.Normal);
            h.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(h);
            DockPanel.SetDock(head, Dock.Top);
            sp.Children.Add(head);
            sp.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
            return new Border
            {
                Background = G.B(PanelBg), BorderBrush = G.B(LineC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 14, 12, 14), Child = sp,
            };
        }
    }
}
