using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Heracles.Editor;
using Heracles.Model;
using Heracles.Notes;
using Heracles.Tests.Editor;

namespace Heracles.Tests;

/// <summary>
/// The app's end of an editor, against a real core in a throwaway folder:
/// what is typed is saved, a save made elsewhere is followed, and what the
/// editor asks of the app is answered.
/// </summary>
public sealed class EditorCoordinatorTests
{
    /// <summary>Stands in for the passing of time: what was put off is run when the test says.</summary>
    private sealed class Clock
    {
        public List<(Action Work, TimeSpan After)> Waiting { get; } = [];

        public void Later(Action work, TimeSpan after) => Waiting.Add((work, after));

        public void Run()
        {
            var due = Waiting.ToList();
            Waiting.Clear();
            foreach (var (work, _) in due) work();
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestLibrary library = new();

        public AppModel Model { get; }

        public Clock Clock { get; } = new();

        public List<string> OpenedOutside { get; } = [];

        public List<string> Copied { get; } = [];

        public Fixture()
        {
            Model = new AppModel(library.Store, action => action());
        }

        public Note Note(string text, string title = "Trip") => library.Store.CreateNote(
            new NewNote(ParentId: library.Store.ListNotes()[0].Id, Title: title, Content: TestLibrary.Document(text)));

        public EditorCoordinator Editor()
        {
            var coordinator = new EditorCoordinator(Model, Clock.Later)
            {
                OpenExternally = OpenedOutside.Add,
                CopyText = Copied.Add,
            };
            Views.Layout(coordinator.Editor.ScrollView);
            return coordinator;
        }

        /// <summary>The words of a note as the core has them stored.</summary>
        public string Stored(Note note) => ReadOnlyDocument.PlainText(Model.Document(note.Id)?.Content ?? "");

        public long Revision(Note note) => Model.Document(note.Id)?.Revision ?? -1;

        public void Dispose()
        {
            Model.Dispose();
            library.Dispose();
        }
    }

    private static void With(Action<Fixture> test) => Sta.Run(() =>
    {
        using var fixture = new Fixture();
        test(fixture);
    });

    private static string Words(EditorCoordinator coordinator) =>
        string.Join("\n", coordinator.Editor.Document.AllBlocks.Where(block => block.HasText).Select(block => block.Text.String));

    private static void Type(EditorCoordinator coordinator, string text)
    {
        var last = coordinator.Editor.Document.Stops[^1];
        coordinator.Editor.Select(EditorSelection.Caret(last.Id, last.Text.Length));
        coordinator.Editor.InsertText(text);
    }

    [Fact]
    public void WhatIsTypedIsSavedAfterAPauseWithTheEditorsIdAsItsOrigin() => With(fixture =>
    {
        var note = fixture.Note("Hello");
        var editor = fixture.Editor();
        editor.Show(note.Id, editable: true, change: null);
        Assert.Equal(note.Id, editor.ShownNoteId);
        Assert.Equal("Hello", Words(editor));
        var revision = fixture.Revision(note);

        Type(editor, " world");
        Type(editor, "!");
        // Not yet: typing must pause first.
        Assert.Equal("Hello", fixture.Stored(note));
        Assert.All(fixture.Clock.Waiting, waiting => Assert.Equal(TimeSpan.FromMilliseconds(300), waiting.After));

        fixture.Clock.Run();
        Assert.Equal("Hello world!", fixture.Stored(note));
        // One save for the run of typing, and the feed says whose it was.
        Assert.Equal(revision + 1, fixture.Revision(note));
        Assert.Equal(editor.EditorId, fixture.Model.DocumentChangeFor(note.Id)?.Origin);

        // Its own save is not a reason to load the note again.
        var document = editor.Editor.Document;
        editor.Show(note.Id, editable: true, fixture.Model.DocumentChangeFor(note.Id));
        Assert.Same(document, editor.Editor.Document);
    });

    [Fact]
    public void ASaveMadeElsewhereReplacesTheWorkingCopy() => With(fixture =>
    {
        var note = fixture.Note("Hello");
        var here = fixture.Editor();
        var there = fixture.Editor();
        here.Show(note.Id, true, null);
        there.Show(note.Id, true, null);

        Type(there, " from the other pane");
        fixture.Clock.Run();
        here.Show(note.Id, true, fixture.Model.DocumentChangeFor(note.Id));
        Assert.Equal("Hello from the other pane", Words(here));
        // And nothing was saved by being shown.
        Assert.Equal(there.EditorId, fixture.Model.DocumentChangeFor(note.Id)?.Origin);
    });

