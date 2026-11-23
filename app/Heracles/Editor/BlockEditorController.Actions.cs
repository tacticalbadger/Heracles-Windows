using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Heracles.Editor;

/// <summary>An entry of a menu the editor offers. An empty title is a line between entries.</summary>
internal sealed record BlockMenuItem(string Title, Action? Run = null, IReadOnlyList<BlockMenuItem>? Inside = null)
{
    public static BlockMenuItem Line { get; } = new("");
}

// Block actions, the menus, the clipboard, pictures, and what the mouse
// does. The rest of the Mac's BlockEditorController.swift.
internal sealed partial class BlockEditorController
{
    /// <summary>The editor's own form on the clipboard: a document, as JSON.</summary>
    public const string ClipboardFormat = "com.heracles.notes.blocks";

    /// <summary>The block a mouse drag started in, while the button is down.</summary>
    private string? mouseAnchor;

    /// <summary>Where in that block's text, when the editor is selecting the text itself.</summary>
    private int? mouseAnchorOffset;

    // Changing blocks

    internal void Turn(string id, BlockTarget into) => Edit((document, ids) => document.Turn(id, into, ids));

    internal void SetChecked(string id, bool isChecked) => Edit((document, _) =>
    {
        document.Update(id, block => block.IsChecked = isChecked);
        return null;
    });

    internal void SetOpen(string id, bool open)
    {
        Edit((document, _) =>
        {
            document.Update(id, block => block.IsOpen = open);
            return null;
        });
        // The caret cannot stay in a block that was just folded away.
        if (!open && Selection is EditorSelection.InText(var selected, _, _) && Document.Stops.All(stop => stop.Id != selected)
            && Document.Block(id)?.Children.FirstOrDefault() is { } first)
        {
            Select(first.HasText ? EditorSelection.Caret(first.Id, first.Text.Length) : EditorSelection.Blocks([id]));
        }
    }

    internal void SetLanguage(string id, string language) => Edit((document, _) =>
    {
        document.Update(id, block => block.Language = language);
        return null;
    });

    internal void SetEmoji(string id, string emoji) => Edit((document, _) =>
    {
        document.Update(id, block => block.Emoji = emoji);
        return null;
    });

    /// <summary>
    /// Set the widths of a row's columns, during a drag. The drag as a
    /// whole is one step to undo, begun with <see cref="BeginUndoGroup"/>.
    /// </summary>
    internal void SetColumnWidths(string id, IReadOnlyList<double> widths) => Edit(
        (document, _) =>
        {
            document.Update(id, row =>
            {
                for (var index = 0; index < row.Children.Count && index < widths.Count; index++)
                {
                    row.Children[index].Width = Math.Round(widths[index] * 1000) / 1000;
                }
            });
            return null;
        },
        undoable: false);

    internal void ChooseEmoji(string id, UIElement from) =>
        _ = new Heracles.Notes.IconPicker(from, Document.Block(id)?.Emoji, emoji => SetEmoji(id, emoji ?? "💡")) { IsOpen = true };

    /// <summary>Add an empty line below a block and put the caret in it.</summary>
    internal void AddBlock(string after) => Edit((document, ids) =>
    {
        var paragraph = new Block(ids.Make(), BlockKind.Paragraph);
        document.InsertAfter([paragraph], after);
        return EditorSelection.Caret(paragraph.Id, 0);
    });

    internal bool Move(IReadOnlyList<string> blocks, DropTarget target) =>
        Edit((document, ids) => document.Move(blocks, target, ids) ? EditorSelection.Blocks(blocks) : null);

    internal void Duplicate(string id) => Edit((document, ids) =>
        document.Duplicate(id, ids) is { } copy ? EditorSelection.Blocks([copy]) : null);

    internal void Remove(IReadOnlyList<string> blocks) => Edit((document, ids) => document.Remove(blocks, ids));

