using Heracles.Boards;
using Microsoft.Win32;

namespace Heracles.Tests;

/// <summary>
/// A test that talks to Windows' own notification platform. It is skipped
/// unless <c>HERACLES_TEST_TOASTS</c> is 1, because it writes a key under
/// the current user's classes (and removes it), which no other test does.
/// </summary>
public sealed class ToastFactAttribute : FactAttribute
{
    public const string Variable = "HERACLES_TEST_TOASTS";

    public ToastFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip = $"Set {Variable}=1 to hand a reminder to Windows and take it back.";
        }
    }
}

/// <summary>
/// The real list of scheduled notifications, under an identity of the
/// test's own. The reminder is for a month from now and is taken back at
/// once, so nothing is ever shown.
/// </summary>
public sealed class ToastCenterTests
{
    private const string Identity = "com.heracles.notes.test";

    private static readonly string IdentityKey = LinkRegistration.ClassesKey + (char)92 + "AppUserModelId" + (char)92 + Identity;

    [ToastFact]
    public void AReminderHandedToWindowsIsListedThereAndCanBeTakenBack()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(IdentityKey))
        {
            key.SetValue("DisplayName", "Heracles (test)");
        }
        try
        {
            var center = new ToastCenter(Identity);
            var when = DateTimeOffset.UtcNow.AddDays(30);
            var planned = new PlannedReminder(
                $"{ReminderPlan.Prefix}test-task.{TaskDates.Format(when)}", "A test reminder: never shown", "list-1", when);
            center.Remove(center.Scheduled().Select(waiting => waiting.Identifier).ToList());

            center.Add(planned);
            // Windows gives it back as it was given: which reminder, and what it says.
            Assert.Equal([new ScheduledReminder(planned.Identifier, planned.Title)], center.Scheduled());

            // What the scheduler would do with it: nothing, while it is wanted.
            var wanted = new Reminder("test-task", "list-1", "A test reminder: never shown", TaskDates.Format(when));
            var plan = new ReminderPlan([wanted], center.Scheduled(), DateTimeOffset.UtcNow);
            Assert.Empty(plan.ToAdd);
            Assert.Empty(plan.ToRemove);

            center.Remove([planned.Identifier]);
            Assert.Empty(center.Scheduled());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(IdentityKey, throwOnMissingSubKey: false);
            // Windows makes two keys of its own for an identity it has seen.
            var windows = string.Join((char)92, "Software", "Microsoft", "Windows", "CurrentVersion");
            foreach (var made in new[] { string.Join((char)92, windows, "Notifications", "Settings", Identity), string.Join((char)92, windows, "PushNotifications", "Backup", Identity) })
            {
                Registry.CurrentUser.DeleteSubKeyTree(made, throwOnMissingSubKey: false);
            }
        }
    }
}
