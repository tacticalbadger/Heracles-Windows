namespace Heracles.Model;

/// <summary>The latest save of a note's content, as the change feed reported it.</summary>
/// <param name="Origin">
/// Whatever the saver passed. An editor passes its own id, so it can tell its
/// own save from another window's.
/// </param>
internal readonly record struct DocumentChange(long Revision, string? Origin);

/// <summary>
/// The app's one view of the library, shared by every window.
///
/// Nothing here is the source of truth. The core is. This model keeps a copy
/// of the note list for the views to read, and replaces it whenever the core's
/// change feed says something changed, whoever changed it. That is what keeps
/// two windows in step without either knowing about the other.
/// </summary>
internal sealed partial class AppModel : IDisposable
{
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly Action<Action> post;
    private readonly ulong subscription;
    private readonly Dictionary<string, DocumentChange> documentChanges = [];
    private readonly Dictionary<string, int> boardVersions = [];
    private volatile bool disposed;
    private string? lastError;
    private AppLink? pendingLink;

    public Store Store { get; }

    public Library Library { get; private set; }

    /// <summary>Something changed: the library, a document, or a board.</summary>
    public event Action? Changed;

    public event Action? ErrorChanged;

    public event Action? PendingLinkChanged;

    /// <summary>
    /// Called after each batch of changes, for work that follows the data
    /// without being shown: rescheduling reminders.
    /// </summary>
    public Action? AfterChange { get; set; }

    /// <param name="post">
    /// How a change made on another thread reaches the thread that owns this
    /// model. Left out, it is the synchronization context current now (the
    /// dispatcher, in the app), or nothing at all where there is none.
    /// </param>
    public AppModel(Store store, Action<Action>? post = null)
    {
        Store = store;
        if (post is null)
        {
            var context = SynchronizationContext.Current;
            post = context is null ? action => action() : action => context.Post(_ => action(), null);
        }
        this.post = post;
        Library = new Library(store.ListNotes());
        subscription = store.Subscribe(new FeedObserver(this));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Store.Unsubscribe(subscription);
    }

    /// <summary>Why the last action failed, in words for the user. Null after a success.</summary>
    public string? LastError
    {
        get => lastError;
        set
        {
            if (lastError == value) return;
            lastError = value;
            ErrorChanged?.Invoke();
        }
    }

    /// <summary>A link the app was asked to open. The frontmost window takes it.</summary>
    public AppLink? PendingLink
    {
        get => pendingLink;
        set
        {
            if (pendingLink == value) return;
            pendingLink = value;
            PendingLinkChanged?.Invoke();
        }
    }

    // The change feed

    /// <summary>Carries the core's change feed to the model. The core calls it on whichever thread made the write.</summary>
    private sealed class FeedObserver(AppModel model) : ChangeObserver
    {
        public void OnChanges(Change[] changes) => model.Receive(changes);
    }

    private void Receive(Change[] changes)
    {
        if (disposed) return;
        if (Environment.CurrentManagedThreadId == ownerThread)
        {
            // Already there, so the views are current by the time the call
            // that made the write returns.
            Apply(changes);
        }
        else
        {
            post(() => Apply(changes));
        }
    }

    private void Apply(Change[] changes)
    {
        // A change can still be on its way when the model goes.
        if (disposed) return;
        var notesChanged = false;
        foreach (var change in changes)
        {
            switch (change)
            {
                case Change.NotesChanged:
                    notesChanged = true;
                    break;
                case Change.NotesDeleted deleted:
                    notesChanged = true;
                    foreach (var id in deleted.Ids)
                    {
                        documentChanges.Remove(id);
                        boardVersions.Remove(id);
                    }
                    break;
                case Change.DocumentChanged saved:
                    // Batches from other threads can arrive out of step with
                    // ones made here. The revision says which is newer.
                    if (!documentChanges.TryGetValue(saved.NoteId, out var known) || saved.Revision >= known.Revision)
                    {
                        documentChanges[saved.NoteId] = new DocumentChange(saved.Revision, saved.Origin);
                    }
                    break;
                case Change.BoardChanged board:
                    boardVersions[board.TodoListId] = boardVersions.GetValueOrDefault(board.TodoListId) + 1;
                    break;
            }
        }
        if (notesChanged)
        {
            try { Library = new Library(Store.ListNotes()); }
            catch (UniffiException) { /* The list stays as it was; the next change tries again. */ }
        }
        Changed?.Invoke();
        AfterChange?.Invoke();
    }

