using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Heracles.Editor;

/// <summary>What an editor needs from the app around it.</summary>
internal interface IBlockEditorHost
{
    /// <summary>The document changed. Time to save it.</summary>
    void EditorDidChange(BlockEditorController editor);

    void EditorDidFocus(BlockEditorController editor);

    void OpenLink(BlockEditorController editor, string href);

    /// <summary>Keep an image and give back the address to store in the document.</summary>
    string? StoreImage(BlockEditorController editor, byte[] data, string mime);

    void LoadImage(BlockEditorController editor, string source, Action<ImageSource?> completion);

    /// <summary>Convert rich text from elsewhere into a document, as JSON.</summary>
    string? DocumentFromHtml(BlockEditorController editor, string html);

    void CopyLinkToBlock(BlockEditorController editor, string id);
}

/// <summary>
/// The scroll view an editor's page is in. It tells the page how much of it
/// can be seen, which a page being measured cannot otherwise know.
/// </summary>
internal sealed class EditorScrollViewer : ScrollViewer
{
    /// <summary>The height there is to show the page in, as last offered.</summary>
    public double VisibleHeight { get; private set; }

    protected override Size MeasureOverride(Size constraint)
    {
        var height = double.IsInfinity(constraint.Height) ? 0 : constraint.Height;
        if (Math.Abs(height - VisibleHeight) > 0.5)
        {
            VisibleHeight = height;
            // The page is never shorter than what can be seen of it.
            (Content as UIElement)?.InvalidateMeasure();
        }
        return base.MeasureOverride(constraint);
    }
}

/// <summary>
/// One open note's editor: the document, the selection, and the views that
/// show them.
///
/// The document is the truth. A text box holds the text of one block while
/// the user types in it, and every keystroke is copied into the document.
/// Everything that changes the shape of the note (a new block, a merge, a
/// move) is done to the document first, and the views are then made to match.
/// </summary>
internal sealed partial class BlockEditorController
{
    private readonly EditorScrollViewer scrollView;
    private bool isEditable = true;
    private int callbacksOff;

    /// <summary>The two ends of a selection of whole blocks.</summary>
    private string? blockAnchor;
    private string? blockHead;

    public EditorDocument Document { get; private set; } = new([]);

    public EditorSelection Selection { get; private set; } = EditorSelection.None;

    public IBlockEditorHost? Host { get; set; }

    /// <summary>The view to put in a window.</summary>
    public ScrollViewer ScrollView => scrollView;

    public BlockEditorView Page { get; }

    internal BlockIds Ids { get; }

    /// <summary>
    /// The text box the caret is in. The editor keeps this itself: with no
    /// window there is no keyboard focus to ask about, and in a window the
    /// text box is only there while this says so.
    /// </summary>
    public BlockTextBox? FocusedText { get; private set; }

    internal double VisibleHeight => scrollView.VisibleHeight;

    public BlockEditorController(BlockIds? ids = null)
    {
        Ids = ids ?? BlockIds.Random();
        Page = new BlockEditorView(this);
        scrollView = new EditorScrollViewer
        {
            Content = Page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Background = Brushes.Transparent,
        };
    }

    public bool IsEditable
    {
        get => isEditable;
        set
        {
            if (isEditable == value) return;
            isEditable = value;
            CloseSlashMenu();
            Page.HideHandle();
            Refresh();
        }
    }

    // The document

    /// <summary>Show a stored document. False when the text is not one.</summary>
    public bool Load(string json)
    {
        if (EditorDocument.FromJson(json, Ids) is not { } loaded) return false;
        var previous = Selection;
        var hadFocus = FocusedText is { IsKeyboardFocused: true };
        Document = loaded;
        DocumentReplaced();
        CloseSlashMenu();
        Selection = EditorSelection.None;
        Page.SelectedBlocks = [];
        blockAnchor = null;
        blockHead = null;
        // The same note, saved elsewhere: keep the caret where it was if
        // its block is still there. Otherwise nothing has the caret.
        var kept = hadFocus && previous is EditorSelection.InText text && Document.Block(text.Block)?.HasText == true
            ? previous
            : null;
        if (kept is null) GiveUpText();
        Refresh();
        if (kept is not null) Select(kept);
        return true;
    }

    /// <summary>The document to store.</summary>
    public string Json => Document.Json;

    private void Refresh()
    {
        Page.Reconcile(Document);
        Page.UpdateLayout();
    }

    internal void BlockHeightChanged() => Page.InvalidateMeasure();

    /// <summary>
    /// Run <paramref name="work"/> without the text box's reports of it
    /// being taken for typing. Used whenever the editor itself writes to a
    /// text box.
    /// </summary>
    internal void WithoutTextCallbacks(Action work)
    {
        callbacksOff++;
        try { work(); }
        finally { callbacksOff--; }
    }

