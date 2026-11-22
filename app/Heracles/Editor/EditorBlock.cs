namespace Heracles.Editor;

/// <summary>
/// Makes ids for new blocks. Random in the app, in sequence in tests so that
/// a test can name the blocks an edit creates. Shared by whoever is handed
/// it, as an <c>inout</c> argument is on the Mac.
/// </summary>
internal sealed class BlockIds
{
    private readonly string? prefix;
    private int count;

    private BlockIds(string? prefix)
    {
        this.prefix = prefix;
    }

    /// <summary>Ids like the web editor made: lowercase UUIDs.</summary>
    public static BlockIds Random() => new(null);

    /// <summary><c>n1</c>, <c>n2</c>, and so on.</summary>
    public static BlockIds Sequential(string prefix = "n") => new(prefix);

    public string Make() => prefix is null ? Guid.NewGuid().ToString().ToLowerInvariant() : $"{prefix}{++count}";
}

internal enum BlockKind
{
    Paragraph,
    Heading,

    // Items of a bullet list, a numbered list and a task list.
    Bullet,
    Numbered,
    Todo,
    Code,

    // Blocks that hold other blocks and have no text of their own.
    Quote,
    Callout,
    Toggle,

    // A row of columns, and one column of it.
    Columns,
    Column,
    Divider,
    Image,

    /// <summary>A block this editor does not know. It is kept as stored.</summary>
    Unknown,
}

/// <summary>
/// One block of a note.
///
/// The stored document nests lists the way HTML does: a list holds items, an
/// item holds a paragraph and then anything else. Here a list item is a block
/// of its own with its text on it and the rest as its children, and the list
/// around it is implied by its neighbours. That is the shape the editor works
/// in: every block is a row you can move.
///
/// A block is changed in place, as on the Mac. Where the Mac copies one by
/// assigning it, call <see cref="Clone"/>.
/// </summary>
internal sealed class Block
{
    /// <summary>
    /// The block's id. Links to a block use it. Rows, columns and unknown
    /// blocks have no stored id, and theirs lasts only while the note is open.
    /// </summary>
    public string Id { get; set; }

    public BlockKind Kind { get; set; }

    /// <summary>The block's text. Empty for blocks that have none.</summary>
    public RichText Text { get; set; }

    public List<Block> Children { get; set; }

    /// <summary>
    /// Attributes as stored, apart from the id: a heading's level, a task's
    /// state, and any this editor does not know.
    /// </summary>
    public Dictionary<string, JsonValue> Attrs { get; set; }

    /// <summary>For a list item, the id of the stored paragraph that holds its text.</summary>
    public string? ParagraphId { get; set; }

    /// <summary>
    /// True for an item that begins a list of its own although the block
    /// before it is an item of the same kind.
    /// </summary>
    public bool StartsList { get; set; }

    /// <summary>
    /// The stored attributes of the list an item begins, such as the number a
    /// numbered list starts from. Only the first item's are used.
    /// </summary>
    public Dictionary<string, JsonValue> ListAttrs { get; set; } = [];

    /// <summary>An unknown block, as stored.</summary>
    public JsonValue? Raw { get; set; }

    public Block(
        string id, BlockKind kind, RichText? text = null, IEnumerable<Block>? children = null,
        IEnumerable<KeyValuePair<string, JsonValue>>? attrs = null, string? paragraphId = null)
    {
        Id = id;
        Kind = kind;
        Text = text ?? new RichText();
        Children = children?.ToList() ?? [];
        Attrs = attrs is null ? [] : new Dictionary<string, JsonValue>(attrs);
        ParagraphId = paragraphId;
    }

    // What kind of block

    public bool IsListItem => Kind is BlockKind.Bullet or BlockKind.Numbered or BlockKind.Todo;

    /// <summary>True for blocks whose text the user types into.</summary>
    public bool HasText => Kind is BlockKind.Paragraph or BlockKind.Heading or BlockKind.Code || IsListItem;

    /// <summary>True for blocks that only hold other blocks.</summary>
    public bool IsContainer => Kind is BlockKind.Quote or BlockKind.Callout or BlockKind.Toggle or BlockKind.Columns or BlockKind.Column;

    /// <summary>True for blocks that may have children at all.</summary>
    public bool HoldsBlocks => IsListItem || IsContainer;

    // Attributes by name

