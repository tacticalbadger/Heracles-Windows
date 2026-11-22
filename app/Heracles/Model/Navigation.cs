namespace Heracles.Model;

/// <summary>What to do with a link the user clicked in a note.</summary>
internal enum NavigationDecision
{
    /// <summary>A link to the outside world. The system opens it.</summary>
    OpenExternally,

    /// <summary>A link to a note. The app opens it.</summary>
    OpenInApp,

    Deny,
}

/// <summary>
/// Which links the app follows. A note can hold any address at all, typed or
/// imported, so only the kinds known to be safe to hand on are opened.
/// </summary>
internal static class NavigationPolicy
{
    public static NavigationDecision Decide(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var url) ? Decide(url) : NavigationDecision.Deny;

    public static NavigationDecision Decide(Uri? url)
    {
        if (url is null || !url.IsAbsoluteUri) return NavigationDecision.Deny;
        return url.Scheme.ToLowerInvariant() switch
        {
            "http" or "https" or "mailto" => NavigationDecision.OpenExternally,
            AppLink.Scheme => AppLink.Parse(url) is null ? NavigationDecision.Deny : NavigationDecision.OpenInApp,
            _ => NavigationDecision.Deny,
        };
    }
}

/// <summary>A <c>heracles://note/&lt;id&gt;</c> link, optionally to one block of the note.</summary>
internal sealed record AppLink(string NoteId, string? BlockId)
{
    public const string Scheme = "heracles";
    private const string BlockPrefix = "block=";

    public static AppLink? Parse(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var url) ? Parse(url) : null;

    public static AppLink? Parse(Uri? url)
    {
        if (url is null || !url.IsAbsoluteUri) return null;
        if (!string.Equals(url.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.Equals(url.Host, "note", StringComparison.OrdinalIgnoreCase)) return null;

        // Exactly one path segment: the note's id.
        var path = url.AbsolutePath;
        if (path.Length < 2 || path[0] != '/') return null;
        var id = path[1..];
        if (id.Contains('/')) return null;

        string? block = null;
        var fragment = url.Fragment.TrimStart('#');
        if (fragment.StartsWith(BlockPrefix, StringComparison.Ordinal) && fragment.Length > BlockPrefix.Length)
        {
            block = Uri.UnescapeDataString(fragment[BlockPrefix.Length..]);
        }
        return new AppLink(Uri.UnescapeDataString(id), block);
    }

    public string Url =>
        $"{Scheme}://note/{Uri.EscapeDataString(NoteId)}"
        + (string.IsNullOrEmpty(BlockId) ? "" : "#" + BlockPrefix + Uri.EscapeDataString(BlockId));
}
