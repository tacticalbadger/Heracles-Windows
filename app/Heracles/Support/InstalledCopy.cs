using Microsoft.Win32;

namespace Heracles.Support;

/// <summary>
/// What Windows' list of installed apps says about this copy, for a copy
/// the installer put there (tools\installer\Heracles.iss).
///
/// The installer writes the version it installed. The app then updates
/// itself by replacing its own folder, with no installer involved, and the
/// list would go on showing the version that was first installed. So at
/// start the app writes its own version there, if the entry is about the
/// folder it runs from. A copy taken off a disc, a build made for trying
/// things, or a copy beside the installed one finds no entry of its own and
/// writes nothing.
///
/// This is done on a test library too, unlike the app's links and
/// reminders: it claims nothing for the copy that runs. It can only ever
/// touch the entry of the very folder that copy is in.
/// </summary>
internal static class InstalledCopy
{
    /// <summary>The installer's AppId. The same in tools\installer\Heracles.iss; never changed.</summary>
    public const string AppId = "{7E7666E6-6C97-4920-8107-E156E0BFCBB0}";

    /// <summary>Under HKEY_CURRENT_USER: Inno Setup names the key after the AppId.</summary>
    public static readonly string UninstallKey =
        string.Join((char)92, "Software", "Microsoft", "Windows", "CurrentVersion", "Uninstall", AppId + "_is1");

    /// <summary>At start: makes the installed-apps entry of this copy, if it has one, name this version.</summary>
    public static void RecordVersion()
    {
        try
        {
            var version = AppVersion.Current.Version;
            if (RecordVersion(Registry.CurrentUser, UninstallKey, AppContext.BaseDirectory, version))
            {
                Log.Info($"installed copy: Windows' list of installed apps now says {version}");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("installed copy: the version in Windows' list of installed apps could not be updated", error);
        }
    }

    /// <summary>
    /// Writes <paramref name="version"/> into the entry at <paramref name="keyPath"/>,
    /// if there is one, it is about <paramref name="folder"/>, and it says
    /// something else. Returns whether anything was written.
    /// </summary>
    public static bool RecordVersion(RegistryKey root, string keyPath, string folder, string version)
    {
        using var key = root.OpenSubKey(keyPath, writable: true);
        if (key is null) return false;
        if (key.GetValue("InstallLocation") is not string installed || !SameFolder(installed, folder)) return false;
        if (key.GetValue("DisplayVersion") as string == version) return false;
        key.SetValue("DisplayVersion", version, RegistryValueKind.String);
        // The same number in parts, under both spellings the installer uses.
        if (Version.TryParse(version, out var parsed))
        {
            foreach (var name in new[] { "MajorVersion", "VersionMajor" }) key.SetValue(name, parsed.Major, RegistryValueKind.DWord);
            foreach (var name in new[] { "MinorVersion", "VersionMinor" }) key.SetValue(name, parsed.Minor, RegistryValueKind.DWord);
        }
        return true;
    }

    private static bool SameFolder(string one, string other)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(one)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(other)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
