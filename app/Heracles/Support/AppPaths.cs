namespace Heracles.Support;

/// <summary>Where the app keeps its files.</summary>
internal sealed record AppLocations(string DataDirectory, string LogPath);

/// <summary>
/// Where the app keeps its files. The library folder is the one the Electron
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

        return new AppLocations(
            Override(DataDirectoryVariable) ?? Path.Combine(appData, "Heracles"),
            Override(LogPathVariable) ?? Path.Combine(localAppData, "Heracles", "heracles.log"));
    }

    // Static fields are initialised in the order written: this one first, so
    // the properties below never see it unset.
    private static readonly AppLocations Current = Resolve(
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string DataDirectory => Current.DataDirectory;

    public static string LogPath => Current.LogPath;

    /// <summary>The windows that were open, kept beside the library they show.</summary>
    public static string WindowsPath => Path.Combine(DataDirectory, "windows.json");
}
