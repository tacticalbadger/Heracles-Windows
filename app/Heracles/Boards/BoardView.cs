using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heracles.Model;

namespace Heracles.Boards;

/// <summary>A small check box that can be shown ticked without that counting as the user ticking it.</summary>
internal sealed class SmallCheck : CheckBox
{
    private bool showing;

    public SmallCheck(string name, Action<bool> told)
    {
        Template = Heracles.Editor.TextBlockView.CheckTemplate;
        Width = 16;
        Height = 16;
        MinWidth = 0;
        MinHeight = 0;
        Padding = new Thickness(0);
        Cursor = Cursors.Hand;
        VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetName(this, name);
        // By the mouse, the keyboard or automation: whichever, the model is told.
        Checked += (_, _) => Tell(true);
        Unchecked += (_, _) => Tell(false);

        void Tell(bool isChecked)
        {
            if (!showing) told(isChecked);
        }
    }

    public void Show(bool isChecked)
    {
        showing = true;
        try { IsChecked = isChecked; }
        finally { showing = false; }
        // What a screen reader, and the end-to-end checks, read its state from.
        AutomationProperties.SetItemStatus(this, isChecked ? "done" : "");
    }
}

/// <summary>
/// A todo list as a board: columns of cards. Drag a card to another column,
/// or onto a card to land in front of it. A card opens as a task.
///
/// Views are kept from one reload to the next, by column and by task, so
/// that a box being typed in keeps the keyboard when the board changes
/// under it.
/// </summary>
internal sealed class BoardView : ScrollViewer
{
    public const double ColumnWidth = 260;

    private readonly AppModel model;
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 16, 32, 16) };
    private readonly EntryBox newColumn;
    private readonly Dictionary<string, BoardColumnView> columns = [];
    private readonly Dictionary<string, BoardCardView> cards = [];
    private Note list;
    private int shownVersion = -1;
    private bool? shownTrashed;

    public Board Board { get; }

    public BoardView(AppModel model, string leafId, Note list, Action onFocus)
    {
        this.model = model;
        this.list = list;
        Board = new Board(model, list.Id);

        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Focusable = false;
        Content = row;
        this.Id("Board:" + leafId).Named("Board");

        newColumn = new EntryBox("New column", "NewColumn:" + leafId, Board.AddColumn)
        {
            Width = 160,
            VerticalAlignment = VerticalAlignment.Top,
        };
        // Working in a board makes its pane the one the next note opens in.
        GotKeyboardFocus += (_, _) => onFocus();
        PreviewMouseDown += (_, _) => onFocus();

        Update(list);
    }

    /// <summary>
    /// Show the board as it is now. Called whenever anything changes; the
    /// board is read again only when the change feed said this one changed,
    /// in any window.
    /// </summary>
    public void Update(Note current)
    {
        list = current;
        var version = model.BoardVersion(list.Id);
        if (version == shownVersion && shownTrashed == list.Trashed) return;
        shownVersion = version;
        shownTrashed = list.Trashed;
        // A list in the trash is shown and cannot be changed. (It can still be scrolled.)
        row.IsEnabled = !list.Trashed;
        Board.Reload();
        Draw();
    }

    private void Draw()
    {
        foreach (var gone in cards.Keys.Except(Board.Tasks.Select(task => task.Id)).ToList()) cards.Remove(gone);
        foreach (var gone in columns.Keys.Except(Board.Columns.Select(column => column.Id)).ToList()) columns.Remove(gone);

        var wanted = new List<UIElement>();
        foreach (var column in Board.Columns)
        {
            if (!columns.TryGetValue(column.Id, out var view)) columns[column.Id] = view = new BoardColumnView(this, column.Id);
            view.Update(column, Board.Cards(column.Id).Select(CardFor).ToList());
            wanted.Add(view);
        }
        if (!list.Trashed) wanted.Add(newColumn);
        Place(row, wanted);
    }

    private BoardCardView CardFor(TodoTask task)
    {
        if (!cards.TryGetValue(task.Id, out var view)) cards[task.Id] = view = new BoardCardView(this, task.Id);
        view.Update(task, Board.Facts(task));
        return view;
    }

    /// <summary>
    /// Make a panel hold these views in this order. Nothing is touched when it
    /// already does: a view taken out and put back loses the keyboard.
    /// </summary>
    internal static void Place(Panel panel, IReadOnlyList<UIElement> wanted)
    {
        if (panel.Children.Count == wanted.Count && panel.Children.Cast<UIElement>().SequenceEqual(wanted)) return;
        panel.Children.Clear();
        foreach (var view in wanted)
        {
            // A view moved to a new parent must leave the old one first.
            if (VisualTreeHelper.GetParent(view) is Panel old) old.Children.Remove(view);
            panel.Children.Add(view);
        }
    }

    public void OpenTask(string taskId) => TaskDetailWindow.Open(Window.GetWindow(this), model, list.Id, taskId);

    public void SetCompleted(string taskId, bool completed) => model.SetCompleted(taskId, completed);

    public void DeleteTask(string taskId) => model.DeleteTask(taskId);

    public void RenameColumn(string columnId, string name) => model.RenameColumn(columnId, name);

    public void DeleteColumn(string columnId) => model.DeleteColumn(columnId);

    internal static CardDrag? Dragged(DragEventArgs e) => CardDrag.From(e.Data);
}

