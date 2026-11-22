using System.Collections.Immutable;

namespace Heracles.Editor;

/// <summary>A link on a run of text.</summary>
internal sealed record TextLink
{
    public string Href { get; init; }

    /// <summary>The link's other attributes, kept as they were stored. Always an object.</summary>
    public JsonValue Extra { get; init; }

    public TextLink(string href, JsonValue? extra = null)
    {
        Href = href;
        Extra = extra ?? JsonValue.EmptyObject;
    }
}

/// <summary>The marks a character can carry.</summary>
internal enum TextMark
{
    Bold,
    Italic,
    Strike,
    Code,
}

/// <summary>How a run of text is marked.</summary>
internal sealed record TextStyle
{
    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Strike { get; init; }

    public bool Code { get; init; }

    public TextLink? Link { get; init; }

    /// <summary>Marks this editor does not know, kept as they were stored. Always an array.</summary>
    public JsonValue Other { get; init; }

    public static TextStyle Plain { get; } = new();

    public TextStyle(bool Bold = false, bool Italic = false, bool Strike = false, bool Code = false, TextLink? Link = null, JsonValue? Other = null)
    {
        this.Bold = Bold;
        this.Italic = Italic;
        this.Strike = Strike;
        this.Code = Code;
        this.Link = Link;
        this.Other = Other ?? JsonValue.EmptyArray;
    }

    public bool Has(TextMark mark) => mark switch
    {
        TextMark.Bold => Bold,
        TextMark.Italic => Italic,
        TextMark.Strike => Strike,
        _ => Code,
    };

    public TextStyle With(TextMark mark, bool on) => mark switch
    {
        TextMark.Bold => this with { Bold = on },
        TextMark.Italic => this with { Italic = on },
        TextMark.Strike => this with { Strike = on },
        _ => this with { Code = on },
    };

    /// <summary>Read the marks of a text node.</summary>
    public static TextStyle FromMarks(IEnumerable<JsonValue> marks)
    {
        var style = Plain;
        var other = new List<JsonValue>();
        foreach (var mark in marks)
        {
            switch (mark["type"]?.StringValue)
            {
                case "bold":
                    style = style with { Bold = true };
                    break;
                case "italic":
                    style = style with { Italic = true };
                    break;
                case "strike":
                    style = style with { Strike = true };
                    break;
                case "code":
                    style = style with { Code = true };
                    break;
                case "link":
                    var attrs = mark["attrs"]?.ObjectValue ?? ImmutableDictionary<string, JsonValue>.Empty;
                    style = style with
                    {
                        Link = new TextLink(
                            attrs.GetValueOrDefault("href")?.StringValue ?? "",
                            JsonValue.ObjectOf(attrs.Where(attr => attr.Key != "href"))),
                    };
                    break;
                default:
                    other.Add(mark);
                    break;
            }
        }
        return other.Count == 0 ? style : style with { Other = JsonValue.ArrayOf(other) };
    }

    /// <summary>The marks to store, in the order the core writes them.</summary>
    public IReadOnlyList<JsonValue> Marks
    {
        get
        {
            static JsonValue Mark(string type) => JsonValue.ObjectOf(("type", JsonValue.Of(type)));

            var marks = new List<JsonValue>();
            if (Link is not null)
            {
                var attrs = (Link.Extra.ObjectValue ?? ImmutableDictionary<string, JsonValue>.Empty)
                    .Where(attr => attr.Key != "href")
                    .Append(new KeyValuePair<string, JsonValue>("href", JsonValue.Of(Link.Href)));
                marks.Add(JsonValue.ObjectOf(("type", JsonValue.Of("link")), ("attrs", JsonValue.ObjectOf(attrs))));
            }
            if (Bold) marks.Add(Mark("bold"));
            if (Code) marks.Add(Mark("code"));
            if (Italic) marks.Add(Mark("italic"));
            if (Strike) marks.Add(Mark("strike"));
            marks.AddRange(Other.ArrayValue ?? []);
            return marks;
        }
    }
}

