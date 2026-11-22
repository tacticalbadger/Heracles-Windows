namespace Heracles.Support;

/// <summary>
/// A testing aid: a few notes of every kind in an empty library, so the
/// end-to-end checks have something to look at. It does nothing unless the
/// library folder was given by <c>HERACLES_DATA_DIR</c>, which the real
/// library never is, and nothing in a library that already holds notes.
/// </summary>
internal static class E2eSeed
{
    public const string Argument = "--e2e-seed";

    private static string Document(string text) =>
        """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":""" + System.Text.Json.JsonSerializer.Serialize(text) + "}]}]}";

    public static void Run(Store store)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable)))
        {
            Log.Info("seed: refused, this is not a test library");
            return;
        }
        var existing = store.ListNotes();
        if (existing.Length != 1)
        {
            Log.Info("seed: skipped, the library is not empty");
            return;
        }
        var notebook = existing[0];

        var plans = store.CreateNote(new NewNote(Kind: NoteKind.Section, ParentId: notebook.Id, Title: "Plans"));
        var trip = store.CreateNote(new NewNote(ParentId: plans.Id, Title: "Trip", Icon: "🧭", Content: Document("Pack the lantern.")));
        store.SetNoteFavorited(trip.Id, true);
        store.CreateNote(new NewNote(ParentId: plans.Id, Title: "Budget", Content: Document("Ferry tickets, and a new lantern.")));
        store.CreateNote(new NewNote(Kind: NoteKind.TodoList, ParentId: notebook.Id, Title: "Packing"));
        var old = store.CreateNote(new NewNote(ParentId: notebook.Id, Title: "Old ideas"));
        store.SetNoteArchived(old.Id, true);
        var scrap = store.CreateNote(new NewNote(ParentId: notebook.Id, Title: "Scrap"));
        store.TrashNote(scrap.Id);
        // Titles a view must cope with: none at all, and far too long.
        var unnamed = store.CreateNote(new NewNote(ParentId: notebook.Id));
        store.RenameNote(unnamed.Id, "");
        store.CreateNote(new NewNote(ParentId: notebook.Id, Title: "A very long title " + new string('x', 280)));

        Log.Info($"seed: created {store.ListNotes().Length - 1} notes");
    }
}
