using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heracles.Model;

namespace Heracles.Boards;

/// <summary>
/// Everything about one task, in a window of its own: its text, dates,
/// labels and reminders, its subtasks, and the comments on it. Opening a
/// subtask shows that subtask in the same window, with a way back.
///
/// What it shows and what each change does is <see cref="TaskDetail"/>'s.
/// This only draws that, in fields that are kept from one reload to the
/// next so that typing is never replaced by what is stored.
/// </summary>
internal sealed class TaskDetailWindow : Window
{
    private readonly AppModel model;
    private readonly TaskDetail detail;
    private readonly IDisposable registration;

    private readonly Button back;
    private readonly TextBlock path = new() { Opacity = 0.6, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly SmallCheck done;
    private readonly StoredBox title;
    private readonly TextBox notes;
    private readonly RadioButton[] priorities;
    private readonly DateRow due;
    private readonly DateRow deadline;
    private readonly StoredBox location;
    private readonly FrameworkElement moveLabel;
    private readonly Button makeCard;
    private readonly WrapPanel labels = new();
    private readonly EntryBox newLabel;
    private readonly StackPanel reminders = new();
    private readonly DatePicker reminderDay;
    private readonly TextBox reminderTime;
    private readonly TextBlock reminderProblem = new() { Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel subtasks = new();
    private readonly Dictionary<string, SubtaskRow> subtaskRows = [];
    private readonly EntryBox newSubtask;
    private readonly StackPanel comments = new();
    private readonly EntryBox newComment;

    private string? shownTaskId;
    private int shownVersion = -1;
    private string shownLabels = "\0";
    private string shownReminders = "\0";
    private string shownComments = "\0";
    private bool showing;
    private bool closing;

    /// <summary>
    /// Show a task. One window for each: asked for again, the window that
    /// has it comes forward.
    /// </summary>
    public static void Open(Window? owner, AppModel model, string todoListId, string taskId)
    {
        var app = Application.Current;
        if (app is not null)
        {
            foreach (var open in app.Windows.OfType<TaskDetailWindow>())
            {
                if (open.detail.TaskId != taskId && open.detail.RootTaskId != taskId) continue;
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return;
            }
        }
        var window = new TaskDetailWindow(model, todoListId, taskId);
        // The task may have gone between being drawn and being clicked.
        if (window.detail.Task is null)
        {
            window.Dispose();
            return;
        }
        // An owner that is not showing cannot own a window.
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        window.Show();
    }

    private TaskDetailWindow(AppModel model, string todoListId, string taskId)
    {
        this.model = model;
        detail = new TaskDetail(model, todoListId, taskId);

        Width = 560;
        Height = 640;
        MinWidth = 440;
        MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // The header: the way back, the tasks above this one, and what can be done with it.
        back = Ui.QuietButton(Labelled(0xE76B, "Back"), GoBack, "Back to the task this one is under").Id("TaskBack").Named("Back");
        back.Focusable = true;
        back.Margin = new Thickness(0, 0, 8, 0);
        path.Id("TaskPath");
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 76, Margin = new Thickness(8, 0, 0, 0) }.Id("TaskClose");
        close.Click += (_, _) => Leave();
        var delete = Ui.QuietButton(Labelled(0xE74D, "Delete"), Delete, "Delete this task and its subtasks").Id("TaskDelete").Named("Delete");
        delete.Focusable = true;

        var header = new DockPanel { Margin = new Thickness(12, 10, 12, 10), LastChildFill = true };
        DockPanel.SetDock(back, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(delete, Dock.Right);
        header.Children.Add(back);
        header.Children.Add(close);
        header.Children.Add(delete);
        header.Children.Add(path);

        // The task itself.
        done = new SmallCheck("Done", completed => detail.SetCompleted(completed)) { Margin = new Thickness(0, 9, 10, 0) };
        done.Id("TaskDone");
        title = new StoredBox("Task", "TaskTitle", text => detail.SetTitle(text), quiet: true);
        title.Box.FontSize = 20;
        title.Box.FontWeight = FontWeights.Bold;
        title.Box.TextWrapping = TextWrapping.Wrap;
        var heading = new DockPanel();
        DockPanel.SetDock(done, Dock.Left);
        heading.Children.Add(done);
        heading.Children.Add(title);

        notes = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 70,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
        }.Id("TaskNotes").Named("Notes");
        notes.TextChanged += (_, _) =>
        {
            if (!showing) detail.TypeNotes(notes.Text);
        };

        priorities = Enumerable.Range(1, 4).Select(MakePriority).ToArray();
        var priorityRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var choice in priorities) priorityRow.Children.Add(choice);

        due = new DateRow("Due", day => detail.SetDue(day));
        deadline = new DateRow("Deadline", day => detail.SetDeadline(day));
        location = new StoredBox("None", "TaskLocation", text => detail.SetLocation(text));

        // A subtask can become a card of its own.
        makeCard = new Button { Content = "Make it a card in…", HorizontalAlignment = HorizontalAlignment.Left }.Id("TaskMakeCard");
        makeCard.Click += (_, _) => ShowColumns();
        moveLabel = Caption("Move");

        var grid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddRow(grid, Caption("Priority"), priorityRow);
        AddRow(grid, Caption("Due"), due);
        AddRow(grid, Caption("Deadline"), deadline);
        AddRow(grid, Caption("Location"), location);
        AddRow(grid, moveLabel, makeCard);

        newLabel = new EntryBox("Add a label", "TaskNewLabel", text => detail.AddLabel(text), quiet: true) { Width = 120 };

        reminderDay = new DatePicker { Width = 150, Margin = new Thickness(0, 0, 8, 0) }.Id("TaskReminderDay").Named("Day of the new reminder");
        reminderTime = new TextBox { Width = 84, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center }
            .Id("TaskReminderTime").Named("Time of the new reminder");
        reminderTime.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            AddReminder();
            e.Handled = true;
        };
        var addReminder = new Button { Content = "Add" }.Id("TaskAddReminder").Named("Add reminder");
        addReminder.Click += (_, _) => AddReminder();
        reminderProblem.Id("TaskReminderProblem");
        var newReminder = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        newReminder.Children.Add(reminderDay);
        newReminder.Children.Add(reminderTime);
        newReminder.Children.Add(addReminder);

