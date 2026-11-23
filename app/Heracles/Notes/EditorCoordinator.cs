using System.Diagnostics;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Heracles.Editor;
using Heracles.Model;

namespace Heracles.Notes;

/// <summary>
/// The app's end of one note editor. It gives the editor the note to show,
/// saves what the user changes, and answers what the editor asks of the app:
/// keeping images, converting pasted text, opening links.
///
/// The document lives in the core. The editor holds a working copy while a
/// note is open, and the copy is replaced whenever someone else saves that
/// note: another window, or another pane of this one.
/// </summary>
internal sealed class EditorCoordinator : IBlockEditorHost, IDisposable
{
    /// <summary>How long typing must pause before the note is saved.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Pictures already read, by their address. A note's pictures are asked for again whenever it is shown.</summary>
    private static readonly Dictionary<string, ImageSource> Images = [];

    private static readonly HttpClient Web = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly AppModel model;
    private readonly Action<Action, TimeSpan> later;
    private readonly IDisposable registration;
    private (string NoteId, bool Editable, long Revision)? shown;
    private string? revealed;
    private bool isDirty;

    /// <summary>Goes up with every change, so that only the last save put off is the one that runs.</summary>
    private int saveNumber;

    public BlockEditorController Editor { get; } = new();

    /// <summary>
    /// Passed with every save, and compared against the change feed, so this
    /// editor never reloads because of its own typing.
    /// </summary>
    public string EditorId { get; } = Guid.NewGuid().ToString();

    /// <summary>Called when the editor takes the keyboard focus.</summary>
    public Action? OnFocus { get; set; }

    /// <summary>How a link to the outside world is opened. The system's way, unless a test says otherwise.</summary>
    public Action<string> OpenExternally { get; set; } = href =>
    {
        try { Process.Start(new ProcessStartInfo(href) { UseShellExecute = true })?.Dispose(); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("link: could not be opened", error);
        }
    };

