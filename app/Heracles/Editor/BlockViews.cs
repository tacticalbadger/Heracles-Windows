using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Heracles.Editor;

/// <summary>
/// A view that places what is inside it by hand, as the Mac editor's views
/// do: it is told a width, puts each thing at a place, and says how tall
/// that came out.
/// </summary>
internal abstract class HandLaidView : FrameworkElement
{
    private readonly VisualCollection visuals;
    private readonly List<(UIElement Element, Rect Frame)> placed = [];

    protected HandLaidView()
    {
        visuals = new VisualCollection(this);
    }

    protected override int VisualChildrenCount => visuals.Count;

    protected override Visual GetVisualChild(int index) => visuals[index];

    /// <summary>
    /// Take a view in. It leaves whatever held it before: a view moved to a
    /// new parent must leave the old one first.
    /// </summary>
    internal void Hold(UIElement element)
    {
        var parent = VisualTreeHelper.GetParent(element);
        if (ReferenceEquals(parent, this)) return;
        (parent as HandLaidView)?.Release(element);
        visuals.Add(element);
        InvalidateMeasure();
    }

    internal void Release(UIElement element)
    {
        if (!visuals.Contains(element)) return;
        visuals.Remove(element);
        InvalidateMeasure();
    }

    /// <summary>Draw a view over the others.</summary>
    protected void Raise(UIElement element)
    {
        if (visuals.Count > 0 && ReferenceEquals(visuals[visuals.Count - 1], element)) return;
        visuals.Remove(element);
        visuals.Add(element);
    }

    protected sealed override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? EditorTheme.ContentWidth : available.Width;
        placed.Clear();
        return new Size(width, Math.Max(LayOut(width), 0));
    }

    protected sealed override Size ArrangeOverride(Size final)
    {
        foreach (var (element, frame) in placed) element.Arrange(frame);
        DidLayOut();
        return final;
    }

    /// <summary>Place everything for a width, and return the height that takes.</summary>
    protected abstract double LayOut(double width);

    /// <summary>Called when everything placed has its place.</summary>
    protected virtual void DidLayOut()
    {
    }

    /// <summary>Put a view at a place, as wide as it is given and as tall as it wants. Returns its height.</summary>
    protected double Place(UIElement element, double x, double y, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        var height = element.DesiredSize.Height;
        placed.Add((element, new Rect(x, y, width, height)));
        return height;
    }

    /// <summary>Put a view in a frame of a size already known.</summary>
    protected void PlaceAt(UIElement element, Rect frame)
    {
        element.Measure(frame.Size);
        placed.Add((element, frame));
    }

    /// <summary>Text to draw by hand, in the font the view stands in.</summary>
    protected FormattedText Label(string text, EditorFont font, Brush brush) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(font.Family, FontStyles.Normal, font.Weight, FontStretches.Normal), font.Size, brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}

/// <summary>
/// The view of one block. Subclasses draw the kinds of block. Each lays out
/// its own content and the views of the blocks inside it, for a given width.
/// </summary>
internal abstract class BlockView : HandLaidView
{
    private List<BlockView> childViews = [];

    /// <summary>The block as the document has it now. Not a copy: what the view last drew is its own to remember.</summary>
    public Block Block { get; private set; }

    public BlockEditorController Editor { get; }

    /// <summary>A numbered item's number.</summary>
    public int Ordinal { get; private set; } = 1;

    /// <summary>The views of the blocks inside this one, in order.</summary>
    public IReadOnlyList<BlockView> ChildViews => childViews;

    protected BlockView(Block block, BlockEditorController editor)
    {
        Block = block;
        Editor = editor;
    }

