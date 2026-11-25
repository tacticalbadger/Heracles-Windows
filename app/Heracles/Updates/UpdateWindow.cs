using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Heracles.Updates;

/// <summary>
/// The window that offers an update: what the new version is, what changed
/// in it, and the three things that can be done about it. Once "Install
/// Update" is chosen it shows the download, and then asks when to restart.
///
/// It only shows and asks. <see cref="UpdateCenter"/> does what is chosen.
/// The notes are drawn as text of the window's own: nothing in the app is a
/// web view, and nothing in a feed can be more than words here.
/// </summary>
internal sealed class UpdateWindow : Window
{
    public const string Headline = "A new version of Heracles is available";

    private readonly TextBlock status;
    private readonly ProgressBar progress;
    private readonly Button skip;
    private readonly Button later;
    private readonly Button install;
    private readonly Button cancel;
    private readonly Button restart;
    private readonly Button close;
    private readonly string? whyNotHere;
    private readonly string version;

    /// <summary>"Install Update": fetch it.</summary>
    public event Action? InstallChosen;

    /// <summary>"Skip This Version": say no more about this one.</summary>
    public event Action? SkipChosen;

    /// <summary>"Cancel", while it is being fetched.</summary>
    public event Action? CancelChosen;

    /// <summary>"Install and Restart", once it is fetched and checked.</summary>
    public event Action? RestartChosen;

    /// <summary>How much of the download has arrived, from nothing to one.</summary>
    public IProgress<double> Progress { get; }

    /// <param name="whyNotHere">Why this copy cannot replace itself, when it cannot. The update is then described and not offered.</param>
    public UpdateWindow(UpdateManifest manifest, AppVersion own, string? whyNotHere)
    {
        this.whyNotHere = whyNotHere;
        version = manifest.Version;
        Title = "Heracles";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(22, 20, 22, 20) };
        panel.Children.Add(new TextBlock
        {
            Text = Headline,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        var versions = $"Heracles {manifest.Version} is available. You have {own.Version}.";
        panel.Children.Add(new TextBlock { Text = versions, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }
            .Id("UpdateVersions").Named(versions));

        var notes = UpdateNotes.Read(manifest.Notes);
        if (notes.Count > 0)
        {
            var lines = new StackPanel();
            foreach (var line in notes) lines.Children.Add(Line(line));
            panel.Children.Add(new Landmark
            {
                Child = new ScrollViewer
                {
                    Content = lines,
                    MaxHeight = 280,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Focusable = false,
                    Padding = new Thickness(0, 0, 8, 0),
                },
                BorderBrush = Ui.Hairline,
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0, 10, 0, 6),
                Margin = new Thickness(0, 0, 0, 12),
            }.Id("UpdateNotes").Named(string.Join(" ", notes.Select(line => line.Text))));
        }

        status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed }.Id("UpdateStatus");
        panel.Children.Add(status);
        progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed }.Id("UpdateProgress");
        panel.Children.Add(progress);
        Progress = new Progress<double>(share => progress.Value = share);

        skip = Choice("Skip This Version", "UpdateSkip", () => SkipChosen?.Invoke());
        later = Choice("Remind Me Later", "UpdateLater", Close);
        install = Choice("Install Update", "UpdateInstall", () => InstallChosen?.Invoke());
        cancel = Choice("Cancel", "UpdateCancel", () => CancelChosen?.Invoke());
        restart = Choice("Install and Restart", "UpdateRestart", () => RestartChosen?.Invoke());
        close = Choice("Close", "UpdateClose", Close);
        later.Margin = restart.Margin = install.Margin = new Thickness(8, 0, 0, 0);

        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(skip, Dock.Left);
        buttons.Children.Add(skip);
        foreach (var button in new[] { install, restart, later, cancel, close })
        {
            DockPanel.SetDock(button, Dock.Right);
            buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);

        Content = new Landmark { Child = panel }.Id("UpdateWindow").Named(Headline);
        ShowOffer();
    }

    private static Button Choice(string words, string id, Action chosen)
    {
        var button = new Button { Content = words, MinWidth = 88 }.Id(id);
        button.Click += (_, _) => chosen();
        return button;
    }

    /// <summary>One line of the notes: its words, with a dot before a point of a list.</summary>
    private static TextBlock Line(NoteLine line)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        if (line.Kind == NoteLineKind.Heading) text.FontWeight = FontWeights.SemiBold;
        if (line.Kind == NoteLineKind.Point)
        {
            // U+2022, a bullet, written by number.
            text.Inlines.Add(new Run((char)0x2022 + "  "));
            text.Margin = new Thickness(4, 0, 0, 6);
        }
        foreach (var run in line.Runs)
        {
            var words = new Run(run.Text);
            if (run.Bold) words.FontWeight = FontWeights.SemiBold;
            if (run.Code) words.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            text.Inlines.Add(words);
        }
        return text;
    }

    private void Show(string? words, bool withProgress, params Button[] shown)
    {
        status.Text = words ?? "";
        status.Visibility = words is null ? Visibility.Collapsed : Visibility.Visible;
        // What a screen reader, and a check, is told the window now says.
        status.Named(words ?? "");
        progress.Visibility = withProgress ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { skip, later, install, cancel, restart, close })
        {
            button.Visibility = shown.Contains(button) ? Visibility.Visible : Visibility.Collapsed;
            button.IsDefault = false;
            button.IsCancel = false;
        }
        // Enter takes the first of them; Esc the one that puts the window away.
        if (shown.Length > 0) shown[0].IsDefault = true;
        if (new[] { close, cancel, later }.FirstOrDefault(shown.Contains) is { } away) away.IsCancel = true;
    }

    /// <summary>The offer: install it, be reminded, or skip this version.</summary>
    public void ShowOffer()
    {
        later.Content = "Remind Me Later";
        if (whyNotHere is not null) Show(whyNotHere, withProgress: false, close, skip);
        else Show(null, withProgress: false, install, later, skip);
    }

    public void ShowFetching()
    {
        progress.Value = 0;
        Show($"Downloading Heracles {version}…", withProgress: true, cancel);
    }

    /// <summary>Fetched and checked: all that is left is to swap it in, which needs the app to quit.</summary>
    public void ShowReady()
    {
        Show(
            $"Heracles {version} is ready to install. Installing restarts Heracles, after your notes are saved. "
            + "If you choose Later, it is installed when you next quit.",
            withProgress: false, restart, later);
        later.Content = "Later";
    }

    public void ShowProblem(string words) => Show(words, withProgress: false, close);
}

/// <summary>What the copy that installs an update shows while it works: a line that says so.</summary>
internal sealed class InstallingWindow : Window
{
    public InstallingWindow(string version)
    {
        Title = "Heracles";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // It takes nothing over: whatever is being worked in keeps the keyboard.
        ShowActivated = false;
        var words = $"Installing Heracles {version}…";
        Content = new Landmark
        {
            Child = new TextBlock { Text = words, FontSize = 15, Margin = new Thickness(36, 28, 36, 30) },
        }.Id("UpdateInstalling").Named(words);
    }

    /// <summary>Set once the work is over. Until then the window stays: closing it would stop the install half done.</summary>
    public bool MayClose { get; set; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!MayClose) e.Cancel = true;
        base.OnClosing(e);
    }
}
