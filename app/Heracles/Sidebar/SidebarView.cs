using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Heracles.Model;
using Heracles.Workspaces;

namespace Heracles.Sidebar;

/// <summary>
/// The sidebar: a search box, and under it the library as a list of rows
/// (favorites, each notebook's tree, the archive, the trash), or the notes
/// that match what was typed. It highlights the note in the focused pane.
/// Choosing a note opens it there, or in a new pane beside it with Ctrl held.
/// </summary>
internal sealed class SidebarView : DockPanel
{
    private readonly AppModel model;
    private readonly Workspace workspace;
    private readonly TextBox search;
    private readonly ListBox list;
    private readonly HashSet<string> collapsed = [];
    private readonly Dictionary<string, ListBoxItem> items = [];
    private IReadOnlyList<SidebarRow>? shownRows;
    private bool refreshing;

    public SidebarView(AppModel model, Workspace workspace)
    {
        this.model = model;
        this.workspace = workspace;

        var newButton = new Button { Content = Ui.Glyph("", 12), Padding = new Thickness(8, 4, 8, 4), ToolTip = "New", Margin = new Thickness(6, 0, 0, 0) }
            .Id("NewMenu").Named("New");
        var newMenu = new ContextMenu { PlacementTarget = newButton, Placement = PlacementMode.Bottom };
        newMenu.Items.Add(Ui.Item("Page", () => workspace.CreateNote(NoteKind.Page)));
        newMenu.Items.Add(Ui.Item("Section", () => workspace.CreateNote(NoteKind.Section)));
        newMenu.Items.Add(Ui.Item("To-Do List", () => workspace.CreateNote(NoteKind.TodoList)));
        newMenu.Items.Add(new Separator());
        newMenu.Items.Add(Ui.Item("Notebook", () => workspace.CreateNote(NoteKind.Notebook)));
        newButton.Click += (_, _) => newMenu.IsOpen = true;

        search = new TextBox { ToolTip = "Search notes" }.Id("SidebarSearch").Named("Search notes");
        // Says what the box is for while it is empty.
        var hint = Ui.Line("Search notes", 0.5);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(11, 0, 0, 0);
        search.TextChanged += (_, _) =>
        {
            hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        };
        var searchBox = new Grid();
        searchBox.Children.Add(search);
        searchBox.Children.Add(hint);

        var top = new DockPanel { Margin = new Thickness(8, 8, 8, 6) };
        SetDock(newButton, Dock.Right);
        top.Children.Add(newButton);
        top.Children.Add(searchBox);
        SetDock(top, Dock.Top);
        Children.Add(top);

        list = new ListBox { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent }.Id("Sidebar").Named("Notes");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SelectionChanged += OnSelectionChanged;
        Children.Add(list);

        Refresh();
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(search.Text);

    /// <summary>
    /// Show the model as it is now. Called whenever the library or this
    /// window's panes change, which while someone types is every time their
    /// note is saved. The rows are only drawn again when they would come out
    /// different, and then the list stays scrolled to where it was.
    /// </summary>
    public void Refresh()
    {
        refreshing = true;
        try
        {
            if (IsSearching)
            {
                shownRows = null;
                Redraw(ShowResults);
                return;
            }
            var rows = SidebarRows.For(model.Library, collapsed);
            var active = SidebarRows.RowFor(rows, workspace.Tree.ActiveNoteId)?.Key;
            if (shownRows is null || !SidebarRows.ShowTheSame(shownRows, rows))
            {
                Redraw(() => ShowLibrary(rows));
                shownRows = rows;
            }
            // The highlight is on the note in the focused pane, whatever was
            // clicked last.
            var wanted = active is not null && items.TryGetValue(active, out var item) ? item : null;
            if (!ReferenceEquals(list.SelectedItem, wanted)) list.SelectedItem = wanted;
        }
        finally { refreshing = false; }
    }

    private void Redraw(Action draw)
    {
        var scroll = Scroller(list);
        var offset = scroll?.VerticalOffset ?? 0;
        list.Items.Clear();
        items.Clear();
        draw();
        if (scroll is null || offset <= 0) return;
        // Once the new rows have their places, back to where the list was.
        list.UpdateLayout();
        scroll.ScrollToVerticalOffset(offset);
    }

    private static ScrollViewer? Scroller(DependencyObject parent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer found) return found;
            if (Scroller(child) is { } inner) return inner;
        }
        return null;
    }

    private void ShowLibrary(IReadOnlyList<SidebarRow> rows)
    {
        foreach (var row in rows)
        {
            var item = Item(row);
            list.Items.Add(item);
            items[row.Key] = item;
        }
    }

    private void ShowResults()
    {
        var hits = model.Search(search.Text);
        if (hits.Count == 0)
        {
            var none = Ui.Row(Ui.Line("No notes match", 0.6));
            none.IsEnabled = false;
            list.Items.Add(none.Id("NoResults"));
            return;
        }
        foreach (var hit in hits)
        {
            var snippet = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxHeight = 34, FontSize = 11, Opacity = 0.75 };
            foreach (var part in hit.Snippet)
            {
                snippet.Inlines.Add(new Run(part.Text) { FontWeight = part.Matched ? FontWeights.Bold : FontWeights.Normal });
            }
            var content = new StackPanel();
            content.Children.Add(Label(hit.Note, hit.Note.DisplayTitle(), 0, null));
            content.Children.Add(snippet);
            var item = Ui.Row(content, hit.Note.Id)
                .Id("hit:" + hit.Note.Id).Named(hit.Note.DisplayTitle());
            item.ContextMenu = NoteMenu(hit.Note);
            list.Items.Add(item);
        }
    }

    private ListBoxItem Item(SidebarRow row)
    {
        var item = Ui.Row("", row.Note is { } note && !row.IsHeader ? note.Id : null)
            .Id(row.Key).Named(row.Title);
        Button? toggle = null;
        if (row.HasChildren && row.Note is { } parent)
        {
            toggle = Ui.QuietButton(Ui.Glyph(row.IsExpanded ? "" : "", 9), () => Toggle(parent.Id), row.IsExpanded ? "Collapse" : "Expand")
                .Id("toggle:" + parent.Id).Named(row.IsExpanded ? "Collapse " + row.Title : "Expand " + row.Title);
        }
        if (row.IsHeader)
        {
            var header = Label(row.Note, row.Title, 0, toggle);
            header.Opacity = 0.75;
            TextElement.SetFontWeight(header, FontWeights.SemiBold);
            header.Margin = new Thickness(0, 6, 0, 0);
            item.Content = header;
            if (row.Note is { } notebook) item.ContextMenu = NotebookMenu(notebook);
        }
        else if (row.Note is { } rowNote)
        {
            item.Content = Label(rowNote, row.Title, row.Depth, toggle);
            item.ContextMenu = NoteMenu(rowNote);
            // A row that is already highlighted gets no selection change, so
            // a click on it is taken here: Ctrl-click opens it beside.
            item.PreviewMouseLeftButtonUp += (_, _) =>
            {
                if (item.IsSelected && Ui.ControlIsDown) workspace.Open(rowNote.Id, SplitTarget.Right);
            };
        }
        return item;
    }

    /// <summary>An icon and a name on one line, indented by depth, with an expand toggle if there is one.</summary>
    private static Grid Label(Note? note, string title, int depth, Button? toggle)
    {
        var grid = new Grid { Margin = new Thickness(Math.Max(0, depth - 1) * 14, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (toggle is not null) grid.Children.Add(toggle);
        if (note is not null)
        {
            var icon = NoteIcon.For(note, 13);
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);
        }
        var text = Ui.Line(title);
        text.ToolTip = title.Length > 40 ? title[..Math.Min(title.Length, 200)] : null;
        Grid.SetColumn(text, note is null ? 0 : 2);
        if (note is null) Grid.SetColumnSpan(text, 3);
        grid.Children.Add(text);
        return grid;
    }

    private void Toggle(string noteId)
    {
        if (!collapsed.Remove(noteId)) collapsed.Add(noteId);
        Refresh();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing) return;
        if ((list.SelectedItem as ListBoxItem)?.Tag is string noteId)
        {
            workspace.Open(noteId, Ui.ControlIsDown ? SplitTarget.Right : null);
        }
        // Whatever was chosen, the highlight goes back to the open note.
        if (!IsSearching) Refresh();
    }

    // Menus, as the Mac's sidebar has them.

    private void Create(NoteKind kind, Note parent)
    {
        if (model.CreateNote(kind, parent.Id) is { } note) workspace.Open(note.Id);
    }

    private void Rename(Note note)
    {
        if (Prompts.Ask(Window.GetWindow(this), "Rename", note.Title, "Rename") is { } title) model.Rename(note.Id, title);
    }

    private ContextMenu NotebookMenu(Note notebook)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Ui.Item("New Page", () => Create(NoteKind.Page, notebook)));
        menu.Items.Add(Ui.Item("New Section", () => Create(NoteKind.Section, notebook)));
        menu.Items.Add(Ui.Item("New To-Do List", () => Create(NoteKind.TodoList, notebook)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item("Rename…", () => Rename(notebook)));
        menu.Items.Add(Ui.Item("Delete Notebook", () => model.Delete(notebook.Id)));
        return menu;
    }

    private ContextMenu NoteMenu(Note note)
    {
        var menu = new ContextMenu();
        if (note.Trashed)
        {
            menu.Items.Add(Ui.Item("Put Back", () => model.Restore(note.Id)));
            menu.Items.Add(Ui.Item("Delete Permanently", () => model.Delete(note.Id)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.Item("Empty Trash", () => model.EmptyTrash()));
            return menu;
        }
        if (note.Kind != NoteKind.TodoList)
        {
            menu.Items.Add(Ui.Item("New Page Inside", () => Create(NoteKind.Page, note)));
            menu.Items.Add(Ui.Item("New To-Do List Inside", () => Create(NoteKind.TodoList, note)));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Ui.Item("Rename…", () => Rename(note)));
        menu.Items.Add(Ui.Item("Duplicate", () =>
        {
            if (model.Duplicate(note.Id) is { } copy) workspace.Open(copy.Id);
        }));
        menu.Items.Add(Ui.Item(note.Favorited ? "Remove from Favorites" : "Add to Favorites", () => model.SetFavorited(note.Id, !note.Favorited)));
        menu.Items.Add(Ui.Item("Copy Link", () => Clipboard.SetText(new AppLink(note.Id, null).Url)));
        var move = new MenuItem { Header = "Move to Notebook" };
        foreach (var notebook in model.Library.Notebooks)
        {
            move.Items.Add(Ui.Item(notebook.Note.DisplayTitle(), () => model.Move(note.Id, notebook.Id)));
        }
        menu.Items.Add(move);
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item(note.Archived ? "Unarchive" : "Archive", () => model.SetArchived(note.Id, !note.Archived)));
        menu.Items.Add(Ui.Item("Move to Trash", () => model.Trash(note.Id)));
        return menu;
    }

    public void FocusSearch() => Keyboard.Focus(search);
}
