using System.Text;
using System.Text.RegularExpressions;

namespace Heracles.Updates;

internal enum NoteLineKind
{
    Paragraph,
    Point,
    Heading,
}

/// <summary>A stretch of a line of release notes, bold or as code or neither.</summary>
internal readonly record struct NoteRun(string Text, bool Bold, bool Code);

/// <summary>One line of release notes: a paragraph, a point of a list, or a heading.</summary>
internal sealed record NoteLine(NoteLineKind Kind, IReadOnlyList<NoteRun> Runs)
{
    public string Text => string.Concat(Runs.Select(run => run.Text));
}

/// <summary>
/// Release notes, read for showing. They are written as Markdown, and the
/// update window shows them as text of its own: there is no web view in the
/// app. Only what the notes use is understood (points, paragraphs,
/// headings, bold, code, and a link shown as its words); anything else is
/// shown as it was typed. Notes come from the feed, so nothing in them is
/// ever more than words on the screen.
/// </summary>
internal static partial class UpdateNotes
{
    [GeneratedRegex(@"^\s*[-*]\s+(?<text>.*)$")]
    private static partial Regex PointForm();

    [GeneratedRegex(@"^\s*#{1,6}\s+(?<text>.*)$")]
    private static partial Regex HeadingForm();

    [GeneratedRegex(@"\[(?<words>[^\]]+)\]\((?<address>[^)\s]+)\)")]
    private static partial Regex LinkForm();

    [GeneratedRegex(@"\*\*(?<bold>[^*]+)\*\*|`(?<code>[^`]+)`")]
    private static partial Regex MarkForm();

    public static IReadOnlyList<NoteLine> Read(string markdown)
    {
        var lines = new List<NoteLine>();
        NoteLineKind? kind = null;
        var words = new StringBuilder();

        void Finish()
        {
            if (kind is { } finished && words.Length > 0) lines.Add(new NoteLine(finished, Runs(words.ToString())));
            kind = null;
            words.Clear();
        }

        foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                Finish();
            }
            else if (HeadingForm().Match(line) is { Success: true } heading)
            {
                Finish();
                lines.Add(new NoteLine(NoteLineKind.Heading, Runs(heading.Groups["text"].Value.Trim())));
            }
            else if (PointForm().Match(line) is { Success: true } point)
            {
                Finish();
                kind = NoteLineKind.Point;
                words.Append(point.Groups["text"].Value.Trim());
            }
            else
            {
                // More of what came before, or a paragraph beginning.
                kind ??= NoteLineKind.Paragraph;
                if (words.Length > 0) words.Append(' ');
                words.Append(line.Trim());
            }
        }
        Finish();
        return lines;
    }

    private static List<NoteRun> Runs(string text)
    {
        text = LinkForm().Replace(text, match => match.Groups["words"].Value);
        var runs = new List<NoteRun>();
        var from = 0;
        foreach (Match mark in MarkForm().Matches(text))
        {
            if (mark.Index > from) runs.Add(new NoteRun(text[from..mark.Index], false, false));
            runs.Add(mark.Groups["bold"].Success
                ? new NoteRun(mark.Groups["bold"].Value, Bold: true, Code: false)
                : new NoteRun(mark.Groups["code"].Value, Bold: false, Code: true));
            from = mark.Index + mark.Length;
        }
        if (from < text.Length) runs.Add(new NoteRun(text[from..], false, false));
        return runs;
    }
}
