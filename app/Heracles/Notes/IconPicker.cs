using System.Globalization;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Heracles.Sidebar;

namespace Heracles.Notes;

/// <summary>A grid of icons to give a note, and a way to take its icon away.</summary>
internal sealed class IconPicker : Popup
{
    private static readonly (string Name, string[] Icons)[] Groups =
    [
        ("Notes", ["📝", "📓", "📔", "📒", "📚", "📖", "🗒️", "📄", "📌", "📎", "🔖", "✏️"]),
        ("Work", ["💼", "📊", "📈", "🗂️", "📁", "🧾", "💡", "🎯", "✅", "⏰", "📅", "🔧"]),
        ("Life", ["🏠", "🧭", "✈️", "🚗", "🍳", "🛒", "💰", "🎁", "🎵", "🎬", "🏃", "🌱"]),
        ("Signs", ["⭐", "🔥", "❤️", "⚠️", "❓", "🔒", "🚀", "🌍", "☀️", "🌙", "🧪", "🐛"]),
    ];

    public IconPicker(UIElement target, string? current, Action<string?> choose)
    {
        PlacementTarget = target;
        Placement = PlacementMode.Bottom;
        StaysOpen = false;
        AllowsTransparency = true;

        var panel = new StackPanel { Width = 230, Margin = new Thickness(12) };
        foreach (var (name, icons) in Groups)
        {
            panel.Children.Add(new TextBlock { Text = name, FontSize = 11, Opacity = 0.65, Margin = new Thickness(0, 4, 0, 2) });
            var grid = new UniformGrid { Columns = 6 };
            foreach (var icon in icons)
            {
                var button = Ui.QuietButton(NoteIcon.EmojiText(icon, 18), () => Choose(icon)).Id("Icon:" + icon).Named(icon);
                button.Width = 34;
                button.Height = 34;
                button.Focusable = true;
                if (icon == current) button.Background = Ui.Accent(0.25);
                grid.Children.Add(button);
            }
            panel.Children.Add(grid);
        }
        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });

        // Any emoji at all, from the keyboard or the emoji panel (Win + .).
        var typed = new TextBox { ToolTip = "Or type one", MinWidth = 120 }.Id("IconTyped").Named("Or type one");
        typed.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var text = typed.Text.Trim();
            // The first whole character, however many code units it takes.
            if (text.Length > 0) Choose(StringInfo.GetNextTextElement(text));
            e.Handled = true;
        };
        var remove = new Button { Content = "Remove", IsEnabled = !string.IsNullOrEmpty(current), Margin = new Thickness(8, 0, 0, 0) }.Id("IconRemove");
        remove.Click += (_, _) => Choose(null);
        var row = new DockPanel();
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);
        row.Children.Add(typed);
        panel.Children.Add(row);

        var frame = new Landmark { Child = panel, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(8) };
        Ui.AsSurface(frame);
        Child = frame.Id("IconPicker");

        void Choose(string? icon)
        {
            IsOpen = false;
            choose(icon);
        }
    }
}
