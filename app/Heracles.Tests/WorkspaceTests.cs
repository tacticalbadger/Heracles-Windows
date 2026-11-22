using Heracles.Model;
using Heracles.Workspaces;

namespace Heracles.Tests;

/// <summary>One window's state against a real core in a throwaway folder.</summary>
public sealed class WorkspaceTests : IDisposable
{
    private readonly TestLibrary library = new();
    private readonly AppModel model;
    private readonly Workspace workspace;

    public WorkspaceTests()
    {
        model = new AppModel(library.Store);
        workspace = new Workspace(model);
    }

    public void Dispose()
    {
        model.Dispose();
        library.Dispose();
    }

    private Note Notebook => model.Library.Notebooks[0].Note;

    private Note Created(NoteKind kind, string? title = null)
    {
        var note = model.CreateNote(kind, Notebook.Id, title);
        Assert.NotNull(note);
        return note;
    }

    private Note CreatedHere(NoteKind kind)
    {
        var note = workspace.CreateNote(kind);
        Assert.NotNull(note);
        return note;
    }

    private IEnumerable<string[]> Tabs => workspace.Tree.Leaves.Select(leaf => leaf.Tabs.ToArray());

    [Fact]
    public void ANewNoteOpensInTheFocusedPane()
    {
        var page = CreatedHere(NoteKind.Page);
        Assert.Equal(page.Id, workspace.Tree.ActiveNoteId);
        Assert.Equal(Notebook.Id, page.ParentId); // with nothing selected it goes in the first notebook

        var inside = CreatedHere(NoteKind.Page);
        Assert.Equal(page.Id, inside.ParentId); // then inside the note that is open

        var list = CreatedHere(NoteKind.TodoList);
        var beside = CreatedHere(NoteKind.Page);
        Assert.Equal(list.ParentId, beside.ParentId); // a todo list cannot hold notes, so the new one goes beside it
    }

    [Fact]
    public void OpeningWithSplitPutsTheNoteInANewPane()
    {
        var a = CreatedHere(NoteKind.Page);
        var b = Created(NoteKind.Page);
        workspace.Open(b.Id, SplitTarget.Right);
        Assert.Equal([[a.Id], [b.Id]], Tabs);
        Assert.Equal(b.Id, workspace.Tree.ActiveNoteId);
    }

    [Fact]
    public void NotesDeletedAnywhereLeaveThisWindowsPanes()
    {
        var a = CreatedHere(NoteKind.Page);
        var b = Created(NoteKind.Page);
        workspace.Open(b.Id, SplitTarget.Right);

        // Deleted through the core, as another window would.
        library.Store.DeleteNote(b.Id);
        workspace.LibraryChanged();

        Assert.Equal([[a.Id]], Tabs);
    }

    [Fact]
    public void ClosingTheActiveTabUsesTheFocusedPane()
    {
        var a = CreatedHere(NoteKind.Page);
        var b = Created(NoteKind.Page);
        workspace.Open(b.Id);
        workspace.CloseActiveTab();
        Assert.Equal(a.Id, workspace.Tree.ActiveNoteId);
    }

    [Fact]
    public void ALinkOpensItsNoteAndRemembersTheBlockToShow()
    {
        var page = Created(NoteKind.Page);
        workspace.Follow(new AppLink(page.Id, "block-7"));
        Assert.Equal(page.Id, workspace.Tree.ActiveNoteId);
        Assert.Equal("block-7", workspace.BlockToReveal(page.Id));

        workspace.Follow(new AppLink("missing", null));
        Assert.Equal(page.Id, workspace.Tree.ActiveNoteId); // a link to nothing changes nothing

        workspace.DidReveal(page.Id);
        Assert.Null(workspace.BlockToReveal(page.Id));
    }

    [Fact]
    public void FilesAreImportedIntoTheOpenNoteAndTheLastOneIsShown()
    {
        var section = CreatedHere(NoteKind.Section);
        var folder = Path.Combine(library.Folder, "files");
        Directory.CreateDirectory(folder);
        var first = Path.Combine(folder, "One.md");
        var second = Path.Combine(folder, "Two.html");
        File.WriteAllText(first, "# One\n");
        File.WriteAllText(second, "<p>Two</p>");

        var imported = workspace.ImportFiles([first, second]);

        Assert.Equal(["One", "Two"], imported.Select(note => note.Title));
        Assert.All(imported, note => Assert.Equal(section.Id, note.ParentId));
        Assert.Equal(imported[1].Id, workspace.Tree.ActiveNoteId);
    }

    [Fact]
    public void TheOpenNoteIsExportedAsMarkdown()
    {
        var page = CreatedHere(NoteKind.Page);
        model.SaveDocument(page.Id, TestLibrary.Document("Exported"), null);
        var destination = Path.Combine(library.Folder, "out", "Page.md");

        Assert.True(workspace.ExportActiveNote(destination));
        Assert.Equal("Exported\n", File.ReadAllText(destination));
    }

    [Fact]
    public void TheSwitcherListsRecentNotesAndThenSearches()
    {
        var old = Created(NoteKind.Page, "Older lantern notes");
        Thread.Sleep(5);
        var recent = Created(NoteKind.Page, "Newer");
        model.Trash(Created(NoteKind.Page, "Trashed").Id);

        var recents = workspace.SwitcherResults("  ").Select(note => note.Id).ToList();
        Assert.Equal([recent.Id, old.Id], recents.Take(2)); // most recently changed first
        Assert.DoesNotContain(Notebook.Id, recents); // notebooks are not something to switch to
        Assert.Empty(workspace.SwitcherResults("trashed"));
        Assert.Equal([old.Id], workspace.SwitcherResults("lant").Select(note => note.Id));
    }

    [Fact]
    public void TheWindowHearsOfEachChangeToItsPanesOnce()
    {
        var a = Created(NoteKind.Page);
        var heard = 0;
        workspace.TreeChanged += () => heard++;

        workspace.Open(a.Id);
        Assert.Equal(1, heard);

        workspace.Open(a.Id); // already open and showing: nothing changes
        Assert.Equal(1, heard);

        workspace.SplitFocused(SplitTarget.Down);
        Assert.Equal(2, heard);
    }

    [Fact]
    public void ActionsOnTheOpenNote()
    {
        Assert.Null(workspace.LinkToActive());
        var page = CreatedHere(NoteKind.Page);
        Assert.Equal($"heracles://note/{page.Id}", workspace.LinkToActive());

        workspace.ToggleFavoriteActive();
        Assert.True(model.Library.Note(page.Id)?.Favorited);

        workspace.DuplicateActive();
        Assert.NotEqual(page.Id, workspace.Tree.ActiveNoteId); // the copy is showing

        var copy = workspace.Tree.ActiveNoteId!;
        workspace.TrashActive();
        Assert.True(model.Library.Note(copy)?.Trashed);
    }
}