    /// <summary>The class that shows a kind of block.</summary>
    public static Type ViewClass(BlockKind kind) => kind switch
    {
        BlockKind.Paragraph or BlockKind.Heading or BlockKind.Bullet or BlockKind.Numbered or BlockKind.Todo => typeof(TextBlockView),
        BlockKind.Code => typeof(CodeBlockView),
        BlockKind.Quote => typeof(QuoteBlockView),
        BlockKind.Callout => typeof(CalloutBlockView),
        BlockKind.Toggle => typeof(ToggleBlockView),
        BlockKind.Columns => typeof(ColumnsBlockView),
        BlockKind.Column => typeof(ColumnBlockView),
        BlockKind.Divider => typeof(DividerBlockView),
        BlockKind.Image => typeof(ImageBlockView),
        _ => typeof(UnknownBlockView),
    };

    public static BlockView Make(Block block, BlockEditorController editor) => block.Kind switch
    {
        BlockKind.Paragraph or BlockKind.Heading or BlockKind.Bullet or BlockKind.Numbered or BlockKind.Todo => new TextBlockView(block, editor),
        BlockKind.Code => new CodeBlockView(block, editor),
        BlockKind.Quote => new QuoteBlockView(block, editor),
        BlockKind.Callout => new CalloutBlockView(block, editor),
        BlockKind.Toggle => new ToggleBlockView(block, editor),
        BlockKind.Columns => new ColumnsBlockView(block, editor),
        BlockKind.Column => new ColumnBlockView(block, editor),
        BlockKind.Divider => new DividerBlockView(block, editor),
        BlockKind.Image => new ImageBlockView(block, editor),
        _ => new UnknownBlockView(block, editor),
    };

    /// <summary>
    /// Show the block as it now is. Called after every edit, for every
    /// block, so it must cost little when nothing changed.
    /// </summary>
    public virtual void Configure(Block block, int ordinal)
    {
        if (ordinal != Ordinal) InvalidateVisual();
        Block = block;
        Ordinal = ordinal;
    }

    public void SetChildViews(List<BlockView> views)
    {
        foreach (var view in views) Hold(view);
        if (!views.SequenceEqual(childViews)) InvalidateMeasure();
        childViews = views;
        ChildViewsChanged();
    }

    protected virtual void ChildViewsChanged()
    {
    }

    /// <summary>The text box of the block's own text, while it has one.</summary>
    public virtual BlockTextBox? TextBox => null;

    /// <summary>Extra space above the block.</summary>
    public double TopMargin => EditorTheme.TopMargin(Block);

    /// <summary>The height of the block's own first line, which the block handle is centred on.</summary>
    public virtual double FirstLineHeight => 22;

    /// <summary>The height of the block's own row, apart from the blocks inside it.</summary>
    public virtual double OwnRowHeight => RenderSize.Height;

    /// <summary>Stack the views of the blocks inside this one. Returns where the stack ends.</summary>
    protected double Stack(IEnumerable<BlockView> views, double x, double y, double width)
    {
        var isFirst = true;
        foreach (var view in views)
        {
            if (view.Visibility != Visibility.Visible) continue;
            y += isFirst ? 0 : EditorTheme.BlockSpacing + view.TopMargin;
            y += Place(view, x, y, Math.Max(width, 40));
            isFirst = false;
        }
        return y;
    }

    /// <summary>A click on a block that has no text selects the block.</summary>
    protected void SelectOnClick(MouseButtonEventArgs e)
    {
        Editor.SelectBlocks([Block.Id], extending: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        e.Handled = true;
    }

    protected bool IsEditable => Editor.IsEditable;

    /// <summary>All of the view answers to the mouse, not only where something is drawn.</summary>
    protected HitTestResult WholeView(PointHitTestParameters parameters) => new PointHitTestResult(this, parameters.HitPoint);
}

// Text

/// <summary>
/// A block with text of its own. The text is static until the caret comes:
/// then a text box takes its place, at the same size, and gives it back
/// when the caret has gone.
/// </summary>
internal abstract class TextHoldingView : BlockView
{
    private readonly TextBlock still;
    private BlockTextBox? box;
    private TextBlock? hint;
    private (RichText Text, BlockKind Kind, int Look) shown;
    private double ownRow;

