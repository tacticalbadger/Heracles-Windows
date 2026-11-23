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
        // Nothing typed is lost to a window closing: every editor saves first.
        Closing += (_, _) => model.SaveEverything();
        Closed += (_, _) =>
        {
            panes.Close();
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
        file.Items.Add(Command("_Import…", ChooseFilesToImport, Key.I, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+I"));
        var export = Command("_Export as Markdown…", ChooseExportDestination, Key.E, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+E");
        file.Items.Add(export);
        // There is something to export only while a note is open.
        file.SubmenuOpened += (_, _) => export.IsEnabled = workspace.ActiveNote is not null;
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

        // Edit and Format act on the editor of the pane that has the focus.
        // Their keys are the editor's own, answered while it is being typed
        // in, so they are shown here and not bound to the window.
        Heracles.Editor.BlockEditorController? Editor() => panes?.EditorFor(workspace.Tree.FocusedLeafId);

        var edit = new MenuItem { Header = "_Edit" };
        var undo = Ui.Item("_Undo", () => Editor()?.Undo(), "Ctrl+Z");
        var redo = Ui.Item("_Redo", () => Editor()?.Redo(), "Ctrl+Y");
        var pastePlain = Ui.Item("Paste as Plain Te_xt", () => Editor()?.PasteFromClipboard(plainOnly: true), "Ctrl+Shift+V");
        edit.Items.Add(undo);
        edit.Items.Add(redo);
        edit.Items.Add(new Separator());
        // These go to whatever has the keyboard: a note's text, its title, the search box.
        edit.Items.Add(new MenuItem { Header = "Cu_t", Command = ApplicationCommands.Cut, InputGestureText = "Ctrl+X" });
        edit.Items.Add(new MenuItem { Header = "_Copy", Command = ApplicationCommands.Copy, InputGestureText = "Ctrl+C" });
        edit.Items.Add(new MenuItem { Header = "_Paste", Command = ApplicationCommands.Paste, InputGestureText = "Ctrl+V" });
        edit.Items.Add(pastePlain);
        edit.Items.Add(new Separator());
        edit.Items.Add(new MenuItem { Header = "Select _All", Command = ApplicationCommands.SelectAll, InputGestureText = "Ctrl+A" });
        edit.SubmenuOpened += (_, _) =>
        {
            var editor = Editor();
            undo.IsEnabled = editor is { CanUndo: true, IsEditable: true };
            redo.IsEnabled = editor is { CanRedo: true, IsEditable: true };
            pastePlain.IsEnabled = editor is { IsEditable: true };
        };

        var format = new MenuItem { Header = "F_ormat" };
        MenuItem Mark(string header, Heracles.Editor.TextMark mark, string gesture) =>
            Ui.Item(header, () => Editor()?.Apply(new Heracles.Editor.EditorFormat.OfMark(mark)), gesture);
        MenuItem Kind(string header, Heracles.Editor.BlockTarget target, string? gesture = null) =>
            Ui.Item(header, () => Editor()?.Apply(new Heracles.Editor.EditorFormat.OfBlock(target)), gesture);
        format.Items.Add(Mark("_Bold", Heracles.Editor.TextMark.Bold, "Ctrl+B"));
        format.Items.Add(Mark("_Italic", Heracles.Editor.TextMark.Italic, "Ctrl+I"));
        format.Items.Add(Mark("_Strikethrough", Heracles.Editor.TextMark.Strike, "Ctrl+Shift+X"));
        format.Items.Add(Mark("_Code", Heracles.Editor.TextMark.Code, "Ctrl+E"));
        format.Items.Add(Ui.Item("_Link…", () => Editor()?.Apply(new Heracles.Editor.EditorFormat.OfLink()), "Ctrl+K"));
        format.Items.Add(new Separator());
        format.Items.Add(Kind("_Text", Heracles.Editor.BlockTarget.Paragraph, "Ctrl+Alt+0"));
        format.Items.Add(Kind("Heading _1", Heracles.Editor.BlockTarget.Heading(1), "Ctrl+Alt+1"));
        format.Items.Add(Kind("Heading _2", Heracles.Editor.BlockTarget.Heading(2), "Ctrl+Alt+2"));
        format.Items.Add(Kind("Heading _3", Heracles.Editor.BlockTarget.Heading(3), "Ctrl+Alt+3"));
        format.Items.Add(new Separator());
        format.Items.Add(Kind("B_ullet List", Heracles.Editor.BlockTarget.Bullet, "Ctrl+Shift+8"));
        format.Items.Add(Kind("_Numbered List", Heracles.Editor.BlockTarget.Numbered, "Ctrl+Shift+7"));
        format.Items.Add(Kind("T_ask List", Heracles.Editor.BlockTarget.Todo, "Ctrl+Shift+9"));
        format.Items.Add(new Separator());
        format.Items.Add(Kind("_Quote", Heracles.Editor.BlockTarget.Quote));
        format.Items.Add(Kind("Call_out", Heracles.Editor.BlockTarget.Callout));
        format.Items.Add(Kind("To_ggle", Heracles.Editor.BlockTarget.Toggle));
        format.Items.Add(Kind("Code Bloc_k", Heracles.Editor.BlockTarget.Code));
        format.Items.Add(Kind("_Divider", Heracles.Editor.BlockTarget.Divider));
        format.SubmenuOpened += (_, _) =>
        {
            var canFormat = Editor() is { IsEditable: true };
            foreach (var item in format.Items.OfType<MenuItem>()) item.IsEnabled = canFormat;
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

        foreach (var menu in new[] { file, edit, note, format, view, window, help }) MenuBar.Items.Add(menu);
    }

    // Import and export: the core reads and writes; the window asks which files.

    /// <summary>Ask for Markdown and HTML files, and bring them in as pages.</summary>
    internal void ChooseFilesToImport()
    {
        if (Workspace is not { } workspace) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import",
            Filter = Transfer.ImportFilter,
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true) workspace.ImportFiles(dialog.FileNames);
    }

    /// <summary>Ask where to write the open note as Markdown, and write it there.</summary>
    internal void ChooseExportDestination()
    {
        if (Workspace is not { ActiveNote: { } note } workspace) return;
        // What was typed a moment ago is in the file too.
        workspace.Model.SaveEverything();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export as Markdown",
            Filter = Transfer.ExportFilter,
            FileName = Transfer.ExportFileName(note.DisplayTitle()),
            DefaultExt = ".md",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) == true) workspace.ExportActiveNote(dialog.FileName);
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
