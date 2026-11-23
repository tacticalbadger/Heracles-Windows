using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Heracles.Editor;
using Block = Heracles.Editor.Block;

namespace Heracles.Tests.Editor;

/// <summary>Stands in for the app: records what the editor asks of it.</summary>
internal sealed class TestHost : IBlockEditorHost
{
    public int Changes { get; private set; }

    public int Focuses { get; private set; }

    public List<string> Opened { get; } = [];

    public List<(byte[] Data, string Mime)> Stored { get; } = [];

    public List<string> CopiedLinks { get; } = [];

    public List<string> Html { get; } = [];

    public List<string> Loaded { get; } = [];

    /// <summary>What <see cref="DocumentFromHtml"/> answers.</summary>
    public string? Converted { get; set; }

    public void EditorDidChange(BlockEditorController editor) => Changes++;

    public void EditorDidFocus(BlockEditorController editor) => Focuses++;

    public void OpenLink(BlockEditorController editor, string href) => Opened.Add(href);

    public string? StoreImage(BlockEditorController editor, byte[] data, string mime)
    {
        Stored.Add((data, mime));
        return $"heracles-image://local/image{Stored.Count}";
    }

    public void LoadImage(BlockEditorController editor, string source, Action<ImageSource?> completion)
    {
        Loaded.Add(source);
        completion(null);
    }

    public string? DocumentFromHtml(BlockEditorController editor, string html)
    {
        Html.Add(html);
        return Converted;
    }

    public void CopyLinkToBlock(BlockEditorController editor, string id) => CopiedLinks.Add(id);
}

/// <summary>
/// An editor with its real views, laid out as a window 800 by 600 would lay
/// them out, in no window at all. Made and used inside <see cref="Sta.Run"/>.
/// </summary>
internal sealed class EditorHarness
{
    public BlockEditorController Editor { get; } = new(BlockIds.Sequential());

    public TestHost Host { get; } = new();

    public EditorHarness(params Block[] blocks)
    {
        Editor.Host = Host;
        Editor.Load(new EditorDocument(blocks).Json);
        Layout();
    }

    /// <summary>Lay the editor out again, at another width if one is given.</summary>
    public void Layout(double width = 800) => Views.Layout(Editor.ScrollView, width, 600);

    public BlockEditorView Page => Editor.Page;

    public string Outline => EditorTestSupport.Outline(Editor.Document);

    public string Selection => EditorTestSupport.Shape(Editor.Selection);

    /// <summary>The text a block is showing, whether in its text box or as static text.</summary>
    public string Text(string id) =>
        Page.BlockViewFor(id) is TextHoldingView view ? view.ShownText : throw new InvalidOperationException($"no text for {id}");

    /// <summary>The text box the keyboard is in.</summary>
    public BlockTextBox? Focused => Editor.FocusedText;

    public void Caret(string id, int offset) => Editor.Select(EditorSelection.Caret(id, offset));

    /// <summary>Type, one key at a time, the way the keyboard does.</summary>
    public void Type(string text)
    {
        var characters = StringInfo.GetTextElementEnumerator(text);
        while (characters.MoveNext())
        {
            // The text box can change under the typing: a split, a block turned into another.
            if (Focused is not { } box) return;
            Views.Type(box, characters.GetTextElement());
        }
    }

    /// <summary>A block's frame on the page.</summary>
    public Rect Frame(string id) => Page.FrameOf(id) ?? throw new InvalidOperationException($"no frame for {id}");

    public T View<T>(string id) where T : BlockView =>
        Page.BlockViewFor(id) as T ?? throw new InvalidOperationException($"{id} is not shown by a {typeof(T).Name}");
}