    /// <summary>A heading's level, 1 to 6.</summary>
    public int Level
    {
        get => Math.Clamp(Attrs.GetValueOrDefault("level")?.IntValue ?? 1, 1, 6);
        set => Attrs["level"] = JsonValue.Of(Math.Clamp(value, 1, 6));
    }

    public bool IsChecked
    {
        get => Attrs.GetValueOrDefault("checked")?.BoolValue ?? false;
        set => Attrs["checked"] = JsonValue.Of(value);
    }

    public string Language
    {
        get => Attrs.GetValueOrDefault("language")?.StringValue ?? "plaintext";
        set => Attrs["language"] = JsonValue.Of(value);
    }

    public string Emoji
    {
        get => Attrs.GetValueOrDefault("emoji")?.StringValue ?? "💡";
        set => Attrs["emoji"] = JsonValue.Of(value);
    }

    /// <summary>Whether a toggle shows more than its first block.</summary>
    public bool IsOpen
    {
        get => Attrs.GetValueOrDefault("open")?.BoolValue ?? true;
        set => Attrs["open"] = JsonValue.Of(value);
    }

    /// <summary>A column's share of its row, as a weight. Equal weights are equal widths.</summary>
    public double Width
    {
        get => Attrs.GetValueOrDefault("width")?.DoubleValue is { } stored and > 0 ? stored : 1;
        set => Attrs["width"] = JsonValue.Of(value);
    }

    public string? ImageSource => Attrs.GetValueOrDefault("src")?.StringValue;

    // Copies and comparison: what a Swift struct does by itself.

    /// <summary>A copy with nothing shared: its children are copies too.</summary>
    public Block Clone() => new(Id, Kind, Text, Children.Select(child => child.Clone()), Attrs, ParagraphId)
    {
        StartsList = StartsList,
        ListAttrs = new Dictionary<string, JsonValue>(ListAttrs),
        Raw = Raw,
    };

    /// <summary>True when the two blocks hold the same things all the way down.</summary>
    public bool SameAs(Block other) =>
        Id == other.Id && Kind == other.Kind && Text == other.Text && ParagraphId == other.ParagraphId
        && StartsList == other.StartsList && Raw == other.Raw
        && Same(Attrs, other.Attrs) && Same(ListAttrs, other.ListAttrs)
        && Children.Count == other.Children.Count
        && Children.Zip(other.Children).All(pair => pair.First.SameAs(pair.Second));

    private static bool Same(Dictionary<string, JsonValue> a, Dictionary<string, JsonValue> b) =>
        a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var value) && value == entry.Value);
}

/// <summary>A note's content as the editor holds it: a tree of blocks.</summary>
internal sealed partial class EditorDocument
{
    public List<Block> Blocks { get; set; }

    public EditorDocument(IEnumerable<Block> blocks)
    {
        Blocks = blocks.ToList();
    }

    /// <summary>A copy with nothing shared, for a snapshot or a comparison.</summary>
    public EditorDocument Clone() => new(Blocks.Select(block => block.Clone()));

    public bool SameAs(EditorDocument other) =>
        Blocks.Count == other.Blocks.Count && Blocks.Zip(other.Blocks).All(pair => pair.First.SameAs(pair.Second));

    // Reading and writing the stored document

    private static readonly (string Type, BlockKind Kind)[] ListTypes =
    [
        ("bulletList", BlockKind.Bullet), ("orderedList", BlockKind.Numbered), ("taskList", BlockKind.Todo),
    ];

    private static readonly Dictionary<string, BlockKind> BlockTypes = new()
    {
        ["paragraph"] = BlockKind.Paragraph, ["heading"] = BlockKind.Heading, ["blockquote"] = BlockKind.Quote,
        ["calloutBlock"] = BlockKind.Callout, ["toggleBlock"] = BlockKind.Toggle, ["columnBlock"] = BlockKind.Columns,
        ["column"] = BlockKind.Column, ["horizontalRule"] = BlockKind.Divider, ["codeMirrorBlock"] = BlockKind.Code,
        ["image"] = BlockKind.Image,
    };

    /// <summary>
    /// Read a stored document. Null when the text is not one.
    ///
    /// Every block ends up with an id of its own: a block stored without one
    /// is given one, and so is the second block to carry the same id.
    /// </summary>
    public static EditorDocument? FromJson(string json, BlockIds ids)
    {
        if (JsonValue.Parse(json) is not { } root || root["type"]?.StringValue != "doc") return null;
        var reader = new Reader(ids);
        var blocks = reader.Blocks(root["content"]?.ArrayValue ?? []);
        if (blocks.Count == 0) blocks.Add(new Block(ids.Make(), BlockKind.Paragraph));
        return new EditorDocument(blocks);
    }

