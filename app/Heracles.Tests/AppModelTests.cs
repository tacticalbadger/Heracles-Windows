using System.Collections.Concurrent;
using Heracles.Model;

namespace Heracles.Tests;

/// <summary>The model against a real core in a throwaway folder.</summary>
public sealed class AppModelTests : IDisposable
{
    private readonly TestLibrary library = new();
    private readonly AppModel model;

    public AppModelTests()
    {
        model = new AppModel(library.Store);
    }

    public void Dispose()
    {
        model.Dispose();
        library.Dispose();
    }

    private Store Store => library.Store;

    private Note Notebook => model.Library.Notebooks[0].Note;

    [Fact]
    public void ItStartsWithTheLibraryAsStored()
    {
        Assert.Single(model.Library.Notebooks);
        Assert.Equal(NoteKind.Notebook, Notebook.Kind);
    }

    [Fact]
    public void ItsOwnActionsShowUpInTheLibrary()
    {
        var page = model.CreateNote(NoteKind.Page, Notebook.Id);
        Assert.NotNull(page);
        Assert.Equal([page.Id], model.Library.Notebooks[0].Children.Select(node => node.Id));

        model.Rename(page.Id, "Renamed");
        Assert.Equal("Renamed", model.Library.Note(page.Id)?.Title);

        model.Trash(page.Id);
        Assert.Empty(model.Library.Notebooks[0].Children);
        Assert.Equal([page.Id], model.Library.Trashed.Select(note => note.Id));
    }

    [Fact]
    public void AWriteMadeElsewhereShowsUpWithoutAsking()
    {
        // Another window, or the editor, writes straight to the core.
        var page = Store.CreateNote(new NewNote(Title: "From elsewhere"));
        Assert.Equal("From elsewhere", model.Library.Note(page.Id)?.Title);

        Store.DeleteNote(page.Id);
        Assert.Null(model.Library.Note(page.Id));
    }

    [Fact]
    public void AWriteFromAnotherThreadArrivesOnTheOwningThread()
    {
        using var elsewhere = new TestLibrary();
        var queue = new ConcurrentQueue<Action>();
        using var queued = new AppModel(elsewhere.Store, queue.Enqueue);
        var changes = 0;
        queued.Changed += () => changes++;

        var id = "";
        var thread = new Thread(() => id = elsewhere.Store.CreateNote(new NewNote(Title: "Background")).Id);
        thread.Start();
        thread.Join();

        // The change was delivered on the writing thread and is waiting to hop.
        Assert.Null(queued.Library.Note(id));
        Assert.Equal(0, changes);

        while (queue.TryDequeue(out var action)) action();
        Assert.Equal("Background", queued.Library.Note(id)?.Title);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SavedDocumentsAreAnnouncedWithWhoSavedThem()
    {
        var page = model.CreateNote(NoteKind.Page, Notebook.Id);
        Assert.NotNull(page);
        Assert.Null(model.DocumentChangeFor(page.Id));

        model.SaveDocument(page.Id, TestLibrary.Document("one"), "editor-A");
        Assert.Equal(new DocumentChange(1, "editor-A"), model.DocumentChangeFor(page.Id));

        Store.SaveDocument(page.Id, TestLibrary.Document("two"), null);
        Assert.Equal(new DocumentChange(2, null), model.DocumentChangeFor(page.Id));
    }

    [Fact]
    public void BoardChangesBumpThatBoardOnly()
    {
        var list = model.CreateNote(NoteKind.TodoList, Notebook.Id);
        var other = model.CreateNote(NoteKind.TodoList, Notebook.Id);
        Assert.NotNull(list);
        Assert.NotNull(other);
        var before = (model.BoardVersion(list.Id), model.BoardVersion(other.Id));

        var column = Store.ListColumns(list.Id)[0];
        Store.CreateTask(new NewTask(TodoListId: list.Id, ColumnId: column.Id, Title: "x"));

        Assert.Equal(before.Item1 + 1, model.BoardVersion(list.Id));
        Assert.Equal(before.Item2, model.BoardVersion(other.Id));
    }

    [Fact]
    public void AFailedActionIsReportedAndChangesNothing()
    {
        var reported = 0;
        model.ErrorChanged += () => reported++;
        Assert.Null(model.LastError);

        model.Rename("missing", "x");
        Assert.Contains("does not exist", model.LastError);
        Assert.Equal(1, reported);

        model.Delete(Notebook.Id);
        Assert.Contains("last notebook", model.LastError);
        Assert.Single(model.Library.Notebooks);

        // A success clears it.
        model.CreateNote(NoteKind.Page, Notebook.Id);
        Assert.Null(model.LastError);
    }

    [Fact]
    public void DeletingANoteReportsEveryIDThatWent()
    {
        var page = model.CreateNote(NoteKind.Page, Notebook.Id);
        Assert.NotNull(page);
        var child = model.CreateNote(NoteKind.Page, page.Id);
        Assert.NotNull(child);

        Assert.Equal(new HashSet<string> { page.Id, child.Id }, model.Delete(page.Id).ToHashSet());
        Assert.Null(model.Library.Note(child.Id));
    }

    [Fact]
    public void EveryChangeIsAnnouncedOnce()
    {
        var changes = 0;
        model.Changed += () => changes++;

        var page = model.CreateNote(NoteKind.Page, Notebook.Id);
        Assert.NotNull(page);
        Assert.Equal(1, changes);

        model.Rename(page.Id, "Named");
        Assert.Equal(2, changes);
    }

    [Fact]
    public void AfterItIsDisposedItHearsNothingAndThrowsNothing()
    {
        using var elsewhere = new TestLibrary();
        var queue = new ConcurrentQueue<Action>();
        var gone = new AppModel(elsewhere.Store, queue.Enqueue);
        var changes = 0;
        gone.Changed += () => changes++;

        // A change is already on its way when the model goes.
        var thread = new Thread(() => elsewhere.Store.CreateNote(new NewNote(Title: "Late")));
        thread.Start();
        thread.Join();
        gone.Dispose();

        while (queue.TryDequeue(out var action)) action();
        elsewhere.Store.CreateNote(new NewNote(Title: "Later"));

        Assert.Equal(0, changes);
    }
}
