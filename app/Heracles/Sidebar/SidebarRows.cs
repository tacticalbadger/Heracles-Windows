using Heracles.Model;

namespace Heracles.Sidebar;

/// <summary>
/// One line of the sidebar. The key says where the line stands, so a note
/// listed twice (a favorite is also in its notebook) has two different keys.
/// </summary>
internal sealed record SidebarRow(
    string Key, string Title, Note? Note, int Depth, bool IsHeader, bool HasChildren = false, bool IsExpanded = false);

/// <summary>
/// The library as the lines of the sidebar, top to bottom: favorites, each
/// notebook with its tree, the archive, the trash. Worked out apart from the
/// view so it can be tested.
/// </summary>
internal static class SidebarRows
{
    public const string Favorites = "section:favorites";
    public const string Archive = "section:archive";
    public const string Trash = "section:trash";

    /// <param name="collapsed">The ids of notes whose children are folded away.</param>
    public static IReadOnlyList<SidebarRow> For(Library library, IReadOnlySet<string> collapsed)
    {
        var rows = new List<SidebarRow>();

        void Section(string key, string title, string prefix, IReadOnlyList<Note> notes)
        {
            // A section with nothing in it is left out.
            if (notes.Count == 0) return;
            rows.Add(new SidebarRow(key, title, null, 0, IsHeader: true));
            rows.AddRange(notes.Select(note => new SidebarRow(prefix + note.Id, note.DisplayTitle(), note, 1, IsHeader: false)));
        }

        void Tree(IReadOnlyList<LibraryNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                var open = node.Children.Count > 0 && !collapsed.Contains(node.Id);
                rows.Add(new SidebarRow("note:" + node.Id, node.Note.DisplayTitle(), node.Note, depth, false, node.Children.Count > 0, open));
                if (open) Tree(node.Children, depth + 1);
            }
        }

        Section(Favorites, "Favorites", "favorite:", library.Favorites);
        foreach (var notebook in library.Notebooks)
        {
            var open = notebook.Children.Count > 0 && !collapsed.Contains(notebook.Id);
            rows.Add(new SidebarRow("notebook:" + notebook.Id, notebook.Note.DisplayTitle(), notebook.Note, 0, true, notebook.Children.Count > 0, open));
            if (open) Tree(notebook.Children, 1);
        }
        Section(Archive, "Archive", "archived:", library.Archived);
        Section(Trash, "Trash", "trashed:", library.Trashed);
        return rows;
    }

    /// <summary>
    /// The line to highlight for a note: its place in the tree if it has one
    /// showing, else wherever else it is listed.
    /// </summary>
    public static SidebarRow? RowFor(IReadOnlyList<SidebarRow> rows, string? noteId)
    {
        if (noteId is null) return null;
        foreach (var prefix in new[] { "note:", "archived:", "trashed:", "favorite:" })
        {
            if (rows.FirstOrDefault(row => row.Key == prefix + noteId) is { } row) return row;
        }
        return null;
    }
}