    protected TextHoldingView(Block block, BlockEditorController editor)
        : base(block, editor)
    {
        still = EditorTheme.StaticText(block);
        still.Cursor = Cursors.IBeam;
        still.MouseLeftButtonDown += (_, e) => StillPressed(e, e.ClickCount);
        // The right button puts the caret where it is too, so that the menu
        // that follows is the text box's, for that place.
        still.MouseRightButtonDown += (_, e) => Editor.ActivateAt(Block.Id, e.GetPosition(still));
        shown = State(block);
        Hold(still);
    }

    private void StillPressed(MouseButtonEventArgs e, int clicks)
    {
        e.Handled = true;
        // A shift-click from another block selects the blocks between.
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && Editor.ExtendSelection(Block.Id)) return;
        Editor.Page.StaticTextPressed(Block.Id, e.GetPosition(still), clicks, e.GetPosition(Editor.Page));
    }

    public override BlockTextBox? TextBox => box;

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new BlockTextPeer(this);

    public override double OwnRowHeight => ownRow;

    /// <summary>Shown, faintly, while the block is empty and has the caret.</summary>
    protected abstract string? Placeholder { get; }

    /// <summary>The text the block is showing, in whichever of its two forms.</summary>
    public string ShownText => box is not null
        ? box.Text
        : string.Concat(EditorTheme.Leaves(still.Inlines).Select(inline => inline switch
        {
            Run run => run.Text,
            LineBreak => "\n",
            global::Emoji.Wpf.EmojiInline emoji => emoji.Text,
            _ => "",
        }));

    /// <summary>What decides how a block's text looks: the text, and what its kind does to it.</summary>
    private static (RichText, BlockKind, int) State(Block block) => (
        block.Text, block.Kind,
        block.Kind == BlockKind.Heading ? block.Level : block.Kind == BlockKind.Todo && block.IsChecked ? 1 : 0);

    public override void Configure(Block block, int ordinal)
    {
        base.Configure(block, ordinal);
        // The text is only written when the model's is not what is already
        // shown. While the user types, it always is.
        var state = State(block);
        if (shown != state)
        {
            if (box is null)
            {
                EditorTheme.Fill(still, block);
            }
            else
            {
                var live = box;
                Editor.WithoutTextCallbacks(() =>
                {
                    var (start, end) = live.SelectedRange;
                    live.Show(block);
                    if (end <= block.Text.Length) live.Select(start, end);
                });
            }
            shown = state;
            InvalidateMeasure();
        }
        if (box is not null)
        {
            box.BlockId = block.Id;
            box.IsReadOnly = !IsEditable;
        }
        UpdateHint();
    }

    /// <summary>
    /// Record what the text box now shows, after the user typed in it, so
    /// the next <see cref="Configure"/> does not write the same text back.
    /// </summary>
    public void NoteShown(Block block)
    {
        shown = State(block);
        UpdateHint();
        InvalidateMeasure();
    }

    /// <summary>Put the text box in the static text's place.</summary>
    public BlockTextBox Activate()
    {
        if (box is not null) return box;
        box = new BlockTextBox { BlockId = Block.Id, IsReadOnly = !IsEditable };
        box.Show(Block);
        shown = State(Block);
        Release(still);
        Hold(box);
        Editor.Attach(box);
        UpdateHint();
        return box;
    }

    /// <summary>Put the static text back.</summary>
    public void Deactivate()
    {
        if (box is null) return;
        var old = box;
        box = null;
        Editor.Detach(old);
        Release(old);
        EditorTheme.Fill(still, Block);
        shown = State(Block);
        Hold(still);
        UpdateHint();
    }

