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
}
