using Heracles.Model;

namespace Heracles.Workspaces;

/// <summary>
/// One window's state: its panes, and the actions its menus and toolbar
/// perform. The notes themselves live in the shared <see cref="AppModel"/>.
/// File dialogs and the clipboard are the window's business, not this class's.
/// </summary>
internal sealed class Workspace
{
    private PaneTree tree = new();
    private bool isSwitcherShown;

    /// <summary>
    /// For each note opened by a link to one of its blocks, the block to bring
    /// into view once the note is showing.
    /// </summary>
    private readonly Dictionary<string, string> reveals = [];

    public AppModel Model { get; }

    public event Action? TreeChanged;

    public event Action? SwitcherChanged;

    public Workspace(AppModel model)
    {
        Model = model;
    }

    public PaneTree Tree
    {
        get => tree;
        set
        {
            // A change that leaves the panes as they were is not a change.
            if (value.ToJson() == tree.ToJson()) return;
            tree = value;
            TreeChanged?.Invoke();
        }
    }

    public bool IsSwitcherShown
    {
        get => isSwitcherShown;
        set
        {
            if (isSwitcherShown == value) return;
            isSwitcherShown = value;
            SwitcherChanged?.Invoke();
        }
    }

    public Note? ActiveNote => Tree.ActiveNoteId is { } id ? Model.Library.Note(id) : null;

    // Opening and closing

    /// <summary>Show a note in the focused pane, or in a new pane beside it.</summary>
    public void Open(string noteId, SplitTarget? inSplit = null)
    {
        if (Model.Library.Note(noteId) is null) return;
        Tree = inSplit is { } target
            ? Tree.Split(Tree.FocusedLeafId, target, noteId, out _)
            : Tree.Open(noteId);
    }

    public void CloseActiveTab()
    {
        Tree = Tree.ActiveNoteId is { } active
            ? Tree.CloseTab(active, Tree.FocusedLeafId)
            : Tree.CloseLeaf(Tree.FocusedLeafId);
    }

    /// <summary>
    /// Open a second pane showing the same note, to look at two parts of it or
    /// to pick another note there.
    /// </summary>
    public void SplitFocused(SplitTarget target)
    {
        Tree = Tree.Split(Tree.FocusedLeafId, target, Tree.ActiveNoteId, out _);
    }

    /// <summary>
    /// Let go of notes that no longer exist. Called whenever the library
    /// changes, whichever window changed it.
    /// </summary>
    public void LibraryChanged()
    {
        var gone = Tree.Leaves.SelectMany(leaf => leaf.Tabs).Where(id => Model.Library.Note(id) is null).ToHashSet();
        if (gone.Count > 0) Tree = Tree.Remove(gone);
        foreach (var noteId in reveals.Keys.Where(id => Model.Library.Note(id) is null).ToList()) reveals.Remove(noteId);
    }

    public void Follow(AppLink link)
    {
        if (Model.Library.Note(link.NoteId) is null) return;
        Open(link.NoteId);
        if (link.BlockId is { } block) reveals[link.NoteId] = block;
        else reveals.Remove(link.NoteId);
    }

    public string? BlockToReveal(string noteId) => reveals.GetValueOrDefault(noteId);

    public void DidReveal(string noteId) => reveals.Remove(noteId);

    // Creating

    /// <summary>
    /// Where a new note goes: inside the open note if that can hold notes,
    /// beside it if not, and in the first notebook when nothing is open.
    /// </summary>
    private string? ParentForNewNotes
    {
        get
        {
            if (ActiveNote is not { Trashed: false } active) return null;
            return active.Kind == NoteKind.TodoList ? active.ParentId : active.Id;
        }
    }

    public Note? CreateNote(NoteKind kind)
    {
        var note = Model.CreateNote(kind, ParentForNewNotes);
        if (note is not null && kind != NoteKind.Notebook) Tree = Tree.Open(note.Id);
        return note;
    }

    // Import and export

    /// <summary>Import Markdown and HTML files as pages, and show the last one.</summary>
    public IReadOnlyList<Note> ImportFiles(IEnumerable<string> paths)
    {
        var parent = ParentForNewNotes;
        var imported = new List<Note>();
        var refused = new List<string>();
        var any = false;
        foreach (var path in paths)
        {
            any = true;
            // One that cannot be read does not stop the others.
            try { imported.Add(Model.Store.ImportFile(path, parent)); }
            catch (UniffiException error) { refused.Add($"{Path.GetFileName(path)} ({error.Message})"); }
        }
        if (imported.Count > 0) Tree = Tree.Open(imported[^1].Id);
        // Said once, at the end, naming each file that did not come in.
        if (any) Model.LastError = refused.Count == 0 ? null : "Could not import " + string.Join("; ", refused);
        return imported;
    }

    public bool ExportActiveNote(string destination) =>
        ActiveNote is { } note && Model.Attempt(() => Model.Store.ExportMarkdown(note.Id, destination));

    // The quick switcher

    /// <summary>
    /// What the switcher lists: the most recently changed notes until
    /// something is typed, then the notes that match it.
    /// </summary>
    public IReadOnlyList<Note> SwitcherResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            // The core lists notes most recently changed first.
            Note[] notes;
            try { notes = Model.Store.ListNotes(); }
            catch (UniffiException) { return []; }
            return notes.Where(note => !note.Trashed && note.Kind != NoteKind.Notebook).Take(20).ToList();
        }
        return Model.Search(query).Select(hit => hit.Note).Where(note => note.Kind != NoteKind.Notebook).ToList();
    }

    // Actions on the open note

    public void TrashActive()
    {
        if (ActiveNote is { } note) Model.Trash(note.Id);
    }

    public void DuplicateActive()
    {
        if (ActiveNote is { } note && Model.Duplicate(note.Id) is { } copy) Tree = Tree.Open(copy.Id);
    }

    public void ToggleFavoriteActive()
    {
        if (ActiveNote is { } note) Model.SetFavorited(note.Id, !note.Favorited);
    }

    /// <summary>The link to the open note, for the window to put on the clipboard.</summary>
    public string? LinkToActive() => ActiveNote is { } note ? new AppLink(note.Id, null).Url : null;
}
