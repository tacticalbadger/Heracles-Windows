using System.Windows.Threading;
using Heracles.Model;

namespace Heracles.Boards;

/// <summary>
/// Everything about one task, as the task window shows it: the task, its
/// subtasks, the comments on it, and the way down into a subtask and back.
/// Each change is saved as it is made. The window draws this and calls it;
/// nothing here is a view.
/// </summary>
internal sealed class TaskDetail
{
    /// <summary>How long typing in the notes must pause before they are saved.</summary>
    public static readonly TimeSpan NotesDelay = TimeSpan.FromMilliseconds(600);

    private readonly AppModel model;
    private readonly Action<Action, TimeSpan> later;

    /// <summary>The subtasks drilled into, outermost first.</summary>
    private readonly List<string> path = [];

    private IReadOnlyList<TodoTask> tasks = [];

    /// <summary>Notes typed and not yet saved, and the task they were typed for.</summary>
    private (string TaskId, string Text)? typedNotes;

    /// <summary>Goes up with every key, so that only the last save put off is the one that runs.</summary>
    private int notesNumber;

    public string TodoListId { get; }

    /// <summary>The task the window was opened on, or a subtask of it that has since become a card.</summary>
    public string RootTaskId { get; private set; }

    /// <param name="later">How something is put off for a while. Left out, a timer on this thread's dispatcher.</param>
    public TaskDetail(AppModel model, string todoListId, string rootTaskId, Action<Action, TimeSpan>? later = null)
    {
        this.model = model;
        this.later = later ?? OnATimer;
        TodoListId = todoListId;
        RootTaskId = rootTaskId;
    }

