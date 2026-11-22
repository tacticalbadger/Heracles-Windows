using System.Text;
using System.Text.Json;

namespace Heracles.Notes;

/// <summary>
/// A note's content as plain text, for showing it before the editor exists
/// and wherever a note is only read. One line per block; what is not known
/// is left out.
/// </summary>
internal static class ReadOnlyDocument
{
    public static string PlainText(string documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson)) return "";
        try
        {
            using var document = JsonDocument.Parse(documentJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != "doc") return "";
            var lines = new List<string>();
            Blocks(Content(root), 0, lines);
            return string.Join("\n", lines);
        }
        catch (JsonException) { return ""; }
    }

    private static void Blocks(IEnumerable<JsonElement> nodes, int depth, List<string> lines)
    {
        foreach (var node in nodes)
        {
            if (node.ValueKind != JsonValueKind.Object) continue;
            switch (Text(node, "type"))
            {
                case "paragraph":
                case "heading":
                    lines.Add(Indent(depth) + Inline(node));
                    break;
                case "bulletList":
                case "orderedList":
                case "taskList":
                    Items(node, depth, lines);
                    break;
                case "codeMirrorBlock":
                    if (node.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object && Text(attrs, "code") is { } code)
                    {
                        lines.Add(code);
                    }
                    break;
                // Blocks that only hold other blocks.
                case "blockquote":
                case "calloutBlock":
                case "toggleBlock":
                case "columnBlock":
                case "column":
                    Blocks(Content(node), depth, lines);
                    break;
                case "horizontalRule":
                    lines.Add(Indent(depth) + "———");
                    break;
            }
        }
    }

    private static void Items(JsonElement list, int depth, List<string> lines)
    {
        var kind = Text(list, "type");
        var number = 1;
        if (list.TryGetProperty("attrs", out var listAttrs) && listAttrs.ValueKind == JsonValueKind.Object
            && listAttrs.TryGetProperty("start", out var start) && start.TryGetInt32(out var first))
        {
            number = first;
        }
        foreach (var item in Content(list))
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var inside = Content(item).ToList();
            var text = "";
            // An item's text is its first paragraph. Whatever follows is nested in it.
            if (inside.Count > 0 && inside[0].ValueKind == JsonValueKind.Object && Text(inside[0], "type") == "paragraph")
            {
                text = Inline(inside[0]);
                inside.RemoveAt(0);
            }
            var marker = kind switch
            {
                "orderedList" => $"{number}. ",
                "taskList" => IsChecked(item) ? "☑ " : "☐ ",
                _ => "• ",
            };
            lines.Add(Indent(depth) + marker + text);
            Blocks(inside, depth + 1, lines);
            number++;
        }
    }

    private static bool IsChecked(JsonElement item) =>
        item.TryGetProperty("attrs", out var attrs) && attrs.ValueKind == JsonValueKind.Object
        && attrs.TryGetProperty("checked", out var value) && value.ValueKind == JsonValueKind.True;

    private static string Inline(JsonElement block)
    {
        var text = new StringBuilder();
        foreach (var node in Content(block))
        {
            if (node.ValueKind != JsonValueKind.Object) continue;
            switch (Text(node, "type"))
            {
                case "text":
                    text.Append(Text(node, "text"));
                    break;
                case "hardBreak":
                    text.Append('\n');
                    break;
            }
        }
        return text.ToString();
    }

    private static string Indent(int depth) => new(' ', depth * 2);

    private static IEnumerable<JsonElement> Content(JsonElement node) =>
        node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray()
            : [];

    private static string? Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