        newSubtask = new EntryBox("Add a subtask", "TaskNewSubtask", text => detail.AddSubtask(text), quiet: true);
        newComment = new EntryBox("Add a comment", "TaskNewComment", text => detail.AddComment(text)) { Margin = new Thickness(0, 4, 0, 0) };

        var fields = new StackPanel { Margin = new Thickness(20, 16, 20, 24) };
        fields.Children.Add(heading);
        fields.Children.Add(Section("Notes"));
        fields.Children.Add(notes);
        fields.Children.Add(grid);
        fields.Children.Add(Section("Labels"));
        labels.Children.Add(newLabel);
        fields.Children.Add(labels);
        fields.Children.Add(Section("Reminders"));
        fields.Children.Add(reminders);
        fields.Children.Add(newReminder);
        fields.Children.Add(reminderProblem);
        fields.Children.Add(Section("Subtasks"));
        fields.Children.Add(subtasks);
        fields.Children.Add(newSubtask);
        fields.Children.Add(Section("Comments"));
        fields.Children.Add(comments);
        fields.Children.Add(newComment);

        var scroll = new ScrollViewer
        {
            Content = fields,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        }.Id("TaskFields");
        var line = new Border { Height = 1, Background = Ui.Hairline };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(line, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(line);
        root.Children.Add(scroll);
        // The window's content is what the end-to-end checks look within.
        Content = new Landmark { Child = root }.Id("TaskDetail").Named("Task");

        registration = model.RegisterSaver(Keep);
        model.Changed += OnModelChanged;
        Closing += (_, _) =>
        {
            closing = true;
            Keep();
        };
        Closed += (_, _) => Dispose();

        shownVersion = model.BoardVersion(todoListId);
        if (detail.Reload()) Draw();
    }

    private void Dispose()
    {
        model.Changed -= OnModelChanged;
        registration.Dispose();
    }

    /// <summary>Close, unless that is already under way: saving on the way out can bring news of a change.</summary>
    private void Leave()
    {
        if (!closing) Close();
    }

    /// <summary>Keep what is typed and not yet saved: the window is closing, or the app is.</summary>
    private void Keep()
    {
        title.Commit();
        location.Commit();
        detail.Close();
    }

