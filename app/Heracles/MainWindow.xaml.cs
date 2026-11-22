namespace Heracles;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>The library opened. Until there is more to show, say how much is in it.</summary>
    internal void ShowLibrary(int noteCount)
    {
        LibraryStatus.Text = noteCount == 1 ? "1 note" : $"{noteCount} notes";
    }

    /// <summary>The library did not open. Say so, and why.</summary>
    internal void ShowStartupError(string reason)
    {
        LibraryStatus.Visibility = Visibility.Collapsed;
        StartupErrorTitle.Visibility = Visibility.Visible;
        StartupError.Text = reason;
        StartupError.Visibility = Visibility.Visible;
    }
}
