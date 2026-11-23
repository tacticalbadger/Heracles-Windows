using System.Windows.Media;
using System.Windows.Threading;

namespace Heracles.Editor;

/// <summary>
/// The page of an open note: the views of its blocks, stacked, and what
/// floats over them (the selection of whole blocks, and in time the block
/// handle, the drop line and the slash menu). It is what the editor's scroll
/// view scrolls.
/// </summary>
internal sealed class BlockEditorView : HandLaidView
{
    private readonly BlockEditorController editor;
    private readonly SelectionOverlay overlay = new();
    private readonly List<TextHoldingView> live = [];
    private List<BlockView> rootViews = [];
    private Dictionary<string, BlockView> views = [];
    private IReadOnlyList<string> selectedBlocks = [];
    private IReadOnlyList<string> pendingBlocks = [];
    private DispatcherTimer? flashTimer;

    public BlockEditorView(BlockEditorController editor)
    {
        this.editor = editor;
        Focusable = true;
        FocusVisualStyle = null;
        Hold(overlay);
    }

    public IReadOnlyList<BlockView> RootViews => rootViews;

    /// <summary>Whole blocks that are selected.</summary>
    public IReadOnlyList<string> SelectedBlocks
    {
        get => selectedBlocks;
        set
        {
            if (selectedBlocks.SequenceEqual(value)) return;
            selectedBlocks = [.. value];
            RefreshOverlay();
        }
    }

    /// <summary>Blocks a drag in progress would select.</summary>
    public IReadOnlyList<string> PendingBlocks
    {
        get => pendingBlocks;
        set
        {
            if (pendingBlocks.SequenceEqual(value)) return;
            pendingBlocks = [.. value];
            RefreshOverlay();
        }
    }

    // Blocks to views

    /// <summary>
    /// Make the views match the document. A block keeps its view from one
    /// edit to the next, wherever it moves, so the text box being typed in
    /// is never thrown away under the user.
    /// </summary>
    public void Reconcile(EditorDocument document)
    {
        var old = new Dictionary<string, BlockView>(views);
        var current = new Dictionary<string, BlockView>();

        List<BlockView> Build(List<Block> blocks, HandLaidView container)
        {
            var built = new List<BlockView>(blocks.Count);
            var number = 0;
            for (var index = 0; index < blocks.Count; index++)
            {
                var block = blocks[index];
                if (block.Kind == BlockKind.Numbered)
                {
                    var continues = index > 0 && blocks[index - 1].Kind == BlockKind.Numbered && !block.StartsList;
                    number = continues ? number + 1 : block.ListAttrs.GetValueOrDefault("start")?.IntValue ?? 1;
                }
                BlockView view;
                if (old.TryGetValue(block.Id, out var existing) && existing.GetType() == BlockView.ViewClass(block.Kind))
                {
                    old.Remove(block.Id);
                    view = existing;
                }
                else
                {
                    view = BlockView.Make(block, editor);
                }
                view.Visibility = Visibility.Visible;
                view.Configure(block, number);
                container.Hold(view);
                view.SetChildViews(Build(block.Children, view));
                built.Add(view);
                current[block.Id] = view;
            }
            return built;
        }

        rootViews = Build(document.Blocks, this);
        foreach (var stale in old.Values)
        {
            (stale as TextHoldingView)?.Deactivate();
            (VisualTreeHelper.GetParent(stale) as HandLaidView)?.Release(stale);
        }
        live.RemoveAll(view => !ReferenceEquals(current.GetValueOrDefault(view.Block.Id), view));
        views = current;
        // What floats stays above the blocks.
        Raise(overlay);
        InvalidateMeasure();
    }

    public BlockView? BlockViewFor(string id) => views.GetValueOrDefault(id);

    /// <summary>The text box of a block, if it is the one that has it.</summary>
    public BlockTextBox? TextBoxFor(string id) => views.GetValueOrDefault(id)?.TextBox;

    /// <summary>Give a block the text box, so the caret can be put in it. Null for a block with no text, or none.</summary>
    public BlockTextBox? EnsureTextBox(string id)
    {
        if (views.GetValueOrDefault(id) is not TextHoldingView view || !view.Block.HasText) return null;
        if (!live.Contains(view)) live.Add(view);
        return view.Activate();
    }

