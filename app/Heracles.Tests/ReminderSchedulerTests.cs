using Heracles.Boards;
using Heracles.Model;
using Microsoft.Win32;

namespace Heracles.Tests;

/// <summary>Stands in for Windows' list of scheduled notifications.</summary>
internal sealed class FakeReminderCenter : IReminderCenter
{
    public List<string> Held { get; } = [];

    public List<PlannedReminder> Added { get; } = [];

    public List<string> Removed { get; } = [];

    public int Asked { get; private set; }

    /// <summary>Set to make the next call fail, as Windows can.</summary>
    public Exception? Failure { get; set; }

    /// <summary>A reminder Windows will not take, by a word in its title.</summary>
    public string? Refused { get; set; }

    /// <summary>What each notification held says, where it says anything.</summary>
    public Dictionary<string, string> Says { get; } = [];

    public IReadOnlyList<ScheduledReminder> Scheduled()
    {
        Asked++;
        if (Failure is { } failure) throw failure;
        return Held.Select(identifier => new ScheduledReminder(identifier, Says.GetValueOrDefault(identifier))).ToList();
    }

    public void Remove(IReadOnlyList<string> identifiers)
    {
        Removed.AddRange(identifiers);
        Held.RemoveAll(identifiers.Contains);
        foreach (var identifier in identifiers) Says.Remove(identifier);
    }

    public void Add(PlannedReminder reminder)
    {
        if (Refused is { } word && reminder.Title.Contains(word)) throw new ArgumentException("The delivery time is not in the future.");
        Added.Add(reminder);
        Held.Add(reminder.Identifier);
        Says[reminder.Identifier] = reminder.Title;
    }
}

/// <summary>Keeping Windows' scheduled notifications in step with the reminders on tasks.</summary>
public sealed class ReminderSchedulerTests : BoardFixture
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_796_029_200); // 2026-11-30 09:00 UTC

    private readonly FakeReminderCenter center = new();
    private readonly List<(Action Work, TimeSpan After)> waiting = [];

    private ReminderScheduler Scheduler() =>
        new(Model, center, (work, after) => waiting.Add((work, after)), () => Now);

    private void RunWaiting()
    {
        var due = waiting.ToList();
        waiting.Clear();
        foreach (var (work, _) in due) work();
    }

    private TodoTask TaskWithReminders(string title, params string[] reminders)
    {
        var task = AddCard("Up Next", title);
        Model.UpdateTask(task.Id, new TaskPatch(Reminders: reminders));
        return task;
    }

    [Fact]
    public void RemindersToComeAreHandedOverAndPastOnesAreNot()
    {
        var call = TaskWithReminders("Call", "2026-11-30T10:00:00.000Z", "2026-11-30T08:00:00.000Z");
        Scheduler().Sync();

        var added = Assert.Single(center.Added);
        Assert.Equal($"heracles.reminder.{call.Id}.2026-11-30T10:00:00.000Z", added.Identifier);
        Assert.Equal("Call", added.Title);
        Assert.Equal(List.Id, added.NoteId);
        Assert.Empty(center.Removed);
    }

    [Fact]
    public void ASecondSyncAddsNothingNew()
    {
        TaskWithReminders("Call", "2026-11-30T10:00:00.000Z");
        var scheduler = Scheduler();
        scheduler.Sync();
        scheduler.Sync();
        Assert.Single(center.Added);
        Assert.Empty(center.Removed);
    }

    [Fact]
    public void AReminderTakenOffATaskIsTakenBack()
    {
        var call = TaskWithReminders("Call", "2026-11-30T10:00:00.000Z");
        var scheduler = Scheduler();
        scheduler.Sync();

        Model.UpdateTask(call.Id, new TaskPatch(Reminders: []));
        scheduler.Sync();
        Assert.Equal([$"heracles.reminder.{call.Id}.2026-11-30T10:00:00.000Z"], center.Removed);
        Assert.Empty(center.Held);
    }

    [Fact]
    public void ATaskDoneOrAListInTheTrashRemindsNoOne()
    {
        var call = TaskWithReminders("Call", "2026-11-30T10:00:00.000Z");
        var scheduler = Scheduler();
        scheduler.Sync();
        Assert.Single(center.Held);

        Model.SetCompleted(call.Id, true);
        scheduler.Sync();
        Assert.Empty(center.Held);

        Model.SetCompleted(call.Id, false);
        scheduler.Sync();
        Assert.Single(center.Held);

        Model.Trash(List.Id);
        scheduler.Sync();
        Assert.Empty(center.Held);
    }

    [Fact]
    public void NotificationsThatAreNotThisAppsRemindersAreLeftAlone()
    {
        center.Held.Add("someone.elses.notification");
        center.Held.Add("heracles.reminder.gone.2026-11-30T11:00:00.000Z");
        Scheduler().Sync();
        Assert.Equal(["heracles.reminder.gone.2026-11-30T11:00:00.000Z"], center.Removed);
        Assert.Equal(["someone.elses.notification"], center.Held);
    }

    [Fact]
    public void ABurstOfChangesIsOneSync()
    {
        TaskWithReminders("Call", "2026-11-30T10:00:00.000Z");
        var scheduler = Scheduler();
        scheduler.SyncSoon();
        scheduler.SyncSoon();
        scheduler.SyncSoon();
        Assert.Equal(0, center.Asked);
        Assert.All(waiting, wait => Assert.Equal(ReminderScheduler.Delay, wait.After));

        RunWaiting();
        Assert.Equal(1, center.Asked);
        Assert.Single(center.Added);
    }

    [Fact]
    public void ItFollowsTheModelsChanges()
    {
        var scheduler = Scheduler();
        Model.AfterChange = scheduler.SyncSoon;
        TaskWithReminders("Call", "2026-11-30T10:00:00.000Z");
        RunWaiting();
        Assert.Single(center.Added);
    }

    [Fact]
    public void WindowsIsNotAskedAgainWhileTheRemindersAreAsTheyWere()
    {
        var call = TaskWithReminders("Call", "2026-11-30T10:00:00.000Z");
        var scheduler = Scheduler();
        scheduler.Sync();
        Assert.Equal(1, center.Asked);

        // Typing in a note, renaming a card: the library changes all day, and
        // none of it is news about reminders.
        AddCard("Up Next", "Write");
        Model.RenameTask(call.Id, "Call");
        scheduler.Sync();
        scheduler.Sync();
        Assert.Equal(1, center.Asked);

        // A new title is news: the notification must say it.
        Model.RenameTask(call.Id, "Call the ferry office");
        scheduler.Sync();
        Assert.Equal(2, center.Asked);
    }

    [Fact]
    public void ARenamedTaskIsRemindedOfByItsNewName()
    {
        var call = TaskWithReminders("Call", "2026-11-30T10:00:00.000Z");
        var scheduler = Scheduler();
        scheduler.Sync();
        Model.RenameTask(call.Id, "Call the ferry office");
        scheduler.Sync();
        Assert.Equal("Call the ferry office", center.Added.Last().Title);
        Assert.Single(center.Held);
    }

    [Fact]
    public void OneReminderWindowsWillNotTakeDoesNotStopTheOthers()
    {
        TaskWithReminders("Call", "2026-11-30T10:00:00.000Z");
        TaskWithReminders("Refuse me", "2026-11-30T10:30:00.000Z");
        TaskWithReminders("Write", "2026-11-30T11:00:00.000Z");
        center.Refused = "Refuse";
        Scheduler().Sync();
        Assert.Equal(["Call", "Write"], center.Added.Select(reminder => reminder.Title));
    }

    [Fact]
    public void WhenWindowsRefusesNothingIsLostAndTheNextSyncTriesAgain()
    {
        TaskWithReminders("Call", "2026-11-30T10:00:00.000Z");
        var scheduler = Scheduler();
        center.Failure = new System.Runtime.InteropServices.COMException("Element not found.");
        scheduler.Sync();
        Assert.Empty(center.Added);

        center.Failure = null;
        scheduler.Sync();
        Assert.Single(center.Added);
    }
}