    // Text boxes come and go with the caret

    internal void Attach(BlockTextBox box)
    {
        box.TextChanged += BoxTextChanged;
        box.SelectionChanged += BoxSelectionChanged;
        box.GotKeyboardFocus += BoxGotKeyboardFocus;
        box.KeyHandler = (key, modifiers) => HandleKey(box, key, modifiers);
    }

    internal void Detach(BlockTextBox box)
    {
        box.TextChanged -= BoxTextChanged;
        box.SelectionChanged -= BoxSelectionChanged;
        box.GotKeyboardFocus -= BoxGotKeyboardFocus;
        box.KeyHandler = null;
        if (ReferenceEquals(FocusedText, box)) FocusedText = null;
    }

    private void BoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is BlockTextBox box) TextViewDidFocus(box);
    }

    /// <summary>Nothing has the caret: every block is static text.</summary>
    private void GiveUpText()
    {
        FocusedText = null;
        Page.GiveUpTextBoxes();
    }

    // Selection

    /// <summary>Put the selection somewhere, and the keyboard focus with it.</summary>
    public void Select(EditorSelection selection)
    {
        switch (selection)
        {
            case EditorSelection.OfBlocks(var selected):
            {
                var present = selected.Where(Document.Contains).ToList();
                if (present.Count == 0) return;
                CloseSlashMenu();
                GiveUpText();
                Selection = EditorSelection.Blocks(present);
                Page.SelectedBlocks = present;
                var inside = StopsIn(present);
                if (blockAnchor is null || !inside.Contains(blockAnchor)) blockAnchor = inside.FirstOrDefault();
                if (blockHead is null || !inside.Contains(blockHead)) blockHead = inside.LastOrDefault();
                Page.Focus();
                if (Page.BlockViewFor(present[0]) is { } first) first.BringIntoView(new Rect(0, -24, first.RenderSize.Width, first.RenderSize.Height + 48));
                break;
            }
            case EditorSelection.InText(var id, var start, var end):
            {
                if (Page.EnsureTextBox(id) is not { } box) return;
                Page.SelectedBlocks = [];
                blockAnchor = null;
                blockHead = null;
                // The text box has just taken the block's place: it needs its lines.
                Page.UpdateLayout();
                var length = box.Length;
                var lower = Math.Clamp(start, 0, length);
                var upper = Math.Clamp(end, lower, length);
                if (!box.IsKeyboardFocused) box.Focus();
                // With no window, or a window that is not the active one,
                // the keyboard does not move. The caret still does.
                if (!ReferenceEquals(FocusedText, box)) TextViewDidFocus(box);
                WithoutTextCallbacks(() => box.Select(lower, upper));
                Selection = EditorSelection.Text(id, lower, upper);
                UpdateTypingStyle(box);
                var caret = box.PointerAt(upper).GetCharacterRect(LogicalDirection.Forward);
                if (!caret.IsEmpty) box.BringIntoView(caret);
                break;
            }
            default:
                Selection = EditorSelection.None;
                Page.SelectedBlocks = [];
                CloseSlashMenu();
                GiveUpText();
                break;
        }
        SelectionMoved();
    }

    /// <summary>The caret's stops inside some blocks, in order.</summary>
    private List<string> StopsIn(IReadOnlyList<string> blocks)
    {
        var inside = EditorDocument.Within(blocks.Select(Document.Block).OfType<Block>()).Select(block => block.Id).ToHashSet();
        return Document.Stops.Select(stop => stop.Id).Where(inside.Contains).ToList();
    }

    internal void TextViewDidFocus(BlockTextBox box)
    {
        if (!ReferenceEquals(FocusedText, box))
        {
            CloseSlashMenu();
            FocusedText = box;
            // Only one block's text is ever being typed in: the one that
            // had the text box before gives it up.
            Page.GiveUpTextBoxes(except: box.BlockId);
        }
        Page.SelectedBlocks = [];
        var (start, end) = box.SelectedRange;
        Selection = EditorSelection.Text(box.BlockId, start, end);
        Host?.EditorDidFocus(this);
    }

    public void FocusStart()
    {
        if (Document.Stops.FirstOrDefault() is not { } first) return;
        Select(first.HasText ? EditorSelection.Caret(first.Id, 0) : EditorSelection.Blocks([first.Id]));
    }

    public void ScrollToTop() => scrollView.ScrollToTop();

    // Pictures

    internal void LoadImage(string source, Action<ImageSource?> completion)
    {
        if (Host is null)
        {
            completion(null);
            return;
        }
        Host.LoadImage(this, source, completion);
    }
}
