using Heracles.Boards;
using Heracles.Model;

namespace Heracles.Tests;

/// <summary>A todo list on a real core in a throwaway folder, for the board and task tests.</summary>
public abstract class BoardFixture : IDisposable
{
    private readonly TestLibrary library = new();

    internal AppModel Model { get; }

    internal Note List { get; }

    protected BoardFixture()
    {
        Model = new AppModel(library.Store);
        List = Model.CreateNote(NoteKind.TodoList, Model.Library.Notebooks[0].Note.Id, "Packing")!;
    }

    public void Dispose()
    {
        Model.Dispose();
        library.Dispose();
    }

    internal Store Store => library.Store;

    internal string[] ColumnNames => Model.Columns(List.Id).Select(column => column.Name).ToArray();

    internal TaskColumn Column(string name) => Model.Columns(List.Id).Single(column => column.Name == name);

    /// <summary>The titles of a column's cards, top to bottom.</summary>
    internal string[] Cards(string column)
    {
        var id = Column(column).Id;
        return Model.Tasks(List.Id).Where(task => task.ColumnId == id).Select(task => task.Title).ToArray();
    }

    internal TodoTask Task(string title) => Model.Tasks(List.Id).Single(task => task.Title == title);

    internal TodoTask AddCard(string column, string title)
    {
        Model.AddCard(List.Id, Column(column).Id, title);
        return Task(title);
    }
}

/// <summary>The board actions of the model: each is one write in the core.</summary>
public sealed class AppModelBoardTests : BoardFixture
{
    [Fact]
    public void ANewTodoListHasItsThreeColumns()
    {
        Assert.Equal(["Up Next", "Active", "Done"], ColumnNames);
        Assert.Empty(Model.Tasks(List.Id));
    }

    [Fact]
    public void ColumnsAreAddedRenamedAndDeleted()
    {
        Model.AddColumn(List.Id, "Waiting");
        Assert.Equal(["Up Next", "Active", "Done", "Waiting"], ColumnNames);

        Model.RenameColumn(Column("Waiting").Id, "Blocked");
        Assert.Equal(["Up Next", "Active", "Done", "Blocked"], ColumnNames);

        AddCard("Blocked", "Visa");
        Model.DeleteColumn(Column("Blocked").Id);
        Assert.Equal(["Up Next", "Active", "Done"], ColumnNames);
        // Its cards are not lost: the first column takes them.
        Assert.Equal(["Visa"], Cards("Up Next"));
    }

    [Fact]
    public void AColumnMovesLeftAndRightAndStopsAtTheEnds()
    {
        var active = Column("Active").Id;
        Model.MoveColumn(List.Id, active, -1);
        Assert.Equal(["Active", "Up Next", "Done"], ColumnNames);

        Model.MoveColumn(List.Id, active, -1);
        Assert.Equal(["Active", "Up Next", "Done"], ColumnNames);

        Model.MoveColumn(List.Id, active, 2);
        Assert.Equal(["Up Next", "Done", "Active"], ColumnNames);

        Model.MoveColumn(List.Id, active, 5);
        Assert.Equal(["Up Next", "Done", "Active"], ColumnNames);
        Assert.Null(Model.LastError);
    }

    [Fact]
    public void MovingAColumnThatIsNotThereChangesNothing()
    {
        Model.MoveColumn(List.Id, "no-such-column", 1);
        Assert.Equal(["Up Next", "Active", "Done"], ColumnNames);
    }

