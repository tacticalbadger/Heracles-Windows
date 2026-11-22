using System.Globalization;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Heracles.Model;

namespace Heracles.Workspaces;

/// <summary>
/// The panes of a window, drawn from its tree: splits within splits, down to
/// the panes themselves. A pane's view is kept while the pane exists. The
/// frame of splits around the panes is rebuilt only when the shape of the
/// tree changes, so a change of tabs or of a note never moves the keyboard.
/// </summary>
internal sealed class PaneHost : Border
{
    private const double DividerThickness = 5;

    /// <summary>No pane can be dragged smaller than this share of its split.</summary>
    private const double SmallestShare = 0.12;

    private readonly AppModel model;
    private readonly Workspace workspace;
    private readonly Dictionary<string, PaneLeafView> views = [];
    private string shape = "";

    public PaneHost(AppModel model, Workspace workspace)
    {
        this.model = model;
        this.workspace = workspace;
        Render();
    }

    public void Render()
    {
        var tree = workspace.Tree;
        var wanted = Shape(tree.Root);
        if (wanted != shape)
        {
            shape = wanted;
            var focused = Keyboard.FocusedElement as UIElement;

            // Take every pane out of the old frame before building the new one.
            // A lone pane is this border's own child; panes in a split are
            // children of its grid. An element still attached anywhere cannot
            // be placed again.
            Child = null;
            foreach (var view in views.Values) (view.Parent as Panel)?.Children.Remove(view);
            var live = tree.Leaves.Select(leaf => leaf.Id).ToHashSet();
            foreach (var gone in views.Keys.Where(id => !live.Contains(id)).ToList()) views.Remove(gone);
            Child = Build(tree.Root);

            if (focused is { IsVisible: true } && focused is DependencyObject element && IsAncestorOf(element)) focused.Focus();
        }
        foreach (var leaf in tree.Leaves)
        {
            if (views.TryGetValue(leaf.Id, out var view)) view.Update(leaf);
        }
    }

    /// <summary>The frame of the tree as text: which splits hold which panes, and their shares.</summary>
    private static string Shape(PaneNode node) => node switch
    {
        PaneLeaf leaf => leaf.Id,
        PaneSplit split => (split.Axis == SplitAxis.Horizontal ? "h" : "v") + split.Id + "["
            + string.Join(",", split.Children.Select(Shape)) + "|"
            + string.Join(",", split.Ratios.Select(ratio => ratio.ToString("F4", CultureInfo.InvariantCulture))) + "]",
        _ => "",
    };

    private UIElement Build(PaneNode node)
    {
        if (node is PaneLeaf leaf)
        {
            if (!views.TryGetValue(leaf.Id, out var view))
            {
                view = new PaneLeafView(model, workspace, leaf.Id);
                views[leaf.Id] = view;
            }
            return view;
        }
        var split = (PaneSplit)node;
        var horizontal = split.Axis == SplitAxis.Horizontal;
        var grid = new Grid();
        for (var index = 0; index < split.Children.Length; index++)
        {
            if (index > 0)
            {
                AddTrack(grid, horizontal, new GridLength(DividerThickness));
                var divider = new GridSplitter
                {
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Cursor = horizontal ? Cursors.SizeWE : Cursors.SizeNS,
                    Focusable = false,
                }.Id("Divider:" + split.Id + ":" + (index - 1));
                divider.Background = Ui.Hairline;
                divider.DragCompleted += (_, _) => TakeShares(split, grid, horizontal);
                Place(divider, horizontal, index * 2 - 1);
                grid.Children.Add(divider);
            }
            var share = index < split.Ratios.Length ? split.Ratios[index] : 1.0 / split.Children.Length;
            AddTrack(grid, horizontal, new GridLength(share, GridUnitType.Star));
            var child = Build(split.Children[index]);
            Place(child, horizontal, index * 2);
            grid.Children.Add(child);
        }
        return grid;
    }

    private static void AddTrack(Grid grid, bool horizontal, GridLength length)
    {
        if (horizontal) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        else grid.RowDefinitions.Add(new RowDefinition { Height = length });
    }

    private static void Place(UIElement element, bool horizontal, int track)
    {
        if (horizontal) Grid.SetColumn(element, track);
        else Grid.SetRow(element, track);
    }

    /// <summary>After a divider is let go, the split's shares become what the panes now measure.</summary>
    private void TakeShares(PaneSplit split, Grid grid, bool horizontal)
    {
        var sizes = new List<double>();
        for (var index = 0; index < split.Children.Length; index++)
        {
            sizes.Add(horizontal ? grid.ColumnDefinitions[index * 2].ActualWidth : grid.RowDefinitions[index * 2].ActualHeight);
        }
        var total = sizes.Sum();
        if (total <= 0) return;
        workspace.Tree = workspace.Tree.SetRatios(split.Id, Shares(sizes.Select(size => size / total).ToList()));
    }

    /// <summary>Shares that add up to 1 with none below the smallest allowed.</summary>
    internal static IReadOnlyList<double> Shares(IReadOnlyList<double> raw)
    {
        if (raw.Count == 0 || raw.Count * SmallestShare >= 1) return raw;
        var shares = raw.Select(share => Math.Max(share, SmallestShare)).ToList();
        // What the small panes were given is taken from the ones with room to spare.
        var over = shares.Sum() - 1;
        var spare = shares.Sum(share => share - SmallestShare);
        return spare <= 0 ? shares : shares.Select(share => share - over * (share - SmallestShare) / spare).ToList();
    }
}