/// <summary>
/// The text of one block: characters and the marks on them.
///
/// Offsets and lengths are in UTF-16 units, the way text boxes count, so a
/// selection can be handed over without converting it. A line break inside a
/// block is the character <c>\n</c>. A text never changes: every edit gives
/// a new one.
/// </summary>
internal sealed class RichText : IEquatable<RichText>
{
    /// <summary>
    /// A run of text. <see cref="Atom"/> is an inline node this editor does
    /// not know, kept as stored: it takes one place in the text, shown as an
    /// object character.
    /// </summary>
    public sealed record Span(string Text, TextStyle Style, JsonValue? Atom = null)
    {
        public int Length => Text.Length;
    }

    /// <summary>The character that stands in for an inline node this editor does not know.</summary>
    public const string AtomCharacter = "￼";

    private readonly ImmutableArray<Span> spans;

    /// <summary>Runs of text. No run is empty, and no two neighbours share a style.</summary>
    public IReadOnlyList<Span> Spans => spans;

    public RichText()
    {
        spans = [];
    }

    public RichText(string text, TextStyle? style = null)
    {
        spans = text.Length == 0 ? [] : [new Span(text, style ?? TextStyle.Plain)];
    }

    /// <summary>From runs in any state: empty ones are dropped and neighbours alike are joined.</summary>
    private RichText(IEnumerable<Span> runs)
    {
        var joined = ImmutableArray.CreateBuilder<Span>();
        foreach (var span in runs)
        {
            if (span.Text.Length == 0) continue;
            if (span.Atom is null && joined.Count > 0 && joined[^1] is { Atom: null } last && last.Style == span.Style)
            {
                joined[^1] = last with { Text = last.Text + span.Text };
            }
            else
            {
                joined.Add(span);
            }
        }
        spans = joined.ToImmutable();
    }

    public bool IsEmpty => spans.IsEmpty;

    public int Length => spans.Sum(span => span.Length);

    public string String => string.Concat(spans.Select(span => span.Text));

    public static RichText operator +(RichText left, RichText right) => new(left.spans.Concat(right.spans));

    public bool Equals(RichText? other) => other is not null && spans.SequenceEqual(other.spans);

    public override bool Equals(object? obj) => Equals(obj as RichText);

    public override int GetHashCode() => spans.Aggregate(23, (hash, span) => HashCode.Combine(hash, span));

