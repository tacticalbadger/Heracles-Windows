using System.Globalization;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;

namespace Heracles.Editor;

/// <summary>What the Format menu can ask of an editor.</summary>
internal abstract record EditorFormat
{
    public sealed record OfMark(TextMark Mark) : EditorFormat;

    public sealed record OfLink : EditorFormat;

    public sealed record OfBlock(BlockTarget Target) : EditorFormat;
}

// Editing: undo, typing, keys, marks, links, the slash menu, and whole
// blocks selected. With BlockEditorController.cs this is the Mac's
// BlockEditorController.swift, in the same order.
internal sealed partial class BlockEditorController
{
    private sealed record Snapshot(EditorDocument Document, EditorSelection Selection);

    private readonly List<Snapshot> undoStack = [];
    private readonly List<Snapshot> redoStack = [];

    /// <summary>The run of typing in progress, which is one step to undo.</summary>
    private (string Block, long At)? typing;

    /// <summary>
    /// The marks what is typed next will carry. A text box here has no
    /// typing attributes to hold them, so the editor does: they follow the
    /// caret, and a mark switched on with nothing selected changes them
    /// until the caret moves.
    /// </summary>
    private TextStyle typingStyle = TextStyle.Plain;

    /// <summary>
    /// Marks chosen for what is typed next, and the place they were chosen
    /// at. They hold while the caret stays there: a text box reports its
    /// caret more than once for one move, sometimes after the text changed
    /// and sometimes before, and a report that moves nothing must not undo
    /// the choice.
    /// </summary>
    private (string Block, int Offset, TextStyle Style)? chosenStyle;

    /// <summary>The column the caret keeps while it moves up or down through blocks.</summary>
    private double? goalX;
    private bool isMovingVertically;

    private (string Block, int Start)? slash;
    private Popup? popup;

    private void DocumentReplaced()
    {
        undoStack.Clear();
        redoStack.Clear();
        typing = null;
    }

    private void SelectionMoved()
    {
        if (!isMovingVertically) goalX = null;
    }

    internal void PageDidLayout()
    {
        if (slash is { } open && Page.TextBoxFor(open.Block) is { } box && Page.SlashMenu.Visibility == Visibility.Visible)
        {
            Page.PlaceSlashMenu(box, open.Start);
        }
    }

    // Editing

    /// <summary>
    /// Make an edit to the document. The views follow, the selection goes
    /// where the edit says, and the whole thing is one step to undo. True
    /// when the edit did anything.
    /// </summary>
    internal bool Edit(Func<EditorDocument, BlockIds, EditorSelection?> change, bool undoable = true)
    {
        if (!IsEditable) return false;
        // The edit changes the document where it is. What it was is kept
        // aside first, to compare with and to go back to.
        var before = new Snapshot(Document.Clone(), Selection);
        var chosen = change(Document, Ids);
        var changed = !Document.SameAs(before.Document);
        if (!changed && chosen is null) return false;
        if (changed)
        {
            if (undoable) Push(before);
            Refresh();
        }
        typing = null;
        if (chosen is not null) Select(chosen);
        if (changed) Host?.EditorDidChange(this);
        return true;
    }

    private void Push(Snapshot snapshot)
    {
        undoStack.Add(snapshot);
        if (undoStack.Count > 200) undoStack.RemoveAt(0);
        redoStack.Clear();
    }

    /// <summary>
    /// Start a step to undo that later changes will add to, for a drag
    /// that changes the document many times.
    /// </summary>
    internal void BeginUndoGroup()
    {
        Push(new Snapshot(Document.Clone(), Selection));
        typing = null;
    }

    public bool CanUndo => undoStack.Count > 0;

    public bool CanRedo => redoStack.Count > 0;

    public void Undo()
    {
        if (!IsEditable || undoStack.Count == 0) return;
        var snapshot = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        redoStack.Add(new Snapshot(Document, Selection));
        Restore(snapshot);
    }

    public void Redo()
    {
        if (!IsEditable || redoStack.Count == 0) return;
        var snapshot = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        undoStack.Add(new Snapshot(Document, Selection));
        Restore(snapshot);
    }

    private void Restore(Snapshot snapshot)
    {
        typing = null;
        CloseSlashMenu();
        Document = snapshot.Document;
        Refresh();
        Select(snapshot.Selection);
        Host?.EditorDidChange(this);
    }

    // Selecting whole blocks