    internal void CopyLinkToBlock(string id) => Host?.CopyLinkToBlock(this, id);

    // Menus

    private static readonly (string Title, BlockTarget Target)[] TurnChoices =
    [
        ("Text", BlockTarget.Paragraph), ("Heading 1", BlockTarget.Heading(1)), ("Heading 2", BlockTarget.Heading(2)),
        ("Heading 3", BlockTarget.Heading(3)), ("Bullet List", BlockTarget.Bullet), ("Numbered List", BlockTarget.Numbered),
        ("Task List", BlockTarget.Todo), ("Quote", BlockTarget.Quote), ("Callout", BlockTarget.Callout),
        ("Toggle", BlockTarget.Toggle), ("Code", BlockTarget.Code),
    ];

    /// <summary>What the block handle's menu offers for a block.</summary>
    internal IReadOnlyList<BlockMenuItem> BlockMenu(string id)
    {
        if (Document.Block(id) is not { } block) return [];
        var items = new List<BlockMenuItem>();
        if (block.HasText || block.Kind is BlockKind.Quote or BlockKind.Callout or BlockKind.Toggle)
        {
            items.Add(new BlockMenuItem(
                "Turn Into", Inside: [.. TurnChoices.Select(choice => new BlockMenuItem(choice.Title, () => Turn(id, choice.Target)))]));
            items.Add(BlockMenuItem.Line);
        }
        items.Add(new BlockMenuItem("Duplicate", () => Duplicate(id)));
        items.Add(new BlockMenuItem("Copy Link to Block", () => CopyLinkToBlock(id)));
        items.Add(BlockMenuItem.Line);
        items.Add(new BlockMenuItem("Delete", () => Remove([id])));
        return items;
    }

    /// <summary>What a block's text offers when it is right-clicked, besides what any text box does.</summary>
    internal IReadOnlyList<BlockMenuItem> TextMenu()
    {
        if (!IsEditable || Selection is not EditorSelection.InText(var id, var start, var end)
            || Document.Block(id) is not { } block || block.Kind == BlockKind.Code)
        {
            return [];
        }
        var items = new List<BlockMenuItem>();
        var link = CurrentLink;
        if (start != end || link is not null)
        {
            items.Add(new BlockMenuItem(link is null ? "Add Link…" : "Edit Link…", EditLink));
            if (link is not null) items.Add(new BlockMenuItem("Remove Link", () => SetLink(null)));
        }
        if (start != end)
        {
            items.Add(new BlockMenuItem("Bold", () => Toggle(TextMark.Bold)));
            items.Add(new BlockMenuItem("Italic", () => Toggle(TextMark.Italic)));
            items.Add(new BlockMenuItem("Strikethrough", () => Toggle(TextMark.Strike)));
            items.Add(new BlockMenuItem("Code", () => Toggle(TextMark.Code)));
        }
        items.Add(new BlockMenuItem("Copy Link to Block", () => CopyLinkToBlock(id)));
        return items;
    }

