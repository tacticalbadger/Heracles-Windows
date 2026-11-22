using Heracles.Editor;

namespace Heracles.Tests.Editor;

/// <summary>
/// Ways to print the editor's values so an expectation can be written as a
/// line or two of text.
/// </summary>
internal static class EditorTestSupport
{
    /// <summary>
    /// Rich text as a string: each span in brackets with its marks.
    /// <c>[b:Hello][ world]</c>.
    /// </summary>
    public static string Shape(RichText text) => string.Concat(text.Spans.Select(span =>
    {
        var marks = "";
        if (span.Style.Bold) marks += "b";
        if (span.Style.Italic) marks += "i";
        if (span.Style.Strike) marks += "s";
        if (span.Style.Code) marks += "c";
        if (span.Style.Link is { } link) marks += $"<{link.Href}>";
        if (span.Atom is not null) marks += "@";
        return marks.Length == 0 ? $"[{span.Text}]" : $"[{marks}:{span.Text}]";
    }));

    /// <summary>The items of a JSON array given as text.</summary>
    public static IReadOnlyList<JsonValue> Items(string json) => JsonValue.Parse(json)!.ArrayValue!;

    public static bool SameJson(string a, string b) =>
        JsonValue.Parse(a) is { } first && JsonValue.Parse(b) is { } second && first == second;

    // Short ways to build blocks in tests.

    public static Block P(string id, string text = "") => new(id, BlockKind.Paragraph, new RichText(text));

    public static Block H(int level, string id, string text = "") =>
        new(id, BlockKind.Heading, new RichText(text)) { Level = level };

    public static Block Bullet(string id, string text = "", params Block[] children) =>
        new(id, BlockKind.Bullet, new RichText(text), children, paragraphId: id + "-p");

    public static Block Numbered(string id, string text = "", params Block[] children) =>
        new(id, BlockKind.Numbered, new RichText(text), children, paragraphId: id + "-p");

    public static Block Todo(string id, string text = "", bool isChecked = false, params Block[] children) =>
        new(id, BlockKind.Todo, new RichText(text), children, paragraphId: id + "-p") { IsChecked = isChecked };

    public static Block Quote(string id, params Block[] children) => new(id, BlockKind.Quote, children: children);

    public static Block Callout(string id, params Block[] children) => new(id, BlockKind.Callout, children: children);

    public static Block Toggle(string id, bool open, params Block[] children) =>
        new(id, BlockKind.Toggle, children: children) { IsOpen = open };

    public static Block Toggle(string id, params Block[] children) => Toggle(id, true, children);

    public static Block Columns(string id, params Block[] columns) => new(id, BlockKind.Columns, children: columns);

    public static Block Column(string id, params Block[] children) => new(id, BlockKind.Column, children: children);

    public static Block Divider(string id) => new(id, BlockKind.Divider);

    public static Block Code(string id, string text = "", string language = "plaintext") =>
        new(id, BlockKind.Code, new RichText(text)) { Language = language };

    public static Block Image(string id, string src = "heracles-image://local/abc")
    {
        var block = new Block(id, BlockKind.Image);
        block.Attrs["src"] = JsonValue.Of(src);
        return block;
    }

    public static EditorDocument Doc(params Block[] blocks) => new(blocks);

    /// <summary>
    /// A document as an outline: one line per block, children indented.
    /// <c>bullet#b One</c> is a bullet with the id <c>b</c> and the text "One".
    /// </summary>
    public static string Outline(EditorDocument document)
    {
        var lines = new List<string>();

        void Walk(IEnumerable<Block> blocks, int depth)
        {
            foreach (var block in blocks)
            {
                var name = block.Kind switch
                {
                    BlockKind.Paragraph => "p",
                    BlockKind.Heading => $"h{block.Level}",
                    BlockKind.Bullet => "bullet",
                    BlockKind.Numbered => "numbered",
                    BlockKind.Todo => block.IsChecked ? "done" : "todo",
                    BlockKind.Code => "code",
                    BlockKind.Quote => "quote",
                    BlockKind.Callout => "callout",
                    BlockKind.Toggle => block.IsOpen ? "toggle" : "toggle(closed)",
                    BlockKind.Columns => "columns",
                    BlockKind.Column => "column",
                    BlockKind.Divider => "divider",
                    BlockKind.Image => "image",
                    _ => "unknown",
                };
                if (block.StartsList) name = "^" + name;
                var text = block.Text.String.Replace("\n", "⏎");
                lines.Add(new string(' ', depth * 2) + $"{name}#{block.Id}" + (text.Length == 0 ? "" : " " + text));
                Walk(block.Children, depth + 1);
            }
        }

        Walk(document.Blocks, 0);
        return string.Join("\n", lines);
    }

    /// <summary>An outline written in a test, with its indentation and line endings made plain.</summary>
    public static string Lines(string text) => text.ReplaceLineEndings("\n");

    /// <summary>A selection as text: <c>a|3</c> is a caret, <c>a|1-4</c> a range, <c>[a b]</c> blocks.</summary>
    public static string Shape(EditorSelection selection) => selection switch
    {
        EditorSelection.InText text => text.Start == text.End ? $"{text.Block}|{text.Start}" : $"{text.Block}|{text.Start}-{text.End}",
        EditorSelection.OfBlocks blocks => "[" + string.Join(" ", blocks.Ids) + "]",
        _ => "none",
    };
}
