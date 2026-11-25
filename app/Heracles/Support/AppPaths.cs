namespace Heracles.Support;

/// <summary>Where the app keeps its files.</summary>
/// <param name="IsTestLibrary">
/// The library folder was given by <c>HERACLES_DATA_DIR</c>: a test, or a try
/// on a copy. Such a run takes nothing of the user's for itself: not the link
/// scheme, and no notifications.
/// </param>
/// <param name="UpdatesDirectory">
/// What the app keeps about updates: when it last looked, and what it has
/// downloaded. It is about this copy on this PC, so it is beside the log
/// and not in the library; a run on a test library keeps its own, inside
/// that library's folder, and never touches the real app's.
/// </param>
internal sealed record AppLocations(string DataDirectory, string LogPath, bool IsTestLibrary, string UpdatesDirectory);

/// <summary>
/// Where the app keeps its files. The library folder is the one the original
/// app used, so its database is found beside the new one on first launch.
/// Two environment variables point a run somewhere else: tests use them so
/// that the real library is never touched.
/// </summary>
internal static class AppPaths
{
    /// <summary>The library folder: the database, the image store, backups.</summary>
    public const string DataDirectoryVariable = "HERACLES_DATA_DIR";

    /// <summary>The log file.</summary>
    public const string LogPathVariable = "HERACLES_LOG_PATH";

    public static AppLocations Resolve(Func<string, string?> environment, string appData, string localAppData)
    {
        // A variable set to nothing is the same as one not set.
        string? Override(string name) => environment(name) is { Length: > 0 } value ? value : null;

        var trial = Override(DataDirectoryVariable);
        return new AppLocations(
            trial ?? Path.Combine(appData, "Heracles"),
            Override(LogPathVariable) ?? Path.Combine(localAppData, "Heracles", "heracles.log"),
            IsTestLibrary: trial is not null,
            UpdatesDirectory: trial is not null ? Path.Combine(trial, "updates") : Path.Combine(localAppData, "Heracles", "updates"));
    }

    // Static fields are initialised in the order written: this one first, so
    // the properties below never see it unset.
    private static readonly AppLocations Current = Resolve(
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string DataDirectory => Current.DataDirectory;

    public static string LogPath => Current.LogPath;

    public static bool IsTestLibrary => Current.IsTestLibrary;

    public static string UpdatesDirectory => Current.UpdatesDirectory;

    /// <summary>The windows that were open, kept beside the library they show.</summary>
    public static string WindowsPath => Path.Combine(DataDirectory, "windows.json");
}
