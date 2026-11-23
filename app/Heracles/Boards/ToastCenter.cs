using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Heracles.Boards;

/// <summary>
/// Windows' own list of notifications still to come, for one application
/// identity. A toast put on it is delivered at its time whether or not the
/// app is running, and kept in the notification centre afterwards.
///
/// Windows knows each toast by a tag too short for a reminder's identifier,
/// so the identifier is read back out of the toast's own content
/// (<see cref="ReminderToast"/>).
/// </summary>
internal sealed class ToastCenter : IReminderCenter
{
    private const string Group = "reminders";

    private readonly ToastNotifier notifier;

    public ToastCenter(string identity)
    {
        notifier = ToastNotificationManager.CreateToastNotifier(identity);
    }

    private static string? IdentifierOf(ScheduledToastNotification toast) => ReminderToast.Identifier(toast.Content.GetXml());

    public IReadOnlyList<ScheduledReminder> Scheduled()
    {
        var waiting = new List<ScheduledReminder>();
        foreach (var toast in notifier.GetScheduledToastNotifications())
        {
            var xml = toast.Content.GetXml();
            // A toast that is not one of these has no identifier, and is nothing to do with reminders.
            if (ReminderToast.Identifier(xml) is { } identifier) waiting.Add(new ScheduledReminder(identifier, ReminderToast.Title(xml)));
        }
        return waiting;
    }

    public void Remove(IReadOnlyList<string> identifiers)
    {
        var unwanted = identifiers.ToHashSet(StringComparer.Ordinal);
        foreach (var toast in notifier.GetScheduledToastNotifications())
        {
            if (IdentifierOf(toast) is { } identifier && unwanted.Contains(identifier)) notifier.RemoveFromSchedule(toast);
        }
    }

    public void Add(PlannedReminder reminder)
    {
        var content = new XmlDocument();
        content.LoadXml(ReminderToast.Xml(reminder));
        notifier.AddToSchedule(new ScheduledToastNotification(content, reminder.Date)
        {
            Tag = ReminderToast.Tag(reminder.Identifier),
            Group = Group,
        });
    }
}
