using System.Windows.Controls;
using System.Windows.Input;
using Heracles.Model;
using Heracles.Sidebar;
using Heracles.Workspaces;

namespace Heracles;

/// <summary>
/// One window: the menu bar, the sidebar, and the panes beside it. Every
/// window shares the same model, so what one changes the others show.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppModel? model;
    private readonly SidebarView? sidebar;
    private readonly PaneHost? panes;
    private readonly SwitcherView? switcher;
    private MenuItem? favoriteItem;
    private MenuItem[] noteItems = [];

    internal Workspace? Workspace { get; }

    /// <summary>A window on the library.</summary>
    internal MainWindow(AppModel model)
    {
        InitializeComponent();
        this.model = model;
        Workspace = new Workspace(model);

        sidebar = new SidebarView(model, Workspace);
        SidebarHost.Child = sidebar;
        panes = new PaneHost(model, Workspace);
        PaneArea.Child = panes;
        switcher = new SwitcherView(Workspace);
        Overlay.Children.Add(switcher);
        Overlay.MouseDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, Overlay)) Workspace.IsSwitcherShown = false;
        };

        BuildMenus();

        model.Changed += OnModelChanged;
        Workspace.TreeChanged += OnTreeChanged;
        Workspace.SwitcherChanged += OnSwitcherChanged;
        Closed += (_, _) =>
        {
            // The model outlives the window: it must not keep calling it.
            model.Changed -= OnModelChanged;
            Workspace.TreeChanged -= OnTreeChanged;
            Workspace.SwitcherChanged -= OnSwitcherChanged;
        };
        UpdateTitle();
    }

    /// <summary>A window that only says the library could not be opened, and why.</summary>
    internal MainWindow(string startupError)
    {
        InitializeComponent();
        Shell.Visibility = Visibility.Collapsed;
        StartupFailure.Visibility = Visibility.Visible;
        StartupError.Text = startupError;
    }

    // Following the model and this window's panes

    private void OnModelChanged()
    {
        // A note deleted here or in another window leaves this window's panes.
        Workspace!.LibraryChanged();
        sidebar!.Refresh();
        panes!.Render();
        UpdateTitle();
    }

    private void OnTreeChanged()
    {
        sidebar!.Refresh();
        panes!.Render();
        UpdateTitle();
    }

    private void OnSwitcherChanged()
    {
        if (Workspace!.IsSwitcherShown)
        {
            Overlay.Visibility = Visibility.Visible;
            switcher!.Begin();
        }
        else
        {
            Overlay.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateTitle()
    {
        Title = Workspace?.ActiveNote?.DisplayTitle() is { } title ? Cut(title, 80) : "Heracles";
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length] + "…";

    // Menus and shortcuts: the Mac's, with Ctrl for Command.

    private void BuildMenus()
    {
        var workspace = Workspace!;

        MenuItem Command(string header, Action action, Key key = Key.None, ModifierKeys modifiers = ModifierKeys.None, string? gesture = null)
        {
            if (key != Key.None) InputBindings.Add(new KeyBinding(new RelayCommand(action), key, modifiers));
            return Ui.Item(header, action, gesture);
        }

        var file = new MenuItem { Header = "_File" };
        file.Items.Add(Command("New _Page", () => workspace.CreateNote(NoteKind.Page), Key.N, ModifierKeys.Control, "Ctrl+N"));
        file.Items.Add(Command("New _Section", () => workspace.CreateNote(NoteKind.Section), Key.N, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+N"));
        file.Items.Add(Command("New _To-Do List", () => workspace.CreateNote(NoteKind.TodoList), Key.T, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+T"));
        file.Items.Add(Command("New Note_book", () => workspace.CreateNote(NoteKind.Notebook)));
        file.Items.Add(new Separator());
        file.Items.Add(Command("_Quick Open…", () => workspace.IsSwitcherShown = true, Key.K, ModifierKeys.Control, "Ctrl+K"));
        file.Items.Add(new Separator());
        file.Items.Add(Command("_Close Tab", workspace.CloseActiveTab, Key.W, ModifierKeys.Control, "Ctrl+W"));
        file.Items.Add(new Separator());
        file.Items.Add(Command("E_xit", () => ((App)Application.Current).ExitApp()));

        var note = new MenuItem { Header = "_Note" };
        favoriteItem = Command("Add to _Favorites", workspace.ToggleFavoriteActive, Key.F, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+F");
        var duplicate = Command("_Duplicate", workspace.DuplicateActive, Key.D, ModifierKeys.Control, "Ctrl+D");
        var copyLink = Command("Copy _Link", CopyLink, Key.C, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+C");
        var trash = Command("Move to _Trash", workspace.TrashActive);
        note.Items.Add(favoriteItem);
        note.Items.Add(duplicate);
        note.Items.Add(copyLink);
        note.Items.Add(new Separator());
        note.Items.Add(trash);
        noteItems = [favoriteItem, duplicate, copyLink, trash];
        // What the items say and whether they can be chosen depends on the open note.
        note.SubmenuOpened += (_, _) =>
        {
            var active = workspace.ActiveNote;
            foreach (var item in noteItems) item.IsEnabled = active is not null;
            favoriteItem.Header = active?.Favorited == true ? "Remove from _Favorites" : "Add to _Favorites";
        };

        var view = new MenuItem { Header = "_View" };
        // Oem5 is the backslash key on the keyboards that have one there.
        view.Items.Add(Command("Split _Right", () => workspace.SplitFocused(SplitTarget.Right), Key.Oem5, ModifierKeys.Control, "Ctrl+\\"));
        view.Items.Add(Command("Split _Down", () => workspace.SplitFocused(SplitTarget.Down), Key.Oem5, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+\\"));

        var window = new MenuItem { Header = "_Window" };
        window.Items.Add(Command("_New Window", () => ((App)Application.Current).OpenWindow()));

        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(Command("_About Heracles", () =>
            MessageBox.Show(this, $"Heracles {App.VersionText}", "About Heracles", MessageBoxButton.OK, MessageBoxImage.Information)));

        foreach (var menu in new[] { file, note, view, window, help }) MenuBar.Items.Add(menu);
    }

    private void CopyLink()
    {
        if (Workspace?.LinkToActive() is { } link) Clipboard.SetText(link);
    }

    // Remembering the window between runs

    internal SavedWindow Capture()
    {
        // The size to come back to is the one the window has when it is not maximized.
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        return new SavedWindow(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized, Workspace?.Tree.ToJson());
    }

    internal void Restore(SavedWindow saved)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var wanted = new Rect(saved.Left, saved.Top, saved.Width, saved.Height);
        var visible = Rect.Intersect(screen, wanted);
        // A window saved on a display that is no longer there opens where it can be seen.
        if (!visible.IsEmpty && visible.Width >= 120 && visible.Height >= 80)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left;
            Top = saved.Top;
        }
        Width = saved.Width;
        Height = saved.Height;
        if (saved.Maximized) WindowState = WindowState.Maximized;

        if (Workspace is not null && model is not null && PaneTree.FromJson(saved.Panes) is { } tree)
        {
            // Notes deleted since the layout was saved have no tab to come back to.
            Workspace.Tree = tree.Keep(model.Store.ListNotes().Select(note => note.Id).ToHashSet());
        }
    }
}