    [Fact]
    public void UnsavedTypingIsNotDroppedForACopySavedElsewhere() => With(fixture =>
    {
        var note = fixture.Note("Hello");
        var here = fixture.Editor();
        var there = fixture.Editor();
        here.Show(note.Id, true, null);
        there.Show(note.Id, true, null);

        Type(here, ", typed here");
        var pending = fixture.Clock.Waiting.ToList();
        fixture.Clock.Waiting.Clear();
        Type(there, ", typed there");
        fixture.Clock.Run();
        Assert.Equal("Hello, typed there", fixture.Stored(note));

        // What is being typed here is kept, and saved in its turn.
        here.Show(note.Id, true, fixture.Model.DocumentChangeFor(note.Id));
        Assert.Equal("Hello, typed here", Words(here));
        foreach (var (work, _) in pending) work();
        Assert.Equal("Hello, typed here", fixture.Stored(note));
        // The other editor then follows this one.
        there.Show(note.Id, true, fixture.Model.DocumentChangeFor(note.Id));
        Assert.Equal("Hello, typed here", Words(there));
    });

    [Fact]
    public void LeavingANoteSavesItAtOnce() => With(fixture =>
    {
        var first = fixture.Note("First");
        var second = fixture.Note("Second", "Budget");
        var editor = fixture.Editor();
        editor.Show(first.Id, true, null);
        Type(editor, " and more");

        editor.Show(second.Id, true, null);
        Assert.Equal("First and more", fixture.Stored(first));
        Assert.Equal("Second", Words(editor));
        Assert.Equal(second.Id, editor.ShownNoteId);
        // The save that was waiting has nothing left to do, and does not touch the other note.
        var revision = fixture.Revision(second);
        fixture.Clock.Run();
        Assert.Equal(revision, fixture.Revision(second));
        Assert.Equal("Second", fixture.Stored(second));
    });

    [Fact]
    public void ClosingAndQuittingSaveWhatIsUnsaved() => With(fixture =>
    {
        var note = fixture.Note("Hello");
        var other = fixture.Note("Other", "Budget");
        var closing = fixture.Editor();
        var quitting = fixture.Editor();
        closing.Show(note.Id, true, null);
        quitting.Show(other.Id, true, null);
        var revision = fixture.Revision(note);

        // With nothing changed, nothing is written.
        closing.Save();
        fixture.Model.SaveEverything();
        Assert.Equal(revision, fixture.Revision(note));

        Type(closing, " there");
        Type(quitting, " too");
        // A tab closed: its editor saves and is let go.
        closing.Dispose();
        Assert.Equal("Hello there", fixture.Stored(note));
        Assert.Equal("Other", fixture.Stored(other));
        // The app ending: every editor still open saves.
        fixture.Model.SaveEverything();
        Assert.Equal("Other too", fixture.Stored(other));
        // An editor that was let go is not asked again.
        var after = fixture.Revision(note);
        fixture.Model.SaveEverything();
        fixture.Clock.Run();
        Assert.Equal(after, fixture.Revision(note));
    });

    [Fact]
    public void ANoteInTheTrashIsShownAndCannotBeChanged() => With(fixture =>
    {
        var note = fixture.Note("Hello");
        var editor = fixture.Editor();
        editor.Show(note.Id, editable: false, change: null);
        Assert.False(editor.Editor.IsEditable);
        Type(editor, " more");
        Assert.Equal("Hello", Words(editor));
        // Restored: it can be typed in again, without being loaded again.
        var document = editor.Editor.Document;
        editor.Show(note.Id, editable: true, change: null);
        Assert.True(editor.Editor.IsEditable);
        Assert.Same(document, editor.Editor.Document);
        // A note that is not there shows nothing and breaks nothing.
        editor.Show("missing", true, null);
        Assert.Equal(note.Id, editor.ShownNoteId);
    });

    [Fact]
    public void APictureGoesIntoTheImageStoreAndComesBack() => With(fixture =>
    {
        var editor = fixture.Editor();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, new byte[24], 12)));
        using var png = new MemoryStream();
        encoder.Save(png);

        var source = editor.StoreImage(editor.Editor, png.ToArray(), "image/png");
        Assert.NotNull(source);
        Assert.StartsWith(CoreApi.ImageUrlScheme() + ":", source);

