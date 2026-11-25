using System.Text.Json;

namespace Heracles.Updates;

/// <summary>
/// Where updates come from and the key they are checked against: a small
/// file beside the program, <c>Heracles.update.json</c>, which the package
/// step writes (tools\build.ps1) from tools\update-config.json. A build made
/// any other way has no such file, and does not look for updates.
/// </summary>
/// <param name="Feed">The address of the feed: a file on the newest GitHub release.</param>
/// <param name="Key">
/// The public half of the update key. A feed is believed only if it carries
/// a signature made with the private half.
/// </param>
internal sealed record UpdateSettings(string Feed, string Key)
{
    public const string FileName = "Heracles.update.json";

    /// <summary>What the file in a folder says, or null when it is not there or does not name both.</summary>
    public static UpdateSettings? Load(string folder)
    {
        try { return Parse(File.ReadAllText(Path.Combine(folder, FileName))); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static UpdateSettings? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var feed = Text(root, "feed");
            var key = Text(root, "key");
            return feed is null || key is null ? null : new UpdateSettings(feed, key);
        }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}

/// <summary>Whether this run looks for updates, and where.</summary>
/// <param name="IsOn">Whether it looks at all.</param>
/// <param name="Feed">A feed named for this run, or null for the one in the settings.</param>
/// <param name="WhyOff">For a run that does not look, the reason, for the log.</param>
internal readonly record struct UpdatePolicy(bool IsOn, string? Feed, string WhyOff)
{
    /// <summary>Names a feed for this run. Taken only in a run on a library of its own: see <see cref="Decide"/>.</summary>
    public const string FeedArgument = "--updater-feed";

    public static UpdatePolicy On(string? feed) => new(true, feed, "");

    public static UpdatePolicy Off(string why) => new(false, null, why);

    /// <param name="arguments">The command line.</param>
    /// <param name="environment">The process's environment.</param>
    /// <param name="settings">What the file beside the program says, if there is one.</param>
    public static UpdatePolicy Decide(IReadOnlyList<string> arguments, Func<string, string?> environment, UpdateSettings? settings)
    {
        // A build made without the feed address and the key has nothing to
        // check against: one built for trying things out, for one.
        if (settings is null) return Off($"this build has no {UpdateSettings.FileName} beside it");
        // A run against a library named in the environment is a trial or a
        // test. It must not find the published app and offer to replace
        // itself with it. The update rehearsal names a feed of its own
        // instead; the everyday app takes no feed from the command line.
        if (environment(AppPaths.DataDirectoryVariable) is { Length: > 0 })
        {
            var flag = arguments.ToList().IndexOf(FeedArgument);
            if (flag < 0 || flag + 1 >= arguments.Count)
            {
                return Off($"the library is one named in {AppPaths.DataDirectoryVariable}, and no feed was named for it");
            }
            return On(arguments[flag + 1]);
        }
        return On(null);
    }
}