    /// <summary>The document to store.</summary>
    public string Json => JsonValue.ObjectOf(("type", JsonValue.Of("doc")), ("content", JsonValue.ArrayOf(Nodes(Blocks)))).Text;

    /// <summary>Blocks as the nodes of a stored document.</summary>
    internal static List<JsonValue> Nodes(IReadOnlyList<Block> blocks)
    {
        var nodes = new List<JsonValue>();
        var index = 0;
        while (index < blocks.Count)
        {
            var block = blocks[index];
            if (!block.IsListItem)
            {
                nodes.Add(Node(block));
                index++;
                continue;
            }
            // Items of one kind in a row are one list, until an item says it
            // begins another.
            var items = new List<JsonValue> { Item(block) };
            index++;
            while (index < blocks.Count && blocks[index].Kind == block.Kind && !blocks[index].StartsList)
            {
                items.Add(Item(blocks[index]));
                index++;
            }
            var list = new Dictionary<string, JsonValue>
            {
                ["type"] = JsonValue.Of(ListTypes.First(type => type.Kind == block.Kind).Type),
                ["content"] = JsonValue.ArrayOf(items),
            };
            if (block.ListAttrs.Count > 0) list["attrs"] = JsonValue.ObjectOf(block.ListAttrs);
            nodes.Add(JsonValue.ObjectOf(list));
        }
        return nodes;
    }

    private static JsonValue Item(Block block)
    {
        var paragraph = new Dictionary<string, JsonValue> { ["type"] = JsonValue.Of("paragraph") };
        if (block.ParagraphId is { } paragraphId) paragraph["attrs"] = JsonValue.ObjectOf(("blockId", JsonValue.Of(paragraphId)));
        var inline = block.Text.InlineJson;
        if (inline.Count > 0) paragraph["content"] = JsonValue.ArrayOf(inline);

        var attrs = new Dictionary<string, JsonValue>(block.Attrs) { ["blockId"] = JsonValue.Of(block.Id) };
        if (block.Kind == BlockKind.Todo && !attrs.ContainsKey("checked")) attrs["checked"] = JsonValue.Of(false);
        return JsonValue.ObjectOf(
            ("type", JsonValue.Of(block.Kind == BlockKind.Todo ? "taskItem" : "listItem")),
            ("attrs", JsonValue.ObjectOf(attrs)),
            ("content", JsonValue.ArrayOf(Nodes(block.Children).Prepend(JsonValue.ObjectOf(paragraph)))));
    }

    private static JsonValue Node(Block block)
    {
        var attrs = new Dictionary<string, JsonValue>(block.Attrs);
        var node = new Dictionary<string, JsonValue>();
        switch (block.Kind)
        {
            case BlockKind.Paragraph:
            case BlockKind.Heading:
                node["type"] = JsonValue.Of(block.Kind == BlockKind.Heading ? "heading" : "paragraph");
                attrs["blockId"] = JsonValue.Of(block.Id);
                var inline = block.Text.InlineJson;
                if (inline.Count > 0) node["content"] = JsonValue.ArrayOf(inline);
                break;
            case BlockKind.Code:
                node["type"] = JsonValue.Of("codeMirrorBlock");
                attrs["blockId"] = JsonValue.Of(block.Id);
                attrs["language"] = JsonValue.Of(block.Language);
                attrs["code"] = JsonValue.Of(block.Text.String);
                break;
            case BlockKind.Quote:
            case BlockKind.Callout:
            case BlockKind.Toggle:
                node["type"] = JsonValue.Of(block.Kind switch
                {
                    BlockKind.Quote => "blockquote",
                    BlockKind.Callout => "calloutBlock",
                    _ => "toggleBlock",
                });
                attrs["blockId"] = JsonValue.Of(block.Id);
                // These must hold at least one block to be a valid document.
                var inside = Nodes(block.Children);
                node["content"] = JsonValue.ArrayOf(inside.Count == 0 ? [JsonValue.ObjectOf(("type", JsonValue.Of("paragraph")))] : inside);
                break;
            case BlockKind.Columns:
                node["type"] = JsonValue.Of("columnBlock");
                node["content"] = JsonValue.ArrayOf(Nodes(block.Children));
                break;
            case BlockKind.Column:
                node["type"] = JsonValue.Of("column");
                node["content"] = JsonValue.ArrayOf(Nodes(block.Children));
                break;
            case BlockKind.Divider:
                node["type"] = JsonValue.Of("horizontalRule");
                attrs["blockId"] = JsonValue.Of(block.Id);
                break;
            case BlockKind.Image:
                node["type"] = JsonValue.Of("image");
                attrs["blockId"] = JsonValue.Of(block.Id);
                break;
            case BlockKind.Unknown:
                return block.Raw ?? JsonValue.ObjectOf(("type", JsonValue.Of("paragraph")));
            default:
                // List items are written by Nodes, which knows the list around them.
                return Item(block);
        }
        if (attrs.Count > 0) node["attrs"] = JsonValue.ObjectOf(attrs);
        return JsonValue.ObjectOf(node);
    }

