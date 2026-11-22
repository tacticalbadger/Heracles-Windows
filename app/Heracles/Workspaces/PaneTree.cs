using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Heracles.Workspaces;

/// <summary>How a split lays out its panes: side by side, or one above another.</summary>
internal enum SplitAxis
{
    Horizontal,
    Vertical,
}

/// <summary>Which side of a pane a new pane appears on.</summary>
internal enum SplitTarget
{
    Right,
    Down,
    Left,
    Up,
}

internal abstract record PaneNode(string Id);

/// <summary>One pane: an ordered row of tabs, each a note, with one showing.</summary>
internal sealed record PaneLeaf(string Id, ImmutableArray<string> Tabs, string? ActiveTab) : PaneNode(Id)
{
    public static PaneLeaf Create(string? noteId = null) =>
        new(Guid.NewGuid().ToString(), noteId is null ? [] : [noteId], noteId);

    /// <summary>
    /// Remove a tab. If it was showing, the tab that slides into its place
    /// shows next, or the one before it at the end of the row.
    /// </summary>
    public PaneLeaf Close(string noteId)
    {
        var index = Tabs.IndexOf(noteId);
        if (index < 0) return this;
        var tabs = Tabs.RemoveAt(index);
        var active = ActiveTab != noteId ? ActiveTab : index < tabs.Length ? tabs[index] : tabs.LastOrDefault();
        return this with { Tabs = tabs, ActiveTab = active };
    }
}

/// <summary>Panes sharing a space, each with a share of it. The shares add up to 1.</summary>
internal sealed record PaneSplit(string Id, SplitAxis Axis, ImmutableArray<PaneNode> Children, ImmutableArray<double> Ratios)
    : PaneNode(Id);

/// <summary>
/// One window's panes. A value: every change makes a new tree, which is what
/// lets a window save its layout and compare it.
/// </summary>
internal sealed class PaneTree
{
    public PaneNode Root { get; }

    /// <summary>The pane the next note opens in.</summary>
    public string FocusedLeafId { get; }

    public IReadOnlyList<PaneLeaf> Leaves { get; }

    public PaneTree() : this(PaneLeaf.Create(), "") { }

    /// <summary>A tree is never without a focused pane: one that is missing is replaced by the first.</summary>
    private PaneTree(PaneNode root, string focused)
    {
        Root = root;
        Leaves = LeavesOf(root).ToImmutableArray();
        FocusedLeafId = Leaves.Any(leaf => leaf.Id == focused) ? focused : Leaves[0].Id;
    }

    public PaneLeaf? Leaf(string id) => Leaves.FirstOrDefault(leaf => leaf.Id == id);

    /// <summary>The note showing in the focused pane.</summary>
    public string? ActiveNoteId => Leaf(FocusedLeafId)?.ActiveTab;

    private static IEnumerable<PaneLeaf> LeavesOf(PaneNode node) => node switch
    {
        PaneLeaf leaf => [leaf],
        PaneSplit split => split.Children.SelectMany(LeavesOf),
        _ => [],
    };

    // Changes. Each returns a new tree and leaves this one as it was.

    public PaneTree Focus(string leafId) => Leaf(leafId) is null ? this : new PaneTree(Root, leafId);

    /// <summary>Show a note in a pane, adding a tab for it if it has none there.</summary>
    public PaneTree Open(string noteId, string? leafId = null)
    {
        var target = leafId is not null && Leaf(leafId) is not null ? leafId : FocusedLeafId;
        var root = Replacing(target, Root, leaf => leaf with
        {
            Tabs = leaf.Tabs.Contains(noteId) ? leaf.Tabs : leaf.Tabs.Add(noteId),
            ActiveTab = noteId,
        });
        return new PaneTree(root, target);
    }

    public PaneTree Activate(string noteId, string leafId)
    {
        if (Leaf(leafId)?.Tabs.Contains(noteId) != true) return this;
        return new PaneTree(Replacing(leafId, Root, leaf => leaf with { ActiveTab = noteId }), leafId);
    }

    /// <summary>Close a tab. A pane left with no tabs closes too, unless it is the only one.</summary>
    public PaneTree CloseTab(string noteId, string leafId)
    {
        var closed = Update(leafId, leaf => leaf.Close(noteId));
        return closed.Leaf(leafId)?.Tabs.IsEmpty == true && closed.Leaves.Count > 1 ? closed.CloseLeaf(leafId) : closed;
    }

