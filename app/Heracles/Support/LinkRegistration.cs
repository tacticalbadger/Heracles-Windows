using Microsoft.Win32;

namespace Heracles.Support;

/// <summary>
/// The <c>heracles</c> link scheme, registered for the current user so that
/// a link clicked anywhere (a reminder, another program) starts this copy of
/// the app with the link. Written under <c>HKCU\Software\Classes</c>, which
/// needs no administrator. The key to write under is passed in, so the
/// tests can use one of their own.
/// </summary>
internal static class LinkRegistration
{
    /// <summary>
    /// Where the current user's own classes are kept, under <c>HKCU</c>. The
    /// app registers itself here; the tests never open it.
    /// </summary>
    public const string ClassesKey = @"Software\Classes";

    private const string Scheme = Model.AppLink.Scheme;
    private const string CommandKey = @"shell\open\command";

    private static string Command(string exePath) => $"\"{exePath}\" \"%1\"";

    public static bool IsRegistered(RegistryKey classes, string exePath)
    {
        using var scheme = classes.OpenSubKey(Scheme);
        if (scheme?.GetValue("URL Protocol") is null) return false;
        using var command = scheme.OpenSubKey(CommandKey);
        return string.Equals(command?.GetValue(null) as string, Command(exePath), StringComparison.OrdinalIgnoreCase);
    }

    public static void Register(RegistryKey classes, string exePath)
    {
        if (IsRegistered(classes, exePath)) return;
        using var scheme = classes.CreateSubKey(Scheme);
        scheme.SetValue(null, "URL:Heracles");
        // Its presence, with any value, is what makes this a link scheme.
        scheme.SetValue("URL Protocol", "");
        using var command = scheme.CreateSubKey(CommandKey);
        command.SetValue(null, Command(exePath));
    }
}

/// <summary>
/// The name Windows knows the app's notifications by. Notifications belong
/// to an identity, and an app with no installer gives itself one: a key
/// under <c>HKCU\Software\Classes\AppUserModelId</c>.
///
/// The process is not told to go by this identity. (The spike did that, and
/// handing a reminder to Windows turned out not to need it: a test hands
/// one over from a process that never said who it was.) An explicit
/// identity on the process parts it from a taskbar pin made from the
/// program file, which would then show beside the running app, not as it.
/// </summary>
internal static class AppIdentity
{
    public const string Id = "com.heracles.notes.native";

    /// <summary>The app's program file, without its extension.</summary>
    public const string ProgramName = "Heracles";

    /// <summary>The picture of the app's icon that the build puts beside the program.</summary>
    public const string PictureName = "Heracles.png";

    private const string DisplayName = "Heracles";

    /// <summary>Where Windows looks for the picture to show on the identity's notifications.</summary>
    private const string IconValue = "IconUri";

    private static string KeyPath => $@"AppUserModelId\{Id}";

    /// <summary>Whether <paramref name="exePath"/> is the app's own program, and not something that hosts it.</summary>
    public static bool IsOwnProgram(string? exePath) =>
        exePath is not null
        && string.Equals(Path.GetFileNameWithoutExtension(exePath), ProgramName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The picture of the icon beside the app's own program, if it is there.
    /// Beside anything else (a host, a debugger) there is none of the app's.
    /// </summary>
    public static string? PictureBeside(string? exePath)
    {
        if (!IsOwnProgram(exePath) || Path.GetDirectoryName(exePath) is not { Length: > 0 } folder) return null;
        var picture = Path.Combine(folder, PictureName);
        return File.Exists(picture) ? picture : null;
    }

    /// <summary>Whether the identity is there under its name, with this picture or, for null, with none.</summary>
    public static bool IsRegistered(RegistryKey classes, string? picture = null)
    {
        using var identity = classes.OpenSubKey(KeyPath);
        return identity?.GetValue("DisplayName") as string == DisplayName
            && string.Equals(identity.GetValue(IconValue) as string, picture, StringComparison.OrdinalIgnoreCase);
    }

    public static void Register(RegistryKey classes, string? picture = null)
    {
        if (IsRegistered(classes, picture)) return;
        using var identity = classes.CreateSubKey(KeyPath);
        identity.SetValue("DisplayName", DisplayName);
        // A picture that is no longer there must not stay named: the app may
        // have moved, or been replaced by a copy without one.
        if (picture is null) identity.DeleteValue(IconValue, throwOnMissingValue: false);
        else identity.SetValue(IconValue, picture);
    }
}

/// <summary>
/// What the app does at start to be known to Windows: its identity and its
/// link scheme in the registry, and its reminders handed over and kept in
/// step from then on. Everything it touches is passed in, so the tests use
/// a registry key and a list of notifications of their own.
/// </summary>
internal static class WindowsIntegration
{
    /// <returns>
    /// What now keeps Windows' notifications in step with the library, or
    /// null when Windows has none to give. The links are registered either way.
    /// </returns>
    public static Boards.ReminderScheduler? Register(
        RegistryKey classes,
        string? exePath,
        Model.AppModel model,
        Func<Boards.IReminderCenter> center,
        Action<Action, TimeSpan>? later = null)
    {
        // The identity, with the app's icon for its notifications where the
        // picture is beside the program.
        AppIdentity.Register(classes, AppIdentity.PictureBeside(exePath));
        // Links go to the app's own program. Started some other way (by a
        // host, in a debugger), the path is the host's, and links must not
        // be sent there.
        if (exePath is not null && AppIdentity.IsOwnProgram(exePath)) LinkRegistration.Register(classes, exePath);

        Boards.IReminderCenter notifications;
        try { notifications = center(); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            Log.Error("reminders: Windows has no notifications to give", error);
            return null;
        }
        var scheduler = new Boards.ReminderScheduler(model, notifications, later);
        model.AfterChange = scheduler.SyncSoon;
        // The reminders the library already holds, soon after start.
        scheduler.SyncSoon();
        return scheduler;
    }
}
