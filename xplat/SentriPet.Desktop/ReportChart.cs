using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SentriPet
{
    /// <summary>
    /// The quota-use bars (#16), in the widget's menu and on the settings page: one bar per finished weekly/monthly
    /// window (its height is the share used: green used well, amber some wasted, red mostly wasted), and a hollow bar
    /// for the window in progress.
    /// </summary>
    static class ReportChart
    {
        public static Color ColorFor(double used) { return Palette.Hex(used >= 80 ? "#4ADE80" : used >= 50 ? "#FBBF24" : "#F87171"); }

        /// <param name="windows">finished windows, oldest first</param>
        /// <param name="current">the window in progress (its Used so far), or null</param>
        /// <param name="dates">a date under every bar (the settings page) or none (the menu: the tooltips have them)</param>
        public static StackPanel Bars(IList<WindowResult> windows, Meter current, double height, double width, bool dates)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var r in windows)
            {
                var col = Column(r.Used, height, width, false, r.SeenToEnd ? 0.9 : 0.45);
                if (dates) col.Children.Add(Date(r.EndedAt.ToLocalTime().Month + "/" + r.EndedAt.ToLocalTime().Day));
                ToolTip.SetTip(col, L.F("{0} 重置：用掉 {1}", Fmt.When(r.EndedAt), Fmt.Pct(r.Used)) +
                                    (r.SeenToEnd ? "" : "\n" + L.T("這一期最後一段 SentriPet 沒在執行，實際可能用得更多")) +
                                    (r.NudgeLevel > 0 ? "\n" + L.T("這一期有催過你") : ""));
                sp.Children.Add(col);
            }
            if (current != null)
            {
                var col = Column(current.Used, height, width, true, 0.5);
                if (dates) col.Children.Add(Date(L.T("這期")));
                ToolTip.SetTip(col, current.ResetsAt.HasValue
                    ? L.F("這期到目前用了 {0}，{1} 重置", Fmt.Pct(current.Used), Fmt.When(current.ResetsAt))
                    : L.F("這期到目前用了 {0}", Fmt.Pct(current.Used)));
                sp.Children.Add(col);
            }
            return sp;
        }

        static StackPanel Column(double used, double height, double width, bool inProgress, double alpha)
        {
            var col = new StackPanel { Width = width, Margin = new Thickness(2, 0, 2, 0) };
            var track = new Border
            {
                Height = height, CornerRadius = new CornerRadius(4), ClipToBounds = true,
                Background = G.B(Colors.White, inProgress ? 0.02 : 0.06),
                // the window in progress: an outline, so it reads as "not over yet"
                BorderBrush = inProgress ? G.B(Colors.White, 0.35) : null,
                BorderThickness = new Thickness(inProgress ? 1 : 0),
            };
            track.Child = new Border
            {
                Height = Math.Max(2, (height - (inProgress ? 2 : 0)) * Math.Max(0, Math.Min(100, used)) / 100),
                VerticalAlignment = VerticalAlignment.Bottom,
                // (a window in progress is not good or bad yet: a neutral colour)
                Background = G.B(inProgress ? Palette.Hex("#93C5FD") : ColorFor(used), alpha), CornerRadius = new CornerRadius(3),
            };
            col.Children.Add(track);
            return col;
        }

        static TextBlock Date(string text)
        {
            return new TextBlock { Text = text, FontSize = 9.5, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        }

        /// <summary>
        /// A quota in the menu: its name and the numbers (last window, average, this one so far) above its bars — the
        /// report at a glance, without opening the settings.
        /// </summary>
        public static Control MenuEntry(string name, Meter current, IList<WindowResult> past)
        {
            var box = new StackPanel { MinWidth = 280, Margin = new Thickness(0, 3, 0, 3) };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.Children.Add(new TextBlock { Text = name + " · " + current.Label, FontWeight = FontWeight.SemiBold });
            var last = past.Count > 0 ? past[past.Count - 1] : null;
            var numbers = new TextBlock
            {
                Text = last != null
                    ? L.F("上期 {0} · 平均 {1} · 這期 {2}", Fmt.Pct(last.Used), Fmt.Pct(past.Average(r => r.Used)), Fmt.Pct(current.Used))
                    : L.F("這期到目前用了 {0}", Fmt.Pct(current.Used)),
                Opacity = 0.65, FontSize = 12, Margin = new Thickness(16, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(numbers, 1);
            head.Children.Add(numbers);
            box.Children.Add(head);
            var bars = Bars(past, current, 30, 15, false);
            bars.Margin = new Thickness(-2, 6, 0, 0);
            box.Children.Add(bars);
            if (last == null)
                box.Children.Add(new TextBlock { Text = L.T("第一次重置後就會開始記錄每一期"), FontSize = 11, Opacity = 0.5, Margin = new Thickness(0, 4, 0, 0) });
            return box;
        }
    }
}
