using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Heracles.Editor;

// What the mouse does on the page: the block handle that follows the
// pointer, dragging blocks by it, clicks on the page itself, a drag from one
// block's text into another, and picture files dropped from elsewhere.
//
// Each thing the mouse can do is a method that takes a point, which is what
// the tests call. The handlers at the end only turn mouse events into those
// calls.
internal sealed partial class BlockEditorView
{
    private sealed class GripDrag(string id, Point start, IReadOnlyList<string> moving)
    {
        public string Id { get; } = id;

        public Point Start { get; } = start;

        public IReadOnlyList<string> Moving { get; } = moving;

        public bool IsDragging { get; set; }

        public DropTarget? Target { get; set; }
    }

    private readonly Border dropLine = new()
    {
        CornerRadius = new CornerRadius(1.5),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private BlockHandleView handle = null!;
    private string? hoveredBlock;
    private Rect handleFrame;
    private Rect dropLineFrame;
    private GripDrag? gripDrag;
    private DropTarget? fileDropTarget;
    private Point? pressedAt;

    private void InitializeMouse()
    {
        handle = new BlockHandleView(this) { Visibility = Visibility.Collapsed };
        dropLine.Background = Ui.Accent(1);
        AllowDrop = true;
        Hold(dropLine);
        Hold(handle);
    }

    private void PlaceFloating()
    {
        if (dropLine.Visibility == Visibility.Visible) PlaceAt(dropLine, dropLineFrame);
        if (handle.Visibility == Visibility.Visible) PlaceAt(handle, handleFrame);
    }

    private void RaiseFloating()
    {
        Raise(dropLine);
        Raise(handle);
        if (hoveredBlock is not null && !views.ContainsKey(hoveredBlock)) HideHandle();
    }

    internal BlockEditorController Editor => editor;

    // The block handle

    /// <summary>The block the handle is showing for.</summary>
    public string? HandleBlock => handle.Visibility == Visibility.Visible ? hoveredBlock : null;

    public Rect? HandleFrame => HandleBlock is null ? null : handleFrame;

    /// <summary>The pointer moved over the page with no button down.</summary>
    public void PointerMoved(Point point)
    {
        if (!editor.IsEditable || gripDrag is not null) return;
        ShowHandle(point);
    }

    public void ShowHandle(Point point)
    {
        if (BlockViewAt(point) is not { } view || FrameOf(view) is not { } frame)
        {
            HideHandle();
            return;
        }
        var size = BlockHandleView.Size;
        var wanted = new Rect(
            frame.Left - size.Width - 6, frame.Top + (view.FirstLineHeight - size.Height) / 2, size.Width, size.Height);
        if (hoveredBlock == view.Block.Id && handle.Visibility == Visibility.Visible && wanted == handleFrame) return;
        hoveredBlock = view.Block.Id;
        handleFrame = wanted;
        handle.Visibility = Visibility.Visible;
        InvalidateMeasure();
    }

    public void HideHandle()
    {
        hoveredBlock = null;
        if (handle.Visibility != Visibility.Visible) return;
        handle.Visibility = Visibility.Collapsed;
        InvalidateMeasure();
    }

    internal void AddBlockAfterHandle()
    {
        if (HandleBlock is { } id) editor.AddBlock(id);
    }

    // The drop line

    public Rect? DropLineFrame => dropLine.Visibility == Visibility.Visible ? dropLineFrame : null;

    private void ShowDropLine(DropTarget? target)
    {
        if (target is not { } where || FrameOf(where.BlockId) is not { } frame)
        {
            HideDropLine();
            return;
        }
        var gap = EditorTheme.BlockSpacing / 2;
        dropLineFrame = where.Side switch
        {
            DropSide.Before => new Rect(frame.Left, frame.Top - gap - 1.5, frame.Width, 3),
            DropSide.After => new Rect(frame.Left, frame.Bottom + gap - 1.5, frame.Width, 3),
            DropSide.LeftOf => new Rect(frame.Left - 10, frame.Top, 3, frame.Height),
            _ => new Rect(frame.Right + 7, frame.Top, 3, frame.Height),
        };
        dropLine.Visibility = Visibility.Visible;
        InvalidateMeasure();
    }

    private void HideDropLine()
    {
        if (dropLine.Visibility != Visibility.Visible) return;
        dropLine.Visibility = Visibility.Collapsed;
        InvalidateMeasure();
    }

    // Dragging blocks by the handle's grip

    /// <summary>The mouse went down on the grip: a drag moves the block, and a click opens its menu.</summary>
    public void BeginGripDrag(string id, Point start)
    {
        var moving = selectedBlocks.Contains(id) ? selectedBlocks : [id];
        gripDrag = new GripDrag(id, start, moving);
    }

    public void ContinueGripDrag(Point point)
    {
        if (gripDrag is not { } drag) return;
        if (!drag.IsDragging)
        {
            if ((point - drag.Start).Length <= 4) return;
            drag.IsDragging = true;
            PendingBlocks = drag.Moving;
        }
        drag.Target = DropTargetAt(point, drag.Moving);
        ShowDropLine(drag.Target);
    }

    /// <summary>
    /// The button was let go. True when it was a drag. After a click, the
    /// block is selected and its menu is the caller's to open.
    /// </summary>
    public bool EndGripDrag()
    {
        if (gripDrag is not { } drag) return false;
        gripDrag = null;
        HideDropLine();
        PendingBlocks = [];
        if (!drag.IsDragging)
        {
            editor.SelectBlocks([drag.Id], extending: false);
            return false;
        }
        if (drag.Target is { } target) editor.Move(drag.Moving, target);
        return true;
    }

    /// <summary>Scroll when a drag nears the top or bottom of what can be seen.</summary>
    internal void Autoscroll(Point point)
    {
        var view = editor.ScrollView;
        var y = point.Y - view.VerticalOffset;
        if (y < 30) view.ScrollToVerticalOffset(view.VerticalOffset - 16);
        else if (y > view.ViewportHeight - 30) view.ScrollToVerticalOffset(view.VerticalOffset + 16);
    }

    // The mouse on the page itself

    /// <summary>A click on the page where no block took it.</summary>
    public void ClickAt(Point point, bool shift)
    {
        editor.CloseSlashMenu();
        if (rootViews.Count > 0 && FrameOf(rootViews[^1]) is { } last && point.Y > last.Bottom)
        {
            // Below the note: carry on writing at its end.
            editor.FocusEnd();
            return;
        }
        if (BlockViewAt(point, nearest: true) is not { } view || FrameOf(view) is not { } frame)
        {
            editor.FocusStart();
            return;
        }
        // Beside a block: the caret goes to the nearer end of its text.
        if (view.Block.HasText)
        {
            var end = point.X > frame.Left + frame.Width / 2 ? view.Block.Text.Length : 0;
            editor.Select(EditorSelection.Caret(view.Block.Id, end));
        }
        else
        {
            editor.SelectBlocks([view.Block.Id], extending: shift);
        }
    }

    // The mouse in a block's text

    /// <summary>The mouse went down in a block's static text.</summary>
    internal void StaticTextPressed(string blockId, Point pointInText, int clicks, Point onPage)
    {
        editor.ActivateAt(blockId, pointInText, clicks);
        if (editor.FocusedText is not { } box || box.BlockId != blockId) return;
        pressedAt = onPage;
        // The text box never saw the button go down, so the editor follows
        // the drag itself.
        editor.BeginTextDrag(blockId, clicks == 1 ? box.SelectedRange.Start : null);
        CaptureMouse();
    }

    /// <summary>The mouse went down in the text box. The box follows the drag; the editor watches where it goes.</summary>
    internal void TextBoxPressed(string blockId, Point onPage)
    {
        pressedAt = onPage;
        editor.BeginTextDrag(blockId);
    }

    // Picture files dragged in from elsewhere

    /// <summary>
    /// Files are being dragged over the page. True when there are pictures
    /// among them, which become image blocks wherever they are dropped.
    /// </summary>
    public bool FilesDraggedOver(IReadOnlyList<string> files, Point point)
    {
        if (!editor.IsEditable || !files.Any(file => BlockEditorController.ImageType(file) is not null))
        {
            HideDropLine();
            return false;
        }
        fileDropTarget = DropTargetAt(point, [], allowColumns: false);
        ShowDropLine(fileDropTarget);
        return true;
    }

    public bool FilesDropped(IReadOnlyList<string> files)
    {
        HideDropLine();
        var pictures = files.Where(file => BlockEditorController.ImageType(file) is not null).ToList();
        return pictures.Count > 0 && editor.InsertImages(pictures, fileDropTarget);
    }

    // From mouse events to the methods above

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton == MouseButtonState.Released) PointerMoved(e.GetPosition(this));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (gripDrag is null) HideHandle();
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (e.LeftButton != MouseButtonState.Pressed || !editor.IsDraggingText) return;
        var point = e.GetPosition(this);
        editor.ContinueTextDrag(point);
        Autoscroll(point);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!editor.IsDraggingText) return;
        var point = e.GetPosition(this);
        var selectedBlocks = editor.EndTextDrag(point);
        if (IsMouseCaptured) ReleaseMouseCapture();
        // A click that went nowhere, on a link, opens it.
        if (!selectedBlocks && pressedAt is { } pressed && (point - pressed).Length < 3 && editor.FocusedText is { } box)
        {
            var (start, end) = box.SelectedRange;
            if (start == end) editor.TextClicked(box.BlockId, start);
        }
        pressedAt = null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled) return;
        ClickAt(e.GetPosition(this), (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        e.Handled = true;
    }

    private static IReadOnlyList<string> FilesIn(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files ? files : [];

    protected override void OnPreviewDragOver(DragEventArgs e)
    {
        base.OnPreviewDragOver(e);
        if (!FilesDraggedOver(FilesIn(e), e.GetPosition(this))) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        HideDropLine();
    }

    protected override void OnPreviewDrop(DragEventArgs e)
    {
        base.OnPreviewDrop(e);
        if (FilesDropped(FilesIn(e))) e.Handled = true;
    }
}

/// <summary>
/// What appears beside the block under the pointer: a button to add a
/// block below, and a grip to drag the block by or click for its menu.
/// </summary>
internal sealed class BlockHandleView : HandLaidView
{
    public static readonly Size Size = new(40, 22);

    private readonly Button add;
    private readonly GripView grip;

    public BlockHandleView(BlockEditorView page)
    {
        add = new Button
        {
            Content = Ui.Glyph(((char)0xE710).ToString(), 10),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Opacity = 0.55,
            Focusable = false,
            Cursor = Cursors.Hand,
            ToolTip = "Add a block below",
        }.Id("HandleAdd").Named("Add a block below");
        add.Click += (_, _) => page.AddBlockAfterHandle();
        grip = new GripView(page) { ToolTip = "Drag to move. Click for the menu." };
        Hold(add);
        Hold(grip);
    }

    protected override double LayOut(double width)
    {
        PlaceAt(add, new Rect(0, 0, 20, 22));
        PlaceAt(grip, new Rect(20, 0, 20, 22));
        return Size.Height;
    }
}

/// <summary>Six dots to take hold of a block by.</summary>
internal sealed class GripView : FrameworkElement
{
    private readonly BlockEditorView page;

    public GripView(BlockEditorView page)
    {
        this.page = page;
        Cursor = Cursors.SizeAll;
        AutomationProperties.SetName(this, "Block menu");
    }

    protected override void OnRender(DrawingContext drawing)
    {
        // Two across and three down.
        var brush = EditorTheme.TextBrush(this, 0.45);
        var middle = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        for (var column = 0; column < 2; column++)
        {
            for (var row = 0; row < 3; row++)
            {
                drawing.DrawEllipse(brush, null, new Point(middle.X - 2.5 + column * 5, middle.Y - 5 + row * 5), 1.5, 1.5);
            }
        }
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters parameters) => new PointHitTestResult(this, parameters.HitPoint);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (page.HandleBlock is not { } id) return;
        page.BeginGripDrag(id, e.GetPosition(page));
        // A drag source must capture the mouse on button-down.
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        var point = e.GetPosition(page);
        page.ContinueGripDrag(point);
        page.Autoscroll(point);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        var id = page.HandleBlock;
        if (!page.EndGripDrag() && id is not null) page.Editor.ShowBlockMenu(id, this);
        e.Handled = true;
    }
}