    /// <summary>Something changed somewhere. This window follows when it was its board, in any window.</summary>
    private void OnModelChanged()
    {
        // A list in the trash cannot be changed, and one deleted is not there to change.
        if (model.Library.Note(detail.TodoListId) is not { Trashed: false })
        {
            Leave();
            return;
        }
        var version = model.BoardVersion(detail.TodoListId);
        if (version == shownVersion) return;
        shownVersion = version;
        // The task, or one it was reached through, may have been deleted.
        if (!detail.Reload())
        {
            Leave();
            return;
        }
        Draw();
    }

    // Moving about

    private void OpenSubtask(string id)
    {
        title.Commit();
        location.Commit();
        detail.Open(id);
        Draw();
    }

    private void GoBack()
    {
        title.Commit();
        location.Commit();
        detail.Back();
        Draw();
    }

    private void Delete()
    {
        if (detail.Delete()) Leave();
        else Draw();
    }

    private void ShowColumns()
    {
        var menu = new ContextMenu { PlacementTarget = makeCard, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var column in detail.Columns)
        {
            var id = column.Id;
            menu.Items.Add(Ui.Item(column.Name, () =>
            {
                detail.MakeCard(id);
                Draw();
            }).Id("MakeCard:" + id));
        }
        menu.IsOpen = true;
    }

    private void AddReminder()
    {
        if (reminderDay.SelectedDate is not { } day)
        {
            Complain("Choose a day for the reminder.");
            return;
        }
        if (TaskDates.TimeOfDay(reminderTime.Text) is not { } time)
        {
            Complain("That is not a time of day. Try 9:30 or 17:00.");
            return;
        }
        Complain(null);
        if (!detail.AddReminder(TaskDates.At(DateOnly.FromDateTime(day), time))) Complain("There is already a reminder for then.");
    }

    private void Complain(string? problem)
    {
        reminderProblem.Text = problem ?? "";
        AutomationProperties.SetName(reminderProblem, problem ?? "");
        reminderProblem.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // Drawing

    private void Draw()
    {
        if (detail.Task is not { } task) return;
        showing = true;
        try
        {
            var isAnotherTask = shownTaskId != task.Id;
            shownTaskId = task.Id;

            Title = string.IsNullOrWhiteSpace(task.Title) ? "Untitled" : task.Title;
            back.Visibility = detail.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
            path.Text = detail.Breadcrumb;
            AutomationProperties.SetName(path, detail.Breadcrumb);

            done.Show(task.Completed);
            if (isAnotherTask)
            {
                // A different task gets fresh fields, not the last one's drafts.
                title.Show(task.Title);
                location.Show(task.Location ?? "");
                newLabel.Show("");
                newSubtask.Show("");
                newComment.Show("");
                var soon = DateTime.Now.AddHours(1);
                reminderDay.SelectedDate = soon.Date;
                reminderTime.Text = soon.ToString("t", CultureInfo.CurrentCulture);
                Complain(null);
            }
            else
            {
                title.ShowStored(task.Title);
                location.ShowStored(task.Location ?? "");
            }
            // What is being typed is what the task says its notes are, so this leaves typing alone.
            if (notes.Text != detail.Notes) notes.Text = detail.Notes;

            for (var index = 0; index < priorities.Length; index++)
            {
                var chosen = task.Priority == index + 1;
                priorities[index].IsChecked = chosen;
                AutomationProperties.SetItemStatus(priorities[index], chosen ? "chosen" : "");
            }
            due.Show(task.DueDate);
            deadline.Show(task.Deadline);
            makeCard.Visibility = moveLabel.Visibility = detail.CanMakeCard ? Visibility.Visible : Visibility.Collapsed;

            DrawLabels(task);
            DrawReminders(task);
            DrawSubtasks();
            DrawComments();
        }
        finally { showing = false; }
    }

    private void DrawLabels(TodoTask task)
    {
        var key = string.Join("\n", task.Labels);
        if (key == shownLabels) return;
        shownLabels = key;
        // The box for a new one stays where it is, with the keyboard if it has it.
        while (labels.Children.Count > 1) labels.Children.RemoveAt(0);
        var at = 0;
        foreach (var label in task.Labels)
        {
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }.Id("Label:" + label).Named(label);
            var chip = new StackPanel { Orientation = Orientation.Horizontal };
            chip.Children.Add(text);
            chip.Children.Add(RemoveButton("LabelRemove:" + label, "Remove " + label, () => detail.RemoveLabel(label)));
            labels.Children.Insert(at++, new Border
            {
                Child = chip,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(9, 1, 3, 1),
                Margin = new Thickness(0, 0, 6, 6),
                Background = Chip,
            });
        }
    }

