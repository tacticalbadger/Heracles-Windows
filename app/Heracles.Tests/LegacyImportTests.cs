using System.Security.Cryptography;
using Heracles.Model;

namespace Heracles.Tests;

/// <summary>
/// The original app's library, brought across on the first start. The
/// library imported is the one described in <c>Fixtures\README.md</c>,
/// copied to where the original app would have left it.
/// </summary>
public sealed class LegacyImportTests : IDisposable
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy.sqlite");

    private readonly TestLibrary library = new();
    private readonly AppModel model;

    public LegacyImportTests()
    {
        model = new AppModel(library.Store);
    }

    public void Dispose()
    {
        model.Dispose();
        library.Dispose();
    }

    /// <summary>The library folder, which is where the original app kept its own.</summary>
    private string DataDirectory => Path.Combine(library.Folder, "library");

    private string OldLibrary => Path.Combine(DataDirectory, "heracles.sqlite");

    private void LeaveAnOldLibrary() => File.Copy(Fixture, OldLibrary);

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public void ThereIsAnOldLibraryOnlyWhereTheOriginalAppLeftOne()
    {
        Assert.Null(AppModel.LegacyDatabase(DataDirectory));
        LeaveAnOldLibrary();
        Assert.Equal(OldLibrary, AppModel.LegacyDatabase(DataDirectory));
        Assert.Null(AppModel.LegacyDatabase(Path.Combine(library.Folder, "no-such-folder")));
    }

    [Fact]
    public void WithNoOldLibraryThereIsNothingToDo()
    {
        Assert.Null(model.ImportLegacyLibraryIfNeeded(AppModel.LegacyDatabase(DataDirectory)));
        Assert.Null(model.LastError);
        Assert.Single(model.Library.Notebooks);
    }

    [Fact]
    public void AnEmptyLibraryTakesTheOldOneAndSaysWhatCame()
    {
        LeaveAnOldLibrary();
        var report = model.ImportLegacyLibraryIfNeeded(AppModel.LegacyDatabase(DataDirectory));

        Assert.NotNull(report);
        Assert.Equal(7u, report.Notes);
        Assert.Equal(3u, report.Tasks);
        Assert.Equal(2u, report.Columns);
        Assert.Equal(1u, report.Comments);
        Assert.Equal(1u, report.Images);
        Assert.Contains(report.Repairs, line => line.Contains("old-orphan") || line.Contains("Lost page"));
        Assert.True(File.Exists(report.BackupPath));
        Assert.Null(model.LastError);
    }

    [Fact]
    public void TheOldNotesAreInTheLibraryWithoutAsking()
    {
        LeaveAnOldLibrary();
        model.ImportLegacyLibraryIfNeeded(OldLibrary);

        // The model heard of them through the change feed, as it hears of everything.
        Assert.Equal("Old trip", model.Library.Note("old-page")?.Title);
        Assert.True(model.Library.Note("old-page")?.Favorited);
        Assert.Equal(NoteKind.TodoList, model.Library.Note("old-list")?.Kind);
        Assert.Contains(model.Library.Trashed, note => note.Id == "old-scrap");
        // The page that had lost its place has one again.
        Assert.NotNull(model.Library.Note("old-orphan")?.ParentId);
        // And its board came with it.
        Assert.Equal(["Up Next", "Done"], model.Columns("old-list").Select(column => column.Name));
        Assert.Equal(3, model.Tasks("old-list").Count);
    }

    [Fact]
    public void TheOldLibraryIsCopiedFirstAndNeverChanged()
    {
        LeaveAnOldLibrary();
        var before = Sha256(OldLibrary);
        var report = model.ImportLegacyLibraryIfNeeded(OldLibrary);

        Assert.NotNull(report);
        Assert.Equal(before, Sha256(OldLibrary));
        // The copy is one SQLite made (the same library, not the same bytes),
        // and it is kept with the new library.
        Assert.StartsWith(Path.Combine(DataDirectory, "backups"), report.BackupPath);
        Assert.True(new FileInfo(report.BackupPath).Length > 0);
    }

    [Fact]
    public void ALibraryThatAlreadyHoldsANoteIsLeftAlone()
    {
        LeaveAnOldLibrary();
        var mine = model.CreateNote(NoteKind.Page, model.Library.Notebooks[0].Note.Id, "Mine");
        Assert.NotNull(mine);

        Assert.Null(model.ImportLegacyLibraryIfNeeded(OldLibrary));
        Assert.Null(model.LastError);
        Assert.Null(model.Library.Note("old-page"));
        Assert.Equal("Mine", model.Library.Note(mine.Id)?.Title);
        // And no copy of the old library was made for an import that did not happen.
        Assert.False(Directory.Exists(Path.Combine(DataDirectory, "backups")));
    }

    [Fact]
    public void ASecondStartImportsNothingAndComplainsOfNothing()
    {
        LeaveAnOldLibrary();
        Assert.NotNull(model.ImportLegacyLibraryIfNeeded(OldLibrary));
        var notes = library.Store.ListNotes().Length;

        Assert.Null(model.ImportLegacyLibraryIfNeeded(OldLibrary));
        Assert.Null(model.LastError);
        Assert.Equal(notes, library.Store.ListNotes().Length);
    }

    [Fact]
    public void EvenWithEverythingDeletedSinceTheImportIsNotRunAgain()
    {
        LeaveAnOldLibrary();
        Assert.NotNull(model.ImportLegacyLibraryIfNeeded(OldLibrary));
        // The user empties the library, down to the one notebook that cannot go.
        foreach (var note in library.Store.ListNotes().Where(note => note.Kind != NoteKind.Notebook))
        {
            try { library.Store.DeleteNote(note.Id); }
            catch (UniffiException) { /* Went with its parent. */ }
        }

        // What was deleted stays deleted: the old library does not come back.
        Assert.Null(model.ImportLegacyLibraryIfNeeded(OldLibrary));
        Assert.Null(model.LastError);
        Assert.Null(model.Library.Note("old-page"));
    }

    [Fact]
    public void AnOldLibraryThatCannotBeReadIsSaidToBeUntouched()
    {
        File.WriteAllText(OldLibrary, "This is not a database.");
        var before = Sha256(OldLibrary);

        Assert.Null(model.ImportLegacyLibraryIfNeeded(OldLibrary));

        Assert.NotNull(model.LastError);
        Assert.Contains("has not been changed", model.LastError);
        Assert.Equal(before, Sha256(OldLibrary));
        // The new library is as it was: one notebook, and nothing else.
        Assert.Single(library.Store.ListNotes());
    }
}

/// <summary>What the summary says once the old library is across.</summary>
public sealed class ImportSummaryTests
{
    private static ImportReport Report(uint notes, uint tasks, uint images, params string[] repairs) =>
        new(Notes: notes, Documents: notes, Images: images, Columns: 0, Tasks: tasks, Comments: 0, BackupPath: "backup", Repairs: repairs);

    [Fact]
    public void ItCountsWhatCame()
    {
        Assert.Equal(
            "7 notes, 3 tasks and 1 image were brought across from the previous version of Heracles.",
            ImportSummary.Counts(Report(7, 3, 1)));
    }

    [Fact]
    public void OneOfSomethingIsNotSeveral()
    {
        Assert.Equal(
            "1 note, 1 task and 0 images were brought across from the previous version of Heracles.",
            ImportSummary.Counts(Report(1, 1, 0)));
    }

    [Fact]
    public void ItSaysTheOldLibraryIsSafeAndWhereTheCopyIs()
    {
        Assert.Equal("Your library has been imported", ImportSummary.Headline);
        Assert.Equal("The previous library was not changed. A copy of it was saved at:", ImportSummary.Kept);
        Assert.Equal("Some items needed adjusting to fit:", ImportSummary.Adjusted);
    }
}
