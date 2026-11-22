using System.Text.RegularExpressions;

namespace Heracles.Model;

/// <summary>A note in the sidebar tree, with the notes beneath it.</summary>
internal sealed record LibraryNode(Note Note, IReadOnlyList<LibraryNode> Children)
{
    public string Id => Note.Id;
}

/// <summary>
/// The notes arranged the way the sidebar shows them. Built from the flat list
/// the core returns. It never changes: a new one is made when the notes do.
/// </summary>
internal sealed class Library
{
    private readonly Dictionary<string, Note> byId = [];

    /// <summary>
    /// The visible tree. A note in the trash or the archive is left out, along
    /// with everything under it.
    /// </summary>
    public IReadOnlyList<LibraryNode> Notebooks { get; }

    public IReadOnlyList<Note> Favorites { get; }

    public IReadOnlyList<Note> Archived { get; }

    /// <summary>
    /// The top of each trashed subtree. Restoring or deleting one of these
    /// takes what is under it along.
    /// </summary>
    public IReadOnlyList<Note> Trashed { get; }

    public Library() : this([]) { }

    public Library(IEnumerable<Note> notes)
    {
        var all = notes.ToList();
        foreach (var note in all) byId.TryAdd(note.Id, note);

        var visible = all.Where(note => !note.Trashed && !note.Archived).ToList();
        var children = visible
            .Where(note => note.ParentId != null)
            .GroupBy(note => note.ParentId!)
            .ToDictionary(group => group.Key, group => group.OrderBy(note => note, InOrder).ToList());

        // Built from the notebooks down, so a note that no notebook leads to
        // is simply never reached.
        LibraryNode Node(Note note) =>
            new(note, children.TryGetValue(note.Id, out var under) ? under.Select(Node).ToList() : []);

        Notebooks = visible
            .Where(note => note.Kind == NoteKind.Notebook && note.ParentId == null)
            .OrderBy(note => note, InOrder)
            .Select(Node)
            .ToList();
        Favorites = visible.Where(note => note.Favorited).OrderBy(note => note, ByTitle).ToList();
        Archived = all.Where(note => note.Archived && !note.Trashed).OrderBy(note => note, ByTitle).ToList();
        Trashed = all
            .Where(note => note.Trashed && !(note.ParentId != null && byId.TryGetValue(note.ParentId, out var parent) && parent.Trashed))
            .OrderByDescending(note => note.UpdatedAt, StringComparer.Ordinal)
            .ToList();
    }

    private static readonly Comparer<Note> ByTitle = Comparer<Note>.Create((a, b) =>
    {
        var titles = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
        return titles != 0 ? titles : string.CompareOrdinal(a.Id, b.Id);
    });

    private static readonly Comparer<Note> InOrder = Comparer<Note>.Create((a, b) =>
        a.Position != b.Position ? a.Position.CompareTo(b.Position) : ByTitle.Compare(a, b));

    public Note? Note(string id) => byId.GetValueOrDefault(id);

    /// <summary>The note and its ancestors, outermost first.</summary>
    public IReadOnlyList<Note> Path(string id)
    {
        var path = new List<Note>();
        var seen = new HashSet<string>();
        var next = Note(id);
        while (next != null && seen.Add(next.Id))
        {
            path.Add(next);
            next = next.ParentId is { } parent ? Note(parent) : null;
        }
        path.Reverse();
        return path;
    }

    public Note? Notebook(string containingId) =>
        Path(containingId).FirstOrDefault(note => note.Kind == NoteKind.Notebook);
}

internal static partial class NoteExtensions
{
    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex LineBreaks();

    /// <summary>A note's title as it is shown: on one line, and never empty.</summary>
    public static string DisplayTitle(this Note note)
    {
        var title = LineBreaks().Replace(note.Title, " ").Trim();
        return title.Length == 0 ? "Untitled" : title;
    }
}