    private void UpdateHint()
    {
        var wanted = box is not null && IsEditable && Block.Text.IsEmpty ? Placeholder : null;
        if (wanted is null)
        {
            if (hint is null) return;
            Release(hint);
            hint = null;
            return;
        }
        if (hint is null)
        {
            hint = new TextBlock
            {
                IsHitTestVisible = false,
                Opacity = 0.4,
                Padding = new Thickness(EditorTheme.TextPadding, 0, EditorTheme.TextPadding, 0),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Hold(hint);
        }
        var font = EditorTheme.FontFor(Block);
        hint.FontFamily = font.Family;
        hint.FontSize = font.Size;
        hint.FontWeight = font.Weight;
        hint.LineHeight = EditorTheme.LineHeight(Block);
        hint.Text = wanted;
    }

    /// <summary>
    /// Place the block's text with its letters starting at <paramref name="x"/>.
    /// The text keeps a little room of its own at each side, so it is put
    /// that much further out. Returns its height.
    /// </summary>
    protected double PlaceText(double x, double y, double width)
    {
        var padding = EditorTheme.TextPadding;
        var text = (UIElement?)box ?? still;
        ownRow = Place(text, x - padding, y, width + padding * 2);
        if (hint is not null) PlaceAt(hint, new Rect(x - padding, y, width + padding * 2, ownRow));
        return ownRow;
    }
}

/// <summary>A paragraph, a heading, or a list item with its marker.</summary>
internal sealed class TextBlockView : TextHoldingView
{
    private static readonly Brush CheckFill = MakeCheckFill();
    /// <summary>The small check box. A card on a board and a task in its window use it too.</summary>
    internal static readonly ControlTemplate CheckTemplate = MakeCheckTemplate();

    private CheckBox? checkbox;
    private bool settingCheck;
    private BlockKind drawnKind;

    public TextBlockView(Block block, BlockEditorController editor)
        : base(block, editor)
    {
        drawnKind = block.Kind;
    }

    private double MarkerWidth => Block.IsListItem ? EditorTheme.Indent : 0;

    public override double FirstLineHeight => EditorTheme.LineHeight(Block);

    protected override string? Placeholder =>
        Block.Kind == BlockKind.Heading ? $"Heading {Block.Level}" : Block.IsListItem ? "List" : "Type / for blocks";

    public override void Configure(Block block, int ordinal)
    {
        base.Configure(block, ordinal);
        if (block.Kind != drawnKind)
        {
            drawnKind = block.Kind;
            InvalidateMeasure();
            InvalidateVisual();
        }
        if (block.Kind == BlockKind.Todo)
        {
            checkbox ??= MakeCheckbox();
            AutomationProperties.SetAutomationId(checkbox, "Check:" + block.Id);
            settingCheck = true;
            checkbox.IsChecked = block.IsChecked;
            settingCheck = false;
            checkbox.IsEnabled = IsEditable;
        }
        else if (checkbox is not null)
        {
            Release(checkbox);
            checkbox = null;
        }
    }

    private static Brush MakeCheckFill()
    {
        var brush = new SolidColorBrush(SystemColors.AccentColor);
        brush.Freeze();
        return brush;
    }