    private void DrawReminders(TodoTask task)
    {
        var key = string.Join("\n", task.Reminders);
        if (key == shownReminders) return;
        shownReminders = key;
        reminders.Children.Clear();
        foreach (var reminder in task.Reminders)
        {
            var said = TaskDates.DateAndTime(reminder);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var bell = Ui.Glyph(((char)0xEA8F).ToString(), 12);
            bell.Margin = new Thickness(0, 0, 8, 0);
            bell.Opacity = 0.7;
            var remove = RemoveButton("ReminderRemove:" + reminder, "Remove the reminder for " + said, () => detail.RemoveReminder(reminder));
            DockPanel.SetDock(bell, Dock.Left);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(bell);
            row.Children.Add(remove);
            row.Children.Add(new TextBlock { Text = said, VerticalAlignment = VerticalAlignment.Center }.Id("Reminder:" + reminder).Named(said));
            reminders.Children.Add(row);
        }
    }

    private void DrawSubtasks()
    {
        foreach (var gone in subtaskRows.Keys.Except(detail.Subtasks.Select(task => task.Id)).ToList()) subtaskRows.Remove(gone);
        var wanted = new List<UIElement>();
        foreach (var subtask in detail.Subtasks)
        {
            if (!subtaskRows.TryGetValue(subtask.Id, out var row))
            {
                var id = subtask.Id;
                subtaskRows[id] = row = new SubtaskRow(
                    id,
                    completed => detail.SetCompleted(id, completed),
                    () => OpenSubtask(id),
                    () => detail.DeleteTask(id));
            }
            row.Update(subtask);
            wanted.Add(row);
        }
        BoardView.Place(subtasks, wanted);
    }