    private static void Fill(ItemCollection menu, IReadOnlyList<BlockMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.Title.Length == 0)
            {
                menu.Add(new Separator());
                continue;
            }
            var entry = new MenuItem { Header = item.Title };
            if (item.Run is { } run) entry.Click += (_, _) => run();
            if (item.Inside is { } inside) Fill(entry.Items, inside);
            menu.Add(entry);
        }
    }

    /// <summary>Open the block handle's menu for a block, under the handle.</summary>
    internal void ShowBlockMenu(string id, UIElement under)
    {
        var items = BlockMenu(id);
        if (items.Count == 0) return;
        var menu = new ContextMenu { PlacementTarget = under, Placement = PlacementMode.Bottom };
        Fill(menu.Items, items);
        menu.IsOpen = true;
    }

    /// <summary>Fill a text box's own menu as it opens: spelling first, then the editor's entries, then the clipboard.</summary>
    internal void FillTextMenu(BlockTextBox box, ContextMenu menu)
    {
        menu.Items.Clear();
        if (box.GetSpellingError(box.CaretPosition) is { } error)
        {
            foreach (var suggestion in error.Suggestions.Take(5))
            {
                menu.Items.Add(new MenuItem
                {
                    Header = suggestion, FontWeight = FontWeights.SemiBold, Command = EditingCommands.CorrectSpellingError,
                    CommandParameter = suggestion, CommandTarget = box,
                });
            }
            menu.Items.Add(new MenuItem { Header = "Ignore", Command = EditingCommands.IgnoreSpellingError, CommandTarget = box });
            menu.Items.Add(new Separator());
        }
        var own = TextMenu();
        if (own.Count > 0)
        {
            Fill(menu.Items, own);
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(new MenuItem { Header = "Cut", Command = ApplicationCommands.Cut, CommandTarget = box });
        menu.Items.Add(new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy, CommandTarget = box });
        menu.Items.Add(new MenuItem { Header = "Paste", Command = ApplicationCommands.Paste, CommandTarget = box });
        var plain = new MenuItem { Header = "Paste as Plain Text", IsEnabled = IsEditable };
        plain.Click += (_, _) => PasteFromClipboard(plainOnly: true);
        menu.Items.Add(plain);
    }

    // Copy and paste

    private static string DocumentOf(IReadOnlyList<Block> blocks) => JsonValue.ObjectOf(
        ("type", JsonValue.Of("doc")), ("content", JsonValue.ArrayOf(EditorDocument.Nodes(blocks)))).Text;

    /// <summary>
    /// What is selected, to be put on the clipboard. Null when a text box
    /// should copy its own selection, which it knows how to offer to other
    /// apps. The editor then adds the marks in its own form with
    /// <see cref="AddCopiedText"/>.
    /// </summary>
    internal IDataObject? CopySelection()
    {
        if (Selection is not EditorSelection.OfBlocks(var selected)) return null;
        var blocks = selected.Select(Document.Block).OfType<Block>().ToList();
        if (blocks.Count == 0) return null;
        var data = new DataObject();
        // For other apps, lines that end as Windows ends them.
        data.SetData(DataFormats.UnicodeText, string.Join("\r\n", EditorDocument.Lines(blocks, "")));
        data.SetData(ClipboardFormat, DocumentOf(blocks));
        return data;
    }

    /// <summary>
    /// After a text box copied its selection: add the same text with the
    /// editor's own marks, so pasting it back loses nothing.
    /// </summary>
    internal void AddCopiedText(IDataObject data)
    {
        if (Selection is not EditorSelection.InText(var id, var start, var end) || start == end
            || Document.Block(id) is not { } block || block.Kind == BlockKind.Code)
        {
            return;
        }
        data.SetData(ClipboardFormat, DocumentOf([new Block("copy", BlockKind.Paragraph, block.Text.Subtext(start, end))]));
    }

    internal IDataObject? CutSelection()
    {
        if (!IsEditable || CopySelection() is not { } data) return null;
        DeleteSelectedBlocks();
        return data;
    }

    private static T? Read<T>(IDataObject data, string format) where T : class
    {
        // The system's clipboard can be busy, or hold something that will
        // not come out. Either way there is nothing to paste from it.
        try { return data.GetDataPresent(format) ? data.GetData(format) as T : null; }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or OutOfMemoryException) { return null; }
    }

    /// <summary>
    /// Paste at the selection. False when the clipboard holds nothing the
    /// editor can use.
    /// </summary>
    internal bool Paste(IDataObject? data, bool plainOnly = false)
    {
        if (!IsEditable || data is null) return false;
        var text = Read<string>(data, DataFormats.UnicodeText);
        var own = Read<string>(data, ClipboardFormat);

        // Code takes text exactly as it is.
        if (Selection is EditorSelection.InText(var codeId, _, _) && Document.Block(codeId)?.Kind == BlockKind.Code)
        {
            if (text is null || Page.TextBoxFor(codeId) is not { } box) return false;
            TypeInto(box, EditorTheme.Clean(text));
            return true;
        }

        // A web address pasted over selected text links that text.
        if (!plainOnly && Selection is EditorSelection.InText(_, var from, var to) && from != to && own is null
            && text?.Trim() is { } address && RichText.IsWebAddress(address))
        {
            SetLink(address);
            return true;
        }

        List<Block>? blocks = null;
        if (!plainOnly && own is not null) blocks = BlocksFrom(own);
        var files = ImageFiles(data);
        var hasText = text is not null || data.GetDataPresent(DataFormats.Html) || data.GetDataPresent(DataFormats.Rtf);
        if (blocks is null && (!hasText || files.Count > 0))
        {
            // A picture, or picture files copied in Explorer.
            blocks = ImageBlocks(files.Count > 0 ? ImagesIn(files) : ImagesOn(data));
        }
        if (blocks is null && !plainOnly && Read<string>(data, DataFormats.Html) is { Length: > 0 } html
            && Host?.DocumentFromHtml(this, HtmlFragment(html)) is { } converted)
        {
            blocks = BlocksFrom(converted);
        }
        if (blocks is null && text is not null)
        {
            var lines = EditorTheme.Clean(text).Split('\n');
            var kept = lines.Length == 1 ? lines : [.. lines.Where(line => line.Trim().Length > 0)];
            blocks = [.. kept.Select(line =>
            {
                // An address on a line of its own arrives as a link.
                var address = line.Trim();
                var style = !plainOnly && RichText.IsWebAddress(address) ? new TextStyle(Link: new TextLink(address)) : TextStyle.Plain;
                return new Block("pasted", BlockKind.Paragraph, new RichText(line, style));
            })];
        }
        if (blocks is not { Count: > 0 }) return false;
        var target = Selection;
        return Edit((document, ids) => document.Paste(blocks, target, ids));
    }

    private static List<Block>? BlocksFrom(string json)
    {
        if (EditorDocument.FromJson(json, BlockIds.Random()) is not { } document) return null;
        // A document with nothing in it reads as one empty paragraph.
        if (document.Blocks is [{ Kind: BlockKind.Paragraph } only] && only.Text.IsEmpty) return null;
        return document.Blocks;
    }

    /// <summary>
    /// The page on a Windows clipboard is wrapped: a header of offsets,
    /// then the whole page with the copied part marked. This is the copied
    /// part.
    /// </summary>
    internal static string HtmlFragment(string html)
    {
        const string StartMark = "<!--StartFragment-->";
        const string EndMark = "<!--EndFragment-->";
        var start = html.IndexOf(StartMark, StringComparison.OrdinalIgnoreCase);
        var end = html.LastIndexOf(EndMark, StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start) return html[(start + StartMark.Length)..end];
        if (html.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
        {
            var page = html.IndexOf('<');
            return page < 0 ? "" : html[page..];
        }
        return html;
    }

    /// <summary>The system clipboard, for the views. Tests give <see cref="Paste"/> a clipboard of their own.</summary>
    internal bool PasteFromClipboard(bool plainOnly = false)
    {
        try { return Paste(Clipboard.GetDataObject(), plainOnly); }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }

    internal void CopyToClipboard(bool cut)
    {
        if ((cut ? CutSelection() : CopySelection()) is not { } data) return;
        try { Clipboard.SetDataObject(data, copy: true); }
        catch (System.Runtime.InteropServices.COMException) { Log.Info("clipboard: could not be written"); }
    }

    // Images

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".heic"] = "image/heic", [".tiff"] = "image/tiff", [".tif"] = "image/tiff",
        [".bmp"] = "image/bmp",
    };

    /// <summary>The kind of image a file is, by its name. Null when it is not one the editor takes.</summary>
    internal static string? ImageType(string path) => ImageTypes.GetValueOrDefault(Path.GetExtension(path));

    private static List<string> ImageFiles(IDataObject data) =>
        [.. (Read<string[]>(data, DataFormats.FileDrop) ?? []).Where(file => ImageType(file) is not null)];

    private sealed record Picture(byte[] Data, string Mime, string? Name);

    private static List<Picture> ImagesIn(IEnumerable<string> files)
    {
        var pictures = new List<Picture>();
        foreach (var file in files)
        {
            if (ImageType(file) is not { } mime) continue;
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > 64 * 1024 * 1024) continue;
                pictures.Add(new Picture(File.ReadAllBytes(file), mime, Path.GetFileNameWithoutExtension(file)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A file that cannot be read is left out.
            }
        }
        return pictures;
    }

    /// <summary>A picture on a clipboard: as a PNG, or as Windows itself holds one.</summary>
    private static List<Picture> ImagesOn(IDataObject data)
    {
        if (Read<Stream>(data, "PNG") is { } png)
        {
            using var copy = new MemoryStream();
            if (png.CanSeek) png.Position = 0;
            png.CopyTo(copy);
            return [new Picture(copy.ToArray(), "image/png", null)];
        }
        if (Read<BitmapSource>(data, DataFormats.Bitmap) is { } bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            return [new Picture(encoded.ToArray(), "image/png", null)];
        }
        return [];
    }

    private List<Block>? ImageBlocks(IEnumerable<Picture> pictures)
    {
        var blocks = new List<Block>();
        foreach (var picture in pictures)
        {
            if (Host?.StoreImage(this, picture.Data, picture.Mime) is not { } source) continue;
            var block = new Block("image", BlockKind.Image);
            block.Attrs["src"] = JsonValue.Of(source);
            block.Attrs["alt"] = picture.Name is null ? JsonValue.Null : JsonValue.Of(picture.Name);
            block.Attrs["title"] = JsonValue.Null;
            blocks.Add(block);
        }
        return blocks.Count == 0 ? null : blocks;
    }

    /// <summary>Put image files into the note, where they were dropped.</summary>
    internal bool InsertImages(IReadOnlyList<string> files, DropTarget? target)
    {
        if (ImageBlocks(ImagesIn(files)) is not { } blocks) return false;
        return Edit((document, ids) =>
        {
            var fresh = EditorDocument.Fresh(blocks, ids);
            if (target is not { } where) document.Blocks.AddRange(fresh);
            else if (where.Side == DropSide.Before) document.InsertBefore(fresh, where.BlockId);
            else document.InsertAfter(fresh, where.BlockId);
            return EditorSelection.Blocks(fresh.Select(block => block.Id));
        });
    }

    // For the app around the editor

    /// <summary>
    /// Bring a block into view and mark it for a moment. The id may be a
    /// list item's or that of the paragraph inside it.
    /// </summary>
    public bool Reveal(string blockId)
    {
        if (Document.AllBlocks.FirstOrDefault(candidate => candidate.Id == blockId || candidate.ParagraphId == blockId) is not { } block)
        {
            return false;
        }
        // Open any closed toggle it is folded away in.
        var path = Document.PathTo(block.Id) ?? [];
        for (var length = path.Length - 1; length > 0; length--)
        {
            var ancestor = Document.At(path.AsSpan(0, length));
            if (ancestor.Kind == BlockKind.Toggle && !ancestor.IsOpen) SetOpen(ancestor.Id, true);
        }
        Page.UpdateLayout();
        if (Page.BlockViewFor(block.Id) is not { } view || Page.FrameOf(block.Id) is null) return false;
        view.BringIntoView(new Rect(0, -80, view.RenderSize.Width, view.RenderSize.Height + 160));
        Page.Flash(block.Id);
        return true;
    }

    // The mouse in a block's text

    /// <summary>
    /// A click in a block's static text: the text box takes its place, and
    /// the caret goes where the click was. Twice selects the word there,
    /// three times all the block's text.
    /// </summary>
    internal void ActivateAt(string blockId, Point pointInText, int clicks = 1)
    {
        if (Page.EnsureTextBox(blockId) is not { } box) return;
        Page.UpdateLayout();
        var offset = box.OffsetAt(pointInText);
        if (clicks <= 1)
        {
            Select(EditorSelection.Caret(blockId, offset));
            return;
        }
        var text = box.Text;
        var (start, end) = clicks == 2 ? WordAt(text, offset) : (0, text.Length);
        Select(EditorSelection.Text(blockId, start, end));
    }

    private static (int Start, int End) WordAt(string text, int offset)
    {
        static bool InWord(char character) => char.IsLetterOrDigit(character) || character is '_' or '\'' || char.IsSurrogate(character);
        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset;
        var end = offset;
        while (end < text.Length && InWord(text[end])) end++;
        while (start > 0 && InWord(text[start - 1])) start--;
        // On a space or a mark, that one character.
        if (start == end && end < text.Length) end++;
        return (start, end);
    }

    /// <summary>A plain click in a block's text, at an offset. True when it was on a link, which is opened.</summary>
    internal bool TextClicked(string blockId, int offset)
    {
        if (Document.Block(blockId) is not { } block) return false;
        if ((block.Text.LinkAt(offset) ?? block.Text.LinkAt(offset + 1)) is not { } link) return false;
        if (offset < link.Start || offset > link.End) return false;
        OpenLink(link.Link.Href);
        return true;
    }

    /// <summary>
    /// The mouse went down in a block's text. With an offset, the editor
    /// selects the text as the mouse moves: the click landed on static
    /// text, so the text box never saw the button go down.
    /// </summary>
    internal void BeginTextDrag(string blockId, int? anchorOffset = null)
    {
        CloseSlashMenu();
        mouseAnchor = blockId;
        mouseAnchorOffset = anchorOffset;
    }

    internal bool IsDraggingText => mouseAnchor is not null;

    /// <summary>
    /// The block under the pointer, when the drag has left the block it
    /// began in.
    /// </summary>
    private string? BlockUnderDrag(Point onPage)
    {
        if (mouseAnchor is not { } anchor) return null;
        var hovered = Page.BlockViewAt(onPage, nearest: true)?.Block.Id;
        if (hovered is null && Document.Stops is { Count: > 0 } stops && Document.Blocks.Count > 0)
        {
            // Past either end of the note, the selection runs to that end.
            var top = Page.FrameOf(Document.Blocks[0].Id)?.Top ?? 0;
            hovered = onPage.Y < top ? stops[0].Id : stops[^1].Id;
        }
        return hovered is null || hovered == anchor ? null : hovered;
    }

    internal void ContinueTextDrag(Point onPage)
    {
        if (mouseAnchor is not { } anchor) return;
        var hovered = BlockUnderDrag(onPage);
        Page.PendingBlocks = hovered is null ? [] : Document.BlocksBetween(anchor, hovered);
        if (hovered is null && mouseAnchorOffset is { } from && FocusedText is { } box && box.BlockId == anchor)
        {
            var to = box.OffsetAt(Page.TranslatePoint(onPage, box));
            Select(EditorSelection.Text(anchor, Math.Min(from, to), Math.Max(from, to)));
        }
    }

    /// <summary>The button was let go. True when whole blocks ended up selected.</summary>
    internal bool EndTextDrag(Point onPage)
    {
        if (mouseAnchor is not { } anchor) return false;
        var head = BlockUnderDrag(onPage);
        mouseAnchor = null;
        mouseAnchorOffset = null;
        Page.PendingBlocks = [];
        if (head is null) return false;
        ExtendBlockSelection(anchor, head);
        return true;
    }
}
