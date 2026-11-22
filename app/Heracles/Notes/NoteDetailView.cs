using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heracles.Model;
using Heracles.Sidebar;
using Heracles.Workspaces;

namespace Heracles.Notes;

/// <summary>
/// One note in a pane: its icon and title, then its content. Until the editor
/// exists the content is shown as text that cannot be changed, and a todo
/// list says its board is still to come.
/// </summary>
internal sealed class NoteDetailView : DockPanel
{
    /// <summary>The widest a line of text is allowed to get, as in the Mac editor.</summary>
    private const double ContentWidth = 760;

    private readonly AppModel model;
    private readonly Workspace workspace;
    private readonly string leafId;
    private readonly Button iconButton;
    private readonly TextBox title;
    private readonly global::Emoji.Wpf.TextBlock text;
    private Note note;
    private long shownRevision = -1;
    private bool updating;

    public string NoteId => note.Id;

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
        // A title set from outside (a screen reader, an automated check) never
        // has the keyboard, so nothing would ever commit it. It is taken at once.
        title.TextChanged += (_, _) =>
        {
            if (!updating && !title.IsKeyboardFocused) CommitTitle();
        };
        title.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitTitle();
            e.Handled = true;
        };

        var header = new DockPanel { MaxWidth = ContentWidth, Margin = new Thickness(32, 20, 32, 4) };
        SetDock(iconButton, Dock.Left);
        header.Children.Add(iconButton);
        header.Children.Add(title);
        SetDock(header, Dock.Top);
        Children.Add(header);

        text = new global::Emoji.Wpf.TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 22 };
        text.Id("NoteText:" + leafId);
        Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { MaxWidth = ContentWidth, Margin = new Thickness(34, 8, 32, 24), Child = text },
        });

        Update(note);
    }

    /// <summary>Show the note as it is now. Called whenever the library or a document changes.</summary>
    public void Update(Note current)
    {
        note = current;
        iconButton.Content = string.IsNullOrWhiteSpace(note.Icon)
            ? new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 20, Opacity = 0.4 }
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

        if (note.Kind == NoteKind.TodoList)
        {
            Show("Boards arrive in a later version.", 0.6);
            return;
        }
        var revision = model.DocumentChangeFor(note.Id)?.Revision ?? 0;
        if (revision == shownRevision) return;
        shownRevision = revision;
        Show(ReadOnlyDocument.PlainText(model.Document(note.Id)?.Content ?? ""), 1);
    }

    private void Show(string content, double opacity)
    {
        if (text.Text != content) text.Text = content;
        text.Opacity = opacity;
        // A screen reader, and the end-to-end checks, read the text from here.
        System.Windows.Automation.AutomationProperties.SetName(text, content.Length == 0 ? "Empty note" : content);
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