    private static void OnATimer(Action work, TimeSpan after)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = after };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            work();
        };
        timer.Start();
    }

    /// <summary>The task being shown.</summary>
    public string TaskId => path.Count > 0 ? path[^1] : RootTaskId;

    /// <summary>The task being shown, as last read. Null once it has gone.</summary>
    public TodoTask? Task { get; private set; }

    public IReadOnlyList<TodoTask> Subtasks { get; private set; } = [];

    public IReadOnlyList<TaskColumn> Columns { get; private set; } = [];

    public IReadOnlyList<TaskComment> Comments { get; private set; } = [];

    public bool CanGoBack => path.Count > 0;

    /// <summary>A subtask can become a card of its own.</summary>
    public bool CanMakeCard => Task?.ParentTaskId is not null && Columns.Count > 0;

    /// <summary>The chain of tasks above this one, outermost first.</summary>
    public string Breadcrumb
    {
        get
        {
            var above = new[] { RootTaskId }.Concat(path).SkipLast(1);
            var titles = above
                .Select(id => tasks.FirstOrDefault(task => task.Id == id))
                .Where(task => task is not null)
                .Select(task => string.IsNullOrWhiteSpace(task!.Title) ? "Untitled" : task.Title);
            // U+203A, a single angle pointing right.
            return string.Join($" {(char)0x203A} ", titles);
        }
    }

    /// <summary>The notes to show: what is being typed, or else what is stored.</summary>
    public string Notes => typedNotes is { } typed && typed.TaskId == TaskId ? typed.Text : Task?.Description ?? "";

    /// <summary>
    /// Read everything again. Called when another task is shown and whenever
    /// the board changes, in this window or any other. False when the task
    /// the window was opened on has gone: the window should close.
    /// </summary>
    public bool Reload()
    {
        tasks = model.Tasks(TodoListId);
        Columns = model.Columns(TodoListId);
        var byId = tasks.ToDictionary(task => task.Id);

        var chain = new List<string> { RootTaskId };
        chain.AddRange(path);
        // A subtask that has become a card is no longer under anything: the
        // window starts from it, and there is no way back up.
        for (var index = chain.Count - 1; index > 0; index--)
        {
            if (!byId.TryGetValue(chain[index], out var member) || member.ParentTaskId is not null) continue;
            chain.RemoveRange(0, index);
            RootTaskId = chain[0];
            break;
        }

        path.Clear();
        if (!byId.ContainsKey(RootTaskId))
        {
            Task = null;
            Subtasks = [];
            Comments = [];
            return false;
        }
        // The way down holds for as long as each task is there, under the one before.
        var parent = RootTaskId;
        foreach (var id in chain.Skip(1))
        {
            if (!byId.TryGetValue(id, out var member) || member.ParentTaskId != parent) break;
            path.Add(id);
            parent = id;
        }

        Task = byId[TaskId];
        Subtasks = tasks.Where(task => task.ParentTaskId == TaskId).ToList();
        Comments = model.Comments(TaskId);
        return true;
    }

    // Moving about

    /// <summary>Show a subtask of the task being shown, in the same window.</summary>
    public void Open(string subtaskId)
    {
        if (Subtasks.All(task => task.Id != subtaskId)) return;
        SaveNotes();
        path.Add(subtaskId);
        Reload();
    }

    public void Back()
    {
        if (path.Count == 0) return;
        SaveNotes();
        path.RemoveAt(path.Count - 1);
        Reload();
    }

    /// <summary>The window is closing: what was typed and not yet saved is saved now.</summary>
    public void Close() => SaveNotes();

    /// <summary>
    /// Delete the task being shown, with its subtasks. True when that was the
    /// task the window was opened on, and the window should close.
    /// </summary>
    public bool Delete()
    {
        if (Task is null) return true;
        // Nothing is saved to a task that is going.
        if (typedNotes?.TaskId == TaskId) typedNotes = null;
        model.DeleteTask(TaskId);
        if (path.Count == 0) return true;
        path.RemoveAt(path.Count - 1);
        Reload();
        return false;
    }

    // The fields

    private void Patch(TaskPatch patch)
    {
        if (Task is null) return;
        model.UpdateTask(TaskId, patch);
        Reload();
    }

    public void SetCompleted(bool completed)
    {
        if (Task is not null) SetCompleted(TaskId, completed);
    }

    /// <summary>Tick the task being shown, or one of its subtasks.</summary>
    public void SetCompleted(string taskId, bool completed)
    {
        if (Task is null) return;
        model.SetCompleted(taskId, completed);
        Reload();
    }

    public void SetTitle(string title)
    {
        if (Task is not null && title != Task.Title) Patch(new TaskPatch(Title: title));
    }

    public void SetPriority(int priority)
    {
        if (Task is null || priority is < 1 or > 4 || priority == Task.Priority) return;
        Patch(new TaskPatch(Priority: (byte)priority));
    }

    /// <summary>Nine in the morning on the day, or no due date at all.</summary>
    public void SetDue(DateOnly? day)
    {
        if (Task is null) return;
        var stored = day is { } chosen ? TaskDates.DueDate(chosen) : null;
        if (stored == Task.DueDate) return;
        Patch(stored is null ? new TaskPatch(ClearDueDate: true) : new TaskPatch(DueDate: stored));
    }

    /// <summary>Five in the afternoon on the day, or no deadline at all.</summary>
    public void SetDeadline(DateOnly? day)
    {
        if (Task is null) return;
        var stored = day is { } chosen ? TaskDates.Deadline(chosen) : null;
        if (stored == Task.Deadline) return;
        Patch(stored is null ? new TaskPatch(ClearDeadline: true) : new TaskPatch(Deadline: stored));
    }

    public void SetLocation(string location)
    {
        if (Task is null) return;
        var trimmed = location.Trim();
        if (trimmed == (Task.Location ?? "")) return;
        Patch(trimmed.Length == 0 ? new TaskPatch(ClearLocation: true) : new TaskPatch(Location: trimmed));
    }

    public void AddLabel(string label)
    {
        if (Task is null) return;
        var trimmed = label.Trim();
        if (trimmed.Length == 0 || Task.Labels.Contains(trimmed)) return;
        Patch(new TaskPatch(Labels: [.. Task.Labels, trimmed]));
    }

    public void RemoveLabel(string label)
    {
        if (Task is null || !Task.Labels.Contains(label)) return;
        Patch(new TaskPatch(Labels: Task.Labels.Where(existing => existing != label).ToArray()));
    }

    /// <summary>Add a reminder. False when the task already has one for that moment.</summary>
    public bool AddReminder(DateTimeOffset when)
    {
        if (Task is null) return false;
        var stored = TaskDates.Format(when);
        if (Task.Reminders.Contains(stored)) return false;
        Patch(new TaskPatch(Reminders: [.. Task.Reminders, stored]));
        return true;
    }

    public void RemoveReminder(string stored)
    {
        if (Task is null || !Task.Reminders.Contains(stored)) return;
        Patch(new TaskPatch(Reminders: Task.Reminders.Where(existing => existing != stored).ToArray()));
    }

    // Notes

    /// <summary>The notes were typed in. They are saved once typing pauses, not on every key.</summary>
    public void TypeNotes(string text)
    {
        if (Task is null) return;
        typedNotes = (TaskId, text);
        var number = ++notesNumber;
        later(
            () =>
            {
                if (number == notesNumber) SaveNotes();
            },
            NotesDelay);
    }

    /// <summary>Save now the notes that were typed, if any were.</summary>
    public void SaveNotes()
    {
        notesNumber++;
        if (typedNotes is not { } typed) return;
        typedNotes = null;
        // The task may have gone while the notes waited. Then there is
        // nothing to save them to, and nothing to tell the user about.
        TodoTask? stored;
        try { stored = model.Store.GetTask(typed.TaskId); }
        catch (UniffiException) { return; }
        if (stored is null || stored.Description == typed.Text) return;
        model.UpdateTask(typed.TaskId, new TaskPatch(Description: typed.Text));
        Reload();
    }

    // Subtasks

    public void AddSubtask(string title)
    {
        if (Task is null) return;
        var trimmed = title.Trim();
        if (trimmed.Length == 0) return;
        model.AddSubtask(Task, trimmed);
        Reload();
    }

    /// <summary>Delete a subtask of the task being shown.</summary>
    public void DeleteTask(string taskId)
    {
        if (Subtasks.All(task => task.Id != taskId)) return;
        model.DeleteTask(taskId);
        Reload();
    }

    /// <summary>Make the subtask being shown a card of its own, at the foot of a column.</summary>
    public void MakeCard(string columnId)
    {
        if (!CanMakeCard) return;
        SaveNotes();
        model.MoveCard(TaskId, columnId);
        Reload();
    }

    // Comments

    public void AddComment(string body)
    {
        if (Task is null) return;
        var trimmed = body.Trim();
        if (trimmed.Length == 0) return;
        model.AddComment(TaskId, trimmed);
        Reload();
    }

    /// <summary>Change a comment's words. One left with no words is left as it was: deleting is another thing.</summary>
    public void EditComment(string commentId, string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0 || Comments.All(comment => comment.Id != commentId)) return;
        model.UpdateComment(commentId, trimmed);
        Reload();
    }

    public void DeleteComment(string commentId)
    {
        if (Comments.All(comment => comment.Id != commentId)) return;
        model.DeleteComment(commentId);
        Reload();
    }
}