    /// <summary>Put a new pane beside an existing one, and focus it.</summary>
    public PaneTree Split(string leafId, SplitTarget target, string? noteId, out string newLeafId)
    {
        var added = PaneLeaf.Create(noteId);
        var axis = target is SplitTarget.Right or SplitTarget.Left ? SplitAxis.Horizontal : SplitAxis.Vertical;
        var comesAfter = target is SplitTarget.Right or SplitTarget.Down;
        var root = Replacing(leafId, Root, existing => new PaneSplit(
            Guid.NewGuid().ToString(), axis,
            comesAfter ? [existing, added] : [added, existing],
            [0.5, 0.5]));
        newLeafId = added.Id;
        // If the pane to split was not there, nothing was added and the focus stays.
        return new PaneTree(root, LeavesOf(root).Any(leaf => leaf.Id == added.Id) ? added.Id : FocusedLeafId);
    }

    /// <summary>
    /// Close a pane. The others take up its space. The last pane is emptied in
    /// place, since a window always has one.
    /// </summary>
    public PaneTree CloseLeaf(string leafId) =>
        new(Pruning(Root, leaf => leaf.Id == leafId ? null : leaf) ?? PaneLeaf.Create(), FocusedLeafId);

    public PaneTree SetRatios(string splitId, IReadOnlyList<double> ratios)
    {
        PaneNode Apply(PaneNode node)
        {
            if (node is not PaneSplit split) return node;
            return split.Id == splitId && split.Ratios.Length == ratios.Count
                ? split with { Ratios = [.. ratios] }
                : split with { Children = [.. split.Children.Select(Apply)] };
        }
        return new PaneTree(Apply(Root), FocusedLeafId);
    }

    /// <summary>Take notes that no longer exist out of every pane. A pane this empties is closed.</summary>
    public PaneTree Remove(IReadOnlySet<string> notes)
    {
        var root = Pruning(Root, leaf =>
        {
            var kept = leaf;
            foreach (var note in leaf.Tabs.Where(notes.Contains)) kept = kept.Close(note);
            return kept.Tabs.IsEmpty && !leaf.Tabs.IsEmpty ? null : kept;
        });
        return new PaneTree(root ?? PaneLeaf.Create(), FocusedLeafId);
    }

    /// <summary>
    /// Keep only tabs for notes in <paramref name="notes"/>. For a layout
    /// restored from an earlier session, whose notes may have gone since.
    /// </summary>
    public PaneTree Keep(IReadOnlySet<string> notes) =>
        Remove(Leaves.SelectMany(leaf => leaf.Tabs).Where(tab => !notes.Contains(tab)).ToHashSet());

    /// <summary>Move a tab to a place in its own pane or another. Out-of-range places mean the end.</summary>
    public PaneTree MoveTab(string noteId, string sourceId, string destinationId, int index)
    {
        if (Leaf(sourceId)?.Tabs.Contains(noteId) != true || Leaf(destinationId) is null) return this;
        var moved = sourceId == destinationId ? this : Update(sourceId, leaf => leaf.Close(noteId));
        moved = moved.Update(destinationId, leaf =>
        {
            var tabs = leaf.Tabs.Remove(noteId);
            return leaf with { Tabs = tabs.Insert(index >= 0 && index <= tabs.Length ? index : tabs.Length, noteId), ActiveTab = noteId };
        });
        if (sourceId != destinationId && moved.Leaf(sourceId)?.Tabs.IsEmpty == true) moved = moved.CloseLeaf(sourceId);
        return moved.Focus(destinationId);
    }

    // Helpers

    private PaneTree Update(string leafId, Func<PaneLeaf, PaneLeaf> change) =>
        new(Replacing(leafId, Root, change), FocusedLeafId);

    private static PaneNode Replacing(string leafId, PaneNode node, Func<PaneLeaf, PaneNode> make) => node switch
    {
        PaneLeaf leaf => leaf.Id == leafId ? make(leaf) : leaf,
        PaneSplit split => split with { Children = [.. split.Children.Select(child => Replacing(leafId, child, make))] },
        _ => node,
    };

