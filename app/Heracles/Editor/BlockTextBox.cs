using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Heracles.Editor;

/// <summary>
/// The text of one block while it is being typed in: a system text box, so
/// typing, selection, spelling and input methods behave as they do
/// everywhere else on Windows. What crosses from one block to the next
/// (Enter, Backspace at the start, the arrow keys at an edge) is offered to
/// the editor first.
///
/// Its document is always one paragraph of runs and line breaks. Places in
/// it are counted the way the model counts: each UTF-16 unit of a run's text
/// is one, a line break is one, and the edges of elements are none.
/// </summary>
internal sealed class BlockTextBox : RichTextBox
{
    /// <summary>
    /// Nothing but the text: no border, no scroll bars, no room around it.
    /// The box is as tall as its lines and static text can stand in for it.
    /// </summary>
    private static readonly ControlTemplate Bare = MakeTemplate();

    private static ControlTemplate MakeTemplate()
    {
        var template = new ControlTemplate(typeof(RichTextBox))
        {
            VisualTree = new FrameworkElementFactory(typeof(Border), "PART_ContentHost"),
        };
        template.Seal();
        return template;
    }

    /// <summary>
    /// What a rich text box would do by itself that the model has no place
    /// for, or that the editor does its own way.
    /// </summary>
    private static readonly RoutedUICommand[] NotTheBoxes =
    [
        EditingCommands.ToggleBold, EditingCommands.ToggleItalic, EditingCommands.ToggleUnderline,
        EditingCommands.ToggleSubscript, EditingCommands.ToggleSuperscript, EditingCommands.IncreaseFontSize,
        EditingCommands.DecreaseFontSize, EditingCommands.AlignLeft, EditingCommands.AlignCenter,
        EditingCommands.AlignRight, EditingCommands.AlignJustify, EditingCommands.ToggleBullets,
        EditingCommands.ToggleNumbering, EditingCommands.IncreaseIndentation, EditingCommands.DecreaseIndentation,
        EditingCommands.EnterParagraphBreak, EditingCommands.EnterLineBreak, EditingCommands.TabForward,
        EditingCommands.TabBackward,
    ];