    private sealed class Reader(BlockIds ids)
    {
        private readonly HashSet<string> seen = [];

        /// <summary>
        /// The id stored on a node, or a new one when it has none or when an
        /// earlier block already has it.
        /// </summary>
        private string Id(IReadOnlyDictionary<string, JsonValue>? attrs)
        {
            if (attrs?.GetValueOrDefault("blockId")?.StringValue is { Length: > 0 } stored && seen.Add(stored)) return stored;
            var made = ids.Make();
            while (!seen.Add(made)) made = ids.Make();
            return made;
        }

        private static Dictionary<string, JsonValue> Attrs(JsonValue node, params string[] without)
        {
            var attrs = new Dictionary<string, JsonValue>(node["attrs"]?.ObjectValue ?? new Dictionary<string, JsonValue>());
            foreach (var name in without) attrs.Remove(name);
            return attrs;
        }

        public List<Block> Blocks(IReadOnlyList<JsonValue> nodes)
        {
            var blocks = new List<Block>();
            foreach (var node in nodes)
            {
                var type = node["type"]?.StringValue ?? "";
                var list = ListTypes.FirstOrDefault(candidate => candidate.Type == type);
                if (list.Type is null)
                {
                    blocks.Add(Block(node, type));
                    continue;
                }
                var items = node["content"]?.ArrayValue ?? [];
                for (var index = 0; index < items.Count; index++)
                {
                    var block = Item(items[index], list.Kind);
                    if (index == 0)
                    {
                        block.ListAttrs = Attrs(node);
                        block.StartsList = blocks.Count > 0 && blocks[^1].Kind == list.Kind;
                    }
                    blocks.Add(block);
                }
            }
            return blocks;
        }

        private Block Item(JsonValue node, BlockKind kind)
        {
            var block = new Block(Id(node["attrs"]?.ObjectValue), kind, attrs: Attrs(node, "blockId"));
            var content = (node["content"]?.ArrayValue ?? []).ToList();
            // An item's text is its first paragraph. Whatever follows is nested in it.
            if (content.Count > 0 && content[0]["type"]?.StringValue == "paragraph")
            {
                block.Text = RichText.FromInline(content[0]["content"]?.ArrayValue ?? []);
                block.ParagraphId = Id(content[0]["attrs"]?.ObjectValue);
                content.RemoveAt(0);
            }
            else
            {
                block.ParagraphId = Id(null);
            }
            block.Children = Blocks(content);
            return block;
        }

        private Block Block(JsonValue node, string type)
        {
            var content = node["content"]?.ArrayValue ?? [];
            if (!BlockTypes.TryGetValue(type, out var kind))
            {
                return new Block(Id(node["attrs"]?.ObjectValue), BlockKind.Unknown) { Raw = node };
            }
            var block = new Block(Id(node["attrs"]?.ObjectValue), kind, attrs: Attrs(node, "blockId"));
            switch (kind)
            {
                case BlockKind.Paragraph:
                case BlockKind.Heading:
                    block.Text = RichText.FromInline(content);
                    break;
                case BlockKind.Code:
                    block.Text = new RichText(block.Attrs.GetValueOrDefault("code")?.StringValue ?? "");
                    block.Attrs.Remove("code");
                    break;
                case BlockKind.Quote:
                case BlockKind.Callout:
                case BlockKind.Toggle:
                case BlockKind.Columns:
                case BlockKind.Column:
                    block.Children = Blocks(content);
                    break;
            }
            return block;
        }
    }
}
