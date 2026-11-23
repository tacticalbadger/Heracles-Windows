using System.Globalization;
using Heracles.Model;

namespace Heracles.Boards;

internal enum CardFactKind
{
    Priority,
    Due,
    Subtasks,
    Label,
}

/// <summary>One of the short facts a card shows under its title.</summary>
internal readonly record struct CardFact(CardFactKind Kind, string Text);

/// <summary>
/// A todo list as a board: its columns and the cards in each, as last read,
/// and what a board decides when something is added or dropped. The view
/// draws this and calls it; nothing here is a view.
/// </summary>
internal sealed class Board(AppModel model, string todoListId)
{
    private Dictionary<string, (int Done, int Total)> counts = [];

    public string TodoListId => todoListId;

    public IReadOnlyList<TaskColumn> Columns { get; private set; } = [];

    /// <summary>Every task of the list, subtasks included.</summary>
    public IReadOnlyList<TodoTask> Tasks { get; private set; } = [];

    /// <summary>Read the board again. Called when the change feed says it changed, in any window.</summary>
    public void Reload()
    {
        Columns = model.Columns(todoListId);
        Tasks = model.Tasks(todoListId);
        counts = [];
        foreach (var task in Tasks)
        {
            if (task.ParentTaskId is not { } parent) continue;
            var (done, total) = counts.GetValueOrDefault(parent);
            counts[parent] = (done + (task.Completed ? 1 : 0), total + 1);
        }
    }

    /// <summary>A column's cards, top to bottom.</summary>
    public IReadOnlyList<TodoTask> Cards(string columnId) =>
        Tasks.Where(task => task.ColumnId == columnId && task.ParentTaskId is null).ToList();

    /// <summary>How many of a card's subtasks are done, and how many there are. Null when it has none.</summary>
    public (int Done, int Total)? Subtasks(string taskId) => counts.TryGetValue(taskId, out var count) ? count : null;

    /// <summary>
    /// What a card says under its title: its priority unless it is the
    /// lowest, when it is due, how its subtasks stand, and its labels.
    /// </summary>
    public IReadOnlyList<CardFact> Facts(
        TodoTask card, DateTimeOffset? now = null, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        var facts = new List<CardFact>();
        if (card.Priority < 4) facts.Add(new CardFact(CardFactKind.Priority, $"P{card.Priority}"));
        if (card.DueDate is { } due) facts.Add(new CardFact(CardFactKind.Due, TaskDates.Label(due, now, culture, zone)));
        if (Subtasks(card.Id) is { } subtasks) facts.Add(new CardFact(CardFactKind.Subtasks, $"{subtasks.Done}/{subtasks.Total}"));
        facts.AddRange(card.Labels.Select(label => new CardFact(CardFactKind.Label, label)));
        return facts;
    }

    /// <summary>Add a column at the right-hand end. With no name it is "Untitled", as on the Mac.</summary>
    public void AddColumn(string name) => model.AddColumn(todoListId, name.Trim());

    /// <summary>Add a card at the foot of a column. False when there was no title to give it.</summary>
    public bool AddCard(string columnId, string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0) return false;
        model.AddCard(todoListId, columnId, trimmed);
        return true;
    }

    public bool CanMoveColumn(string columnId, int by)
    {
        var from = Columns.ToList().FindIndex(column => column.Id == columnId);
        if (from < 0) return false;
        var to = from + by;
        return to != from && to >= 0 && to < Columns.Count;
    }

    public void MoveColumn(string columnId, int by) => model.MoveColumn(todoListId, columnId, by);

    /// <summary>
    /// A card let go over a column: in front of the card it was dropped on,
    /// or at the column's end. False when nothing was moved.
    /// </summary>
    public bool Drop(string taskId, string columnId, string? beforeId)
    {
        if (taskId == beforeId) return false;
        // Only a task of this board: what is dragged is text, and text can come from anywhere.
        if (Tasks.All(task => task.Id != taskId) || Columns.All(column => column.Id != columnId)) return false;
        model.MoveCard(taskId, columnId, beforeId);
        return true;
    }
}
