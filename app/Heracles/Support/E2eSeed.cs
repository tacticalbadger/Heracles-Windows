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

    /// <summary>A note holding one of everything the editor draws, to be looked at.</summary>
    private const string EveryKind =
        """
        {"type":"doc","content":[
          {"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"A trip to the coast"}]},
          {"type":"paragraph","content":[
            {"type":"text","text":"Plain words, then "},{"type":"text","text":"bold","marks":[{"type":"bold"}]},
            {"type":"text","text":", "},{"type":"text","text":"italic","marks":[{"type":"italic"}]},
            {"type":"text","text":", "},{"type":"text","text":"struck","marks":[{"type":"strike"}]},
            {"type":"text","text":", "},{"type":"text","text":"code","marks":[{"type":"code"}]},
            {"type":"text","text":" and "},{"type":"text","text":"a link","marks":[{"type":"link","attrs":{"href":"https://example.com"}}]},
            {"type":"text","text":", with a lantern 💡 and a compass 🧭 among them."}]},
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"What to bring"}]},
          {"type":"bulletList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Tent"}]},
              {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Pegs and a mallet"}]}]}]}]},
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Stove"}]}]}]},
          {"type":"orderedList","content":[
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Book the ferry"}]}]},
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Pack the car"}]}]}]},
          {"type":"taskList","content":[
            {"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"Charge the lantern"}]}]},
            {"type":"taskItem","attrs":{"checked":false},"content":[{"type":"paragraph","content":[{"type":"text","text":"Find the map"}]}]}]},
          {"type":"heading","attrs":{"level":3},"content":[{"type":"text","text":"Notes"}]},
          {"type":"blockquote","content":[{"type":"paragraph","content":[{"type":"text","text":"The sea is never the same twice."}]}]},
          {"type":"calloutBlock","attrs":{"emoji":"🔥"},"content":[{"type":"paragraph","content":[{"type":"text","text":"No fires on the beach after June."}]}]},
          {"type":"toggleBlock","attrs":{"open":true},"content":[
            {"type":"paragraph","content":[{"type":"text","text":"Tide times"}]},
            {"type":"paragraph","content":[{"type":"text","text":"High water at six, low at noon."}]}]},
          {"type":"codeMirrorBlock","attrs":{"language":"rust","code":"fn main() {\n    println!(\"ahoy\");\n}"}},
          {"type":"horizontalRule"},
          {"type":"columnBlock","content":[
            {"type":"column","attrs":{"width":1},"content":[{"type":"paragraph","content":[{"type":"text","text":"On the left, the way there."}]}]},
            {"type":"column","attrs":{"width":1},"content":[{"type":"paragraph","content":[{"type":"text","text":"On the right, the way back."}]}]}]},
          {"type":"image","attrs":{"src":"PICTURE","alt":"A map of the coast","title":null}},
          {"type":"image","attrs":{"src":"heracles-image://local/0000000000000000000000000000000000000000000000000000000000000000","alt":"A picture that has gone missing","title":null}},
          {"type":"paragraph","content":[{"type":"text","text":"The end."}]}
        ]}
        """;

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
        Board(store, store.CreateNote(new NewNote(Kind: NoteKind.TodoList, ParentId: notebook.Id, Title: "Packing")));
        var old = store.CreateNote(new NewNote(ParentId: notebook.Id, Title: "Old ideas"));
        store.SetNoteArchived(old.Id, true);
        var scrap = store.CreateNote(new NewNote(ParentId: notebook.Id, Title: "Scrap"));
        store.TrashNote(scrap.Id);
        store.CreateNote(new NewNote(ParentId: plans.Id, Title: "Every kind of block", Icon: "🧪", Content: EveryKind.Replace("PICTURE", store.StoreImage(Picture(), "image/png").Url)));
        // Titles a view must cope with: none at all, and far too long.
        var unnamed = store.CreateNote(new NewNote(ParentId: notebook.Id));
        store.RenameNote(unnamed.Id, "");
        store.CreateNote(new NewNote(ParentId: notebook.Id, Title: "A very long title " + new string('x', 280)));

        Log.Info($"seed: created {store.ListNotes().Length - 1} notes");
    }

    /// <summary>
    /// A picture made here, so the seed needs no file: sea, sand and a sun, 480
    /// by 200. Something with colour and shape in it, to be looked at.
    /// </summary>
    private static byte[] Picture()
    {
        const int width = 480;
        const int height = 200;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Sky above, sea in the middle, sand below; a sun in the sky.
                var (red, green, blue) = y < 90 ? (150, 200, 240) : y < 150 ? (40, 110, 170) : (225, 200, 150);
                var fromSun = Math.Sqrt(Math.Pow(x - 370, 2) + Math.Pow(y - 48, 2));
                if (fromSun < 26) (red, green, blue) = (250, 210, 80);
                var at = (y * width + x) * 4;
                pixels[at] = (byte)blue;
                pixels[at + 1] = (byte)green;
                pixels[at + 2] = (byte)red;
                pixels[at + 3] = 255;
            }
        }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>A board with a card of every sort: plain, done, and one with everything a card can say.</summary>
    private static void Board(Store store, Note list)
    {
        var columns = store.ListColumns(list.Id);
        var tent = store.CreateTask(new NewTask(TodoListId: list.Id, ColumnId: columns[0].Id, Title: "Tent", Description: "The green one.", Priority: 2));
        store.UpdateTask(tent.Id, new TaskPatch(Labels: ["camping"], DueDate: Heracles.Boards.TaskDates.DueDate(DateTimeOffset.Now.AddDays(3))));
        var pegs = store.CreateTask(new NewTask(TodoListId: list.Id, ParentTaskId: tent.Id, Title: "Pegs"));
        store.UpdateTask(pegs.Id, new TaskPatch(Completed: true));
        store.CreateTask(new NewTask(TodoListId: list.Id, ParentTaskId: tent.Id, Title: "Mallet"));
        store.CreateComment(tent.Id, "Borrow the big one?");
        store.CreateTask(new NewTask(TodoListId: list.Id, ColumnId: columns[0].Id, Title: "Stove"));
        store.CreateTask(new NewTask(TodoListId: list.Id, ColumnId: columns[1].Id, Title: "Map"));
        var lantern = store.CreateTask(new NewTask(TodoListId: list.Id, ColumnId: columns[2].Id, Title: "Lantern"));
        store.UpdateTask(lantern.Id, new TaskPatch(Completed: true));
    }
}