/// <summary>One column of a board: its name, its cards, and a box for a new card.</summary>
internal sealed class BoardColumnView : Border
{
    private static readonly Brush Fill = Frozen(new SolidColorBrush(Colors.Gray) { Opacity = 0.14 });
    private static readonly Brush TargetedFill = Frozen(new SolidColorBrush(Colors.Gray) { Opacity = 0.28 });

    private readonly BoardView board;
    private readonly string columnId;
    private readonly StoredBox name;
    private readonly TextBlock count = new() { Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
    private readonly StackPanel cards = new();
    private string storedName = "";

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    public BoardColumnView(BoardView board, string columnId)
    {
        this.board = board;
        this.columnId = columnId;

        Width = BoardView.ColumnWidth;
        MinHeight = 120;
        Padding = new Thickness(10);
        Margin = new Thickness(0, 0, 12, 0);
        CornerRadius = new CornerRadius(10);
        Background = Fill;
        VerticalAlignment = VerticalAlignment.Top;
        AllowDrop = true;
        this.Id("Column:" + columnId);

        name = new StoredBox("Column name", "ColumnName:" + columnId, Rename, quiet: true);
        name.Box.FontWeight = FontWeights.SemiBold;
        name.Box.FontSize = 14;

        Button? more = null;
        more = Ui.QuietButton(Ui.Glyph(((char)0xE712).ToString(), 14), () => ShowMenu(more), "Column actions")
            .Id("ColumnMenu:" + columnId).Named("Column actions");
        more.Focusable = true;
        more.Opacity = 0.7;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(more, Dock.Right);
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(more);
        header.Children.Add(count);
        header.Children.Add(name);
        header.ContextMenuOpening += (_, e) =>
        {
            ShowMenu(null);
            e.Handled = true;
        };
        header.ContextMenu = new ContextMenu();

        var newCard = new EntryBox("Add a card", "NewCard:" + columnId, title => board.Board.AddCard(columnId, title), quiet: true);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(cards);
        stack.Children.Add(newCard);
        Child = stack;

        // A card let go over the column, and not over a card in it, goes to
        // its end. The column hears of the drag on its way down to whatever
        // is under the pointer, and answers for all of itself but its cards:
        // its name and its "Add a card" are text boxes, which would answer
        // "not here" to a card and let nothing through to the column.
        PreviewDragEnter += (_, e) => Target(e);
        PreviewDragOver += (_, e) => Target(e);
        PreviewDrop += (_, e) =>
        {
            if (BoardView.Dragged(e) is not { } drag || IsOverACard(e)) return;
            Background = Fill;
            board.Board.Drop(drag.TaskId, columnId, null);
            e.Handled = true;
        };
        // Heard even when something in the column answered, so the column stops looking aimed at.
        AddHandler(DragLeaveEvent, new DragEventHandler((_, _) => Background = Fill), handledEventsToo: true);
        AddHandler(DropEvent, new DragEventHandler((_, _) => Background = Fill), handledEventsToo: true);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LandmarkPeer(this);

    /// <summary>Whether the pointer of a drag is over one of this column's cards, which answers for itself.</summary>
    private bool IsOverACard(DragEventArgs e)
    {
        for (var at = e.OriginalSource as DependencyObject; at is not null && !ReferenceEquals(at, this);)
        {
            if (at is BoardCardView) return true;
            at = at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at);
        }
        return false;
    }

    private void Target(DragEventArgs e)
    {
        // Something that is not a card is none of the column's business: a
        // text box under the pointer may want it.
        if (BoardView.Dragged(e) is null) return;
        Background = TargetedFill;
        if (IsOverACard(e)) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    public void Update(TaskColumn column, IReadOnlyList<BoardCardView> views)
    {
        storedName = column.Name;
        name.ShowStored(column.Name);
        count.Text = views.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        AutomationProperties.SetName(this, column.Name);
        AutomationProperties.SetItemStatus(this, views.Count == 1 ? "1 card" : $"{views.Count} cards");
        foreach (var view in views) view.ColumnId = columnId;
        BoardView.Place(cards, views);
    }

    private void Rename(string typed)
    {
        // A column is never left with no name: the one it had comes back.
        if (typed.Trim().Length == 0)
        {
            name.Show(storedName);
            return;
        }
        if (typed.Trim() != storedName) board.RenameColumn(columnId, typed);
    }

    /// <summary>The column's menu: under its button, or where the mouse is when it was asked for there.</summary>
    private void ShowMenu(UIElement? under)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = under ?? this,
            Placement = under is null
                ? System.Windows.Controls.Primitives.PlacementMode.MousePoint
                : System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        menu.Items.Add(Ui.Item("Move Left", () => board.Board.MoveColumn(columnId, -1), enabled: board.Board.CanMoveColumn(columnId, -1)).Id("ColumnLeft:" + columnId));
        menu.Items.Add(Ui.Item("Move Right", () => board.Board.MoveColumn(columnId, 1), enabled: board.Board.CanMoveColumn(columnId, 1)).Id("ColumnRight:" + columnId));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item("Delete Column", () => board.DeleteColumn(columnId)).Id("ColumnDelete:" + columnId));
        menu.IsOpen = true;
    }
}