    /// <summary>Put static text back wherever a text box is, except in one block.</summary>
    public void GiveUpTextBoxes(string? except = null)
    {
        for (var index = live.Count - 1; index >= 0; index--)
        {
            if (live[index].Block.Id == except) continue;
            var view = live[index];
            live.RemoveAt(index);
            view.Deactivate();
        }
    }

    // Frames

    /// <summary>A block's frame on the page. Null for a block that is not there, or is folded away.</summary>
    public Rect? FrameOf(string id) => views.GetValueOrDefault(id) is { } view ? FrameOf(view) : null;

    internal Rect? FrameOf(BlockView view)
    {
        // Whether it is shown is read from the views themselves: without a
        // window nothing counts as visible to WPF.
        DependencyObject? at = view;
        while (at is not null && !ReferenceEquals(at, this))
        {
            if (at is UIElement { Visibility: not Visibility.Visible }) return null;
            at = VisualTreeHelper.GetParent(at);
        }
        if (at is null) return null;
        return view.TransformToAncestor(this).TransformBounds(new Rect(view.RenderSize));
    }

    // Layout

    /// <summary>
    /// The text column: as wide as the page allows, up to a comfortable
    /// reading width, and centred.
    /// </summary>
    private static (double X, double Width) Column(double pageWidth)
    {
        var width = Math.Max(Math.Min(EditorTheme.ContentWidth, pageWidth - EditorTheme.Gutter * 2), 120);
        return (Math.Round(Math.Max((pageWidth - width) / 2, EditorTheme.Gutter)), Math.Round(width));
    }

    protected override double LayOut(double width)
    {
        var column = Column(width);
        var y = EditorTheme.TopPadding;
        for (var index = 0; index < rootViews.Count; index++)
        {
            var view = rootViews[index];
            y += index == 0 ? 0 : EditorTheme.BlockSpacing + view.TopMargin;
            y += Place(view, column.X, y, column.Width);
        }
        // Never shorter than the window, so a click anywhere below the
        // text lands on the page.
        var height = Math.Max(y + EditorTheme.BottomPadding, editor.VisibleHeight);
        PlaceAt(overlay, new Rect(0, 0, width, height));
        return height;
    }

    protected override void DidLayOut()
    {
        RefreshOverlay();
        editor.PageDidLayout();
    }

    /// <summary>The page answers to the mouse everywhere, not only where a block is.</summary>
    protected override HitTestResult HitTestCore(PointHitTestParameters parameters) =>
        new PointHitTestResult(this, parameters.HitPoint);

    // Finding the block at a point

    /// <summary>
    /// The block whose row a point is in. A point beside a block, in the
    /// margin, counts as in its row. A quote, callout or toggle is found as
    /// a whole from the margin beside it, and the blocks inside it from
    /// within. <paramref name="nearest"/> settles for the closest block
    /// where a column is shorter than its row.
    /// </summary>
    public BlockView? BlockViewAt(Point point, ISet<string>? excluding = null, bool nearest = false)
    {
        var slack = EditorTheme.BlockSpacing / 2;

        bool Excluded(BlockView view) => excluding?.Contains(view.Block.Id) == true;

        BlockView? Search(IReadOnlyList<BlockView> candidates)
        {
            foreach (var view in candidates)
            {
                if (Excluded(view) || FrameOf(view) is not { } frame) continue;
                if (point.Y < frame.Top - slack || point.Y >= frame.Bottom + slack) continue;
                if (view is ColumnsBlockView)
                {
                    var columns = view.ChildViews;
                    var column = columns.LastOrDefault(candidate =>
                        FrameOf(candidate) is { } inner && point.X >= inner.Left - EditorTheme.ColumnGap) ?? columns.FirstOrDefault();
                    if (column is null) return null;
                    if (Search(column.ChildViews) is { } inside) return inside;
                    return nearest ? column.ChildViews.LastOrDefault(candidate => !Excluded(candidate)) : null;
                }
                var isBox = view.Block.Kind is BlockKind.Quote or BlockKind.Callout or BlockKind.Toggle;
                if (isBox && point.X < frame.Left) return view;
                return Search(view.ChildViews) ?? view;
            }
            return null;
        }

        return Search(rootViews);
    }

