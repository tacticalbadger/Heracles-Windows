using System.Globalization;

namespace Heracles.Boards;

/// <summary>
/// Reading and writing the dates stored on tasks. They are UTC timestamps in
/// the form JavaScript's <c>toISOString</c> writes, which is what the
/// Electron app stored.
/// </summary>
internal static class TaskDates
{
    private static readonly string[] Stored =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mm:ssK",
    ];

    private const string BareDay = "yyyy-MM-dd";

    public static DateTimeOffset? Parse(string text, TimeZoneInfo? zone = null)
    {
        if (DateTimeOffset.TryParseExact(
                text, Stored, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return date.ToUniversalTime();
        }
        // A bare day, such as "2026-12-01".
        if (DateOnly.TryParseExact(text, BareDay, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return At(day, TimeOnly.MinValue, zone);
        }
        return null;
    }

    public static string Format(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A moment: a time of day on a day, where the user is.</summary>
    public static DateTimeOffset At(DateOnly day, TimeOnly time, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        // An hour the clocks skip in spring does not exist. The hour after does.
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>The day a moment falls on, where the user is.</summary>
    private static DateOnly DayOf(DateTimeOffset moment, TimeZoneInfo? zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, zone ?? TimeZoneInfo.Local).DateTime);

    /// <summary>A due date: nine in the morning, local time, on the chosen day.</summary>
    public static string DueDate(DateOnly day, TimeZoneInfo? zone = null) => Format(At(day, new TimeOnly(9, 0), zone));

    public static string DueDate(DateTimeOffset day, TimeZoneInfo? zone = null) => DueDate(DayOf(day, zone), zone);

    /// <summary>A deadline: five in the afternoon, local time, on the chosen day.</summary>
    public static string Deadline(DateOnly day, TimeZoneInfo? zone = null) => Format(At(day, new TimeOnly(17, 0), zone));

    public static string Deadline(DateTimeOffset day, TimeZoneInfo? zone = null) => Deadline(DayOf(day, zone), zone);

    /// <summary>The day a stored date falls on where the user is: what a calendar shows for it.</summary>
    public static DateOnly? Day(string stored, TimeZoneInfo? zone = null) =>
        Parse(stored, zone) is { } date ? DayOf(date, zone) : null;

    /// <summary>
    /// A stored date as a card shows it: "1 Dec", with the year if it is not
    /// this one. The order is the user's own ("Dec 1" for an American).
    /// </summary>
    public static string Label(string stored, DateTimeOffset? now = null, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        if (Parse(stored, zone) is not { } date) return stored;
        zone ??= TimeZoneInfo.Local;
        culture ??= CultureInfo.CurrentCulture;
        var local = TimeZoneInfo.ConvertTime(date, zone);
        var sameYear = local.Year == TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.Now, zone).Year;
        return local.ToString(sameYear ? DayAndMonth(culture) : DayMonthAndYear(culture), culture);
    }

    /// <summary>A stored moment in full, as a reminder or a comment shows it: "30 Nov 2026 09:00".</summary>
    public static string DateAndTime(string stored, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        if (Parse(stored, zone) is not { } date) return stored;
        culture ??= CultureInfo.CurrentCulture;
        var local = TimeZoneInfo.ConvertTime(date, zone ?? TimeZoneInfo.Local);
        return local.ToString(DayMonthAndYear(culture), culture) + " " + local.ToString("t", culture);
    }

    /// <summary>The culture's own day and month, with the month cut short.</summary>
    private static string DayAndMonth(CultureInfo culture) =>
        culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");

    private static string DayMonthAndYear(CultureInfo culture)
    {
        var dayAndMonth = DayAndMonth(culture);
        // Where the year is written first, it is first here too.
        var shortDate = culture.DateTimeFormat.ShortDatePattern;
        if (shortDate.IndexOf('y') is >= 0 and var year && year < shortDate.IndexOf('M')) return "yyyy " + dayAndMonth;
        // "Dec 1, 2027" where the month leads, "1 Dec 2027" where the day does.
        return dayAndMonth.TrimStart().StartsWith('M') ? dayAndMonth + ", yyyy" : dayAndMonth + " yyyy";
    }

    /// <summary>A time of day as someone types it: "9:30", "17:05", "5pm", "7". Null when it is not one.</summary>
    public static TimeOnly? TimeOfDay(string typed, CultureInfo? culture = null)
    {
        var text = typed.Trim();
        if (text.Length == 0) return null;
        // An hour alone.
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var hour))
        {
            return hour is >= 0 and <= 23 ? new TimeOnly(hour, 0) : null;
        }
        foreach (var from in new[] { culture ?? CultureInfo.CurrentCulture, CultureInfo.InvariantCulture })
        {
            if (TimeOnly.TryParse(text, from, DateTimeStyles.AllowWhiteSpaces, out var time))
            {
                return new TimeOnly(time.Hour, time.Minute);
            }
        }
        return null;
    }
}

/// <summary>A notification to schedule for a task's reminder.</summary>
/// <param name="NoteId">The todo list to open when the notification is clicked.</param>
internal sealed record PlannedReminder(string Identifier, string Title, string NoteId, DateTimeOffset Date);

/// <summary>
/// What to change so that the notifications the system holds match the
/// reminders the library has. Worked out apart from the system calls, so it
/// can be tested.
/// </summary>
internal sealed class ReminderPlan
{
    /// <summary>Marks a notification as one of this app's reminders.</summary>
    public const string Prefix = "heracles.reminder.";

    public IReadOnlyList<PlannedReminder> ToAdd { get; }

    public IReadOnlyList<string> ToRemove { get; }

    public ReminderPlan(IEnumerable<Reminder> reminders, IEnumerable<string> scheduled, DateTimeOffset now)
    {
        var wanted = new List<PlannedReminder>();
        var wantedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reminder in reminders)
        {
            if (TaskDates.Parse(reminder.RemindAt) is not { } date || date <= now) continue;
            var identifier = $"{Prefix}{reminder.TaskId}.{reminder.RemindAt}";
            // One notification for a reminder, however often it is listed.
            if (!wantedIds.Add(identifier)) continue;
            var title = reminder.TaskTitle.Trim();
            wanted.Add(new PlannedReminder(identifier, title.Length == 0 ? "Untitled task" : title, reminder.TodoListId, date));
        }
        var held = scheduled.ToList();
        var existing = held.ToHashSet(StringComparer.Ordinal);
        ToAdd = wanted.Where(reminder => !existing.Contains(reminder.Identifier)).ToList();
        ToRemove = held
            .Where(identifier => identifier.StartsWith(Prefix, StringComparison.Ordinal) && !wantedIds.Contains(identifier))
            .Distinct()
            .ToList();
    }
}