    /// <summary>A small square with a tick, in place of the theme's check box, which is made for forms.</summary>
    private static ControlTemplate MakeCheckTemplate()
    {
        var box = new FrameworkElementFactory(typeof(Border), "Box");
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        box.SetValue(Border.BorderBrushProperty, Brushes.Gray);
        box.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var tick = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path), "Tick");
        tick.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 2.5,6.5 L 5.5,9.5 L 10.5,3.5"));
        tick.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.White);
        tick.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.8);
        tick.SetValue(VisibilityProperty, Visibility.Collapsed);
        box.AppendChild(tick);

        var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = box };
        var isChecked = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        isChecked.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "Tick"));
        isChecked.Setters.Add(new Setter(Border.BackgroundProperty, CheckFill, "Box"));
        isChecked.Setters.Add(new Setter(Border.BorderBrushProperty, CheckFill, "Box"));
        template.Triggers.Add(isChecked);
        var isDisabled = new Trigger { Property = IsEnabledProperty, Value = false };
        isDisabled.Setters.Add(new Setter(OpacityProperty, 0.5, "Box"));
        template.Triggers.Add(isDisabled);
        template.Seal();
        return template;
    }

    private CheckBox MakeCheckbox()
    {
        var box = new CheckBox
        {
            Template = CheckTemplate,
            Width = 16,
            Height = 16,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Focusable = false,
            Cursor = Cursors.Hand,
        };
        AutomationProperties.SetName(box, "Done");
        // By the mouse or by automation, either way the document is told.
        box.Checked += (_, _) => Told(true);
        box.Unchecked += (_, _) => Told(false);
        Hold(box);
        return box;

        void Told(bool isChecked)
        {
            if (!settingCheck) Editor.SetChecked(Block.Id, isChecked);
        }
    }

    protected override double LayOut(double width)
    {
        var textWidth = Math.Max(width - MarkerWidth, 40);
        var height = PlaceText(MarkerWidth, 0, textWidth);
        if (checkbox is not null)
        {
            PlaceAt(checkbox, new Rect(2, Math.Max((FirstLineHeight - 16) / 2, 0), 16, 16));
        }
        if (ChildViews.Count == 0) return height;
        return Stack(ChildViews, MarkerWidth, height + EditorTheme.BlockSpacing, textWidth);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (Block.Kind is not (BlockKind.Bullet or BlockKind.Numbered)) return;
        var font = EditorTheme.FontFor(Block);
        var marker = Label(Block.Kind == BlockKind.Bullet ? "•" : $"{Ordinal}.", font, EditorTheme.TextBrush(this, 0.6));
        // A bullet sits in the middle of the marker's room. A number ends
        // just before the text, so the numbers of a list line up.
        var x = Block.Kind == BlockKind.Bullet ? (MarkerWidth - marker.Width) / 2 - 2 : MarkerWidth - marker.Width - 6;
        // On the baseline of the first line of the text beside it.
        var baseline = EditorTheme.LineHeight(Block) * font.Family.Baseline / font.Family.LineSpacing;
        drawing.DrawText(marker, new Point(Math.Max(x, 0), baseline - marker.Baseline));
    }
}

// Containers

internal sealed class QuoteBlockView(Block block, BlockEditorController editor) : BlockView(block, editor)
{
    protected override double LayOut(double width) => Stack(ChildViews, 16, 2, width - 16) + 2;

    protected override void OnRender(DrawingContext drawing) =>
        drawing.DrawRoundedRectangle(EditorTheme.TextBrush(this, 0.75), null, new Rect(0, 0, 3, RenderSize.Height), 1.5, 1.5);
}

internal sealed class CalloutBlockView : BlockView
{
    private readonly Button emoji;
    private string shownEmoji = "";

    public CalloutBlockView(Block block, BlockEditorController editor)
        : base(block, editor)
    {
        emoji = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Focusable = false,
            Cursor = Cursors.Hand,
            ToolTip = "Change the icon",
        };
        AutomationProperties.SetName(emoji, "Change the icon");
        emoji.Click += (_, _) => Editor.ChooseEmoji(Block.Id, emoji);
        Hold(emoji);
    }

    public override void Configure(Block block, int ordinal)
    {
        base.Configure(block, ordinal);
        if (shownEmoji != block.Emoji)
        {
            shownEmoji = block.Emoji;
            emoji.Content = new global::Emoji.Wpf.TextBlock { Text = block.Emoji, FontSize = 18 };
        }
        emoji.IsEnabled = IsEditable;
    }

    protected override double LayOut(double width)
    {
        PlaceAt(emoji, new Rect(10, 10, 26, 24));
        return Stack(ChildViews, 44, 12, width - 44 - 14) + 12;
    }

    protected override void OnRender(DrawingContext drawing) =>
        drawing.DrawRoundedRectangle(EditorTheme.Panel, null, new Rect(RenderSize), 6, 6);
}

internal sealed class ToggleBlockView : BlockView
{
    private readonly Button arrow;
    private bool? shownOpen;

