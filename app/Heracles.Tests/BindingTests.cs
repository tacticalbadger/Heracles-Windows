namespace Heracles.Tests;

/// <summary>
/// These tests go through the generated bindings into the Rust core. The
/// core's own behaviour is tested in Rust. What is checked here is that each
/// kind of value survives the crossing: records, optionals, the flags that
/// clear a field, bytes, errors, and callbacks.
/// </summary>
public sealed class BindingTests : IDisposable
{
    private readonly string directory;
    private readonly Store store;

    public BindingTests()
    {
        directory = Path.Combine(Path.GetTempPath(), $"heracles-binding-tests-{Guid.NewGuid()}");
        store = new Store(directory);
    }

    public void Dispose()
    {
        store.Dispose();
        DeleteQuietly(directory);
    }

    /// <summary>Temporary files are best effort: a leftover must not fail a test.</summary>
    private static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Document(string text) =>
        $$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}]}""";

    /// <summary>
    /// A change as text. The generated records hold arrays, which compare by
    /// reference, so batches are compared field by field through this.
    /// </summary>
    private static string Describe(Change change) => change switch
    {
        Change.NotesChanged changed => $"notes changed: {string.Join(",", changed.Ids)}",
        Change.NotesDeleted deleted => $"notes deleted: {string.Join(",", deleted.Ids)}",
        Change.DocumentChanged saved => $"document changed: {saved.NoteId} revision {saved.Revision} origin {saved.Origin}",
        Change.BoardChanged board => $"board changed: {board.TodoListId}",
        _ => change.ToString(),
    };

    private sealed class Recorder : ChangeObserver
    {
        private readonly object gate = new();
        private List<string[]> batches = [];
        private List<int> threads = [];

        public void OnChanges(Change[] changes)
        {
            lock (gate)
            {
                batches.Add(changes.Select(Describe).ToArray());
                threads.Add(Environment.CurrentManagedThreadId);
            }
        }

        public string[][] Take()
        {
            lock (gate)
            {
                var taken = batches.ToArray();
                batches = [];
                return taken;
            }
        }

        public int[] Threads()
        {
            lock (gate) return threads.ToArray();
        }
    }

    [Fact]
    public void ANewLibraryHasItsDefaultNotebook()
    {
        var notes = store.ListNotes();

        Assert.Single(notes);
        Assert.Equal(NoteKind.Notebook, notes[0].Kind);
        Assert.Null(notes[0].ParentId);
        Assert.True(File.Exists(Path.Combine(directory, "heracles.db")));
    }

    [Fact]
    public void RecordsWithDefaultsAndOptionalsCross()
    {
        var page = store.CreateNote(new NewNote(Title: "Trip", Icon: "🧭"));
        Assert.Equal(NoteKind.Page, page.Kind);
        Assert.Equal("Trip", page.Title);
        Assert.Equal("🧭", page.Icon);

        var cleared = store.SetNoteIcon(page.Id, null);
        Assert.Null(cleared.Icon);

        var section = store.CreateNote(new NewNote(Kind: NoteKind.Section, Title: "Plans"));
        var moved = store.MoveNote(page.Id, section.Id, null);
        Assert.Equal(section.Id, moved.ParentId);
    }

    [Fact]
    public void DocumentsSaveAndSearch()
    {
        var page = store.CreateNote(new NewNote(Title: "Trip"));
        var saved = store.SaveDocument(page.Id, Document("pack the lantern"), "test");
        Assert.Equal(1, saved.Revision);
        Assert.Equal(saved, store.GetDocument(page.Id));

        var hits = store.SearchNotes("lant", 5);
        Assert.Equal([page.Id], hits.Select(hit => hit.Note.Id));
        Assert.Equal(["lantern"], hits[0].Snippet.Where(part => part.Matched).Select(part => part.Text));
    }

    [Fact]
    public void ErrorsArriveAsTheirCase()
    {
        Assert.Throws<CoreException.NotFound>(() => store.RenameNote("missing", "x"));

        var notebook = store.ListNotes()[0];
        var error = Assert.Throws<CoreException.LastNotebook>(() => store.DeleteNote(notebook.Id));
        Assert.Contains("last notebook", error.Message);
    }

    [Fact]
    public void ClearingAFieldIsToldFromLeavingItAlone()
    {
        var list = store.CreateNote(new NewNote(Kind: NoteKind.TodoList));
        var column = store.ListColumns(list.Id)[0];
        var task = store.CreateTask(new NewTask(TodoListId: list.Id, ColumnId: column.Id, Title: "Book ferry"));
        Assert.Equal(4, task.Priority);

        var dated = store.UpdateTask(
            task.Id, new TaskPatch(DueDate: "2026-12-01", Labels: ["travel"], Location: "Harbour"));
        Assert.Equal("2026-12-01", dated.DueDate);
        Assert.Equal(["travel"], dated.Labels);
        Assert.Equal("Harbour", dated.Location);

        var cleared = store.UpdateTask(task.Id, new TaskPatch(ClearDueDate: true));
        Assert.Null(cleared.DueDate);
        // A field left out of the patch is untouched.
        Assert.Equal("Harbour", cleared.Location);

        var moved = store.MoveTask(task.Id, column.Id, null, null);
        Assert.Equal(column.Id, moved.ColumnId);
    }

    [Fact]
    public void ImageBytesCross()
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF];
        var image = store.StoreImage(bytes, "image/png");
        Assert.Equal($"{CoreApi.ImageUrlScheme()}://local/{image.Hash}", image.Url);

        var path = store.ImagePath(image.Url);
        Assert.NotNull(path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ACSharpObserverReceivesTheChangeFeed()
    {
        var recorder = new Recorder();
        var subscription = store.Subscribe(recorder);

        var page = store.CreateNote(new NewNote());
        Assert.Equal([[$"notes changed: {page.Id}"]], recorder.Take());

        store.SaveDocument(page.Id, Document("x"), "editor-1");
        Assert.Equal(
            [[$"notes changed: {page.Id}", $"document changed: {page.Id} revision 1 origin editor-1"]],
            recorder.Take());

        store.Unsubscribe(subscription);
        store.CreateNote(new NewNote());
        Assert.Empty(recorder.Take());
    }

    [Fact]
    public void TheChangeFeedArrivesOnTheThreadThatWrote()
    {
        var recorder = new Recorder();
        store.Subscribe(recorder);

        // A thread of its own: a task could be run on the thread that waits for it.
        var writer = 0;
        var thread = new Thread(() =>
        {
            writer = Environment.CurrentManagedThreadId;
            store.CreateNote(new NewNote());
        });
        thread.Start();
        thread.Join();

        Assert.Equal([writer], recorder.Threads());
        Assert.NotEqual(Environment.CurrentManagedThreadId, writer);
    }

    [Fact]
    public void ALibraryOpensInAFolderWithSpacesAndAccents()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"heracles-binding-tests-{Guid.NewGuid()}", "Notes Ünïcödé 日本");
        try
        {
            using var elsewhere = new Store(folder);

            Assert.Single(elsewhere.ListNotes());
            Assert.True(File.Exists(Path.Combine(folder, "heracles.db")));
        }
        finally
        {
            DeleteQuietly(Path.GetDirectoryName(folder)!);
        }
    }

    [Fact]
    public void AFileInTheWayIsReportedAsAnError()
    {
        var file = Path.Combine(Path.GetTempPath(), $"heracles-binding-tests-{Guid.NewGuid()}.txt");
        File.WriteAllText(file, "not a folder");
        try
        {
            var error = Assert.ThrowsAny<CoreException>(() => { using var opened = new Store(file); });
            Assert.NotEqual("", error.Message.Trim());
        }
        finally
        {
            DeleteQuietly(file);
        }
    }
}
