using System.Windows.Controls;

namespace Heracles.Support;

/// <summary>The app's small dialogs: asking for a line of text, and saying what went wrong.</summary>
internal static class Prompts
{
    /// <summary>Ask for one line of text. Null if the user cancelled.</summary>
    public static string? Ask(Window? owner, string title, string initial, string confirm)
    {
        var input = new TextBox { Text = initial, MinWidth = 320, Margin = new Thickness(0, 0, 0, 16) }.Id("PromptInput");
        var ok = new Button { Content = confirm, IsDefault = true, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) }.Id("PromptOk");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 }.Id("PromptCancel");
        var dialog = Dialog(owner, title, input, cancel, ok);
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    public static void Error(Window? owner, string text)
    {
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(0, 0, 0, 16) }.Id("ErrorText");
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 88 }.Id("ErrorOk");
        Dialog(owner, "Something went wrong", message, ok).ShowDialog();
    }

    /// <summary>Say something that is no fault: a title, a line or two, and OK.</summary>
    public static void Message(Window? owner, string title, string text)
    {
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(0, 0, 0, 16) }
            .Id("MessageText").Named(text);
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 88 }.Id("MessageOk");
        Dialog(owner, title, message, ok).ShowDialog();
    }

    private static Window Dialog(Window? owner, string title, UIElement body, params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in buttons) row.Children.Add(button);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) }.Id("PromptTitle"));
        panel.Children.Add(body);
        panel.Children.Add(row);
        return new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            // An owner that is not showing cannot own a dialog.
            Owner = owner is { IsVisible: true } ? owner : null,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
    }
}