    public ToggleBlockView(Block block, BlockEditorController editor)
        : base(block, editor)
    {
        arrow = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Focusable = false,
            Cursor = Cursors.Hand,
        };
        arrow.Click += (_, _) => Editor.SetOpen(Block.Id, !Block.IsOpen);
        Hold(arrow);
    }

    public override void Configure(Block block, int ordinal)
    {
        base.Configure(block, ordinal);
        AutomationProperties.SetAutomationId(arrow, "Fold:" + block.Id);
        if (shownOpen == block.IsOpen) return;
        shownOpen = block.IsOpen;
        // Chevron down, chevron right.
        arrow.Content = Ui.Glyph(((char)(block.IsOpen ? 0xE70D : 0xE76C)).ToString(), 10);
        AutomationProperties.SetName(arrow, block.IsOpen ? "Collapse" : "Expand");
        ShowOrHide();
    }

    protected override void ChildViewsChanged() => ShowOrHide();

    /// <summary>A closed toggle shows its first block only.</summary>
    private void ShowOrHide()
    {
        for (var index = 0; index < ChildViews.Count; index++)
        {
            ChildViews[index].Visibility = !Block.IsOpen && index > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        InvalidateMeasure();
    }

    protected override double LayOut(double width)
    {
        var first = ChildViews.Count > 0 ? ChildViews[0].FirstLineHeight : 22;
        PlaceAt(arrow, new Rect(0, Math.Max((first - 18) / 2, 0), 18, 18));
        return Stack(ChildViews, 22, 0, width - 22);
    }
}

/// <summary>A row of columns, with a divider to drag between each pair.</summary>
internal sealed class ColumnsBlockView(Block block, BlockEditorController editor) : BlockView(block, editor)
{
    private readonly List<ColumnGrip> grips = [];
    private string shownWidths = "";

    /// <summary>
    /// Move the divider after a column by a distance, taking width from
    /// one neighbour and giving it to the other.
    /// </summary>
    internal void Resize(int after, double by, IReadOnlyList<double> from)
    {
        if (after < 0 || after + 1 >= from.Count) return;
        var gap = EditorTheme.ColumnGap;
        var available = Math.Max(RenderSize.Width - gap * (from.Count - 1), 40);
        var total = from.Sum();
        var pair = from[after] + from[after + 1];
        var least = total * 0.12;
        var moved = Math.Min(Math.Max(from[after] + by / available * total, least), pair - least);
        var widths = from.ToArray();
        widths[after] = moved;
        widths[after + 1] = pair - moved;
        Editor.SetColumnWidths(Block.Id, widths);
    }

    public override void Configure(Block block, int ordinal)
    {
        base.Configure(block, ordinal);
        var widths = string.Join(' ', block.Children.Select(column => column.Width));
        if (widths == shownWidths) return;
        shownWidths = widths;
        InvalidateMeasure();
    }

    /// <summary>Each column's share of the row, as weights.</summary>
    public IReadOnlyList<double> Widths => ChildViews.Select(column => column.Block.Width).ToList();

    protected override double LayOut(double width)
    {
        var columns = ChildViews;
        if (columns.Count == 0) return 0;
        var gap = EditorTheme.ColumnGap;
        var available = Math.Max(width - gap * (columns.Count - 1), 40);
        var total = columns.Sum(column => column.Block.Width);
        var x = 0.0;
        var tallest = 0.0;
        var rights = new List<double>(columns.Count);
        foreach (var column in columns)
        {
            var share = Math.Round(available * column.Block.Width / total);
            tallest = Math.Max(tallest, Place(column, x, 0, share));
            rights.Add(x + share);
            x += share + gap;
        }

        while (grips.Count < columns.Count - 1)
        {
            var grip = new ColumnGrip(this, grips.Count);
            grips.Add(grip);
            Hold(grip);
        }
        while (grips.Count > Math.Max(columns.Count - 1, 0))
        {
            Release(grips[^1]);
            grips.RemoveAt(grips.Count - 1);
        }
        for (var index = 0; index < grips.Count; index++)
        {
            grips[index].Visibility = IsEditable ? Visibility.Visible : Visibility.Collapsed;
            if (IsEditable) PlaceAt(grips[index], new Rect(rights[index] + gap / 2 - 5, 0, 10, tallest));
        }
        return tallest;
    }
}