/// <summary>
/// What the app writes in the registry for itself, tried under a key of the
/// test's own and never where Windows would look.
/// </summary>
public sealed class WindowsRegistrationTests : IDisposable
{
    private readonly string path = $@"Software\HeraclesTests\{Guid.NewGuid():N}";
    private readonly RegistryKey classes;

    public WindowsRegistrationTests()
    {
        classes = Registry.CurrentUser.CreateSubKey(path);
    }

    public void Dispose()
    {
        classes.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        // The folder the tests share goes too, once it is empty.
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\HeraclesTests", writable: false);
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
        {
            Registry.CurrentUser.DeleteSubKey(@"Software\HeraclesTests", throwOnMissingSubKey: false);
        }
    }

    private const string Exe = @"C:\Apps\Heracles\Heracles.exe";

    [Fact]
    public void TheLinkSchemeStartsThisCopyWithTheLink()
    {
        Assert.False(LinkRegistration.IsRegistered(classes, Exe));
        LinkRegistration.Register(classes, Exe);

        using var scheme = classes.OpenSubKey("heracles");
        Assert.NotNull(scheme);
        Assert.Equal("URL:Heracles", scheme.GetValue(null));
        // Its presence, with any value, is what makes it a link scheme.
        Assert.Equal("", scheme.GetValue("URL Protocol"));
        using var command = scheme.OpenSubKey(@"shell\open\command");
        Assert.Equal("\"C:\\Apps\\Heracles\\Heracles.exe\" \"%1\"", command?.GetValue(null));
        Assert.True(LinkRegistration.IsRegistered(classes, Exe));
    }

    [Fact]
    public void AnotherCopyTakesTheSchemeOverWhenItStarts()
    {
        LinkRegistration.Register(classes, @"C:\Old\Heracles.exe");
        Assert.False(LinkRegistration.IsRegistered(classes, Exe));
        LinkRegistration.Register(classes, Exe);
        Assert.True(LinkRegistration.IsRegistered(classes, Exe));
        Assert.False(LinkRegistration.IsRegistered(classes, @"C:\Old\Heracles.exe"));
    }

