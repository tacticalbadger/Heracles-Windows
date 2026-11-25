using System.Text.Json;

namespace Heracles.Updates;

/// <summary>
/// What a copy of the app remembers about updates between runs: whether it
/// looks by itself, when it last looked, and the version its user said to
/// pass over. Kept in a small file of the app's own, not in the library:
/// it is about this copy on this PC.
/// </summary>
/// <param name="Automatic">The daily look. On unless the user switches it off.</param>
/// <param name="LastCheck">When the feed was last read.</param>
/// <param name="SkippedBuild">A build the user chose to skip: the daily look says nothing about it.</param>
internal sealed record UpdatePreferences(bool Automatic = true, DateTimeOffset? LastCheck = null, int? SkippedBuild = null)
{
    public const string FileName = "preferences.json";

    /// <summary>How often the app looks by itself.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions Form = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>What is kept at a path. A file that is not there, or cannot be read, is as if nothing had been chosen yet.</summary>
    public static UpdatePreferences Load(string path)
    {
        try { return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path), Form) ?? new UpdatePreferences(); }
        catch (IOException) { return new UpdatePreferences(); }
        catch (UnauthorizedAccessException) { return new UpdatePreferences(); }
        catch (JsonException) { return new UpdatePreferences(); }
    }

    public void Save(string path)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Form));
    }

    /// <summary>
    /// How long after a look that could not be made the next one is tried.
    /// Not at once: with no network there is nothing to be had by asking
    /// every minute.
    /// </summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// What is remembered once a look is over. Only a look that read the
    /// feed is the day's look. One that could not be made (no network yet,
    /// a feed that was refused) leaves things as they were, so that it is
    /// tried again: counted as the day's look, a PC that always starts
    /// before its network is up would never look at all.
    /// </summary>
    public UpdatePreferences After(UpdateCheckKind kind, DateTimeOffset now) =>
        kind == UpdateCheckKind.Stopped ? this : this with { LastCheck = now };

    /// <summary>Whether it is time for the daily look.</summary>
    public bool IsCheckDue(DateTimeOffset now) => Automatic && UntilNextCheck(now) == TimeSpan.Zero;

    /// <summary>How long from now the next daily look is due. Nothing, when it is due already.</summary>
    public TimeSpan UntilNextCheck(DateTimeOffset now)
    {
        // A last look in the future means the clock was put back since. Such
        // a copy must not wait for a day that has already been.
        if (LastCheck is not { } last || last > now) return TimeSpan.Zero;
        var left = last + Interval - now;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}