    [Fact]
    public void CardsAreAddedTickedRenamedMovedAndDeleted()
    {
        var tent = AddCard("Up Next", "Tent");
        AddCard("Up Next", "Stove");
        AddCard("Up Next", "Map");
        Assert.Equal(["Tent", "Stove", "Map"], Cards("Up Next"));

        Model.SetCompleted(tent.Id, true);
        Assert.True(Task("Tent").Completed);

        Model.RenameTask(tent.Id, "Big tent");
        Assert.Equal(["Big tent", "Stove", "Map"], Cards("Up Next"));

        // In front of another card of its own column.
        Model.MoveCard(Task("Map").Id, Column("Up Next").Id, tent.Id);
        Assert.Equal(["Map", "Big tent", "Stove"], Cards("Up Next"));

        // To the end of another column.
        Model.MoveCard(tent.Id, Column("Done").Id);
        Assert.Equal(["Map", "Stove"], Cards("Up Next"));
        Assert.Equal(["Big tent"], Cards("Done"));

        Model.DeleteTask(tent.Id);
        Assert.Empty(Cards("Done"));
    }

    [Fact]
    public void ATaskHasSubtasksAndComments()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        var pegs = Task("Pegs");
        Assert.Equal(tent.Id, pegs.ParentTaskId);
        Assert.Null(pegs.ColumnId);

        Model.AddComment(tent.Id, "Borrow one?");
        var comment = Assert.Single(Model.Comments(tent.Id));
        Assert.Equal("Borrow one?", comment.Body);

        Model.UpdateComment(comment.Id, "Borrowed.");
        Assert.Equal("Borrowed.", Assert.Single(Model.Comments(tent.Id)).Body);

        Model.DeleteComment(comment.Id);
        Assert.Empty(Model.Comments(tent.Id));
    }

    [Fact]
    public void EveryBoardWriteRaisesTheBoardsVersion()
    {
        var before = Model.BoardVersion(List.Id);
        var tent = AddCard("Up Next", "Tent");
        var afterCard = Model.BoardVersion(List.Id);
        Assert.True(afterCard > before);

        Model.AddComment(tent.Id, "A word");
        Assert.True(Model.BoardVersion(List.Id) > afterCard);
    }

    [Fact]
    public void AWriteThatFailsSaysWhy()
    {
        Model.AddCard(List.Id, "no-such-column", "Lost");
        Assert.NotNull(Model.LastError);
        Assert.Empty(Model.Tasks(List.Id));
    }

    [Fact]
    public void AListThatIsNotThereHasNoColumnsAndNoTasks()
    {
        Assert.Empty(Model.Columns("no-such-list"));
        Assert.Empty(Model.Tasks("no-such-list"));
        Assert.Empty(Model.Comments("no-such-task"));
    }
}

/// <summary>What a board shows and decides.</summary>
public sealed class BoardTests : BoardFixture
{
    private Board Open()
    {
        var board = new Board(Model, List.Id);
        board.Reload();
        return board;
    }

    [Fact]
    public void ItShowsColumnsAndTheCardsInEach()
    {
        AddCard("Up Next", "Tent");
        AddCard("Active", "Stove");
        AddCard("Up Next", "Map");
        var board = Open();

        Assert.Equal(["Up Next", "Active", "Done"], board.Columns.Select(column => column.Name));
        Assert.Equal(["Tent", "Map"], board.Cards(Column("Up Next").Id).Select(card => card.Title));
        Assert.Equal(["Stove"], board.Cards(Column("Active").Id).Select(card => card.Title));
        Assert.Empty(board.Cards(Column("Done").Id));
    }

