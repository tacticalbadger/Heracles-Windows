using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Heracles.Support;

/// <summary>Small things the views share.</summary>
internal static class Ui
{
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The system accent colour, thinned. For highlights that must work in light and dark.</summary>
    public static Brush Accent(double opacity)
    {
        var brush = new SolidColorBrush(SystemColors.AccentColor) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    /// <summary>A quiet grey for lines between things, readable on light and on dark.</summary>
    public static Brush Hairline { get; } = Frozen(new SolidColorBrush(Colors.Gray) { Opacity = 0.3 });

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Make a border a surface that floats over the window (Quick Open, a
    /// popup): the theme's own solid background and text colour, so it reads
    /// in light and in dark. The system's "window" colour is white whatever
    /// the theme, and a popup does not inherit the window's text colour.
    /// </summary>
    public static void AsSurface(Border border)
    {
        border.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        border.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        border.BorderBrush = Hairline;
        border.BorderThickness = new Thickness(1);
    }

    /// <summary>A row of a list, without the room the theme gives rows by default.</summary>
    public static ListBoxItem Row(object content, object? tag = null) => new()
    {
        Content = content,
        Tag = tag,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Padding = new Thickness(8, 3, 8, 3),
        MinHeight = 0,
    };

    /// <summary>A symbol from the system icon font.</summary>
    public static TextBlock Glyph(string code, double size) => new()
    {
        Text = code,
        FontFamily = Icons,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>The id the end-to-end checks and screen readers find an element by.</summary>
    public static T Id<T>(this T element, string id) where T : DependencyObject
    {
        AutomationProperties.SetAutomationId(element, id);
        return element;
    }

    public static T Named<T>(this T element, string name) where T : DependencyObject
    {
        AutomationProperties.SetName(element, name);
        return element;
    }

    /// <summary>A button with no chrome until it is pointed at.</summary>
    public static Button QuietButton(object content, Action onClick, string? tip = null)
    {
        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 0,
            MinHeight = 0,
            Focusable = false,
            ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    public static MenuItem Item(string header, Action onClick, string? gesture = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>Text on one line, cut with an ellipsis where it does not fit.</summary>
    public static TextBlock Line(string text, double opacity = 1) => new()
    {
        Text = text,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = opacity,
    };

    public static bool ControlIsDown => (Keyboard.Modifiers & ModifierKeys.Control) != 0;
}

/// <summary>
/// Makes a part of the window that is only a container (a pane, an overlay)
/// something a screen reader, and the end-to-end checks, can find by its id.
/// Panels and borders are otherwise invisible to them.
/// </summary>
internal sealed class LandmarkPeer(FrameworkElement owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
{
    protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
        System.Windows.Automation.Peers.AutomationControlType.Pane;

    protected override string GetClassNameCore() => Owner.GetType().Name;

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;
}

/// <summary>A border that can be found by its id: see <see cref="LandmarkPeer"/>.</summary>
internal sealed class Landmark : Border
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new LandmarkPeer(this);
}

/// <summary>A command that is just something to do.</summary>
internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