/// <summary>The divider between two columns: drag it to change their widths.</summary>
internal sealed class ColumnGrip : FrameworkElement
{
    private readonly ColumnsBlockView row;
    private readonly int index;
    private IReadOnlyList<double> startWidths = [];
    private double origin;

    public ColumnGrip(ColumnsBlockView row, int index)
    {
        this.row = row;
        this.index = index;
        Cursor = Cursors.SizeWE;
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters parameters) => new PointHitTestResult(this, parameters.HitPoint);

    protected override void OnMouseEnter(MouseEventArgs e) => InvalidateVisual();

    protected override void OnMouseLeave(MouseEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawing)
    {
        if (!IsMouseOver && !IsMouseCaptured) return;
        drawing.DrawRoundedRectangle(Ui.Accent(0.6), null, new Rect(RenderSize.Width / 2 - 1, 0, 2, RenderSize.Height), 1, 1);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        startWidths = row.Widths;
        origin = e.GetPosition(row).X;
        // The drag as a whole is one step to undo.
        row.Editor.BeginUndoGroup();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        row.Resize(index, e.GetPosition(row).X - origin, startWidths);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        InvalidateVisual();
        e.Handled = true;
    }
}

internal sealed class ColumnBlockView(Block block, BlockEditorController editor) : BlockView(block, editor)
{
    protected override double LayOut(double width) => Stack(ChildViews, 0, 0, width);
}

// Blocks without text

internal sealed class DividerBlockView(Block block, BlockEditorController editor) : BlockView(block, editor)
{
    public override double FirstLineHeight => 17;

    protected override double LayOut(double width) => 17;

    protected override void OnRender(DrawingContext drawing) =>
        drawing.DrawRectangle(Ui.Hairline, null, new Rect(0, Math.Round(RenderSize.Height / 2) - 0.5, RenderSize.Width, 1));

    protected override HitTestResult HitTestCore(PointHitTestParameters parameters) => WholeView(parameters);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => SelectOnClick(e);
}

internal sealed class ImageBlockView(Block block, BlockEditorController editor) : BlockView(block, editor)
{
    private ImageSource? image;
    private string? source;
    private string? label;

    /// <summary>True once the picture has been found and is being drawn.</summary>
    public bool HasPicture => image is not null;

    public override double FirstLineHeight => Math.Min(RenderSize.Height, 40);

    public override void Configure(Block block, int ordinal)
    {
        base.Configure(block, ordinal);
        var alt = block.Attrs.GetValueOrDefault("alt")?.StringValue;
        if (alt != label)
        {
            label = alt;
            InvalidateVisual();
        }
        ToolTip = block.Attrs.GetValueOrDefault("title")?.StringValue ?? alt;
        AutomationProperties.SetName(this, alt ?? "Image");
        if (source == block.ImageSource) return;
        source = block.ImageSource;
        image = null;
        InvalidateMeasure();
        InvalidateVisual();
        if (source is not { } wanted) return;
        Editor.LoadImage(wanted, loaded =>
        {
            // The answer may come late, when the block shows another picture.
            if (source != wanted) return;
            image = loaded;
            InvalidateMeasure();
            InvalidateVisual();
            Editor.BlockHeightChanged();
        });
    }

    /// <summary>The size the picture is drawn at: its own size, or as wide as there is room for.</summary>
    private Size DrawnSize(double width)
    {
        if (image is not { Width: > 0, Height: > 0 }) return new Size(width, 64);
        var scale = Math.Min(1, width / image.Width);
        return new Size(Math.Round(image.Width * scale), Math.Round(image.Height * scale));
    }

    protected override double LayOut(double width) => DrawnSize(width).Height + 8;

    protected override void OnRender(DrawingContext drawing)
    {
        var frame = new Rect(new Point(0, 4), DrawnSize(RenderSize.Width));
        if (image is null)
        {
            // No picture to show: say what was meant to be here.
            drawing.DrawRoundedRectangle(EditorTheme.Panel, null, frame, 6, 6);
            var text = Label(label ?? "Image", new EditorFont(EditorTheme.Prose, 12, FontWeights.Normal), EditorTheme.TextBrush(this, 0.6));
            drawing.DrawText(text, new Point(12, frame.Top + (frame.Height - text.Height) / 2));
            return;
        }
        drawing.PushClip(new RectangleGeometry(frame, 6, 6));
        drawing.DrawImage(image, frame);
        drawing.Pop();
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters parameters) => WholeView(parameters);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => SelectOnClick(e);
}