    /// <summary>
    /// Rebuild the tree, letting <paramref name="keep"/> change or drop each
    /// leaf. A split left with one child is replaced by it, and one left with
    /// none goes.
    /// </summary>
    private static PaneNode? Pruning(PaneNode node, Func<PaneLeaf, PaneLeaf?> keep)
    {
        if (node is PaneLeaf leaf) return keep(leaf);
        if (node is not PaneSplit split) return null;

        var children = new List<PaneNode>();
        var ratios = new List<double>();
        for (var index = 0; index < split.Children.Length; index++)
        {
            if (Pruning(split.Children[index], keep) is not { } kept) continue;
            children.Add(kept);
            ratios.Add(index < split.Ratios.Length ? split.Ratios[index] : 1.0 / split.Children.Length);
        }
        if (children.Count <= 1) return children.FirstOrDefault();
        var total = ratios.Sum();
        return split with
        {
            Children = [.. children],
            Ratios = [.. ratios.Select(ratio => total > 0 ? ratio / total : 1.0 / children.Count)],
        };
    }

    // Saving and restoring

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("focused", FocusedLeafId);
            writer.WritePropertyName("root");
            Write(writer, Root);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, PaneNode node)
    {
        writer.WriteStartObject();
        if (node is PaneLeaf leaf)
        {
            writer.WriteStartObject("leaf");
            writer.WriteString("id", leaf.Id);
            writer.WriteStartArray("tabs");
            foreach (var tab in leaf.Tabs) writer.WriteStringValue(tab);
            writer.WriteEndArray();
            if (leaf.ActiveTab is null) writer.WriteNull("active");
            else writer.WriteString("active", leaf.ActiveTab);
            writer.WriteEndObject();
        }
        else if (node is PaneSplit split)
        {
            writer.WriteStartObject("split");
            writer.WriteString("id", split.Id);
            writer.WriteString("axis", split.Axis == SplitAxis.Horizontal ? "h" : "v");
            writer.WriteStartArray("children");
            foreach (var child in split.Children) Write(writer, child);
            writer.WriteEndArray();
            writer.WriteStartArray("ratios");
            foreach (var ratio in split.Ratios) writer.WriteNumberValue(ratio);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Read a saved tree. Null for anything that is not one: the file belongs
    /// to an earlier run and may hold anything at all.
    /// </summary>
    public static PaneTree? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var top = document.RootElement;
            if (top.ValueKind != JsonValueKind.Object || !top.TryGetProperty("root", out var rootElement)) return null;
            var seen = new HashSet<string>();
            if (Read(rootElement, seen) is not { } root) return null;
            var focused = top.TryGetProperty("focused", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString()! : "";
            return new PaneTree(root, focused);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static PaneNode? Read(JsonElement element, HashSet<string> seen)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty("leaf", out var leaf))
        {
            if (Text(leaf, "id") is not { Length: > 0 } id || !seen.Add(id)) return null;
            if (!leaf.TryGetProperty("tabs", out var tabsElement) || tabsElement.ValueKind != JsonValueKind.Array) return null;
            var tabs = new List<string>();
            foreach (var tab in tabsElement.EnumerateArray())
            {
                if (tab.ValueKind != JsonValueKind.String) return null;
                var note = tab.GetString()!;
                if (note.Length > 0 && !tabs.Contains(note)) tabs.Add(note);
            }
            var active = Text(leaf, "active");
            return new PaneLeaf(id, [.. tabs], active is not null && tabs.Contains(active) ? active : tabs.FirstOrDefault());
        }
        if (element.TryGetProperty("split", out var split))
        {
            if (Text(split, "id") is not { Length: > 0 } id || !seen.Add(id)) return null;
            var axis = Text(split, "axis") switch { "h" => SplitAxis.Horizontal, "v" => SplitAxis.Vertical, _ => (SplitAxis?)null };
            if (axis is null) return null;
            if (!split.TryGetProperty("children", out var childrenElement) || childrenElement.ValueKind != JsonValueKind.Array) return null;
            var children = new List<PaneNode>();
            foreach (var childElement in childrenElement.EnumerateArray())
            {
                if (Read(childElement, seen) is not { } child) return null;
                children.Add(child);
            }
            if (children.Count < 2) return null;

            // Shares that do not fit the children are replaced by equal ones.
            var ratios = new List<double>();
            if (split.TryGetProperty("ratios", out var ratiosElement) && ratiosElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var ratio in ratiosElement.EnumerateArray())
                {
                    if (ratio.ValueKind == JsonValueKind.Number && ratio.TryGetDouble(out var value) && value > 0 && double.IsFinite(value)) ratios.Add(value);
                }
            }
            var total = ratios.Sum();
            var shares = ratios.Count == children.Count && total > 0
                ? ratios.Select(ratio => ratio / total)
                : Enumerable.Repeat(1.0 / children.Count, children.Count);
            return new PaneSplit(id, axis.Value, [.. children], [.. shares]);
        }
        return null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