    /// <summary>How text reaches the clipboard. The system's, unless a test says otherwise.</summary>
    public Action<string> CopyText { get; set; } = text =>
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException error) { Log.Error("clipboard: could not be written", error); }
    };

    /// <param name="later">
    /// How something is put off for a while. Left out, a timer on this
    /// thread's dispatcher.
    /// </param>
    public EditorCoordinator(AppModel model, Action<Action, TimeSpan>? later = null)
    {
        this.model = model;
        this.later = later ?? OnATimer;
        Editor.Host = this;
        registration = model.RegisterSaver(Save);
    }

    private static void OnATimer(Action work, TimeSpan after)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = after };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            work();
        };
        timer.Start();
    }

    /// <summary>Save what is unsaved and stop being asked to.</summary>
    public void Dispose()
    {
        Save();
        registration.Dispose();
    }

    /// <summary>
    /// Show a note, or catch up with a save someone else made to the one
    /// already showing.
    /// </summary>
    public void Show(string noteId, bool editable, DocumentChange? change)
    {
        if (shown is { } current && current.NoteId == noteId)
        {
            if (current.Editable != editable)
            {
                Editor.IsEditable = editable;
                shown = current with { Editable = editable };
            }
            var isNewer = (change?.Revision ?? 0) > current.Revision;
            if (!isNewer || change?.Origin == EditorId) return;
            // What is being typed here is not dropped for a copy saved
            // elsewhere. It is saved in a moment, and the other editor
            // then follows this one.
            if (isDirty) return;
        }
        else
        {
            if (model.Document(noteId) is null) return;
            Save();
            revealed = null;
        }
        if (model.Document(noteId) is not { } document) return;
        var isAnotherNote = shown?.NoteId != noteId;
        Editor.IsEditable = editable;
        Editor.Load(document.Content);
        shown = (noteId, editable, document.Revision);
        if (isAnotherNote) Editor.ScrollToTop();
    }

    /// <summary>
    /// Bring a block into view. Once per block: asking again for the same
    /// one does nothing, so a refresh cannot keep scrolling the page.
    /// </summary>
    public void Reveal(string? blockId, Action done)
    {
        if (blockId is null || blockId == revealed || shown is null) return;
        revealed = blockId;
        // After this pass of layout, when the block has its place.
        later(
            () =>
            {
                Editor.Reveal(blockId);
                done();
            },
            TimeSpan.Zero);
    }

    /// <summary>Save now what the editor is holding, if anything.</summary>
    public void Save()
    {
        saveNumber++;
        if (!isDirty || shown is not { } current) return;
        isDirty = false;
        if (!model.SaveDocument(current.NoteId, Editor.Json, EditorId))
        {
            // It is still unsaved, and the next change or the next ask tries again.
            isDirty = true;
            return;
        }
        if (model.DocumentChangeFor(current.NoteId) is { } saved && shown is { } now && now.NoteId == current.NoteId)
        {
            shown = now with { Revision = saved.Revision };
        }
    }

    public string? ShownNoteId => shown?.NoteId;

    // What the editor asks of the app

    public void EditorDidChange(BlockEditorController editor)
    {
        isDirty = true;
        var number = ++saveNumber;
        later(
            () =>
            {
                if (number == saveNumber) Save();
            },
            SaveDelay);
    }

    public void EditorDidFocus(BlockEditorController editor) => OnFocus?.Invoke();

    public void OpenLink(BlockEditorController editor, string href)
    {
        switch (NavigationPolicy.Decide(href))
        {
            case NavigationDecision.OpenExternally:
                OpenExternally(href);
                break;
            case NavigationDecision.OpenInApp:
                model.PendingLink = AppLink.Parse(href);
                break;
        }
    }

    public string? StoreImage(BlockEditorController editor, byte[] data, string mime) =>
        model.Attempt(() => model.Store.StoreImage(data, mime))?.Url;

    public string? DocumentFromHtml(BlockEditorController editor, string html)
    {
        // The core converts by the rules it uses for imports, so nothing but
        // the editor's own blocks and safe links can come in by the clipboard.
        try { return CoreApi.HtmlToDocument(html); }
        catch (UniffiException) { return null; }
    }

    public void CopyLinkToBlock(BlockEditorController editor, string id)
    {
        if (shown is { } current) CopyText(new AppLink(current.NoteId, id).Url);
    }

    public void LoadImage(BlockEditorController editor, string source, Action<ImageSource?> completion)
    {
        if (Images.TryGetValue(source, out var cached))
        {
            completion(cached);
            return;
        }

        void Finish(ImageSource? image)
        {
            if (image is not null) Images[source] = image;
            completion(image);
        }

        var colon = source.IndexOf(':');
        var scheme = colon <= 0 ? "" : source[..colon].ToLowerInvariant();
        if (scheme == CoreApi.ImageUrlScheme())
        {
            // The image store keeps each picture in a file of its own.
            string? path = null;
            try { path = model.Store.ImagePath(source); }
            catch (UniffiException) { }
            Finish(path is null ? null : Decode(() => File.ReadAllBytes(path)));
        }
        else if (scheme == "data")
        {
            var comma = source.IndexOf(',');
            if (comma < 0 || !source[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                completion(null);
                return;
            }
            Finish(Decode(() => Convert.FromBase64String(source[(comma + 1)..])));
        }
        else if (scheme is "http" or "https" && Uri.TryCreate(source, UriKind.Absolute, out var url))
        {
            // A picture a page linked to on the web, as the note was imported.
            _ = FromTheWeb(url, Finish);
        }
        else
        {
            completion(null);
        }
    }

    /// <summary>A picture from its bytes, or null when they are not one or cannot be had.</summary>
    private static ImageSource? Decode(Func<byte[]> bytes)
    {
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes());
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is NotSupportedException or IOException or FormatException or ArgumentException
            or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task FromTheWeb(Uri url, Action<ImageSource?> finish)
    {
        byte[]? data = null;
        try
        {
            using var response = await Web.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            const long Limit = 32 * 1024 * 1024;
            if (response.IsSuccessStatusCode && (response.Content.Headers.ContentLength ?? 0) <= Limit)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length <= Limit) data = bytes;
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            // No picture: the block says what was meant to be there.
        }
        // Back on the thread that asked, which is the views' own.
        finish(data is null ? null : Decode(() => data));
    }
}
