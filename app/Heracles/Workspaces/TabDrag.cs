namespace Heracles.Workspaces;

/// <summary>A tab being dragged, carried as text so it can cross between panes.</summary>
internal sealed record TabDrag(string LeafId, string NoteId)
{
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
}