    /// <summary>
    /// Select whole blocks. With <paramref name="extending"/>, from where
    /// the selection already is to the first of them.
    /// </summary>
    internal void SelectBlocks(IReadOnlyList<string> blocks, bool extending)
    {
        if (blocks.Count == 0) return;
        string? anchor = null;
        if (extending)
        {
            anchor = Selection switch
            {
                EditorSelection.InText text => text.Block,
                EditorSelection.OfBlocks => blockAnchor,
                _ => null,
            };
        }
        if (anchor is not null && StopsIn([blocks[0]]).LastOrDefault() is { } head)
        {
            var range = Document.BlocksBetween(anchor, head);
            blockAnchor = anchor;
            blockHead = head;
            Select(EditorSelection.Blocks(range.Count == 0 ? blocks : range));
        }
        else
        {
            blockAnchor = null;
            blockHead = null;
            Select(EditorSelection.Blocks(blocks));
        }
    }

    /// <summary>
    /// A shift-click in a block's text. When the selection is in another
    /// block, whole blocks from there to here are selected. False when it
    /// is in this block, where the text box extends its own selection.
    /// </summary>
    internal bool ExtendSelection(string toBlock)
    {
        var anchor = Selection switch
        {
            EditorSelection.InText text => text.Block == toBlock ? null : text.Block,
            EditorSelection.OfBlocks => blockAnchor,
            _ => null,
        };
        if (anchor is null) return false;
        ExtendBlockSelection(anchor, toBlock);
        return true;
    }

    private void ExtendBlockSelection(string anchor, string head)
    {
        var range = Document.BlocksBetween(anchor, head);
        if (range.Count == 0) return;
        blockAnchor = anchor;
        blockHead = head;
        Select(EditorSelection.Blocks(range));
    }

    internal void SelectAllBlocks()
    {
        blockAnchor = null;
        blockHead = null;
        Select(EditorSelection.Blocks(Document.Blocks.Select(block => block.Id)));
    }

    internal void DeleteSelectedBlocks()
    {
        if (Selection is not EditorSelection.OfBlocks(var selected)) return;
        Edit((document, ids) => document.Remove(selected, ids));
    }

    /// <summary>
    /// Put the caret at the end of the note, in a new line if the note
    /// does not end in an empty one.
    /// </summary>
    public void FocusEnd()
    {
        if (Document.Blocks.LastOrDefault() is { Kind: BlockKind.Paragraph } last && last.Text.IsEmpty)
        {
            Select(EditorSelection.Caret(last.Id, 0));
            return;
        }
        Edit((document, ids) =>
        {
            var paragraph = new Block(ids.Make(), BlockKind.Paragraph);
            document.Blocks.Add(paragraph);
            return EditorSelection.Caret(paragraph.Id, 0);
        });
        if (!IsEditable && Document.Stops.LastOrDefault(stop => stop.HasText) is { } text)
        {
            Select(EditorSelection.Caret(text.Id, text.Text.Length));
        }
    }

    private static EditorSelection AtStartOf(Block stop) =>
        stop.HasText ? EditorSelection.Caret(stop.Id, 0) : EditorSelection.Blocks([stop.Id]);

    private static EditorSelection AtEndOf(Block stop) =>
        stop.HasText ? EditorSelection.Caret(stop.Id, stop.Text.Length) : EditorSelection.Blocks([stop.Id]);

    // Typing

    private static TextStyle StyleAt(RichText text, int offset)
    {
        var spans = text.Subtext(offset, offset + 1).Spans;
        return spans.Count > 0 && spans[0].Atom is null ? spans[0].Style : TextStyle.Plain;
    }

    /// <summary>
    /// What is typed next takes the marks of the text before the caret, or
    /// of the text it takes the place of.
    /// </summary>
    private void UpdateTypingStyle(BlockTextBox box)
    {
        if (box.IsComposing || Document.Block(box.BlockId) is not { } block) return;
        var (start, end) = box.SelectedRange;
        if (chosenStyle is { } chosen && chosen.Block == block.Id && start == end && start == chosen.Offset)
        {
            typingStyle = chosen.Style;
            return;
        }
        chosenStyle = null;
        typingStyle = block.Kind == BlockKind.Code ? TextStyle.Plain
            : start == end ? block.Text.StyleBefore(start)
            : StyleAt(block.Text, start);
    }

    /// <summary>Choose the marks for what is typed next at the caret, until the caret moves.</summary>
    private void ChooseTypingStyle(string block, int offset, TextStyle style)
    {
        chosenStyle = (block, offset, style);
        typingStyle = style;
    }