        var loaded = new List<ImageSource?>();
        editor.LoadImage(editor.Editor, source, loaded.Add);
        Assert.Equal(3, Assert.IsAssignableFrom<BitmapSource>(Assert.Single(loaded)).PixelWidth);
        // The same picture written into the note itself.
        editor.LoadImage(editor.Editor, "data:image/png;base64," + Convert.ToBase64String(png.ToArray()), loaded.Add);
        Assert.Equal(2, Assert.IsAssignableFrom<BitmapSource>(loaded[1]).PixelHeight);
        // What cannot be a picture is answered with nothing, at once.
        editor.LoadImage(editor.Editor, CoreApi.ImageUrlScheme() + "://local/" + new string('0', 64), loaded.Add);
        editor.LoadImage(editor.Editor, "data:image/png;base64,not a picture", loaded.Add);
        editor.LoadImage(editor.Editor, "file:///C:/Windows/win.ini", loaded.Add);
        editor.LoadImage(editor.Editor, "nonsense", loaded.Add);
        Assert.Equal(6, loaded.Count);
        Assert.All(loaded.Skip(2), Assert.Null);
    });

    /// <summary>Let the thread's own work run until something is so, or ten seconds pass.</summary>
    private static bool WaitFor(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            Views.Pump();
            Thread.Sleep(10);
        }
        return done();
    }

    [Fact]
    public void APictureOnTheWebIsFetchedAndWhatIsNotAPictureIsNot() => With(fixture =>
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, new byte[24], 12)));
        using var png = new MemoryStream();
        encoder.Save(png);
        var picture = png.ToArray();
        var huge = new byte[33 * 1024 * 1024];
        picture.CopyTo(huge, 0);

        using var web = new LocalWeb(path => path switch
        {
            "/map.png" => ("200 OK", "image/png", picture, null),
            "/page.html" => ("200 OK", "text/html", System.Text.Encoding.ASCII.GetBytes("<html>Not a picture.</html>"), null),
            // A real picture, with more after it than a picture may weigh. It is refused by
            // what it says its size is, and is not read to find out.
            "/huge.png" => ("200 OK", "image/png", huge, null),
            // A picture, sent with the news that there is no such thing.
            _ => ("404 Not Found", "image/png", picture, null),
        });
        var editor = fixture.Editor();
        var loaded = new Dictionary<string, ImageSource?>();
        foreach (var name in new[] { "map.png", "missing.png", "page.html", "huge.png" })
        {
            editor.LoadImage(editor.Editor, web.Address(name), image => loaded[name] = image);
        }
        // None of them is answered on the spot: the page is not kept waiting for the web.
        Assert.Empty(loaded);

        Assert.True(WaitFor(() => loaded.Count == 4), $"only {loaded.Count} of 4 were answered");
        Assert.Equal(3, Assert.IsAssignableFrom<BitmapSource>(loaded["map.png"]).PixelWidth);
        Assert.Null(loaded["missing.png"]);
        Assert.Null(loaded["page.html"]);
        Assert.Null(loaded["huge.png"]);

        // Asked for again, the picture is there at once, and the web is not asked twice.
        var again = new List<ImageSource?>();
        editor.LoadImage(editor.Editor, web.Address("map.png"), again.Add);
        Assert.Same(loaded["map.png"], Assert.Single(again));
        lock (web.Asked) Assert.Single(web.Asked, path => path == "/map.png");
    });

    [Fact]
    public void APictureFromAPlaceThatDoesNotAnswerIsNoPicture() => With(fixture =>
    {
        // A port nothing listens on: taken, and given back.
        var taken = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        taken.Start();
        var port = ((System.Net.IPEndPoint)taken.LocalEndpoint).Port;
        taken.Stop();

        var editor = fixture.Editor();
        var loaded = new List<ImageSource?>();
        editor.LoadImage(editor.Editor, $"http://127.0.0.1:{port}/map.png", loaded.Add);
        Assert.True(WaitFor(() => loaded.Count == 1));
        Assert.Null(loaded[0]);
    });

    [Fact]
    public void ALinkIsOpenedInTheAppOrOutsideOrNotAtAll() => With(fixture =>
    {
        var note = fixture.Note("Hello");
        var editor = fixture.Editor();
        editor.Show(note.Id, true, null);

        editor.OpenLink(editor.Editor, "https://example.com/x");
        editor.OpenLink(editor.Editor, "mailto:jdoe@corp.example");
        Assert.Equal(["https://example.com/x", "mailto:jdoe@corp.example"], fixture.OpenedOutside);
        Assert.Null(fixture.Model.PendingLink);

        editor.OpenLink(editor.Editor, $"heracles://note/{note.Id}#block=b1");
        Assert.Equal(new AppLink(note.Id, "b1"), fixture.Model.PendingLink);

        editor.OpenLink(editor.Editor, "javascript:alert(1)");
        editor.OpenLink(editor.Editor, "file:///C:/Windows/notepad.exe");
        editor.OpenLink(editor.Editor, "not a link");
        Assert.Equal(2, fixture.OpenedOutside.Count);
    });

    [Fact]
    public void ALinkToABlockIsCopiedAndABlockIsRevealedOnce() => With(fixture =>
    {
        var note = fixture.Note("Hello");
        var editor = fixture.Editor();
        // Nothing is showing yet: there is no note to link into, and nothing to reveal.
        editor.CopyLinkToBlock(editor.Editor, "b1");
        editor.Reveal("b1", () => { });
        Assert.Empty(fixture.Copied);
        Assert.Empty(fixture.Clock.Waiting);

        editor.Show(note.Id, true, null);
        var block = editor.Editor.Document.Blocks[0].Id;
        editor.CopyLinkToBlock(editor.Editor, block);
        Assert.Equal([new AppLink(note.Id, block).Url], fixture.Copied);

        var revealed = 0;
        editor.Reveal(block, () => revealed++);
        // After this pass of layout, when the block has its place.
        Assert.Equal(0, revealed);
        fixture.Clock.Run();
        Assert.Equal(1, revealed);
        // Asked again for the same block, nothing: a refresh must not keep scrolling the page.
        editor.Reveal(block, () => revealed++);
        editor.Reveal(null, () => revealed++);
        fixture.Clock.Run();
        Assert.Equal(1, revealed);
    });

    [Fact]
    public void RichTextFromElsewhereIsConvertedByTheCore() => With(fixture =>
    {
        var note = fixture.Note("");
        var editor = fixture.Editor();
        editor.Show(note.Id, true, null);
        var board = new DataObject();
        board.SetData(DataFormats.UnicodeText, "From the web\nBody");
        board.SetData(DataFormats.Html, "<h2 onclick='x()'>From the web</h2><p>Body with a <a href='javascript:x()'>bad</a> and a <a href='https://example.com'>good</a> link</p><script>x()</script>");
        editor.Editor.Select(EditorSelection.Caret(editor.Editor.Document.Blocks[0].Id, 0));
        Assert.True(editor.Editor.Paste(board));

        Assert.Equal("From the web\nBody with a bad and a good link", Words(editor));
        Assert.Equal(BlockKind.Heading, editor.Editor.Document.Blocks[0].Kind);
        // Only what the core allows comes in by the clipboard.
        var links = editor.Editor.Document.Blocks[1].Text.Spans.Where(span => span.Style.Link is not null).Select(span => span.Style.Link!.Href);
        Assert.Equal(["https://example.com"], links);
        fixture.Clock.Run();
        Assert.Contains("From the web", fixture.Stored(note));
    });

    [Fact]
    public void TheNotesWordsAreThereToBeReadAndABlockCanBeWrittenByAutomation() => With(fixture =>
    {
        var note = fixture.Note("Hello");
        var editor = fixture.Editor();
        editor.Show(note.Id, true, null);
        Assert.Equal("Hello", editor.Editor.PlainText);
        var block = editor.Editor.Document.Blocks[0].Id;

        // What a screen reader's "set value" does, and the end-to-end checks with it.
        Assert.True(editor.Editor.ReplaceText(block, "Rewritten"));
        Assert.Equal("Rewritten", editor.Editor.PlainText);
        Assert.Equal($"{block}|9", EditorTestSupport.Shape(editor.Editor.Selection));
        fixture.Clock.Run();
        Assert.Equal("Rewritten", fixture.Stored(note));
        editor.Editor.Undo();
        Assert.Equal("Hello", editor.Editor.PlainText);
        Assert.False(editor.Editor.ReplaceText("missing", "x"));
        editor.Editor.IsEditable = false;
        Assert.False(editor.Editor.ReplaceText(block, "No"));
        Assert.Equal("Hello", editor.Editor.PlainText);
    });
}
