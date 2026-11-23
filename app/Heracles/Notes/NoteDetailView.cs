using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heracles.Editor;
using Heracles.Model;
using Heracles.Sidebar;
using Heracles.Workspaces;

namespace Heracles.Notes;

/// <summary>
/// One note in a pane: its icon and title, then its content: the editor for
/// a page, the board for a todo list.
/// </summary>
internal sealed class NoteDetailView : DockPanel
{
    private readonly AppModel model;
    private readonly Workspace workspace;
    private readonly string leafId;
    private readonly Button iconButton;
    private readonly TextBox title;
    private readonly ContentControl body;
    private readonly DockPanel header;
    private EditorCoordinator? coordinator;
    private Heracles.Boards.BoardView? board;
    private Note note;
    private bool updating;

    public string NoteId => note.Id;

    /// <summary>The note's editor, once it is showing one. A todo list has none.</summary>
    public BlockEditorController? Editor => coordinator?.Editor;

    public NoteDetailView(AppModel model, Workspace workspace, string leafId, Note note)
    {
        this.model = model;
        this.workspace = workspace;
        this.leafId = leafId;
        this.note = note;

        iconButton = Ui.QuietButton("", ChooseIcon, "Choose an icon").Id("NoteIcon:" + leafId).Named("Choose an icon");
        iconButton.Width = 44;
        iconButton.Height = 44;

        title = new TextBox
        {
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 0, 0),
        }.Id("NoteTitle:" + leafId).Named("Title");
        title.GotKeyboardFocus += (_, _) => workspace.Tree = workspace.Tree.Focus(leafId);
        title.LostKeyboardFocus += (_, _) => CommitTitle();
        // A title changed while the box does not have the keyboard is taken at
        // once: nothing else would ever commit it. (UI Automation's "set
        // value" usually brings the keyboard with it, and then the title is
        // kept like any other, when the keyboard moves on.)
        title.TextChanged += (_, _) =>
        {
            if (!updating && !title.IsKeyboardFocused) CommitTitle();
        };
        title.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Down)) return;
            CommitTitle();
            // From the title, on into the note.
            coordinator?.Editor.FocusStart();
            e.Handled = true;
        };

        header = new DockPanel();
        SetDock(iconButton, Dock.Left);
        header.Children.Add(iconButton);
        header.Children.Add(title);
        SetDock(header, Dock.Top);
        Children.Add(header);

        body = new ContentControl { Focusable = false };
        Children.Add(body);

        Update(note);
    }

    /// <summary>Show the note as it is now. Called whenever the library or a document changes.</summary>
    public void Update(Note current)
    {
        note = current;
        iconButton.Content = string.IsNullOrWhiteSpace(note.Icon)
            // The icon font's face: a note with no icon of its own.
            ? new TextBlock { Text = ((char)0xE76E).ToString(), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 20, Opacity = 0.4 }
            : NoteIcon.EmojiText(note.Icon, 26);
        iconButton.IsEnabled = !note.Trashed;
        title.IsEnabled = !note.Trashed;
        // What is being typed is not replaced by what is stored.
        if (!title.IsKeyboardFocused && title.Text != note.Title)
        {
            updating = true;
            try { title.Text = note.Title; }
            finally { updating = false; }
        }

        // The header stands over the editor's text column: as wide, and as far in.
        // A board starts at the left, and so does the header over it.
        var isBoard = note.Kind == NoteKind.TodoList;
        header.MaxWidth = isBoard ? double.PositiveInfinity : EditorTheme.ContentWidth;
        header.Margin = isBoard ? new Thickness(22, 20, 32, 0) : new Thickness(EditorTheme.Gutter - 46, 20, EditorTheme.Gutter, 4);

        if (isBoard)
        {
            if (board is null) board = new Heracles.Boards.BoardView(model, leafId, note, () => workspace.Tree = workspace.Tree.Focus(leafId));
            else board.Update(note);
            if (!ReferenceEquals(body.Content, board)) body.Content = board;
            return;
        }
        coordinator ??= MakeEditor();
        if (!ReferenceEquals(body.Content, coordinator.Editor.ScrollView)) body.Content = coordinator.Editor.ScrollView;
        coordinator.Show(note.Id, editable: !note.Trashed, model.DocumentChangeFor(note.Id));
        coordinator.Reveal(workspace.BlockToReveal(note.Id), () => workspace.DidReveal(note.Id));
    }

    private EditorCoordinator MakeEditor()
    {
        var made = new EditorCoordinator(model)
        {
            // Typing in a pane makes it the one the next note opens in.
            OnFocus = () => workspace.Tree = workspace.Tree.Focus(leafId),
        };
        // A screen reader, and the end-to-end checks, read the note from here.
        made.Editor.Page.Id("NoteText:" + leafId);
        return made;
    }

    /// <summary>The view is going: what was typed and not yet saved is saved now.</summary>
    public void Close()
    {
        CommitTitle();
        coordinator?.Dispose();
        coordinator = null;
    }

    private void CommitTitle()
    {
        if (title.Text != note.Title && !note.Trashed) model.Rename(note.Id, title.Text);
    }

    private void ChooseIcon()
    {
        new IconPicker(iconButton, note.Icon, icon => model.SetIcon(note.Id, icon)) { IsOpen = true };
    }
}
