using System.Runtime.InteropServices;
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
/// The name Windows knows the app by. Notifications belong to an identity,
/// and an app with no installer gives itself one: a key under
/// <c>HKCU\Software\Classes\AppUserModelId</c>, and a call that says this
/// process is that app.
/// </summary>
internal static class AppIdentity
{
    public const string Id = "com.heracles.notes.native";

    private const string DisplayName = "Heracles";

    private static string KeyPath => $@"AppUserModelId\{Id}";

    public static bool IsRegistered(RegistryKey classes)
    {
        using var identity = classes.OpenSubKey(KeyPath);
        return identity?.GetValue("DisplayName") as string == DisplayName;
    }

    public static void Register(RegistryKey classes)
    {
        if (IsRegistered(classes)) return;
        using var identity = classes.CreateSubKey(KeyPath);
        identity.SetValue("DisplayName", DisplayName);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    /// <summary>Say that this process is the app. False if Windows would not have it.</summary>
    public static bool Claim() => SetCurrentProcessExplicitAppUserModelID(Id) >= 0;
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
        AppIdentity.Register(classes);
        if (exePath is not null) LinkRegistration.Register(classes, exePath);

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