    /// <summary>The latest save of a note's content this session, if there was one.</summary>
    public DocumentChange? DocumentChangeFor(string noteId) =>
        documentChanges.TryGetValue(noteId, out var change) ? change : null;

    /// <summary>A number that goes up each time a todo list's board changes. A board view reloads when it does.</summary>
    public int BoardVersion(string todoListId) => boardVersions.GetValueOrDefault(todoListId);

    // Actions

    /// <summary>Run a write. A failure is kept in <see cref="LastError"/> for the window to show.</summary>
    public T? Attempt<T>(Func<T> work) where T : class
    {
        try
        {
            var value = work();
            LastError = null;
            return value;
        }
        catch (UniffiException error)
        {
            LastError = error.Message;
            return null;
        }
    }

    public bool Attempt(Action work) => Attempt(() => { work(); return ""; }) is not null;

    public Note? CreateNote(NoteKind kind, string? parentId, string? title = null) =>
        Attempt(() => Store.CreateNote(new NewNote(Kind: kind, ParentId: parentId, Title: title)));

    public void Rename(string id, string title) => Attempt(() => Store.RenameNote(id, title));

    public void SetIcon(string id, string? icon) => Attempt(() => Store.SetNoteIcon(id, icon));

    public void SetFavorited(string id, bool favorited) => Attempt(() => Store.SetNoteFavorited(id, favorited));

    public void SetArchived(string id, bool archived) => Attempt(() => Store.SetNoteArchived(id, archived));

    public void Trash(string id) => Attempt(() => Store.TrashNote(id));

    public void Restore(string id) => Attempt(() => Store.RestoreNote(id));

    /// <summary>Delete for good. Returns every id that went, so views can let go of them.</summary>
    public IReadOnlyList<string> Delete(string id) => Attempt(() => Store.DeleteNote(id)) ?? [];

    public IReadOnlyList<string> EmptyTrash() => Attempt(Store.EmptyTrash) ?? [];

    public void Move(string id, string? parentId, string? beforeId = null) =>
        Attempt(() => Store.MoveNote(id, parentId, beforeId));

    public Note? Duplicate(string id) => Attempt(() => Store.DuplicateNote(id));

    public StoredDocument? Document(string noteId)
    {
        try { return Store.GetDocument(noteId); }
        catch (UniffiException error)
        {
            LastError = error.Message;
            return null;
        }
    }

    /// <summary>Save a note's content. False when it could not be saved; <see cref="LastError"/> says why.</summary>
    public bool SaveDocument(string noteId, string content, string? origin) =>
        Attempt(() => Store.SaveDocument(noteId, content, origin)) is not null;

    // What is not saved yet

    private readonly List<Action> savers = [];

    private sealed class Registration(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>
    /// An editor holds what was typed for a moment before it saves. It
    /// registers here, so that closing a window or ending the app can ask
    /// every editor to save first. Dispose what this returns to stop being
    /// asked.
    /// </summary>
    public IDisposable RegisterSaver(Action save)
    {
        savers.Add(save);
        return new Registration(() => savers.Remove(save));
    }

    /// <summary>Have every open editor save what it holds. Nothing typed is lost to a window closing.</summary>
    public void SaveEverything()
    {
        foreach (var save in savers.ToList()) save();
    }

    public IReadOnlyList<SearchHit> Search(string query)
    {
        try { return Store.SearchNotes(query, 50); }
        catch (UniffiException) { return []; }
    }
}
