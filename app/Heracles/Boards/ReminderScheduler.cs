using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using Heracles.Model;

namespace Heracles.Boards;

/// <summary>
/// The system's list of notifications still to come. Windows' own in the
/// app; a made-up one in the tests.
/// </summary>
internal interface IReminderCenter
{
    /// <summary>Every notification waiting, this app's reminders or not, with what it says where that can be read.</summary>
    IReadOnlyList<ScheduledReminder> Scheduled();

    void Remove(IReadOnlyList<string> identifiers);

    void Add(PlannedReminder reminder);
}

/// <summary>
/// Keeps the system's scheduled notifications in step with the reminders on
/// tasks. The system delivers them whether or not the app is running.
/// </summary>
internal sealed class ReminderScheduler
{
    /// <summary>Changes arrive in bursts, and one sync covers a burst.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);

    private readonly AppModel model;
    private readonly IReminderCenter center;
    private readonly Action<Action, TimeSpan> later;
    private readonly Func<DateTimeOffset> now;

    /// <summary>Goes up with every ask, so that only the last sync put off is the one that runs.</summary>
    private int asked;

    /// <summary>
    /// The reminders as they stood when Windows last took them all. The
    /// library changes at every pause in typing, and almost none of that is
    /// news about reminders: while they stand as they did, Windows is not asked.
    /// </summary>
    private string? handedOver;

    /// <param name="later">How something is put off for a while. Left out, a timer on this thread's dispatcher.</param>
    /// <param name="now">The time. Left out, the clock's.</param>
    public ReminderScheduler(
        AppModel model, IReminderCenter center, Action<Action, TimeSpan>? later = null, Func<DateTimeOffset>? now = null)
    {
        this.model = model;
        this.center = center;
        this.later = later ?? OnATimer;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
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

    /// <summary>Sync soon.</summary>
    public void SyncSoon()
    {
        var number = ++asked;
        later(
            () =>
            {
                if (number == asked) Sync();
            },
            Delay);
    }

    public void Sync()
    {
        asked++;
        Reminder[] reminders;
        try { reminders = model.Store.ListReminders(); }
        catch (UniffiException) { return; }

        // Windows can refuse any of this (notifications switched off by
        // policy, a platform that is not answering). A reminder not handed
        // over now is handed over at the next change: nothing is lost by
        // going on without it, and the app must not stop for it.
        try
        {
            var moment = now();
            var standing = Standing(reminders, moment);
            if (standing == handedOver) return;

            var plan = new ReminderPlan(reminders, center.Scheduled(), moment);
            if (plan.ToRemove.Count > 0) center.Remove(plan.ToRemove);
            var added = 0;
            foreach (var reminder in plan.ToAdd)
            {
                // One that Windows will not take (its time came while this
                // ran) does not stop the others.
                try
                {
                    center.Add(reminder);
                    added++;
                }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
                {
                    Log.Error($"reminders: one could not be scheduled ({reminder.Identifier})", error);
                }
            }
            if (added > 0 || plan.ToRemove.Count > 0)
            {
                Log.Info($"reminders: {added} scheduled, {plan.ToRemove.Count} taken back");
            }
            // One that was refused is tried again at the next change.
            handedOver = added == plan.ToAdd.Count ? standing : null;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            Log.Error("reminders: could not be scheduled", error);
        }
    }

    /// <summary>The reminders still to come, as one string: which, for which list, and under what name.</summary>
    private static string Standing(IEnumerable<Reminder> reminders, DateTimeOffset moment) =>
        string.Join(
            "\n",
            new ReminderPlan(reminders, [], moment).Wanted.Select(reminder => $"{reminder.Identifier}\t{reminder.NoteId}\t{reminder.Title}"));
}

/// <summary>
/// A reminder as Windows shows it: a toast that says the task, and opens its
/// todo list through a link when it is clicked, with no code of the app's
/// running.
/// </summary>
internal static class ReminderToast
{
    private const string IdentifierKey = "reminder=";

    /// <summary>
    /// The link a click opens. It also carries the reminder's identifier:
    /// Windows keeps a toast's content, and a tag is too short to hold one.
    /// </summary>
    public static string Link(PlannedReminder reminder) =>
        new AppLink(reminder.NoteId, null).Url + "?" + IdentifierKey + Uri.EscapeDataString(reminder.Identifier);

    public static string Xml(PlannedReminder reminder) =>
        new XElement(
            "toast",
            new XAttribute("activationType", "protocol"),
            new XAttribute("launch", Link(reminder)),
            new XElement(
                "visual",
                new XElement(
                    "binding",
                    new XAttribute("template", "ToastGeneric"),
                    new XElement("text", reminder.Title),
                    new XElement("text", "Reminder")))).ToString(SaveOptions.DisableFormatting);

    /// <summary>The identifier a toast of this app's carries, or null for any other toast.</summary>
    public static string? Identifier(string xml)
    {
        XElement toast;
        try { toast = XElement.Parse(xml); }
        catch (XmlException) { return null; }

        var launch = toast.Attribute("launch")?.Value;
        if (launch is null || AppLink.Parse(launch) is null) return null;
        var query = launch.IndexOf('?');
        if (query < 0) return null;
        foreach (var part in launch[(query + 1)..].Split('#')[0].Split('&'))
        {
            if (part.StartsWith(IdentifierKey, StringComparison.Ordinal) && part.Length > IdentifierKey.Length)
            {
                return Uri.UnescapeDataString(part[IdentifierKey.Length..]);
            }
        }
        return null;
    }

    /// <summary>What a toast says first: the task's title. Null when it cannot be read.</summary>
    public static string? Title(string xml)
    {
        try { return XElement.Parse(xml).Descendants("text").FirstOrDefault()?.Value; }
        catch (XmlException) { return null; }
    }

    /// <summary>A short name for a reminder, the same every time, for Windows to tell toasts apart by.</summary>
    public static string Tag(string identifier) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)), 0, 8);
}