    private readonly Paragraph paragraph = new()
    {
        Margin = new Thickness(0),
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    public string BlockId { get; set; } = "";

    /// <summary>Code is typed as written: one font, no marks.</summary>
    public bool IsCode { get; private set; }

    /// <summary>True while an input method has text it has not finished. Nothing may be redrawn under it.</summary>
    public bool IsComposing { get; private set; }

    /// <summary>
    /// The editor's answer to a key: true when it took the key, false to
    /// leave it to the text box.
    /// </summary>
    public Func<Key, ModifierKeys, bool>? KeyHandler { get; set; }

    public BlockTextBox()
    {
        Template = Bare;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Background = Brushes.Transparent;
        MinHeight = 0;
        MinWidth = 0;
        FocusVisualStyle = null;
        ContextMenu = null;
        // The editor keeps one undo history for the whole note.
        IsUndoEnabled = false;
        AcceptsReturn = false;
        AcceptsTab = false;
        AllowDrop = false;
        Document.Blocks.Clear();
        Document.Blocks.Add(paragraph);

        // Asked whether it can, the box says no and lets the key travel on:
        // Ctrl+Shift+N is the window's New Section, not a numbered list.
        CommandManager.AddPreviewCanExecuteHandler(this, (_, e) =>
        {
            if (Array.IndexOf(NotTheBoxes, e.Command) < 0) return;
            e.CanExecute = false;
            e.ContinueRouting = true;
            e.Handled = true;
        });
        // Told to anyway, it does nothing.
        CommandManager.AddPreviewExecutedHandler(this, (_, e) =>
        {
            if (Array.IndexOf(NotTheBoxes, e.Command) >= 0) e.Handled = true;
        });
        // Text is not dragged out of a box: where it would land, and what
        // the box would then hold, is not something the editor could follow.
        DataObject.AddCopyingHandler(this, (_, e) =>
        {
            if (e.IsDragDrop) e.CancelCommand();
        });
        TextCompositionManager.AddPreviewTextInputStartHandler(this, (_, e) => IsComposing = e.TextComposition is FrameworkTextComposition);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(this, (_, e) => IsComposing = e.TextComposition is FrameworkTextComposition);
        TextCompositionManager.AddPreviewTextInputHandler(this, (_, _) => IsComposing = false);
    }

    // What it shows

    /// <summary>
    /// Show a block's text, as the model has it. The selection is not kept:
    /// whoever calls puts it back.
    /// </summary>
    public void Show(Block block)
    {
        IsCode = block.Kind == BlockKind.Code;
        var font = EditorTheme.FontFor(block);
        BeginChange();
        try
        {
            if (Document.Blocks.Count != 1 || Document.Blocks.FirstBlock != paragraph)
            {
                Document.Blocks.Clear();
                Document.Blocks.Add(paragraph);
            }
            FontFamily = Document.FontFamily = font.Family;
            FontSize = Document.FontSize = font.Size;
            FontWeight = Document.FontWeight = font.Weight;
            paragraph.LineHeight = EditorTheme.LineHeight(block);
            paragraph.Inlines.Clear();
            paragraph.Inlines.AddRange(EditorTheme.Inlines(block, emoji: false));
        }
        finally
        {
            EndChange();
        }
    }

    private IEnumerable<Inline> Leaves => EditorTheme.Leaves(paragraph.Inlines);

    private static string TextOf(Inline inline) => inline switch
    {
        Run run => run.Text,
        LineBreak => "\n",
        _ => "",
    };

    /// <summary>What the box holds, with its marks, as the model would hold it.</summary>
    public RichText Read() => EditorTheme.RichTextFrom(paragraph, IsCode);

    /// <summary>What the box holds, as plain text. A line break is <c>\n</c>.</summary>
    public string Text => string.Concat(Leaves.Select(TextOf));

    public int Length => Leaves.Sum(inline => TextOf(inline).Length);

    // Places

    /// <summary>
    /// The place in the box for an offset in the block's text. Where two
    /// runs meet it is the end of the earlier one, and an offset between the
    /// two halves of a character is that character's start.
    /// </summary>
    public TextPointer PointerAt(int offset)
    {
        var remaining = Math.Max(offset, 0);
        var last = paragraph.ContentStart;
        foreach (var inline in Leaves)
        {
            switch (inline)
            {
                case Run run:
                    var text = run.Text;
                    if (remaining <= text.Length)
                    {
                        if (remaining > 0 && remaining < text.Length && char.IsLowSurrogate(text[remaining])
                            && char.IsHighSurrogate(text[remaining - 1]))
                        {
                            remaining--;
                        }
                        return run.ContentStart.GetPositionAtOffset(remaining, LogicalDirection.Forward) ?? run.ContentEnd;
                    }
                    remaining -= text.Length;
                    last = run.ContentEnd;
                    break;
                case LineBreak lineBreak:
                    if (remaining == 0) return lineBreak.ElementStart;
                    remaining--;
                    last = lineBreak.ElementEnd;
                    break;
            }
        }
        return last;
    }

    /// <summary>The offset in the block's text of a place in the box.</summary>
    public int OffsetOf(TextPointer pointer)
    {
        var offset = 0;
        foreach (var inline in Leaves)
        {
            switch (inline)
            {
                case Run run:
                    if (pointer.CompareTo(run.ContentStart) <= 0) return offset;
                    if (pointer.CompareTo(run.ContentEnd) <= 0) return offset + run.ContentStart.GetOffsetToPosition(pointer);
                    offset += run.Text.Length;
                    break;
                case LineBreak lineBreak:
                    if (pointer.CompareTo(lineBreak.ElementStart) <= 0) return offset;
                    offset++;
                    break;
            }
        }
        return offset;
    }

    /// <summary>What is selected, in the model's offsets. A caret is an empty range.</summary>
    public (int Start, int End) SelectedRange => (OffsetOf(Selection.Start), OffsetOf(Selection.End));

    public void Select(int start, int end)
    {
        var from = PointerAt(start);
        Selection.Select(from, end == start ? from : PointerAt(end));
    }

    // Lines

    private static int LinesBeyond(TextPointer pointer, int direction)
    {
        // Null, and no lines counted, before the box has been laid out.
        pointer.GetLineStartPosition(direction, out var moved);
        return moved;
    }

    public bool CaretIsOnFirstLine => LinesBeyond(Selection.Start, -1) == 0;

    public bool CaretIsOnLastLine => LinesBeyond(Selection.End, 1) == 0;

    /// <summary>
    /// How far from the box's left edge the caret is, for keeping its column
    /// when it moves to another block.
    /// </summary>
    public double CaretX => Selection.Start.GetCharacterRect(LogicalDirection.Forward).X;

    /// <summary>The place in the text nearest to <paramref name="x"/> on the first or the last line.</summary>
    public int OffsetNearest(double x, bool onFirstLine)
    {
        var edge = (onFirstLine ? PointerAt(0) : PointerAt(int.MaxValue)).GetCharacterRect(LogicalDirection.Forward);
        if (edge.IsEmpty) return onFirstLine ? 0 : Length;
        return OffsetAt(new Point(x, edge.Top + edge.Height / 2));
    }

    /// <summary>The place in the text nearest to a point in the box.</summary>
    public int OffsetAt(Point point) =>
        GetPositionFromPoint(point, snapToText: true) is { } pointer ? OffsetOf(pointer) : 0;

    // Keys

    /// <summary>Offer a key to the editor, as a key press does. True when the editor took it.</summary>
    public bool OfferKey(Key key, ModifierKeys modifiers) => KeyHandler?.Invoke(key, modifiers) == true;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // A key pressed with Alt arrives as a system key. A key an input
        // method is using is the input method's.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // The right-hand Alt of many keyboards types characters (a brace, an
        // at sign) and reports itself as Ctrl+Alt. Those are not commands.
        var typesACharacter = Keyboard.IsKeyDown(Key.RightAlt) && (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (key != Key.ImeProcessed && !IsComposing && !typesACharacter && OfferKey(key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    // Spelling is checked only where the keyboard is: it is most of what a
    // text box costs.

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SpellCheck.IsEnabled = !IsCode && !IsReadOnly;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        SpellCheck.IsEnabled = false;
    }
}
