using System.Globalization;
using Heracles.Boards;

namespace Heracles.Tests;

/// <summary>The Mac's <c>TaskDatesTests</c>, and what Windows adds: a card's short date, and a typed time of day.</summary>
public sealed class TaskDatesTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo NewYork = Zone("America/New_York", "Eastern Standard Time");

    /// <summary>A zone by the name the rest of the world uses, or by Windows' own where that is not known.</summary>
    private static TimeZoneInfo Zone(string name, string windowsName)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(name); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById(windowsName); }
    }

    private static DateTimeOffset Seconds(double sinceEpoch) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)Math.Round(sinceEpoch * TimeSpan.TicksPerSecond));

    [Fact]
    public void StoredTimesAreReadWithOrWithoutFractionsOfASecond()
    {
        var expected = Seconds(1_796_029_200); // 2026-11-30 09:00:00 UTC
        Assert.Equal(expected, TaskDates.Parse("2026-11-30T09:00:00.000Z"));
        Assert.Equal(expected, TaskDates.Parse("2026-11-30T09:00:00Z"));
        Assert.Null(TaskDates.Parse("nonsense"));
        Assert.Null(TaskDates.Parse(""));
    }

    [Fact]
    public void ABareDateIsReadAsThatDay()
    {
        Assert.Equal(Seconds(1_796_083_200), TaskDates.Parse("2026-12-01", Utc)); // 2026-12-01 00:00 UTC
        // Midnight where the reader is, not midnight in Greenwich.
        Assert.Equal(Seconds(1_796_083_200 + 5 * 3600), TaskDates.Parse("2026-12-01", NewYork));
    }

    [Fact]
    public void TimesAreWrittenTheWayTheOriginalAppWroteThem()
    {
        var date = Seconds(1_796_029_200.25);
        Assert.Equal("2026-11-30T09:00:00.250Z", TaskDates.Format(date));
        Assert.Equal(date, TaskDates.Parse(TaskDates.Format(date)));
    }

    [Fact]
    public void ATimeWithAnOffsetIsWrittenInGreenwichTime()
    {
        var date = new DateTimeOffset(2026, 11, 30, 4, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal("2026-11-30T09:00:00.000Z", TaskDates.Format(date));
        Assert.Equal(Seconds(1_796_029_200), TaskDates.Parse("2026-11-30T04:00:00-05:00"));
    }

    [Fact]
    public void ADueDateIsNineInTheMorningAndADeadlineFiveInTheAfternoonLocalTime()
    {
        var day = TaskDates.Parse("2026-12-01T15:30:00Z")!.Value; // 10:30 in New York
        Assert.Equal("2026-12-01T14:00:00.000Z", TaskDates.DueDate(day, NewYork));
        Assert.Equal("2026-12-01T22:00:00.000Z", TaskDates.Deadline(day, NewYork));
    }

    [Fact]
    public void TheDayIsTheOneItIsWhereTheUserIs()
    {
        // Three in the morning in Greenwich is still the evening before in New York.
        var moment = TaskDates.Parse("2026-12-02T03:00:00Z")!.Value;
        Assert.Equal("2026-12-01T14:00:00.000Z", TaskDates.DueDate(moment, NewYork));
        Assert.Equal("2026-12-02T09:00:00.000Z", TaskDates.DueDate(moment, Utc));
    }

    [Fact]
    public void ADayChosenInACalendarIsThatDayWhateverTheHour()
    {
        Assert.Equal("2026-12-01T14:00:00.000Z", TaskDates.DueDate(new DateOnly(2026, 12, 1), NewYork));
        Assert.Equal("2026-07-01T21:00:00.000Z", TaskDates.Deadline(new DateOnly(2026, 7, 1), NewYork)); // summer time
    }

    [Fact]
    public void AStoredDateGivesBackTheDayItWasSetFor()
    {
        Assert.Equal(new DateOnly(2026, 12, 1), TaskDates.Day("2026-12-01T14:00:00.000Z", NewYork));
        // A deadline at five in the afternoon in New York is already tomorrow in Greenwich for part of the year; not this one.
        Assert.Equal(new DateOnly(2026, 12, 1), TaskDates.Day("2026-12-01T22:00:00.000Z", NewYork));
        Assert.Equal(new DateOnly(2026, 12, 2), TaskDates.Day("2026-12-02T03:00:00.000Z", Utc));
        Assert.Null(TaskDates.Day("nonsense", Utc));
    }

    [Fact]
    public void ACardShowsADateShortWithTheYearOnlyWhenItIsNotThisOne()
    {
        var now = TaskDates.Parse("2026-11-23T12:00:00Z")!.Value;
        var british = CultureInfo.GetCultureInfo("en-GB");
        var american = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("1 Dec", TaskDates.Label("2026-12-01T14:00:00.000Z", now, british, Utc));
        Assert.Equal("Dec 1", TaskDates.Label("2026-12-01T14:00:00.000Z", now, american, Utc));
        Assert.Equal("1 Jan 2027", TaskDates.Label("2027-01-01T14:00:00.000Z", now, british, Utc));
        Assert.Equal("Jan 1, 2027", TaskDates.Label("2027-01-01T14:00:00.000Z", now, american, Utc));
    }

    [Fact]
    public void ADateThatCannotBeReadIsShownAsItIsStored()
    {
        Assert.Equal("someday", TaskDates.Label("someday"));
        Assert.Equal("someday", TaskDates.DateAndTime("someday"));
    }

    [Fact]
    public void AReminderIsShownWithItsDayAndItsTime()
    {
        var british = CultureInfo.GetCultureInfo("en-GB");
        Assert.Equal("30 Nov 2026 09:00", TaskDates.DateAndTime("2026-11-30T09:00:00.000Z", british, Utc));
        Assert.Equal("30 Nov 2026 04:00", TaskDates.DateAndTime("2026-11-30T09:00:00.000Z", british, NewYork));
    }

    [Theory]
    [InlineData("9:30", 9, 30)]
    [InlineData("09:30", 9, 30)]
    [InlineData("17:05", 17, 5)]
    [InlineData("5:05 PM", 17, 5)]
    [InlineData("5pm", 17, 0)]
    [InlineData(" 7 ", 7, 0)]
    [InlineData("0:00", 0, 0)]
    public void ATimeOfDayCanBeTypedSeveralWays(string typed, int hour, int minute)
    {
        Assert.Equal(new TimeOnly(hour, minute), TaskDates.TimeOfDay(typed, CultureInfo.GetCultureInfo("en-US")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("25:00")]
    [InlineData("9:75")]
    [InlineData("2026-12-01")]
    public void WhatIsNotATimeOfDayIsNotTakenForOne(string typed)
    {
        Assert.Null(TaskDates.TimeOfDay(typed, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void ADayAndATimeMakeAMomentWhereTheUserIs()
    {
        var moment = TaskDates.At(new DateOnly(2026, 12, 1), new TimeOnly(9, 30), NewYork);
        Assert.Equal("2026-12-01T14:30:00.000Z", TaskDates.Format(moment));
    }
}

/// <summary>The Mac's <c>ReminderPlanTests</c>.</summary>
public sealed class ReminderPlanTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_796_029_200); // 2026-11-30 09:00 UTC

    private static Reminder Reminder(string task, string at, string title = "Task") =>
        new(TaskId: task, TodoListId: "list", TaskTitle: title, RemindAt: at);

    [Fact]
    public void FutureRemindersAreScheduledAndPastOnesAreNot()
    {
        var plan = new ReminderPlan(
            [
                Reminder("t1", "2026-11-30T10:00:00.000Z", title: "Call"),
                Reminder("t2", "2026-11-30T08:00:00.000Z"),
                Reminder("t3", "not a date"),
            ],
            [], Now);

        Assert.Equal(["heracles.reminder.t1.2026-11-30T10:00:00.000Z"], plan.ToAdd.Select(reminder => reminder.Identifier));
        Assert.Equal("Call", plan.ToAdd[0].Title);
        Assert.Equal("list", plan.ToAdd[0].NoteId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_796_032_800), plan.ToAdd[0].Date);
        Assert.Empty(plan.ToRemove);
    }

    [Fact]
    public void WhatIsAlreadyScheduledIsLeftAloneAndWhatIsNoLongerWantedIsRemoved()
    {
        const string kept = "heracles.reminder.t1.2026-11-30T10:00:00.000Z";
        const string stale = "heracles.reminder.t9.2026-11-30T11:00:00.000Z";
        var plan = new ReminderPlan(
            [Reminder("t1", "2026-11-30T10:00:00.000Z")],
            [kept, stale, "someone.elses.notification"], Now);

        Assert.Empty(plan.ToAdd);
        // Only this app's reminders are ever removed.
        Assert.Equal([stale], plan.ToRemove);
    }

    [Fact]
    public void AnUntitledTaskStillSaysSomething()
    {
        var plan = new ReminderPlan([Reminder("t1", "2026-11-30T10:00:00.000Z", title: " ")], [], Now);
        Assert.Equal("Untitled task", plan.ToAdd[0].Title);
    }

    [Fact]
    public void AReminderDueThisVeryMomentIsAlreadyPast()
    {
        var plan = new ReminderPlan([Reminder("t1", "2026-11-30T09:00:00.000Z")], [], Now);
        Assert.Empty(plan.ToAdd);
    }

    [Fact]
    public void TheSameReminderListedTwiceIsScheduledOnce()
    {
        var plan = new ReminderPlan(
            [Reminder("t1", "2026-11-30T10:00:00.000Z"), Reminder("t1", "2026-11-30T10:00:00.000Z")], [], Now);
        Assert.Single(plan.ToAdd);
    }
}

/// <summary>What a reminder looks like as a Windows toast, and how the app knows its own again.</summary>
public sealed class ReminderToastTests
{
    private static PlannedReminder Planned(string title = "Book ferry", string list = "list-1") =>
        new("heracles.reminder.t1.2026-11-30T10:00:00.000Z", title, list, DateTimeOffset.FromUnixTimeSeconds(1_796_032_800));

    [Fact]
    public void AClickOpensTheTodoListThroughALink()
    {
        var link = ReminderToast.Link(Planned());
        Assert.StartsWith("heracles://note/list-1?", link);
        // The link is one the app follows, and it names the list.
        Assert.Equal(new Heracles.Model.AppLink("list-1", null), Heracles.Model.AppLink.Parse(link));
    }

    [Fact]
    public void TheToastSaysTheTaskAndOpensByItsLinkWithNoCodeRunning()
    {
        var xml = System.Xml.Linq.XElement.Parse(ReminderToast.Xml(Planned()));
        Assert.Equal("toast", xml.Name.LocalName);
        Assert.Equal("protocol", xml.Attribute("activationType")?.Value);
        Assert.Equal(ReminderToast.Link(Planned()), xml.Attribute("launch")?.Value);
        var lines = xml.Descendants("text").Select(text => text.Value).ToArray();
        Assert.Equal(["Book ferry", "Reminder"], lines);
        Assert.Equal("ToastGeneric", xml.Descendants("binding").Single().Attribute("template")?.Value);
    }

    [Fact]
    public void ATitleWithMarkupInItIsOnlyText()
    {
        var title = "Tom & <b>Jerry</b> \"quoted\"";
        var xml = System.Xml.Linq.XElement.Parse(ReminderToast.Xml(Planned(title)));
        Assert.Equal(title, xml.Descendants("text").First().Value);
        Assert.Empty(xml.Descendants("b"));
    }

    [Fact]
    public void TheToastCarriesItsIdentifierSoItIsKnownAgain()
    {
        var planned = Planned();
        Assert.Equal(planned.Identifier, ReminderToast.Identifier(ReminderToast.Xml(planned)));
    }

    [Fact]
    public void WhatAToastSaysCanBeReadBack()
    {
        var title = "Tom & <b>Jerry</b>";
        Assert.Equal(title, ReminderToast.Title(ReminderToast.Xml(Planned(title))));
        Assert.Null(ReminderToast.Title("not xml at all"));
        Assert.Null(ReminderToast.Title("<toast><visual/></toast>"));
    }

    [Theory]
    [InlineData("<toast launch=\"https://example.com\"><visual/></toast>")]
    [InlineData("<toast><visual/></toast>")]
    [InlineData("<toast launch=\"heracles://note/abc\"/>")]
    [InlineData("not xml at all")]
    [InlineData("")]
    public void AToastThatIsNotOneOfTheseHasNoIdentifier(string xml)
    {
        Assert.Null(ReminderToast.Identifier(xml));
    }

    [Fact]
    public void ATagIsShortAndTheSameForTheSameReminder()
    {
        var tag = ReminderToast.Tag(Planned().Identifier);
        Assert.Equal(tag, ReminderToast.Tag(Planned().Identifier));
        Assert.NotEqual(tag, ReminderToast.Tag("heracles.reminder.t2.2026-11-30T10:00:00.000Z"));
        // Windows takes at most sixteen characters on older versions.
        Assert.InRange(tag.Length, 1, 16);
    }
}

/// <summary>What Windows adds to the plan: a notification says the task's name, so a renamed task's is made again.</summary>
public sealed class ReminderPlanTitleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_796_029_200); // 2026-11-30 09:00 UTC

    private const string Identifier = "heracles.reminder.t1.2026-11-30T10:00:00.000Z";

    private static Reminder Wanted(string title) => new("t1", "list", title, "2026-11-30T10:00:00.000Z");

    [Fact]
    public void AReminderWhoseTaskWasRenamedIsScheduledAgainUnderItsNewName()
    {
        var plan = new ReminderPlan([Wanted("Call the ferry office")], [new ScheduledReminder(Identifier, "Call")], Now);
        Assert.Equal([Identifier], plan.ToRemove);
        Assert.Equal("Call the ferry office", Assert.Single(plan.ToAdd).Title);
    }

    [Fact]
    public void OneThatSaysWhatItShouldIsLeftAlone()
    {
        var plan = new ReminderPlan([Wanted("Call")], [new ScheduledReminder(Identifier, "Call")], Now);
        Assert.Empty(plan.ToAdd);
        Assert.Empty(plan.ToRemove);
    }

    [Fact]
    public void OneWhoseWordsCannotBeReadIsLeftAlone()
    {
        var plan = new ReminderPlan([Wanted("Call")], [new ScheduledReminder(Identifier, null)], Now);
        Assert.Empty(plan.ToAdd);
        Assert.Empty(plan.ToRemove);
    }

    [Fact]
    public void AnUntitledTaskIsComparedByWhatItsNotificationSays()
    {
        var plan = new ReminderPlan([Wanted("  ")], [new ScheduledReminder(Identifier, "Untitled task")], Now);
        Assert.Empty(plan.ToAdd);
        Assert.Empty(plan.ToRemove);
    }
}