    [Fact]
    public void ASubtaskIsNotACard()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        Assert.Equal(["Tent"], Open().Cards(Column("Up Next").Id).Select(card => card.Title));
    }

    [Fact]
    public void ACardKnowsHowManyOfItsSubtasksAreDone()
    {
        var tent = AddCard("Up Next", "Tent");
        var stove = AddCard("Up Next", "Stove");
        Model.AddSubtask(tent, "Pegs");
        Model.AddSubtask(tent, "Mallet");
        Model.SetCompleted(Task("Pegs").Id, true);
        var board = Open();

        Assert.Equal((1, 2), board.Subtasks(tent.Id));
        Assert.Null(board.Subtasks(stove.Id));
    }

    [Fact]
    public void ItSeesNothingNewUntilItReloads()
    {
        var board = Open();
        AddCard("Up Next", "Tent");
        Assert.Empty(board.Cards(Column("Up Next").Id));
        board.Reload();
        Assert.Single(board.Cards(Column("Up Next").Id));
    }

    [Fact]
    public void TwoBoardsOnOneListAgreeAfterAReload()
    {
        var first = Open();
        var second = Open();
        first.AddCard(Column("Up Next").Id, "Tent");
        second.Reload();
        Assert.Equal(["Tent"], second.Cards(Column("Up Next").Id).Select(card => card.Title));
    }

    [Fact]
    public void ANewCardsTitleIsTrimmedAndABlankOneMakesNoCard()
    {
        var board = Open();
        var column = Column("Up Next").Id;
        Assert.True(board.AddCard(column, "  Tent  "));
        Assert.False(board.AddCard(column, "   "));
        Assert.False(board.AddCard(column, ""));
        Assert.Equal(["Tent"], Cards("Up Next"));
    }

    [Fact]
    public void ANewColumnWithNoNameIsCalledUntitledAsOnTheMac()
    {
        var board = Open();
        board.AddColumn("  Waiting ");
        board.AddColumn("");
        Assert.Equal(["Up Next", "Active", "Done", "Waiting", "Untitled"], ColumnNames);
    }

    [Fact]
    public void ACardDroppedOnACardLandsInFrontOfIt()
    {
        var tent = AddCard("Up Next", "Tent");
        var stove = AddCard("Up Next", "Stove");
        var map = AddCard("Active", "Map");
        var board = Open();

        Assert.True(board.Drop(map.Id, Column("Up Next").Id, stove.Id));
        Assert.Equal(["Tent", "Map", "Stove"], Cards("Up Next"));
        Assert.Empty(Cards("Active"));

        Assert.True(board.Drop(stove.Id, Column("Up Next").Id, tent.Id));
        Assert.Equal(["Stove", "Tent", "Map"], Cards("Up Next"));
    }

    [Fact]
    public void ACardDroppedOnAColumnGoesToItsEnd()
    {
        var tent = AddCard("Up Next", "Tent");
        AddCard("Done", "Stove");
        var board = Open();
        Assert.True(board.Drop(tent.Id, Column("Done").Id, null));
        Assert.Equal(["Stove", "Tent"], Cards("Done"));
    }

    [Fact]
    public void ACardDroppedOnItselfStaysWhereItIs()
    {
        var tent = AddCard("Up Next", "Tent");
        AddCard("Up Next", "Stove");
        var board = Open();
        var version = Model.BoardVersion(List.Id);
        Assert.False(board.Drop(tent.Id, Column("Up Next").Id, tent.Id));
        Assert.Equal(["Tent", "Stove"], Cards("Up Next"));
        Assert.Equal(version, Model.BoardVersion(List.Id));
    }

    [Fact]
    public void SomethingThatIsNotACardOfThisBoardCannotBeDropped()
    {
        var board = Open();
        Assert.False(board.Drop("no-such-task", Column("Up Next").Id, null));
        Assert.Null(Model.LastError);
    }

    [Fact]
    public void AColumnSaysWhichWaysItCanMove()
    {
        var board = Open();
        Assert.False(board.CanMoveColumn(Column("Up Next").Id, -1));
        Assert.True(board.CanMoveColumn(Column("Up Next").Id, 1));
        Assert.True(board.CanMoveColumn(Column("Active").Id, -1));
        Assert.False(board.CanMoveColumn(Column("Done").Id, 1));
        Assert.False(board.CanMoveColumn("no-such-column", 1));
    }

    [Fact]
    public void WhatACardSaysAboutItself()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.UpdateTask(tent.Id, new TaskPatch(Priority: 2, DueDate: "2026-12-01T09:00:00.000Z", Labels: ["camping", "heavy"]));
        Model.AddSubtask(tent, "Pegs");
        var board = Open();
        var now = TaskDates.Parse("2026-11-23T12:00:00Z")!.Value;
        var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");

        var facts = board.Facts(board.Tasks.Single(task => task.Id == tent.Id), now, british, TimeZoneInfo.Utc);
        Assert.Equal(["P2", "1 Dec", "0/1", "camping", "heavy"], facts.Select(fact => fact.Text));
        Assert.Equal(
            [CardFactKind.Priority, CardFactKind.Due, CardFactKind.Subtasks, CardFactKind.Label, CardFactKind.Label],
            facts.Select(fact => fact.Kind));

        // The lowest priority is no priority, and says nothing.
        var stove = AddCard("Up Next", "Stove");
        board.Reload();
        Assert.Empty(board.Facts(board.Tasks.Single(task => task.Id == stove.Id), now, british, TimeZoneInfo.Utc));
    }
}