internal sealed class CodeBlockView : TextHoldingView
{
    public static readonly string[] Languages =
    [
        "plaintext", "bash", "c", "cpp", "csharp", "css", "go", "html", "java", "javascript", "json", "kotlin",
        "markdown", "python", "ruby", "rust", "sql", "swift", "typescript", "yaml",
    ];

    private readonly Button language;

    public CodeBlockView(Block block, BlockEditorController editor)
        : base(block, editor)
    {
        language = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 1, 6, 1),
            MinWidth = 0,
            MinHeight = 0,
            FontSize = 11,
            Opacity = 0.7,
            Focusable = false,
            Cursor = Cursors.Hand,
            ToolTip = "The language of this code",
        };
        language.Click += (_, _) => ChooseLanguage();
        Hold(language);
    }

    protected override string? Placeholder => null;

    public override double FirstLineHeight => 40;

    public override void Configure(Block block, int ordinal)
    {
        base.Configure(block, ordinal);
        if (!Equals(language.Content, block.Language))
        {
            language.Content = block.Language;
            InvalidateMeasure();
        }
        AutomationProperties.SetAutomationId(language, "Language:" + block.Id);
        AutomationProperties.SetName(language, "Language: " + block.Language);
        language.IsEnabled = IsEditable;
    }

    private void ChooseLanguage()
    {
        // A language this list does not have is still shown and kept.
        var names = Languages.Contains(Block.Language) ? Languages : [.. Languages, Block.Language];
        var menu = new ContextMenu { PlacementTarget = language, Placement = PlacementMode.Bottom };
        foreach (var name in names)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = name == Block.Language };
            item.Click += (_, _) => Editor.SetLanguage(Block.Id, name);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    protected override double LayOut(double width)
    {
        language.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = language.DesiredSize;
        PlaceAt(language, new Rect(Math.Max(width - size.Width - 6, 0), 4, size.Width, size.Height));
        var height = PlaceText(14, 26, Math.Max(width - 28, 40));
        return height + 26 + 12;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var frame = new Rect(0.5, 0.5, Math.Max(RenderSize.Width - 1, 0), Math.Max(RenderSize.Height - 1, 0));
        drawing.DrawRoundedRectangle(EditorTheme.Panel, new Pen(Ui.Hairline, 1), frame, 6, 6);
    }
}

/// <summary>A block from a newer version of the app. It is kept, and cannot be edited.</summary>
internal sealed class UnknownBlockView(Block block, BlockEditorController editor) : BlockView(block, editor)
{
    private static readonly Pen Dashed = MakeDashed();

    private static Pen MakeDashed()
    {
        var pen = new Pen(Brushes.Gray, 1) { DashStyle = new DashStyle([4, 3], 0) };
        pen.Freeze();
        return pen;
    }

    protected override double LayOut(double width) => 34;

    protected override void OnRender(DrawingContext drawing)
    {
        var frame = new Rect(0.5, 0.5, Math.Max(RenderSize.Width - 1, 0), Math.Max(RenderSize.Height - 1, 0));
        drawing.DrawRoundedRectangle(null, Dashed, frame, 6, 6);
        var text = Label(
            "This block needs a newer version of Heracles. It is kept as it is.",
            new EditorFont(EditorTheme.Prose, 12, FontWeights.Normal), EditorTheme.TextBrush(this, 0.6));
        text.MaxTextWidth = Math.Max(RenderSize.Width - 24, 20);
        text.MaxLineCount = 1;
        text.Trimming = TextTrimming.CharacterEllipsis;
        drawing.DrawText(text, new Point(12, 9));
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters parameters) => WholeView(parameters);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => SelectOnClick(e);
}