/// <summary>One card: a box to tick, its title, and the short facts about it. A click opens the task.</summary>
internal sealed class BoardCardView : Border
{
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly BoardView board;
    private readonly string taskId;
    private readonly SmallCheck done;
    private readonly Button open;
    private readonly TextBlock title = new() { TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel facts = new() { Margin = new Thickness(0, 3, 0, 0) };
    private Point? pressedAt;

    /// <summary>The column the card is in, as last drawn. A card dropped on this one joins it there.</summary>
    public string ColumnId { get; set; } = "";

    public BoardCardView(BoardView board, string taskId)
    {
        this.board = board;
        this.taskId = taskId;

        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(8);
        Margin = new Thickness(0, 0, 0, 8);
        BorderThickness = new Thickness(1);
        BorderBrush = Ui.Hairline;
        // A little lighter than the column it lies on, in light and in dark.
        SetResourceReference(BackgroundProperty, "ControlFillColorDefaultBrush");
        AllowDrop = true;

        done = new SmallCheck("Done", completed => board.SetCompleted(taskId, completed)) { Margin = new Thickness(0, 2, 8, 0) };
        done.Id("CardDone:" + taskId);

        var words = new StackPanel();
        words.Children.Add(title);
        words.Children.Add(facts);
        open = new Button
        {
            Content = words,
            Template = BareTemplate,
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0,
            MinHeight = 0,
        }.Id("Card:" + taskId);
        open.Click += (_, _) => board.OpenTask(taskId);

        var grid = new DockPanel();
        DockPanel.SetDock(done, Dock.Left);
        grid.Children.Add(done);
        grid.Children.Add(open);
        Child = grid;

        var menu = new ContextMenu();
        menu.Items.Add(Ui.Item("Open", () => board.OpenTask(taskId)));
        menu.Items.Add(Ui.Item("Delete Card", () => board.DeleteTask(taskId)));
        ContextMenu = menu;

        // A card is picked up once the mouse has moved a little with the button down.
        open.PreviewMouseLeftButtonDown += (_, e) => pressedAt = e.GetPosition(this);
        open.PreviewMouseLeftButtonUp += (_, _) => pressedAt = null;
        open.PreviewMouseMove += (_, e) =>
        {
            if (pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var moved = e.GetPosition(this) - start;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            pressedAt = null;
            // The button took the mouse when it was pressed. It gives it up, or it would open the task when let go.
            open.ReleaseMouseCapture();
            DragDrop.DoDragDrop(this, new CardDrag(taskId).Data(), DragDropEffects.Move);
        };

        // A card let go over this one lands in front of it.
        DragOver += (_, e) =>
        {
            e.Effects = BoardView.Dragged(e) is null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (BoardView.Dragged(e) is not { } drag) return;
            board.Board.Drop(drag.TaskId, ColumnId, taskId);
            e.Handled = true;
        };
    }

    /// <summary>A button that is only what it holds, and can be clicked anywhere within it.</summary>
    internal static ControlTemplate BareTemplate { get; } = MakeBareTemplate();

    private static ControlTemplate MakeBareTemplate()
    {
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        surface.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
        template.Seal();
        return template;
    }

    public void Update(TodoTask task, IReadOnlyList<CardFact> said)
    {
        var name = string.IsNullOrWhiteSpace(task.Title) ? "Untitled" : task.Title;
        done.Show(task.Completed);
        title.Text = name;
        title.TextDecorations = task.Completed ? TextDecorations.Strikethrough : null;
        title.Opacity = task.Completed ? 0.55 : 1;

        facts.Children.Clear();
        foreach (var fact in said) facts.Children.Add(Fact(fact));
        facts.Visibility = said.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        AutomationProperties.SetName(open, name);
        AutomationProperties.SetItemStatus(open, Status(task, said));
    }

    /// <summary>The card in words, for a screen reader and for the end-to-end checks.</summary>
    internal static string Status(TodoTask task, IReadOnlyList<CardFact> said)
    {
        var parts = new List<string>();
        if (task.Completed) parts.Add("done");
        foreach (var fact in said)
        {
            parts.Add(fact.Kind switch
            {
                CardFactKind.Due => "due " + fact.Text,
                CardFactKind.Subtasks => fact.Text + " subtasks",
                _ => fact.Text,
            });
        }
        return string.Join(", ", parts);
    }

    private static UIElement Fact(CardFact fact)
    {
        var text = new TextBlock { Text = fact.Text, FontSize = 11.5, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center };
        var glyph = fact.Kind switch
        {
            CardFactKind.Priority => 0xE7C1, // a flag
            CardFactKind.Due => 0xE787, // a calendar
            CardFactKind.Subtasks => 0xE9D5, // a list of ticks
            _ => 0,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        if (glyph != 0)
        {
            row.Children.Add(new TextBlock
            {
                Text = ((char)glyph).ToString(),
                FontFamily = Icons,
                FontSize = 10,
                Opacity = 0.65,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 3, 0),
            });
        }
        row.Children.Add(text);
        return row;
    }
}

/// <summary>
/// A card being dragged. It is carried under a name of its own, not as
/// text: a text box takes any text dropped on it for itself, and a card let
/// go over "Add a card" would type its own name into the box.
/// </summary>
internal sealed record CardDrag(string TaskId)
{
    /// <summary>The name the dragged card goes under, from one column or pane to another.</summary>
    public const string Format = "Heracles.Card";

    private const string Prefix = "heracles-card:";

    public string Text => Prefix + TaskId;

    public static CardDrag? Parse(string? text)
    {
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal) || text.Length == Prefix.Length) return null;
        return new CardDrag(text[Prefix.Length..]);
    }

    /// <summary>What is handed to the system when the drag begins.</summary>
    public IDataObject Data() => new DataObject(Format, Text);

    /// <summary>The card in what was dragged here, or null when it is something else.</summary>
    public static CardDrag? From(IDataObject data) => data.GetDataPresent(Format) ? Parse(data.GetData(Format) as string) : null;
}