    private void DrawComments()
    {
        var key = string.Join("\n", detail.Comments.Select(comment => $"{comment.Id}\t{comment.UpdatedAt}\t{comment.Body}"));
        if (key == shownComments) return;
        shownComments = key;
        comments.Children.Clear();
        foreach (var comment in detail.Comments)
        {
            var id = comment.Id;
            var body = comment.Body;
            var edit = Ui.QuietButton(Ui.Glyph(((char)0xE70F).ToString(), 11), () => EditComment(id, body), "Edit").Id("CommentEdit:" + id).Named("Edit comment");
            var remove = RemoveButton("CommentDelete:" + id, "Delete comment", () => detail.DeleteComment(id));
            edit.Focusable = true;
            edit.Opacity = 0.6;

            var words = new StackPanel();
            words.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap }.Id("Comment:" + id).Named(body));
            words.Children.Add(new TextBlock { Text = TaskDates.DateAndTime(comment.CreatedAt), FontSize = 11.5, Opacity = 0.6 });

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Background = Brushes.Transparent };
            DockPanel.SetDock(remove, Dock.Right);
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(edit);
            row.Children.Add(words);

            var menu = new ContextMenu();
            menu.Items.Add(Ui.Item("Edit…", () => EditComment(id, body)));
            menu.Items.Add(Ui.Item("Delete", () => detail.DeleteComment(id)));
            row.ContextMenu = menu;
            comments.Children.Add(row);
        }
    }

    private void EditComment(string id, string body)
    {
        if (Prompts.Ask(this, "Edit comment", body, "Save") is { } edited) detail.EditComment(id, edited);
    }

    // Parts

    private static readonly Brush Chip = MakeChip();

    private static Brush MakeChip()
    {
        var brush = new SolidColorBrush(Colors.Gray) { Opacity = 0.22 };
        brush.Freeze();
        return brush;
    }

    private RadioButton MakePriority(int priority)
    {
        var choice = new RadioButton
        {
            Content = $"P{priority}",
            GroupName = "TaskPriority",
            MinWidth = 0,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
        }.Id($"TaskPriority:{priority}").Named($"Priority {priority}");
        choice.Checked += (_, _) =>
        {
            if (!showing) detail.SetPriority(priority);
        };
        return choice;
    }

    private static UIElement Labelled(int glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Ui.Glyph(((char)glyph).ToString(), 11);
        icon.Margin = new Thickness(0, 1, 5, 0);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static TextBlock Section(string name) => new()
    {
        Text = name,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 18, 0, 6),
    };

    private static TextBlock Caption(string name) => new()
    {
        Text = name,
        Opacity = 0.65,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static void AddRow(Grid grid, FrameworkElement caption, FrameworkElement field)
    {
        var index = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        caption.Margin = new Thickness(0, 0, 0, 10);
        field.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetRow(caption, index);
        Grid.SetRow(field, index);
        Grid.SetColumn(field, 1);
        grid.Children.Add(caption);
        grid.Children.Add(field);
    }

    internal static Button RemoveButton(string id, string name, Action remove)
    {
        var button = Ui.QuietButton(Ui.Glyph(((char)0xE711).ToString(), 9), remove, "Remove").Id(id).Named(name);
        button.Focusable = true;
        button.Opacity = 0.6;
        button.Margin = new Thickness(4, 0, 0, 0);
        return button;
    }

    /// <summary>
    /// A row for a date that may not be set: a calendar once it is, with a
    /// way to take it off, and a button to set it until then.
    /// </summary>
    private sealed class DateRow : StackPanel
    {
        private readonly Button set;
        private readonly DatePicker picker;
        private readonly Button clear;
        private bool showing;

        public DateRow(string name, Action<DateOnly?> chosen)
        {
            Orientation = Orientation.Horizontal;
            set = new Button { Content = "Set a date" }.Id($"Task{name}Set").Named($"Set a date: {name}");
            // Today, as on the Mac: the calendar then says which day is wanted.
            set.Click += (_, _) => chosen(DateOnly.FromDateTime(DateTime.Now));
            picker = new DatePicker { Width = 150 }.Id($"Task{name}").Named(name);
            picker.SelectedDateChanged += (_, _) =>
            {
                if (showing) return;
                chosen(picker.SelectedDate is { } day ? DateOnly.FromDateTime(day) : null);
            };
            clear = RemoveButton($"Task{name}Clear", $"Remove the date: {name}", () => chosen(null));
            Children.Add(set);
            Children.Add(picker);
            Children.Add(clear);
        }

        public void Show(string? stored)
        {
            var day = stored is null ? null : TaskDates.Day(stored);
            showing = true;
            try { picker.SelectedDate = day?.ToDateTime(TimeOnly.MinValue); }
            finally { showing = false; }
            set.Visibility = day is null ? Visibility.Visible : Visibility.Collapsed;
            picker.Visibility = clear.Visibility = day is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>One subtask in the list: a box to tick, its title (a click opens it), when it is due, and a way to delete it.</summary>
    private sealed class SubtaskRow : DockPanel
    {
        private readonly SmallCheck done;
        private readonly Button open;
        private readonly TextBlock title = new() { TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock due = new() { FontSize = 11.5, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

        public SubtaskRow(string id, Action<bool> tick, Action show, Action delete)
        {
            Margin = new Thickness(0, 0, 0, 4);
            done = new SmallCheck("Done", tick) { Margin = new Thickness(0, 3, 8, 0) };
            done.Id("SubtaskDone:" + id);
            open = new Button
            {
                Content = title,
                Template = BoardCardView.BareTemplate,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MinWidth = 0,
                MinHeight = 0,
            }.Id("Subtask:" + id);
            open.Click += (_, _) => show();
            var remove = RemoveButton("SubtaskRemove:" + id, "Delete subtask", delete);
            SetDock(done, Dock.Left);
            SetDock(remove, Dock.Right);
            SetDock(due, Dock.Right);
            Children.Add(done);
            Children.Add(remove);
            Children.Add(due);
            Children.Add(open);
        }

        public void Update(TodoTask subtask)
        {
            var name = string.IsNullOrWhiteSpace(subtask.Title) ? "Untitled" : subtask.Title;
            done.Show(subtask.Completed);
            title.Text = name;
            title.TextDecorations = subtask.Completed ? TextDecorations.Strikethrough : null;
            title.Opacity = subtask.Completed ? 0.55 : 1;
            due.Text = subtask.DueDate is { } stored ? TaskDates.Label(stored) : "";
            AutomationProperties.SetName(open, name);
            AutomationProperties.SetItemStatus(open, subtask.Completed ? "done" : "");
        }
    }
}
