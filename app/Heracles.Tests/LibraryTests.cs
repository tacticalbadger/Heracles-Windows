using Heracles.Model;

namespace Heracles.Tests;

public sealed class LibraryTests
{
    private static Note Note(
        string id, NoteKind kind = NoteKind.Page, string? parent = null, long at = 0,
        bool favorited = false, bool archived = false, bool trashed = false, string? title = null) =>
        new(
            Id: id, Kind: kind, ParentId: parent, Title: title ?? id, Icon: null, Position: at,
            Favorited: favorited, Archived: archived, Trashed: trashed,
            CreatedAt: "2026-01-01T00:00:00.000Z", UpdatedAt: "2026-01-01T00:00:00.000Z");

    /// <summary>The ids of a tree, written as nested lists: "a[b[c] d]".</summary>
    private static string Outline(IEnumerable<LibraryNode> nodes) =>
        string.Join(" ", nodes.Select(node => node.Children.Count == 0 ? node.Id : $"{node.Id}[{Outline(node.Children)}]"));

    [Fact]
    public void TheTreeFollowsParentsAndManualOrder()
    {
        var library = new Library([
            Note("page-b", parent: "section", at: 1),
            Note("nb-2", NoteKind.Notebook, at: 1),
            Note("section", NoteKind.Section, parent: "nb-1", at: 0),
            Note("page-a", parent: "section", at: 0),
            Note("nb-1", NoteKind.Notebook, at: 0),
            Note("child", parent: "page-a", at: 0),
            Note("list", NoteKind.TodoList, parent: "nb-1", at: 1),
        ]);
        Assert.Equal("nb-1[section[page-a[child] page-b] list] nb-2", Outline(library.Notebooks));
    }

    [Fact]
    public void NotesThatSharePositionAreOrderedByTitle()
    {
        var library = new Library([
            Note("nb", NoteKind.Notebook),
            Note("z", parent: "nb", at: 0, title: "zebra"),
            Note("a", parent: "nb", at: 0, title: "Apple"),
        ]);
        Assert.Equal("nb[a z]", Outline(library.Notebooks));
    }

    [Fact]
    public void TrashedAndArchivedNotesLeaveTheTreeWithEverythingUnderThem()
    {
        var library = new Library([
            Note("nb", NoteKind.Notebook),
            Note("kept", parent: "nb", at: 0),
            Note("trashed", parent: "nb", at: 1, trashed: true),
            Note("under-trashed", parent: "trashed", trashed: true),
            Note("archived", parent: "nb", at: 2, archived: true),
            Note("under-archived", parent: "archived"),
        ]);
        Assert.Equal("nb[kept]", Outline(library.Notebooks));
        Assert.Equal(["archived"], library.Archived.Select(note => note.Id));
    }

    [Fact]
    public void TheTrashListsOnlyTheTopOfEachTrashedSubtree()
    {
        var library = new Library([
            Note("nb", NoteKind.Notebook),
            Note("gone", parent: "nb", trashed: true),
            Note("gone-child", parent: "gone", trashed: true),
            Note("alive", parent: "nb"),
            Note("gone-leaf", parent: "alive", trashed: true),
        ]);
        Assert.Equal(["gone", "gone-leaf"], library.Trashed.Select(note => note.Id).Order());
    }

    [Fact]
    public void FavoritesAreListedByTitleAndSkipTheTrash()
    {
        var library = new Library([
            Note("nb", NoteKind.Notebook),
            Note("b", parent: "nb", favorited: true, title: "Beta"),
            Note("a", parent: "nb", favorited: true, title: "alpha"),
            Note("t", parent: "nb", favorited: true, trashed: true),
        ]);
        Assert.Equal(["a", "b"], library.Favorites.Select(note => note.Id));
    }

    [Fact]
    public void ANoteCanBeLookedUpWithItsAncestors()
    {
        var library = new Library([
            Note("nb", NoteKind.Notebook),
            Note("section", NoteKind.Section, parent: "nb"),
            Note("page", parent: "section"),
        ]);
        Assert.Equal("page", library.Note("page")?.Id);
        Assert.Null(library.Note("missing"));
        Assert.Equal(["nb", "section", "page"], library.Path("page").Select(note => note.Id));
        Assert.Empty(library.Path("missing"));
        Assert.Equal("nb", library.Notebook("page")?.Id);
    }

    [Fact]
    public void ALoopInTheDataCannotHangTheTree()
    {
        // The core's schema forbids this. A view model should still survive it.
        var library = new Library([
            Note("nb", NoteKind.Notebook),
            Note("a", parent: "b"),
            Note("b", parent: "a"),
        ]);
        Assert.Equal("nb", Outline(library.Notebooks));
        Assert.True(library.Path("a").Count <= 2);
    }

    [Theory]
    [InlineData("", "Untitled")]
    [InlineData("  ", "Untitled")]
    [InlineData("Trip", "Trip")]
    [InlineData("a\r\nb", "a b")]
    public void ATitleIsShownOnOneLineAndNeverEmpty(string title, string shown)
    {
        Assert.Equal(shown, Note("n", title: title).DisplayTitle());
    }
}
