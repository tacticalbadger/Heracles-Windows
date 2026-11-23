namespace Heracles.Workspaces;

/// <summary>
/// A tab being dragged, within a pane or to another. It is carried under a
/// name of its own, not as text: a text box takes any text dropped on it
/// for itself, and a tab let go over a note's title would type its own name
/// into the title.
/// </summary>
internal sealed record TabDrag(string LeafId, string NoteId)
{
    /// <summary>The name the dragged tab goes under.</summary>
    public const string Format = "Heracles.Tab";

    private const string Prefix = "heracles-tab:";

    public string Text => $"{Prefix}{LeafId}|{NoteId}";

    public static TabDrag? Parse(string? text)
    {
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var rest = text[Prefix.Length..];
        var bar = rest.IndexOf('|');
        if (bar <= 0 || bar == rest.Length - 1) return null;
        return new TabDrag(rest[..bar], rest[(bar + 1)..]);
    }

    /// <summary>What is handed to the system when the drag begins.</summary>
    public IDataObject Data() => new DataObject(Format, Text);

    /// <summary>The tab in what was dragged here, or null when it is something else.</summary>
    public static TabDrag? From(IDataObject data) => data.GetDataPresent(Format) ? Parse(data.GetData(Format) as string) : null;
}