    private void BoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (callbacksOff > 0 || sender is not BlockTextBox box) return;
        SyncText(box);
    }

    /// <summary>
    /// What changed between two texts, given where the caret ended up:
    /// what follows the caret is what was there before. That tells apart
    /// what a plain comparison cannot, such as which of four letters alike
    /// is the new one.
    /// </summary>
    internal static (int Start, int End, string Inserted) Diff(string old, string now, int caret)
    {
        caret = Math.Clamp(caret, 0, now.Length);
        var tail = now.Length - caret;
        int start;
        int end;
        if (tail <= old.Length && string.CompareOrdinal(old, old.Length - tail, now, caret, tail) == 0)
        {
            end = old.Length - tail;
            var limit = Math.Min(end, caret);
            start = 0;
            while (start < limit && old[start] == now[start]) start++;
        }
        else
        {
            // The caret is not where the change ended. What the two texts
            // share at each end is the best that can be said.
            var limit = Math.Min(old.Length, now.Length);
            start = 0;
            while (start < limit && old[start] == now[start]) start++;
            var shared = 0;
            while (shared < limit - start && old[old.Length - 1 - shared] == now[now.Length - 1 - shared]) shared++;
            end = old.Length - shared;
            caret = now.Length - shared;
        }
        // Never from the middle of a character that is two units long:
        // two emoji can share their first half.
        if (start > 0 && char.IsHighSurrogate(old[start - 1])) start--;
        return (start, end, now[start..caret]);
    }

    private static bool IsOneCharacter(string text) => text.Length > 0 && new StringInfo(text).LengthInTextElements == 1;

    /// <summary>A run of typing in one block is one step to undo.</summary>
    private void BeginTyping(string block)
    {
        var now = Environment.TickCount64;
        if (typing is { } run && run.Block == block && now - run.At < 1500)
        {
            typing = (block, now);
            return;
        }
        Push(new Snapshot(Document.Clone(), Selection));
        typing = (block, now);
    }

    /// <summary>Copy what a text box now holds into the document.</summary>
    private void SyncText(BlockTextBox box)
    {
        if (Document.Block(box.BlockId) is not { } block) return;
        var old = block.Text.String;
        var now = EditorTheme.Clean(box.Text);
        if (now == old) return;
        if (!IsEditable)
        {
            // A box that may not be changed was changed all the same.
            WithoutTextCallbacks(() => box.Show(block));
            return;
        }
        var (start, end, inserted) = Diff(old, now, box.SelectedRange.End);
        var typed = start == end && IsOneCharacter(inserted) ? inserted : null;
        BeginTyping(block.Id);
        var style = block.Kind == BlockKind.Code ? TextStyle.Plain : typingStyle;
        block.Text = block.Text.Replace(start, end, new RichText(inserted, style));
        AfterTyping(box, block, typed);
    }

    /// <summary>
    /// Type text into a block as the keyboard would, by way of the
    /// document: for what a text box must not be left to do itself, such as
    /// a line break, which it would make a new paragraph of.
    /// </summary>
    private void TypeInto(BlockTextBox box, string text)
    {
        if (!IsEditable || Document.Block(box.BlockId) is not { } block) return;
        var (start, end) = box.SelectedRange;
        BeginTyping(block.Id);
        var style = block.Kind == BlockKind.Code ? TextStyle.Plain : typingStyle;
        block.Text = block.Text.Replace(start, end, new RichText(text, style));
        var caret = start + text.Length;
        WithoutTextCallbacks(() =>
        {
            box.Show(block);
            box.Select(caret, caret);
        });
        AfterTyping(box, block, start == end && IsOneCharacter(text) ? text : null);
    }

    /// <summary>What follows every change to a block's text, however it was made.</summary>
    private void AfterTyping(BlockTextBox box, Block block, string? typed)
    {
        (Page.BlockViewFor(block.Id) as TextHoldingView)?.NoteShown(block);
        Page.HideHandle();
        // While an input method is composing, the text is not final.
        if (!box.IsComposing)
        {
            var (start, end) = box.SelectedRange;
            if (typed is not null && start == end && block.Kind != BlockKind.Code && ApplyShortcut(typed, block, end)) return;
            Restyle(box, block);
            Selection = EditorSelection.Text(block.Id, start, end);
            if (typed == "/" && block.Kind != BlockKind.Code) OpenSlashMenu(box, end);
            UpdateSlashMenu(box);
            UpdateTypingStyle(box);
        }
        Host?.EditorDidChange(this);
    }

    /// <summary>
    /// Make a text box show the marks the document has, when typing left
    /// it showing otherwise: text typed where two runs meet goes into the
    /// later one, and text typed after a link is taken into the link.
    /// </summary>
    private void Restyle(BlockTextBox box, Block block)
    {
        if (box.Read() == block.Text) return;
        WithoutTextCallbacks(() =>
        {
            var (start, end) = box.SelectedRange;
            box.Show(block);
            box.Select(Math.Min(start, block.Text.Length), Math.Min(end, block.Text.Length));
        });
    }

    /// <summary>Markdown typed at the start of a block, or around a few words.</summary>
    private bool ApplyShortcut(string typed, Block block, int caret)
    {
        if (typed is not (" " or "-" or "*" or "_" or "`" or "~")) return false;
        var id = block.Id;
        var before = new Snapshot(Document.Clone(), EditorSelection.Caret(id, caret));
        EditorSelection? chosen = null;
        TextMark? closed = null;
        if (typed is " " or "-") chosen = Document.ApplyBlockShortcut(id, caret, Ids);
        if (chosen is null && typed is "*" or "_" or "`" or "~" && block.Text.ApplyingInlineShortcut(caret) is { } result)
        {
            block.Text = result.Text;
            chosen = EditorSelection.Caret(id, result.Caret);
            closed = result.Mark;
        }
        if (chosen is null && typed == " " && block.Text.Autolinked(caret) is { } linked)
        {
            block.Text = linked;
            chosen = EditorSelection.Caret(id, caret);
        }
        if (chosen is null) return false;
        // A step of its own, so undoing gives back what was typed.
        Push(before);
        typing = null;
        Refresh();
        Select(chosen);
        // The closing mark ended it: what is typed next does not carry on
        // in bold.
        if (closed is { } mark && FocusedText is { } box && Document.Block(id) is { } current)
        {
            var at = box.SelectedRange.Start;
            ChooseTypingStyle(id, at, current.Text.StyleBefore(at).With(mark, false));
        }
        Host?.EditorDidChange(this);
        return true;
    }

    private void BoxSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (callbacksOff > 0 || sender is not BlockTextBox box || !ReferenceEquals(FocusedText, box)) return;
        if (Document.Block(box.BlockId) is not { } block) return;
        // When the text has changed too, the caret is reported before the
        // text is. The change to the text will say where the caret is, and
        // needs the selection and the typing marks as they were.
        if (EditorTheme.Clean(box.Text) != block.Text.String) return;
        var (start, end) = box.SelectedRange;
        Selection = EditorSelection.Text(box.BlockId, start, end);
        SelectionMoved();
        UpdateTypingStyle(box);
        UpdateSlashMenu(box);
    }

    /// <summary>
    /// Type text at the caret, as the keyboard would. With no caret, at
    /// the end of the note.
    /// </summary>
    public void InsertText(string text)
    {
        if (!IsEditable) return;
        if (Selection is not EditorSelection.InText) FocusEnd();
        if (Selection is EditorSelection.InText && FocusedText is { } box) TypeInto(box, text);
    }

    internal void OpenLink(string href)
    {
        if (href.Length > 0) Host?.OpenLink(this, href);
    }

    // Keys in a block's text

    /// <summary>
    /// A key pressed in a block's text. True when the editor took it: what
    /// crosses from one block to the next, and the keys that are commands.
    /// False leaves the key to the text box.
    /// </summary>
    internal bool HandleKey(BlockTextBox box, Key key, ModifierKeys modifiers)
    {
        if (box.IsComposing) return false;
        if (HandleKeyEquivalent(key, modifiers, box)) return true;
        if (slash is not null && HandleSlashKey(key, modifiers)) return true;
        var id = box.BlockId;
        var (start, end) = box.SelectedRange;
        var length = box.Length;
        var plain = modifiers == ModifierKeys.None;
        var shift = modifiers == ModifierKeys.Shift;
        var control = modifiers == ModifierKeys.Control;

        switch (key)
        {
            case Key.Enter when plain || shift || control:
                if (!IsEditable) return true;
                if (control)
                {
                    if (box.IsCode)
                    {
                        // The way out of a code block: a new line below it.
                        Edit((document, ids) =>
                        {
                            var paragraph = new Block(ids.Make(), BlockKind.Paragraph);
                            document.InsertAfter([paragraph], id);
                            return EditorSelection.Caret(paragraph.Id, 0);
                        });
                    }
                    else
                    {
                        TypeInto(box, "\n");
                    }
                }
                else if (box.IsCode)
                {
                    InsertCodeNewline(box);
                }
                else if (shift)
                {
                    TypeInto(box, "\n");
                }
                else
                {
                    Edit((document, ids) => document.Split(id, start, end, ids));
                }
                return true;
            case Key.Back when plain || shift:
                if (start != end || start != 0) return false;
                Edit((document, ids) => document.DeleteBackward(id, ids));
                return true;
            case Key.Delete when plain:
                if (start != end || start != length) return false;
                Edit((document, _) => document.DeleteForward(id));
                return true;
            case Key.Tab when plain:
                if (box.IsCode) TypeInto(box, "  ");
                else Edit((document, _) => document.Indent(id) ? EditorSelection.Text(id, start, end) : null);
                return true;
            case Key.Tab when shift:
                if (!box.IsCode) Edit((document, _) => document.Outdent(id) ? EditorSelection.Text(id, start, end) : null);
                return true;
            case Key.Up when plain:
                return box.CaretIsOnFirstLine && MoveCaret(box, up: true);
            case Key.Down when plain:
                return box.CaretIsOnLastLine && MoveCaret(box, up: false);
            case Key.Left when plain:
                if (start != end || start != 0 || Document.StopBefore(id) is not { } before) return false;
                Select(AtEndOf(before));
                return true;
            case Key.Right when plain:
                if (start != end || start != length || Document.StopAfter(id) is not { } after) return false;
                Select(AtStartOf(after));
                return true;
            case Key.Up when shift:
                if (!box.CaretIsOnFirstLine || Document.StopBefore(id) is not { } above) return false;
                ExtendBlockSelection(id, above.Id);
                return true;
            case Key.Down when shift:
                if (!box.CaretIsOnLastLine || Document.StopAfter(id) is not { } below) return false;
                ExtendBlockSelection(id, below.Id);
                return true;
            case Key.Home when control:
                FocusStart();
                return true;
            case Key.End when control:
                if (Document.Stops.LastOrDefault() is not { } last) return false;
                Select(AtEndOf(last));
                return true;
            case Key.A when control:
                // The first Select All takes the block's text, the second the note.
                if (end - start != length) return false;
                SelectAllBlocks();
                return true;
            case Key.Escape when plain:
                SelectBlocks([id], extending: false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Move the caret to the block above or below, keeping its column.</summary>
    private bool MoveCaret(BlockTextBox box, bool up)
    {
        var id = box.BlockId;
        if ((up ? Document.StopBefore(id) : Document.StopAfter(id)) is not { } stop) return false;
        // The block the caret is going to needs its text box before a
        // place in it can be found.
        if (!stop.HasText || Page.EnsureTextBox(stop.Id) is not { } target)
        {
            Select(EditorSelection.Blocks([stop.Id]));
            return true;
        }
        var x = goalX ?? box.TranslatePoint(new Point(box.CaretX, 0), Page).X;
        Page.UpdateLayout();
        var offset = target.OffsetNearest(Page.TranslatePoint(new Point(x, 0), target).X, onFirstLine: !up);
        isMovingVertically = true;
        Select(EditorSelection.Caret(stop.Id, offset));
        isMovingVertically = false;
        goalX = x;
        return true;
    }

    /// <summary>A new line in code starts as far in as the line above it.</summary>
    private void InsertCodeNewline(BlockTextBox box)
    {
        var text = box.Text;
        var caret = box.SelectedRange.Start;
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var indent = new string([.. text[lineStart..caret].TakeWhile(character => character is ' ' or '\t')]);
        TypeInto(box, "\n" + indent);
    }

    // Keys while whole blocks are selected

    internal bool HandleBlockKey(Key key, ModifierKeys modifiers)
    {
        if (HandleKeyEquivalent(key, modifiers, null)) return true;
        if (Selection is not EditorSelection.OfBlocks(var selected) || selected.Count == 0) return false;
        if ((modifiers & ~ModifierKeys.Shift) != ModifierKeys.None)
        {
            if (modifiers != ModifierKeys.Control || key != Key.A) return false;
            SelectAllBlocks();
            return true;
        }
        var extending = modifiers == ModifierKeys.Shift;
        switch (key)
        {
            case Key.Back or Key.Delete:
                DeleteSelectedBlocks();
                break;
            case Key.Up or Key.Left:
                MoveBlockSelection(up: true, extending);
                break;
            case Key.Down or Key.Right:
                MoveBlockSelection(up: false, extending);
                break;
            case Key.Enter:
                // Back to typing, at the end of the selection.
                if (StopsIn(selected).LastOrDefault() is { } last && Document.Block(last) is { HasText: true } block)
                {
                    Select(EditorSelection.Caret(last, block.Text.Length));
                }
                break;
            case Key.Escape:
                Select(EditorSelection.None);
                break;
            case Key.Tab:
                Edit((document, _) =>
                {
                    var moved = false;
                    foreach (var id in extending ? selected.Reverse() : selected)
                    {
                        moved = (extending ? document.Outdent(id) : document.Indent(id)) || moved;
                    }
                    return moved ? EditorSelection.Blocks(selected) : null;
                });
                break;
            default:
                return false;
        }
        return true;
    }

    private void MoveBlockSelection(bool up, bool extending)
    {
        if (Selection is not EditorSelection.OfBlocks(var selected)) return;
        var order = Document.Stops;
        var inside = StopsIn(selected);
        if (inside.Count == 0) return;
        var first = inside[0];
        var last = inside[^1];

        int IndexOf(string id)
        {
            for (var index = 0; index < order.Count; index++)
            {
                if (order[index].Id == id) return index;
            }
            return -1;
        }

        if (extending)
        {
            var anchor = blockAnchor ?? (up ? last : first);
            var head = blockHead ?? (up ? first : last);
            var at = IndexOf(head);
            if (at < 0) return;
            var beyond = up ? at - 1 : at + 1;
            if (beyond < 0 || beyond >= order.Count) return;
            ExtendBlockSelection(anchor, order[beyond].Id);
            return;
        }
        var edge = IndexOf(up ? first : last);
        if (edge < 0) return;
        var next = up ? edge - 1 : edge + 1;
        if (next < 0 || next >= order.Count)
        {
            // Nothing beyond: back to typing in what is selected.
            if (Document.Block(up ? first : last) is { HasText: true } block)
            {
                Select(EditorSelection.Caret(block.Id, up ? 0 : block.Text.Length));
            }
            return;
        }
        var stop = order[next];
        Select(stop.HasText ? EditorSelection.Caret(stop.Id, up ? stop.Text.Length : 0) : EditorSelection.Blocks([stop.Id]));
    }

    // Keys that are commands

    /// <summary>
    /// Control keys the editor answers itself. <paramref name="box"/> is
    /// the text box being typed in, or null while whole blocks are selected.
    /// </summary>
    private bool HandleKeyEquivalent(Key key, ModifierKeys modifiers, BlockTextBox? box)
    {
        if ((modifiers & ModifierKeys.Control) == 0) return false;
        const ModifierKeys ControlShift = ModifierKeys.Control | ModifierKeys.Shift;
        const ModifierKeys ControlAlt = ModifierKeys.Control | ModifierKeys.Alt;
        switch (key, modifiers)
        {
            case (Key.Z, ModifierKeys.Control):
                Undo();
                return true;
            case (Key.Y, ModifierKeys.Control) or (Key.Z, ControlShift):
                Redo();
                return true;
            // Pasting is the editor's wherever the caret is: it knows blocks
            // and pictures, and a text box only text.
            case (Key.V, ModifierKeys.Control) or (Key.Insert, ModifierKeys.Shift):
                PasteFromClipboard();
                return true;
            case (Key.V, ControlShift):
                PasteFromClipboard(plainOnly: true);
                return true;
            case (Key.C, ModifierKeys.Control) when box is null:
                CopyToClipboard(cut: false);
                return true;
            case (Key.X, ModifierKeys.Control) when box is null:
                CopyToClipboard(cut: true);
                return true;
        }
        if (box is null || !IsEditable || box.IsCode) return false;
        switch (key, modifiers)
        {
            case (Key.B, ModifierKeys.Control):
                Toggle(TextMark.Bold);
                break;
            case (Key.I, ModifierKeys.Control):
                Toggle(TextMark.Italic);
                break;
            case (Key.E, ModifierKeys.Control):
                Toggle(TextMark.Code);
                break;
            case (Key.X, ControlShift):
                Toggle(TextMark.Strike);
                break;
            case (Key.K, ModifierKeys.Control):
                // With nothing to link, Ctrl+K is the app's Quick Open.
                var (start, end) = box.SelectedRange;
                if (start == end && CurrentLink is null) return false;
                EditLink();
                break;
            case (Key.D0, ControlAlt):
                TurnCurrentBlock(BlockTarget.Paragraph);
                break;
            case (Key.D1, ControlAlt):
                TurnCurrentBlock(BlockTarget.Heading(1));
                break;
            case (Key.D2, ControlAlt):
                TurnCurrentBlock(BlockTarget.Heading(2));
                break;
            case (Key.D3, ControlAlt):
                TurnCurrentBlock(BlockTarget.Heading(3));
                break;
            case (Key.D4, ControlAlt):
                TurnCurrentBlock(BlockTarget.Heading(4));
                break;
            case (Key.D7, ControlShift):
                TurnCurrentBlock(BlockTarget.Numbered);
                break;
            case (Key.D8, ControlShift):
                TurnCurrentBlock(BlockTarget.Bullet);
                break;
            case (Key.D9, ControlShift):
                TurnCurrentBlock(BlockTarget.Todo);
                break;
            default:
                return false;
        }
        return true;
    }

    // Marks and links

    /// <summary>A command from the Format menu.</summary>
    internal void Apply(EditorFormat format)
    {
        switch (format)
        {
            case EditorFormat.OfMark(var mark):
                Toggle(mark);
                break;
            case EditorFormat.OfLink:
                EditLink();
                break;
            case EditorFormat.OfBlock(var target):
                TurnCurrentBlock(target);
                break;
        }
    }

    /// <summary>
    /// Turn a mark on or off for the selected text, or for what is typed
    /// next when nothing is selected.
    /// </summary>
    public void Toggle(TextMark mark)
    {
        if (!IsEditable || Selection is not EditorSelection.InText(var id, var start, var end)
            || Document.Block(id) is not { } block || block.Kind == BlockKind.Code || Page.TextBoxFor(id) is null)
        {
            return;
        }
        if (start == end)
        {
            var style = typingStyle;
            if (mark == TextMark.Code)
            {
                style = style.Code ? new TextStyle(Other: style.Other) : new TextStyle(Code: true, Other: style.Other);
            }
            else if (!style.Code)
            {
                style = style.With(mark, !style.Has(mark));
            }
            ChooseTypingStyle(id, start, style);
            return;
        }
        var on = !block.Text.Has(mark, start, end);
        Edit((document, _) =>
        {
            document.Update(id, changed => changed.Text = changed.Text.Set(mark, on, start, end));
            return EditorSelection.Text(id, start, end);
        });
    }

    /// <summary>The link the caret is in, or that the selection is within.</summary>
    private (string Block, int Start, int End, TextLink Link)? CurrentLink
    {
        get
        {
            if (Selection is not EditorSelection.InText(var id, var start, var end) || Document.Block(id) is not { } block) return null;
            var found = block.Text.LinkAt(Math.Max(end, start + 1)) ?? block.Text.LinkAt(start);
            return found is { } link ? (id, link.Start, link.End, link.Link) : null;
        }
    }

    /// <summary>
    /// Link the selected text, or change or remove the link the caret is
    /// in. Null or an empty address removes the link.
    /// </summary>
    public void SetLink(string? address)
    {
        if (!IsEditable || Selection is not EditorSelection.InText(var id, var selectedStart, var selectedEnd)) return;
        var current = CurrentLink;
        var (start, end) = selectedStart == selectedEnd && current is { } within
            ? (within.Start, within.End)
            : (selectedStart, selectedEnd);
        if (start == end) return;
        var cleaned = address is null ? null : CleanAddress(address);
        var link = cleaned is null ? null : new TextLink(cleaned, current?.Link.Extra);
        Edit((document, _) =>
        {
            document.Update(id, changed => changed.Text = changed.Text.SetLink(link, start, end));
            return EditorSelection.Text(id, selectedStart, selectedEnd);
        });
    }

    /// <summary>
    /// An address fit to be a link: one of the kinds the app opens, with
    /// <c>https://</c> assumed when none is given. Null for anything else.
    /// </summary>
    internal static string? CleanAddress(string address)
    {
        var trimmed = address.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace)) return null;
        var colon = trimmed.IndexOf(':');
        if (colon < 0) return "https://" + trimmed;
        var scheme = trimmed[..colon];
        var rest = trimmed[(colon + 1)..];
        // What stands before a colon is a kind of link when it is a bare
        // word: "mailto:", "https:". "example.com:8080" is a host and a port.
        var isScheme = scheme.Length > 0 && char.IsLetter(scheme[0])
            && scheme.All(character => char.IsLetterOrDigit(character) || character is '+' or '-')
            && !(rest.Length > 0 && char.IsDigit(rest[0]));
        if (!isScheme) return "https://" + trimmed;
        return scheme.ToLowerInvariant() is "http" or "https" or "mailto" or "heracles" ? trimmed : null;
    }

    /// <summary>Ask for the address of a link, in a small panel by the text.</summary>
    internal void EditLink()
    {
        if (Selection is not EditorSelection.InText(var id, var start, var end) || Page.TextBoxFor(id) is not { } box) return;
        var current = CurrentLink;
        var (shownStart, shownEnd) = current is { } within ? (within.Start, within.End) : (start, end);
        if (shownStart == shownEnd) return;

        var field = new TextBox { Text = current?.Link.Href ?? "", Width = 260, VerticalContentAlignment = VerticalAlignment.Center }
            .Id("LinkAddress").Named("Paste or type a link");
        var remove = new Button { Content = "Remove", IsEnabled = current is not null, Margin = new Thickness(6, 0, 0, 0) }.Id("LinkRemove");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
        row.Children.Add(field);
        row.Children.Add(remove);
        var surface = new Border { Child = row, CornerRadius = new CornerRadius(6) };
        Ui.AsSurface(surface);
        var from = box.PointerAt(shownStart).GetCharacterRect(LogicalDirection.Forward);
        var to = box.PointerAt(shownEnd).GetCharacterRect(LogicalDirection.Backward);
        var panel = new Popup
        {
            Child = surface,
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            PlacementRectangle = from.IsEmpty || to.IsEmpty ? Rect.Empty : Rect.Union(from, to),
            StaysOpen = false,
            AllowsTransparency = true,
        };

        void Done(string? address)
        {
            panel.IsOpen = false;
            SetLink(address);
            // The keyboard goes back to the text it was taken from.
            FocusedText?.Focus();
        }

        field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Done(field.Text);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                panel.IsOpen = false;
                FocusedText?.Focus();
            }
        };
        remove.Click += (_, _) => Done(null);
        if (popup is not null) popup.IsOpen = false;
        popup = panel;
        panel.IsOpen = true;
        field.Focus();
        field.SelectAll();
    }

    // Changing blocks

    /// <summary>Turn the block being typed in into another kind, keeping the caret.</summary>
    public void TurnCurrentBlock(BlockTarget into)
    {
        switch (Selection)
        {
            case EditorSelection.InText(var id, var start, var end):
                Edit((document, ids) =>
                {
                    var chosen = document.Turn(id, into, ids);
                    return chosen is EditorSelection.InText landed && landed.Block == id ? EditorSelection.Text(id, start, end) : chosen;
                });
                break;
            case EditorSelection.OfBlocks(var selected):
                Edit((document, ids) =>
                {
                    foreach (var id in selected) document.Turn(id, into, ids);
                    return null;
                });
                Select(EditorSelection.Blocks(selected.Where(Document.Contains)));
                break;
        }
    }

    // The slash menu

    /// <summary>The commands the slash menu is offering, or none while it is closed.</summary>
    internal IReadOnlyList<SlashCommand> SlashCommands => slash is null ? [] : Page.SlashMenu.Items;

    private void OpenSlashMenu(BlockTextBox box, int caret)
    {
        var text = box.Text;
        var start = caret - 1;
        if (start < 0 || start >= text.Length || text[start] != '/') return;
        // Only where a word could start, so a slash inside a date or a
        // path is just a slash.
        if (start > 0 && !char.IsWhiteSpace(text[start - 1])) return;
        slash = (box.BlockId, start);
        Page.SlashMenu.Selected = 0;
    }

    private void UpdateSlashMenu(BlockTextBox box)
    {
        if (slash is not { } open) return;
        var text = box.Text;
        var (start, end) = box.SelectedRange;
        if (box.BlockId != open.Block || start != end || end <= open.Start || open.Start >= text.Length || text[open.Start] != '/')
        {
            CloseSlashMenu();
            return;
        }
        var query = text[(open.Start + 1)..end];
        if (query.Contains(' ') || query.Contains('\n') || query.Length > 24)
        {
            CloseSlashMenu();
            return;
        }
        var menu = Page.SlashMenu;
        menu.Items = SlashCommand.Matching(query);
        menu.Selected = Math.Min(menu.Selected, Math.Max(menu.Items.Count - 1, 0));
        menu.Visibility = Visibility.Visible;
        Page.PlaceSlashMenu(box, open.Start);
    }

    internal void CloseSlashMenu()
    {
        slash = null;
        Page.SlashMenu.Visibility = Visibility.Collapsed;
    }

    private bool HandleSlashKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.None) return false;
        var menu = Page.SlashMenu;
        switch (key)
        {
            case Key.Down:
                if (menu.Items.Count > 0) menu.Selected = (menu.Selected + 1) % menu.Items.Count;
                break;
            case Key.Up:
                if (menu.Items.Count > 0) menu.Selected = (menu.Selected + menu.Items.Count - 1) % menu.Items.Count;
                break;
            case Key.Enter or Key.Tab:
                if (menu.Items.Count == 0)
                {
                    CloseSlashMenu();
                    return false;
                }
                ChooseSlashCommand(menu.Selected);
                break;
            case Key.Escape:
                CloseSlashMenu();
                break;
            default:
                return false;
        }
        return true;
    }

    /// <summary>
    /// Take a command from the slash menu: what was typed to find it goes
    /// away, and the block becomes what was chosen.
    /// </summary>
    internal void ChooseSlashCommand(int index)
    {
        if (slash is not { } open || index < 0 || index >= Page.SlashMenu.Items.Count || Page.TextBoxFor(open.Block) is not { } box)
        {
            return;
        }
        var command = Page.SlashMenu.Items[index];
        var caret = box.SelectedRange.Start;
        CloseSlashMenu();
        Edit((document, ids) =>
        {
            document.Update(open.Block, block => block.Text = block.Text.Replace(open.Start, Math.Max(caret, open.Start), new RichText()));
            var chosen = document.Turn(open.Block, command.Target, ids);
            return chosen is EditorSelection.InText landed && landed.Block == open.Block
                ? EditorSelection.Caret(open.Block, open.Start)
                : chosen;
        });
    }
}