    /// <summary>Where a block dropped at a point would land.</summary>
    public DropTarget? DropTargetAt(Point point, IReadOnlyList<string> moving, bool allowColumns = true)
    {
        var document = editor.Document;
        // A block is never dropped on itself or inside itself.
        var excluded = EditorDocument.Within(moving.Select(document.Block).OfType<Block>()).Select(block => block.Id).ToHashSet();
        var view = BlockViewAt(point, excluded, nearest: true);
        if (view is null)
        {
            // Above the first block or below the last.
            var candidates = rootViews.Where(candidate => !excluded.Contains(candidate.Block.Id)).ToList();
            if (candidates.Count == 0) return null;
            view = point.Y < (FrameOf(candidates[0])?.Top ?? 0) ? candidates[0] : candidates[^1];
        }
        if (FrameOf(view) is not { } frame) return null;
        var id = view.Block.Id;

        // The right-hand end of a block makes a column beside it.
        var path = document.PathTo(id) ?? [];
        var inColumn = path.Length > 1 && document.At(path.AsSpan(..^1)).Kind == BlockKind.Column;
        if (allowColumns && (path.Length == 1 || inColumn) && point.X > frame.Right - Math.Min(90, frame.Width * 0.25))
        {
            return DropTarget.RightOf(id);
        }
        // The top half of a block's own row is before it, and the bottom
        // half after it. Under a list item's own line is the head of the
        // blocks inside the item.
        var ownRow = view.OwnRowHeight;
        if (point.Y < frame.Top + ownRow / 2) return DropTarget.Before(id);
        if (view.Block.IsListItem && point.Y < frame.Top + ownRow + EditorTheme.BlockSpacing
            && view.ChildViews.FirstOrDefault(child => child.Visibility == Visibility.Visible && !excluded.Contains(child.Block.Id)) is { } first)
        {
            return DropTarget.Before(first.Block.Id);
        }
        return DropTarget.After(id);
    }

    // Selection and flash

    private void RefreshOverlay()
    {
        overlay.Rects = [.. selectedBlocks.Concat(pendingBlocks).Select(FrameOf).OfType<Rect>().Select(Around)];
    }

    private static Rect Around(Rect frame) => Rect.Inflate(frame, 6, 2);

    /// <summary>Mark a block for a moment, after a link brought the reader to it.</summary>
    public void Flash(string id)
    {
        if (FrameOf(id) is not { } frame) return;
        overlay.Flash = Around(frame);
        flashTimer?.Stop();
        flashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        flashTimer.Tick += (_, _) =>
        {
            flashTimer?.Stop();
            flashTimer = null;
            overlay.Flash = null;
        };
        flashTimer.Start();
    }

    /// <summary>The block handle comes with the mouse, in a later task.</summary>
    public void HideHandle()
    {
    }
}

/// <summary>Draws the selection of whole blocks over them. Clicks pass through.</summary>
internal sealed class SelectionOverlay : FrameworkElement
{
    private static readonly Brush FlashFill = MakeFlashFill();

    private IReadOnlyList<Rect> rects = [];
    private Rect? flash;

    private static Brush MakeFlashFill()
    {
        var brush = new SolidColorBrush(Colors.Gold) { Opacity = 0.3 };
        brush.Freeze();
        return brush;
    }

    public SelectionOverlay()
    {
        IsHitTestVisible = false;
    }

    public IReadOnlyList<Rect> Rects
    {
        get => rects;
        set
        {
            if (rects.SequenceEqual(value)) return;
            rects = value;
            InvalidateVisual();
        }
    }

    public Rect? Flash
    {
        get => flash;
        set
        {
            flash = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var fill = Ui.Accent(0.18);
        foreach (var rect in rects) drawing.DrawRoundedRectangle(fill, null, rect, 4, 4);
        if (flash is { } marked) drawing.DrawRoundedRectangle(FlashFill, null, marked, 4, 4);
    }
}