    [Fact]
    public void TheAppsIdentityHasANameForItsNotifications()
    {
        Assert.False(AppIdentity.IsRegistered(classes));
        AppIdentity.Register(classes);
        using var identity = classes.OpenSubKey($@"AppUserModelId\{AppIdentity.Id}");
        Assert.Equal("Heracles", identity?.GetValue("DisplayName"));
        Assert.True(AppIdentity.IsRegistered(classes));
    }

    [Fact]
    public void TheAppRegistersAmongTheUsersOwnClasses()
    {
        // Spelt out by character: a backslash lost on the way into the source
        // would put the keys where Windows never looks.
        Assert.Equal(["Software", "Classes"], LinkRegistration.ClassesKey.Split((char)92));
    }

    [Fact]
    public void TheIdentityIsTheNativeAppsOwn()
    {
        Assert.Equal("com.heracles.notes.native", AppIdentity.Id);
    }

    [Fact]
    public void RegisteringTwiceLeavesThingsAsTheyWere()
    {
        LinkRegistration.Register(classes, Exe);
        LinkRegistration.Register(classes, Exe);
        AppIdentity.Register(classes);
        AppIdentity.Register(classes);
        Assert.Equal(["AppUserModelId", "heracles"], classes.GetSubKeyNames().Order());
    }
}

public sealed class AppLinkFromAReminderTests
{
    [Fact]
    public void ALinkWithMoreAfterTheNoteStillNamesTheNote()
    {
        Assert.Equal(new AppLink("abc-123", null), AppLink.Parse("heracles://note/abc-123?reminder=heracles.reminder.t1"));
        Assert.Equal(new AppLink("abc-123", "b-9"), AppLink.Parse("heracles://note/abc-123?reminder=x#block=b-9"));
    }
}

/// <summary>What the app does at start to be known to Windows, with a registry key and a notification list of the test's own.</summary>
public sealed class WindowsIntegrationTests : BoardFixture
{
    private readonly string path = string.Join((char)92, "Software", "HeraclesTests", Guid.NewGuid().ToString("N"));
    private readonly FakeReminderCenter center = new();
    private readonly List<(Action Work, TimeSpan After)> waiting = [];

    private void RunWaiting()
    {
        var due = waiting.ToList();
        waiting.Clear();
        foreach (var (work, _) in due) work();
    }

    private void InAKeyOfItsOwn(Action<RegistryKey> work)
    {
        var parent = string.Join((char)92, "Software", "HeraclesTests");
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(path);
            work(classes);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            using var shared = Registry.CurrentUser.OpenSubKey(parent, writable: false);
            if (shared is { SubKeyCount: 0, ValueCount: 0 }) Registry.CurrentUser.DeleteSubKey(parent, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void TheAppMakesItselfKnownAndHandsItsRemindersOver()
    {
        var call = AddCard("Up Next", "Call");
        Model.UpdateTask(call.Id, new TaskPatch(Reminders: [TaskDates.Format(DateTimeOffset.UtcNow.AddDays(2))]));

        InAKeyOfItsOwn(classes =>
        {
            var exe = string.Join((char)92, "C:", "Apps", "Heracles.exe");
            var scheduler = WindowsIntegration.Register(classes, exe, Model, () => center, (work, after) => waiting.Add((work, after)));

            Assert.NotNull(scheduler);
            Assert.True(AppIdentity.IsRegistered(classes));
            Assert.True(LinkRegistration.IsRegistered(classes, exe));

            // The reminders already in the library are handed over soon after start.
            RunWaiting();
            Assert.Equal(["Call"], center.Added.Select(reminder => reminder.Title));

            // And from then on, whenever the library changes.
            var write = AddCard("Up Next", "Write");
            Model.UpdateTask(write.Id, new TaskPatch(Reminders: [TaskDates.Format(DateTimeOffset.UtcNow.AddDays(3))]));
            RunWaiting();
            Assert.Equal(["Call", "Write"], center.Added.Select(reminder => reminder.Title));
        });
    }

    [Fact]
    public void WithNoPathToItselfTheAppStillHasItsReminders()
    {
        InAKeyOfItsOwn(classes =>
        {
            var scheduler = WindowsIntegration.Register(classes, null, Model, () => center, (work, after) => waiting.Add((work, after)));
            Assert.NotNull(scheduler);
            Assert.True(AppIdentity.IsRegistered(classes));
            Assert.Null(classes.OpenSubKey("heracles"));
        });
    }

    [Fact]
    public void WhenWindowsHasNoNotificationsTheLinksAreStillRegistered()
    {
        InAKeyOfItsOwn(classes =>
        {
            var exe = string.Join((char)92, "C:", "Apps", "Heracles.exe");
            var scheduler = WindowsIntegration.Register(
                classes, exe, Model, () => throw new System.Runtime.InteropServices.COMException("The notification platform is unavailable."));
            Assert.Null(scheduler);
            Assert.True(LinkRegistration.IsRegistered(classes, exe));
            // Nothing follows the library, and nothing fails when it changes.
            AddCard("Up Next", "Call");
            Assert.Null(Model.LastError);
        });
    }
}
