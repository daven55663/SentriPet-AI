using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SentriPet
{
    /// <summary>
    /// A theme made by the user (#24), drawn from its <see cref="ThemeSpec"/>: a card per AI with pictures (one per
    /// mood if the theme has them), texts, bars, rings and boxes. Speech goes to the floating bubble.
    /// </summary>
    class CustomTheme : Theme
    {
        readonly ThemeSpec spec;
        readonly Dictionary<string, Bitmap> pictures = new Dictionary<string, Bitmap>();
        Panel list;

        class Item
        {
            public ElementSpec E;
            public Control Control;
            public Image Img;
            public string Picture;
            public TextBlock Text;
            public Border Fill;
            public Path Arc;
            public string Brush;               // what the dynamic colour was last set from
            public TranslateTransform Move;
            public ScaleTransform Scale;
            public double Phase;
        }

        class Card
        {
            public string Id;
            public List<Item> Items = new List<Item>();
        }

        readonly List<Card> cards = new List<Card>();

        public CustomTheme(ThemeSpec spec) { this.spec = spec; }

        public override string Id { get { return spec.Id; } }
        public override string Name { get { return spec.Name; } }
        public override string Mood { get { return spec.Mood; } }
        public override string Blurb { get { return spec.Blurb; } }
        /// <summary>The theme's own lines (#28), or null.</summary>
        public LineSet Lines { get { return spec.Lines; } }

        /// <summary>A picture of the theme, loaded once (null when it can't be decoded).</summary>
        Bitmap Picture(string file)
        {
            if (file == null || spec.Folder == null) return null;
            Bitmap b;
            if (pictures.TryGetValue(file, out b)) return b;
            try { b = new Bitmap(System.IO.Path.Combine(spec.Folder, file)); }
            catch (Exception ex) { Log.Warn("theme " + spec.Id + ": " + file + ": " + ex.Message); b = null; }
            pictures[file] = b;
            return b;
        }

        IBrush BoxBrush(BoxSpec box)
        {
            if (box == null) return null;
            var img = Picture(box.Image);
            if (img != null) return new ImageBrush(img) { Stretch = Stretch.Fill };
            return box.Color != null ? G.B(Palette.Hex(box.Color)) : null;
        }

        Border Boxed(BoxSpec box, Control child)
        {
            var b = new Border { Child = child };
            if (box == null) return b;
            b.Background = BoxBrush(box);
            b.CornerRadius = new CornerRadius(box.Radius);
            b.ClipToBounds = box.Radius > 0 || box.Image != null;
            if (box.Border != null) { b.BorderBrush = G.B(Palette.Hex(box.Border)); b.BorderThickness = new Thickness(box.BorderWidth); }
            b.Padding = new Thickness(box.Padding);
            return b;
        }

        protected override Control CreateRoot()
        {
            if (spec.Layout == "grid") list = new UniformGrid { Columns = spec.Columns };
            else list = new StackPanel { Orientation = spec.Layout == "column" ? Orientation.Vertical : Orientation.Horizontal, Spacing = spec.Gap };
            var root = Boxed(spec.Background, list);
            root.HorizontalAlignment = HorizontalAlignment.Left;
            return root;
        }

        protected override void Rebuild()
        {
            list.Children.Clear();
            cards.Clear();
            foreach (var v in Views.Count > 0 ? Views : new List<ProviderView> { new ProviderView { Id = "_", Name = L.T("偵測中"), Color = Rgba.Hex("#94A3B8"), Error = L.T("正在尋找電腦上的 AI…"), Mood = SentriPet.Mood.Unknown } })
            {
                var card = new Card { Id = v.Id };
                var canvas = new Canvas { Width = spec.CardWidth, Height = spec.CardHeight, ClipToBounds = true };
                foreach (var e in spec.Elements)
                {
                    var item = Make(e);
                    if (item == null) continue;
                    Canvas.SetLeft(item.Control, e.X);
                    Canvas.SetTop(item.Control, e.Y);
                    canvas.Children.Add(item.Control);
                    card.Items.Add(item);
                }
                var box = Boxed(spec.Card, canvas);
                box.Tag = "pv:" + v.Id;
                if (spec.Layout == "grid") box.Margin = new Thickness(spec.Gap / 2);
                list.Children.Add(box);
                cards.Add(card);
            }
        }

        Item Make(ElementSpec e)
        {
            var item = new Item { E = e, Phase = Rng.NextDouble() * 6 };
            switch (e.Type)
            {
                case "image":
                    item.Img = new Image { Width = e.Width, Height = e.Height, Stretch = Stretch.Uniform };
                    if (e.Animate == "bob" || e.Animate == "breathe")
                    {
                        item.Move = new TranslateTransform();
                        item.Scale = new ScaleTransform(1, 1);
                        var tg = new TransformGroup();
                        tg.Children.Add(item.Scale);
                        tg.Children.Add(item.Move);
                        item.Img.RenderTransform = tg;
                        item.Img.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
                    }
                    item.Control = item.Img;
                    break;
                case "text":
                    item.Text = new TextBlock
                    {
                        FontSize = e.Size,
                        FontWeight = e.Bold ? FontWeight.Bold : FontWeight.Normal,
                        FontFamily = e.Font == "number" ? G.Num : e.Font == "mono" ? G.Mono : G.Ui,
                        TextAlignment = e.Align == "center" ? TextAlignment.Center : e.Align == "right" ? TextAlignment.Right : TextAlignment.Left,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = e.Height > e.Size * 1.8 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    };
                    if (e.Width > 0) item.Text.Width = e.Width;
                    if (e.Height > 0) item.Text.MaxHeight = e.Height;
                    item.Control = item.Text;
                    break;
                case "bar":
                {
                    Border fill;
                    var bar = G.Bar(e.Width, e.Height, Solid(e.Track), out fill);
                    if (e.Radius > 0)   // (rounded ends by default)
                        foreach (var b in bar.Children.OfType<Border>()) b.CornerRadius = new CornerRadius(e.Radius);
                    item.Fill = fill;
                    item.Control = bar;
                    break;
                }
                case "ring":
                {
                    double d = e.Width, t = e.Thickness, r = (d - t) / 2;
                    var host = new Canvas { Width = d, Height = d };
                    host.Children.Add(new Ellipse { Width = d - t, Height = d - t, Stroke = Solid(e.Track), StrokeThickness = t, Margin = new Thickness(t / 2) });
                    item.Arc = new Path { StrokeThickness = t, StrokeLineCap = PenLineCap.Round };
                    host.Children.Add(item.Arc);
                    item.Control = host;
                    break;
                }
                case "rect":
                    item.Control = new Border { Width = e.Width, Height = e.Height, CornerRadius = new CornerRadius(e.Radius), Background = Solid(e.Color) };
                    break;
                default:
                    return null;
            }
            item.Control.IsHitTestVisible = false;
            return item;
        }

        /// <summary>A fixed colour ("#…"); dynamic ones are set in Refresh.</summary>
        static IBrush Solid(string c)
        {
            return c != null && c.StartsWith("#") ? G.B(Palette.Hex(c)) : null;
        }

        static Color Dynamic(string c, ProviderView v, Meter m)
        {
            var provider = v.Color.ToColor();
            switch (c)
            {
                case "level": return !v.HasData ? Palette.Hex("#94A3B8") : Palette.Level(m == null || m.Unlimited ? 100 : m.Remaining);
                case "provider": return provider;
                case "provider-light": return Palette.Lighten(provider, 0.45);
                case "provider-dark": return Palette.Darken(provider, 0.35);
                default: return Palette.Hex(c);
            }
        }

        protected override void Refresh()
        {
            var views = Views.Count > 0 ? Views : null;
            foreach (var card in cards)
            {
                var v = views != null ? views.FirstOrDefault(x => x.Id == card.Id) : null;
                if (v == null) v = new ProviderView { Id = card.Id, Name = L.T("偵測中"), Color = Rgba.Hex("#94A3B8"), Error = L.T("正在尋找電腦上的 AI…"), Mood = SentriPet.Mood.Unknown };
                foreach (var it in card.Items)
                {
                    var e = it.E;
                    bool shown = ThemeSpec.Shown(e, v);
                    if (it.Control.IsVisible != shown) it.Control.IsVisible = shown;
                    if (!shown) continue;
                    var m = ThemeSpec.MeterFor(e, v);
                    switch (e.Type)
                    {
                        case "image":
                        {
                            string pic = ThemeSpec.PictureFor(e, v);
                            if (pic != it.Picture) { it.Picture = pic; it.Img.Source = Picture(pic); }
                            break;
                        }
                        case "text":
                        {
                            string text = ThemeSpec.TextFor(e, v);
                            if (it.Text.Text != text) it.Text.Text = text;
                            SetBrush(it, e.Color, v, m, b => it.Text.Foreground = b);
                            break;
                        }
                        case "bar":
                        {
                            double left = !v.HasData || m == null ? 0 : m.Unlimited ? 100 : m.Remaining;
                            double w = Math.Round(e.Width * Math.Max(0, Math.Min(100, left)) / 100, 1);
                            if (Math.Abs(it.Fill.Width - w) > 0.05) it.Fill.Width = w;
                            SetBrush(it, e.Fill, v, m, b => it.Fill.Background = b);
                            break;
                        }
                        case "ring":
                        {
                            double left = !v.HasData || m == null ? 0 : m.Unlimited ? 100 : m.Remaining;
                            double sweep = Math.Round(360 * Math.Max(0, Math.Min(100, left)) / 100, 1);
                            string key = sweep.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (it.Arc.Tag as string != key)
                            {
                                it.Arc.Tag = key;
                                it.Arc.Data = sweep < 0.5 ? null : G.Arc(new Point(e.Width / 2, e.Width / 2), (e.Width - e.Thickness) / 2, 0, Math.Min(359.9, sweep));
                            }
                            SetBrush(it, e.Fill, v, m, b => it.Arc.Stroke = b);
                            break;
                        }
                        case "rect":
                            if (e.Color != null && !e.Color.StartsWith("#")) SetBrush(it, e.Color, v, m, b => ((Border)it.Control).Background = b);
                            break;
                    }
                }
            }
        }

        static void SetBrush(Item it, string c, ProviderView v, Meter m, Action<IBrush> set)
        {
            var color = Dynamic(c, v, m);
            string key = color.ToString();
            if (it.Brush == key) return;
            it.Brush = key;
            set(G.B(color));
        }

        public override void Tick(double dt)
        {
            base.Tick(dt);
            foreach (var card in cards)
                foreach (var it in card.Items)
                {
                    if (it.Move == null || !it.Control.IsVisible) continue;
                    if (it.E.Animate == "bob")
                    {
                        double y = Math.Round(Math.Sin(Time * 2.2 + it.Phase) * 2.5, 1);
                        if (it.Move.Y != y) it.Move.Y = y;
                    }
                    else
                    {
                        double s = Math.Round(1 + Math.Sin(Time * 1.7 + it.Phase) * 0.025, 3);
                        if (it.Scale.ScaleY != s) { it.Scale.ScaleX = 2 - s; it.Scale.ScaleY = s; }
                    }
                }
        }

        public override void Detach()
        {
            foreach (var b in pictures.Values) if (b != null) b.Dispose();
            pictures.Clear();
        }
    }
}