/// <summary>What the task window shows and decides.</summary>
public sealed class TaskDetailTests : BoardFixture
{
    private readonly List<(Action Work, TimeSpan After)> waiting = [];

    private TaskDetail Open(TodoTask task)
    {
        var detail = new TaskDetail(Model, List.Id, task.Id, (work, after) => waiting.Add((work, after)));
        Assert.True(detail.Reload());
        return detail;
    }

    /// <summary>Let everything put off run, as time passing would.</summary>
    private void RunWaiting()
    {
        var due = waiting.ToList();
        waiting.Clear();
        foreach (var (work, _) in due) work();
    }

    [Fact]
    public void ItShowsTheTaskItsSubtasksItsColumnsAndItsComments()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        Model.AddSubtask(tent, "Mallet");
        Model.AddComment(tent.Id, "Borrow one?");
        var detail = Open(tent);

        Assert.Equal("Tent", detail.Task?.Title);
        Assert.Equal(["Pegs", "Mallet"], detail.Subtasks.Select(task => task.Title));
        Assert.Equal(["Up Next", "Active", "Done"], detail.Columns.Select(column => column.Name));
        Assert.Equal(["Borrow one?"], detail.Comments.Select(comment => comment.Body));
        Assert.Equal("", detail.Breadcrumb);
        Assert.False(detail.CanGoBack);
    }

    [Fact]
    public void ASubtaskOpensInTheSameWindowWithAWayBack()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        var pegs = Task("Pegs");
        Model.AddSubtask(pegs, "Count them");
        var detail = Open(tent);

        detail.Open(pegs.Id);
        Assert.Equal("Pegs", detail.Task?.Title);
        Assert.Equal("Tent", detail.Breadcrumb);
        Assert.True(detail.CanGoBack);
        Assert.Equal(["Count them"], detail.Subtasks.Select(task => task.Title));

        detail.Open(Task("Count them").Id);
        Assert.Equal("Tent › Pegs", detail.Breadcrumb);

        detail.Back();
        Assert.Equal("Pegs", detail.Task?.Title);
        detail.Back();
        Assert.Equal("Tent", detail.Task?.Title);
        Assert.False(detail.CanGoBack);
        detail.Back();
        Assert.Equal("Tent", detail.Task?.Title);
    }

    [Fact]
    public void OnlyASubtaskOfTheTaskShownCanBeOpened()
    {
        var tent = AddCard("Up Next", "Tent");
        var stove = AddCard("Up Next", "Stove");
        var detail = Open(tent);
        detail.Open(stove.Id);
        detail.Open("no-such-task");
        Assert.Equal("Tent", detail.Task?.Title);
        Assert.False(detail.CanGoBack);
    }

    [Fact]
    public void CommentsAreThoseOfTheTaskShown()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        Model.AddComment(tent.Id, "On the tent");
        Model.AddComment(Task("Pegs").Id, "On the pegs");
        var detail = Open(tent);

        detail.Open(Task("Pegs").Id);
        Assert.Equal(["On the pegs"], detail.Comments.Select(comment => comment.Body));
        detail.Back();
        Assert.Equal(["On the tent"], detail.Comments.Select(comment => comment.Body));
    }

    [Fact]
    public void WhenTheTaskIsDeletedElsewhereTheWindowIsToClose()
    {
        var tent = AddCard("Up Next", "Tent");
        var detail = Open(tent);
        Model.DeleteTask(tent.Id);
        Assert.False(detail.Reload());
    }

    [Fact]
    public void WhenASubtaskBeingShownIsDeletedElsewhereTheWindowGoesBackUp()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        var pegs = Task("Pegs");
        Model.AddSubtask(pegs, "Count them");
        var detail = Open(tent);
        detail.Open(pegs.Id);
        detail.Open(Task("Count them").Id);

        // The middle one goes, and takes the one under it.
        Model.DeleteTask(pegs.Id);
        Assert.True(detail.Reload());
        Assert.Equal("Tent", detail.Task?.Title);
        Assert.False(detail.CanGoBack);
    }

    [Fact]
    public void DeletingTheTaskShownClosesTheWindowOrGoesBackUp()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        var detail = Open(tent);
        detail.Open(Task("Pegs").Id);

        Assert.False(detail.Delete());
        Assert.Equal("Tent", detail.Task?.Title);
        Assert.Empty(detail.Subtasks);

        Assert.True(detail.Delete());
        Assert.Empty(Model.Tasks(List.Id));
    }

    [Fact]
    public void TheTitleIsSavedOnlyWhenItChanged()
    {
        var tent = AddCard("Up Next", "Tent");
        var detail = Open(tent);
        var version = Model.BoardVersion(List.Id);

        detail.SetTitle("Tent");
        Assert.Equal(version, Model.BoardVersion(List.Id));

        detail.SetTitle("Big tent");
        Assert.Equal("Big tent", Task("Big tent").Title);
        Assert.Equal("Big tent", detail.Task?.Title);
    }

    [Fact]
    public void ItCanBeTickedAndGivenAPriority()
    {
        var detail = Open(AddCard("Up Next", "Tent"));
        detail.SetCompleted(true);
        detail.SetPriority(1);
        Assert.True(Task("Tent").Completed);
        Assert.Equal((byte)1, Task("Tent").Priority);
        Assert.Equal((byte)1, detail.Task!.Priority);

        // Not a priority: nothing changes and nothing is complained of.
        detail.SetPriority(9);
        Assert.Equal((byte)1, Task("Tent").Priority);
        Assert.Null(Model.LastError);
    }

    [Fact]
    public void ADueDateAndADeadlineAreSetAndCleared()
    {
        var detail = Open(AddCard("Up Next", "Tent"));
        var day = new DateOnly(2026, 12, 1);

        detail.SetDue(day);
        detail.SetDeadline(day);
        Assert.Equal(TaskDates.DueDate(day), Task("Tent").DueDate);
        Assert.Equal(TaskDates.Deadline(day), Task("Tent").Deadline);
        Assert.Equal(day, TaskDates.Day(detail.Task!.DueDate!));

        detail.SetDue(null);
        Assert.Null(Task("Tent").DueDate);
        Assert.NotNull(Task("Tent").Deadline);
        detail.SetDeadline(null);
        Assert.Null(Task("Tent").Deadline);
    }

    [Fact]
    public void SettingTheDayItAlreadyHasWritesNothing()
    {
        var detail = Open(AddCard("Up Next", "Tent"));
        var day = new DateOnly(2026, 12, 1);
        detail.SetDue(day);
        var version = Model.BoardVersion(List.Id);
        detail.SetDue(day);
        Assert.Equal(version, Model.BoardVersion(List.Id));
    }

    [Fact]
    public void ALocationIsTrimmedAndAnEmptyOneIsNone()
    {
        var detail = Open(AddCard("Up Next", "Tent"));
        detail.SetLocation("  The shed ");
        Assert.Equal("The shed", Task("Tent").Location);

        var version = Model.BoardVersion(List.Id);
        detail.SetLocation("The shed");
        Assert.Equal(version, Model.BoardVersion(List.Id));

        detail.SetLocation("   ");
        Assert.Null(Task("Tent").Location);
    }

    [Fact]
    public void LabelsAreTrimmedAndNeverTwice()
    {
        var detail = Open(AddCard("Up Next", "Tent"));
        detail.AddLabel(" camping ");
        detail.AddLabel("heavy");
        detail.AddLabel("camping");
        detail.AddLabel("  ");
        Assert.Equal(["camping", "heavy"], Task("Tent").Labels);

        detail.RemoveLabel("camping");
        Assert.Equal(["heavy"], Task("Tent").Labels);
        Assert.Equal(["heavy"], detail.Task!.Labels);
    }

    [Fact]
    public void RemindersAreAddedOnceAndRemoved()
    {
        var detail = Open(AddCard("Up Next", "Tent"));
        var when = TaskDates.Parse("2026-12-01T08:30:00Z")!.Value;

        Assert.True(detail.AddReminder(when));
        Assert.False(detail.AddReminder(when));
        Assert.Equal(["2026-12-01T08:30:00.000Z"], Task("Tent").Reminders);
        // The core lists it for scheduling.
        Assert.Equal(["2026-12-01T08:30:00.000Z"], Store.ListReminders().Select(reminder => reminder.RemindAt));

        detail.RemoveReminder("2026-12-01T08:30:00.000Z");
        Assert.Empty(Task("Tent").Reminders);
    }

    [Fact]
    public void SubtasksAreAddedTickedAndRemoved()
    {
        var detail = Open(AddCard("Up Next", "Tent"));
        detail.AddSubtask("  Pegs ");
        detail.AddSubtask("   ");
        Assert.Equal(["Pegs"], detail.Subtasks.Select(task => task.Title));

        var pegs = Task("Pegs");
        detail.SetCompleted(pegs.Id, true);
        Assert.True(Task("Pegs").Completed);
        Assert.False(Task("Tent").Completed);

        detail.DeleteTask(pegs.Id);
        Assert.Empty(detail.Subtasks);
    }

    [Fact]
    public void ASubtaskCanBeMadeACardAndIsThenShownAsOne()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        var detail = Open(tent);
        Assert.False(detail.CanMakeCard);

        detail.Open(Task("Pegs").Id);
        Assert.True(detail.CanMakeCard);
        detail.MakeCard(Column("Active").Id);

        Assert.Equal(["Pegs"], Cards("Active"));
        Assert.Null(Task("Pegs").ParentTaskId);
        // It is no longer under the tent, so there is no way back up to it.
        Assert.Equal("Pegs", detail.Task?.Title);
        Assert.Equal("", detail.Breadcrumb);
        Assert.False(detail.CanGoBack);
        Assert.False(detail.CanMakeCard);
    }

    [Fact]
    public void ASubtaskMadeACardElsewhereIsStillShownWhenItsOldParentGoes()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        var pegs = Task("Pegs");
        var detail = Open(tent);
        detail.Open(pegs.Id);

        // Another window promotes it, then deletes the task it was under.
        Model.MoveCard(pegs.Id, Column("Active").Id);
        Model.DeleteTask(tent.Id);

        Assert.True(detail.Reload());
        Assert.Equal("Pegs", detail.Task?.Title);
        Assert.False(detail.CanGoBack);
    }

    [Fact]
    public void CommentsAreAddedTrimmedEditedAndDeleted()
    {
        var detail = Open(AddCard("Up Next", "Tent"));
        detail.AddComment("  Borrow one?\n");
        detail.AddComment(" \n ");
        var comment = Assert.Single(detail.Comments);
        Assert.Equal("Borrow one?", comment.Body);

        detail.EditComment(comment.Id, "Borrowed.");
        Assert.Equal("Borrowed.", Assert.Single(detail.Comments).Body);

        detail.DeleteComment(comment.Id);
        Assert.Empty(detail.Comments);
    }

    [Fact]
    public void NotesAreSavedOnceTypingPauses()
    {
        var detail = Open(AddCard("Up Next", "Tent"));
        detail.TypeNotes("Four");
        detail.TypeNotes("Four seasons");
        Assert.Equal("", Task("Tent").Description);
        Assert.Equal("Four seasons", detail.Notes);
        Assert.All(waiting, wait => Assert.Equal(TaskDetail.NotesDelay, wait.After));

        var version = Model.BoardVersion(List.Id);
        RunWaiting();
        Assert.Equal("Four seasons", Task("Tent").Description);
        // One save for the burst, not one for each key.
        Assert.Equal(version + 1, Model.BoardVersion(List.Id));
    }

    [Fact]
    public void WhatIsBeingTypedIsNotReplacedByWhatIsStored()
    {
        var tent = AddCard("Up Next", "Tent");
        var detail = Open(tent);
        detail.TypeNotes("Four seasons");

        // The board changes under it: a card is ticked in another window.
        Model.SetCompleted(tent.Id, true);
        Assert.True(detail.Reload());
        Assert.Equal("Four seasons", detail.Notes);
        Assert.True(detail.Task?.Completed);

        RunWaiting();
        Assert.Equal("Four seasons", Task("Tent").Description);
        Assert.Equal("Four seasons", detail.Notes);
    }

    [Fact]
    public void NotesTypedAreSavedBeforeTheWindowMovesOnOrCloses()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        var detail = Open(tent);

        detail.TypeNotes("Four seasons");
        detail.Open(Task("Pegs").Id);
        Assert.Equal("Four seasons", Task("Tent").Description);
        Assert.Equal("", detail.Notes);

        detail.TypeNotes("Twelve");
        detail.Back();
        Assert.Equal("Twelve", Task("Pegs").Description);
        Assert.Equal("Four seasons", detail.Notes);

        detail.TypeNotes("Four seasons, two doors");
        detail.Close();
        Assert.Equal("Four seasons, two doors", Task("Tent").Description);

        // A save still waiting finds nothing left to do.
        var version = Model.BoardVersion(List.Id);
        RunWaiting();
        Assert.Equal(version, Model.BoardVersion(List.Id));
    }

    [Fact]
    public void NotesForATaskThatHasGoneAreDropped()
    {
        var tent = AddCard("Up Next", "Tent");
        var detail = Open(tent);
        detail.TypeNotes("Four seasons");
        Model.DeleteTask(tent.Id);
        RunWaiting();
        detail.Close();
        // Nothing to save to, and nothing to tell the user about.
        Assert.Null(Model.LastError);
    }

    [Fact]
    public void ATaskThatWentWhileTheWindowWasNotLookingIsNotWrittenTo()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        var detail = Open(tent);
        // Deleted in another window, and this one has not heard yet.
        Model.DeleteTask(tent.Id);

        detail.SetTitle("Ghost");
        detail.SetLocation("Nowhere");
        detail.SetPriority(1);
        detail.SetCompleted(true);
        detail.SetCompleted(detail.Subtasks[0].Id, true);
        detail.AddLabel("x");
        detail.AddSubtask("y");
        detail.AddComment("z");
        detail.DeleteTask(detail.Subtasks.Count > 0 ? detail.Subtasks[0].Id : "gone");
        // Nothing was there to change, and there is nothing to tell the user about.
        Assert.Null(Model.LastError);
        Assert.Empty(Model.Tasks(List.Id));
    }

    [Fact]
    public void NothingIsChangedInAListThatIsInTheTrash()
    {
        var tent = AddCard("Up Next", "Tent");
        Model.AddSubtask(tent, "Pegs");
        Model.AddComment(tent.Id, "Borrow one?");
        var detail = Open(tent);
        Model.Trash(List.Id);
        var version = Model.BoardVersion(List.Id);

        detail.SetTitle("Changed in the trash");
        detail.SetCompleted(true);
        detail.AddLabel("x");
        detail.AddSubtask("y");
        detail.AddComment("z");
        detail.EditComment(detail.Comments[0].Id, "Edited");
        detail.DeleteComment(detail.Comments[0].Id);
        detail.DeleteTask(detail.Subtasks[0].Id);
        detail.MakeCard(Column("Active").Id);
        Assert.False(detail.AddReminder(DateTimeOffset.UtcNow.AddHours(1)));
        detail.TypeNotes("Typed in the trash");
        RunWaiting();
        detail.Close();
        Assert.False(detail.Delete());

        Assert.Equal(version, Model.BoardVersion(List.Id));
        Assert.Equal("Tent", Task("Tent").Title);
        Assert.Equal("", Task("Tent").Description);
        Assert.Null(Model.LastError);
    }

    [Fact]
    public void NothingCanBeChangedOnceTheTaskHasGone()
    {
        var tent = AddCard("Up Next", "Tent");
        var detail = Open(tent);
        Model.DeleteTask(tent.Id);
        Assert.False(detail.Reload());

        detail.SetTitle("Ghost");
        detail.SetPriority(1);
        detail.AddLabel("x");
        detail.AddSubtask("y");
        detail.AddComment("z");
        Assert.False(detail.AddReminder(DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Null(Model.LastError);
        Assert.Empty(Model.Tasks(List.Id));
    }
}

/// <summary>A card on its way from one place to another.</summary>
public sealed class CardDragTests
{
    [Fact]
    public void ACardIsCarriedAsTextAndReadBack()
    {
        Assert.Equal(new CardDrag("t-1"), CardDrag.Parse(new CardDrag("t-1").Text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("heracles-card:")]
    [InlineData("heracles-tab:leaf|note")]
    [InlineData("some text dragged from another program")]
    public void OtherTextIsNotACard(string? text)
    {
        Assert.Null(CardDrag.Parse(text));
    }
}

/// <summary>
/// How something dragged is carried. It was once carried as plain text, and
/// a text box takes plain text that is dropped on it: a card let go over
/// "Add a card", or a tab over a note's title, typed its own name for
/// itself into the box.
/// </summary>
public sealed class DragDataTests
{
    [Fact]
    public void ACardIsCarriedInAFormOfItsOwnAndNotAsText()
    {
        var data = new CardDrag("t-1").Data();
        Assert.False(data.GetDataPresent(System.Windows.DataFormats.UnicodeText));
        Assert.False(data.GetDataPresent(System.Windows.DataFormats.Text));
        Assert.False(data.GetDataPresent(System.Windows.DataFormats.StringFormat));
        Assert.Equal(new CardDrag("t-1"), CardDrag.From(data));
    }

    [Fact]
    public void ATabIsCarriedInAFormOfItsOwnAndNotAsText()
    {
        var data = new Heracles.Workspaces.TabDrag("pane-1", "note-9").Data();
        Assert.False(data.GetDataPresent(System.Windows.DataFormats.UnicodeText));
        Assert.False(data.GetDataPresent(System.Windows.DataFormats.Text));
        Assert.False(data.GetDataPresent(System.Windows.DataFormats.StringFormat));
        Assert.Equal(new Heracles.Workspaces.TabDrag("pane-1", "note-9"), Heracles.Workspaces.TabDrag.From(data));
    }

    [Fact]
    public void TextDraggedFromAnywhereIsNeitherACardNorATab()
    {
        // Even text that happens to read like one.
        var text = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, new CardDrag("t-1").Text);
        Assert.Null(CardDrag.From(text));
        var tab = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, new Heracles.Workspaces.TabDrag("pane-1", "note-9").Text);
        Assert.Null(Heracles.Workspaces.TabDrag.From(tab));
    }

    [Fact]
    public void ACardIsNotATabAndATabIsNotACard()
    {
        Assert.Null(Heracles.Workspaces.TabDrag.From(new CardDrag("t-1").Data()));
        Assert.Null(CardDrag.From(new Heracles.Workspaces.TabDrag("pane-1", "note-9").Data()));
    }
}
