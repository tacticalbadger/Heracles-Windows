namespace Heracles.Model;

/// <summary>
/// Board actions. Each is one write in the core, and the change feed tells
/// every board view of that todo list to reload.
/// </summary>
internal sealed partial class AppModel
{
    public IReadOnlyList<TaskColumn> Columns(string todoListId)
    {
        try { return Store.ListColumns(todoListId); }
        catch (UniffiException) { return []; }
    }

    public IReadOnlyList<TodoTask> Tasks(string todoListId)
    {
        try { return Store.ListTasks(todoListId); }
        catch (UniffiException) { return []; }
    }

    public void AddColumn(string todoListId, string name) => Attempt(() => Store.CreateColumn(todoListId, name));

    public void RenameColumn(string id, string name) => Attempt(() => Store.RenameColumn(id, name));

    public void DeleteColumn(string id) => Attempt(() => Store.DeleteColumn(id));

    /// <summary>
    /// Move a column left (a negative number) or right by so many places. It
    /// stops at the ends. The Mac's board cannot reorder its columns; the
    /// core can, and the design asks for it.
    /// </summary>
    public void MoveColumn(string todoListId, string columnId, int by)
    {
        var order = Columns(todoListId).Select(column => column.Id).ToList();
        var from = order.IndexOf(columnId);
        if (from < 0) return;
        var to = Math.Clamp(from + by, 0, order.Count - 1);
        if (to == from) return;
        order.RemoveAt(from);
        order.Insert(to, columnId);
        Attempt(() => Store.ReorderColumns(todoListId, order.ToArray()));
    }

    public void AddCard(string todoListId, string columnId, string title) =>
        Attempt(() => Store.CreateTask(new NewTask(TodoListId: todoListId, ColumnId: columnId, Title: title)));

    public void SetCompleted(string taskId, bool completed) => UpdateTask(taskId, new TaskPatch(Completed: completed));

    public void RenameTask(string taskId, string title) => UpdateTask(taskId, new TaskPatch(Title: title));

    /// <summary>Put a card in a column, before another card or at the end.</summary>
    public void MoveCard(string taskId, string columnId, string? beforeId = null) =>
        Attempt(() => Store.MoveTask(taskId, columnId, null, beforeId));

    public void DeleteTask(string taskId) => Attempt(() => Store.DeleteTask(taskId));

    public void UpdateTask(string taskId, TaskPatch patch) => Attempt(() => Store.UpdateTask(taskId, patch));

    public void AddSubtask(TodoTask parent, string title) =>
        Attempt(() => Store.CreateTask(new NewTask(TodoListId: parent.TodoListId, ParentTaskId: parent.Id, Title: title)));

    public IReadOnlyList<TaskComment> Comments(string taskId)
    {
        try { return Store.ListComments(taskId); }
        catch (UniffiException) { return []; }
    }

    public void AddComment(string taskId, string body) => Attempt(() => Store.CreateComment(taskId, body));

    public void UpdateComment(string commentId, string body) => Attempt(() => Store.UpdateComment(commentId, body));

    public void DeleteComment(string commentId) => Attempt(() => Store.DeleteComment(commentId));
}