    public static bool operator ==(RichText? left, RichText? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(RichText? left, RichText? right) => !(left == right);

    // Ranges

    private (int Start, int End) Clamped(int start, int end)
    {
        var length = Length;
        var lower = Math.Min(Math.Max(start, 0), length);
        return (lower, Math.Min(Math.Max(end, lower), length));
    }

    /// <summary>The text from <paramref name="start"/> up to <paramref name="end"/>, with its marks.</summary>
    public RichText Subtext(int start, int end)
    {
        (start, end) = Clamped(start, end);
        var piece = new List<Span>();
        var offset = 0;
        foreach (var span in spans)
        {
            var spanEnd = offset + span.Length;
            var lower = Math.Max(start, offset);
            var upper = Math.Min(end, spanEnd);
            if (lower < upper) piece.Add(span with { Text = span.Text.Substring(lower - offset, upper - lower) });
            offset = spanEnd;
        }
        return new RichText(piece);
    }

    public RichText Replace(int start, int end, RichText replacement)
    {
        (start, end) = Clamped(start, end);
        return Subtext(0, start) + replacement + Subtext(end, Length);
    }

    /// <summary>Rewrite the style of every character in a range.</summary>
    private RichText Restyle(int start, int end, Func<TextStyle, TextStyle> change)
    {
        (start, end) = Clamped(start, end);
        var middle = Subtext(start, end).spans.Select(span => span.Atom is null ? span with { Style = change(span.Style) } : span);
        // The styles changed, so neighbours may now match: joined again here.
        return Replace(start, end, new RichText(middle));
    }

    // Marks

    /// <summary>True when the range is not empty and every character in it has the mark.</summary>
    public bool Has(TextMark mark, int start, int end)
    {
        var piece = Subtext(start, end);
        return !piece.IsEmpty && piece.spans.All(span => span.Style.Has(mark));
    }

    /// <summary>
    /// Turn a mark on or off over a range. Code stands alone: turning it on
    /// takes the other marks away, and nothing else can be added to code.
    /// </summary>
    public RichText Set(TextMark mark, bool on, int start, int end) => Restyle(start, end, style =>
    {
        if (mark == TextMark.Code && on) return new TextStyle(Code: true, Other: style.Other);
        return mark == TextMark.Code || !style.Code ? style.With(mark, on) : style;
    });

    public RichText SetLink(TextLink? link, int start, int end) =>
        Restyle(start, end, style => style.Code ? style : style with { Link = link });

    /// <summary>The link the caret is in or at the end of, and how far it reaches.</summary>
    public (int Start, int End, TextLink Link)? LinkAt(int offset)
    {
        var start = 0;
        var index = 0;
        while (index < spans.Length)
        {
            if (spans[index].Style.Link is not { } link)
            {
                start += spans[index].Length;
                index++;
                continue;
            }
            // A link can run over several spans when part of it is bold.
            var end = start;
            while (index < spans.Length && spans[index].Style.Link == link)
            {
                end += spans[index].Length;
                index++;
            }
            if (offset > start && offset <= end) return (start, end, link);
            start = end;
        }
        return null;
    }

    /// <summary>
    /// The style that text typed at <paramref name="offset"/> takes: that of
    /// the character before it, or of the first character at the very start.
    /// A link is only continued from inside it, so typing after a link is not
    /// linked.
    /// </summary>
    public TextStyle StyleBefore(int offset)
    {
        if (spans.IsEmpty) return TextStyle.Plain;
        var style = spans[0].Style;
        var end = 0;
        foreach (var span in spans)
        {
            end += span.Length;
            style = span.Atom is null ? span.Style : TextStyle.Plain;
            if (offset <= end) break;
        }
        if (style.Link is { } link)
        {
            var at = LinkAt(offset);
            if (at is null || at.Value.Link != link || at.Value.End == offset) style = style with { Link = null };
        }
        return style;
    }

    // The document's inline content

    /// <summary>Read the inline content of a paragraph or heading.</summary>
    public static RichText FromInline(IEnumerable<JsonValue> nodes)
    {
        var read = new List<Span>();
        foreach (var node in nodes)
        {
            switch (node["type"]?.StringValue)
            {
                case "text":
                    read.Add(new Span(node["text"]?.StringValue ?? "", TextStyle.FromMarks(node["marks"]?.ArrayValue ?? [])));
                    break;
                case "hardBreak":
                    read.Add(new Span("\n", TextStyle.Plain));
                    break;
                default:
                    read.Add(new Span(AtomCharacter, TextStyle.Plain, node));
                    break;
            }
        }
        return new RichText(read);
    }

    /// <summary>
    /// The inline content to store: runs of text, with a node of its own for
    /// each line break.
    /// </summary>
    public IReadOnlyList<JsonValue> InlineJson
    {
        get
        {
            var nodes = new List<JsonValue>();
            foreach (var span in spans)
            {
                if (span.Atom is not null)
                {
                    nodes.Add(span.Atom);
                    continue;
                }
                var marks = span.Style.Marks;
                var lines = span.Text.Split('\n');
                for (var index = 0; index < lines.Length; index++)
                {
                    if (index > 0) nodes.Add(JsonValue.ObjectOf(("type", JsonValue.Of("hardBreak"))));
                    if (lines[index].Length == 0) continue;
                    var node = new List<(string, JsonValue)> { ("type", JsonValue.Of("text")), ("text", JsonValue.Of(lines[index])) };
                    if (marks.Count > 0) node.Add(("marks", JsonValue.ArrayOf(marks)));
                    nodes.Add(JsonValue.ObjectOf([.. node]));
                }
            }
            return nodes;
        }
    }
}
