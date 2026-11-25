using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Heracles.Boards;
using Heracles.Model;
using Heracles.Updates;
using Microsoft.Win32;

namespace Heracles;

public partial class App : Application
{
    private readonly List<MainWindow> windows = [];
    private SingleInstance? instance;
    private MainWindow? frontmost;
    private ReminderScheduler? reminders;
    private bool exiting;
    // This run only installs an update: see InstallUpdate.
    private bool installing;

    /// <summary>The library, once it is open. Every window reads from this one store.</summary>
    internal static Store? Store { get; private set; }

    /// <summary>The app's one view of the library, shared by every window.</summary>
    internal static AppModel? Model { get; private set; }

    /// <summary>The version and build, as "0.2.0 (build 12)".</summary>
    internal static string VersionText =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private const int AnyProcess = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Logged, not swallowed: the app still stops, and the log says why.
        DispatcherUnhandledException += (_, args) => Log.Error("unhandled exception", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("unhandled exception (fatal): " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("unobserved task exception", args.Exception);
            args.SetObserved();
        };

        // Started by a copy of the app that has fetched an update: this is
        // the new version, run from where it was unpacked, and all it does
        // is put itself in that copy's place. It opens no library, claims
        // nothing, and takes nothing of Windows for itself.
        if (UpdateInstaller.Parse(e.Args, AppContext.BaseDirectory) is { } request)
        {
            InstallUpdate(request);
            return;
        }
        Log.Info($"Heracles {VersionText}");

        // One process per library: the change feed that keeps windows in step
        // lives inside the process.
        instance = SingleInstance.Claim(AppPaths.DataDirectory, args =>
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => OnSecondLaunch(args)));
        if (instance is null)
        {
            // Let the running copy bring its window to the front.
            AllowSetForegroundWindow(AnyProcess);
            var handed = SingleInstance.Send(AppPaths.DataDirectory, e.Args, TimeSpan.FromSeconds(5));
            Log.Info(handed ? "instance: handed over to the running app" : "instance: the running app did not answer");
            Shutdown(handed ? 0 : 1);
            return;
        }
        Log.Info("instance: first");

        // A copy the installer put here says which version it is now: it may
        // have updated itself since, and Windows' list would not know.
        InstalledCopy.RecordVersion();

        // A testing aid: the app in light or in dark whatever Windows is set
        // to, so that both can be looked at on one machine.
        if (e.Args.Contains("--theme-light")) ThemeMode = ThemeMode.Light;
        else if (e.Args.Contains("--theme-dark")) ThemeMode = ThemeMode.Dark;

        try
        {
            Store = new Store(AppPaths.DataDirectory);
            if (e.Args.Contains(E2eSeed.Argument)) E2eSeed.Run(Store);
            // A change made on another thread comes back to this one through
            // the dispatcher, named here so it does not depend on what the
            // thread's synchronization context happens to be at start-up.
            Model = new AppModel(Store, action => Dispatcher.BeginInvoke(DispatcherPriority.Normal, action));
            Log.Info($"library: opened, {Store.ListNotes().Length} notes");
        }
        // Whatever stopped the library opening (the folder cannot be used, the
        // database is damaged, the core's DLL is missing), a window says so.
        catch (Exception error)
        {
            Log.Error($"library: could not be opened: {error.Message}");
            new MainWindow(error.Message).Show();
            // A copy that cannot open its library can still be replaced by
            // one that can.
            UpdateCenter.Shared.Start(e.Args, () => null, () => Shutdown());
            return;
        }

        Model.PendingLinkChanged += TakePendingLink;
        Model.ErrorChanged += ShowError;

        // A library the original app left behind is brought across before the
        // first window opens, so that the window opens on it. If it cannot be
        // read, the app starts on its own empty library and says so.
        var imported = Model.ImportLegacyLibraryIfNeeded(AppModel.LegacyDatabase(AppPaths.DataDirectory));
        if (imported is not null)
        {
            Log.Info($"legacy: imported {imported.Notes} notes, {imported.Tasks} tasks, {imported.Images} images");
        }
        else if (Model.LastError is { } failure)
        {
            Log.Error("legacy: could not be imported: " + failure);
        }

        RegisterWithWindows(Model);

        var saved = WindowStates.Load(AppPaths.WindowsPath);
        if (saved.Count == 0) OpenWindow();
        foreach (var window in saved) OpenWindow(window);

        // Said once, over the first window. It is not modal: the app can be
        // used behind it, and a second copy started meanwhile still hands over.
        if (imported is not null)
        {
            var summary = new ImportSummaryWindow(imported);
            if (frontmost is { IsVisible: true }) summary.Owner = frontmost;
            else summary.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            summary.Show();
        }

        if (e.Args.Select(AppLink.Parse).FirstOrDefault(link => link is not null) is { } link) Model.PendingLink = link;

        // Over-the-air updates. Not every run looks: see UpdatePolicy.
        UpdateCenter.Shared.Start(e.Args, () => frontmost, ExitApp);
    }

    /// <summary>
    /// All that a run started with <c>--install-update</c> does: say that an
    /// update is being installed, install it off this thread, and end.
    /// </summary>
    private void InstallUpdate(InstallRequest request)
    {
        installing = true;
        // The window is put away when the work is over, not before: closed
        // half way, an install would stop with the app's folder moved aside.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var notice = new InstallingWindow(request.Version);
        notice.Show();
        Task.Run(() => UpdateCenter.RunInstaller(request)).ContinueWith(work =>
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                if (work.Exception is { } failure) Log.Error("updates: stopped: the install failed", failure);
                notice.MayClose = true;
                Shutdown(work is { IsCompletedSuccessfully: true, Result: true } ? 0 : 1);
            }));
    }

    /// <summary>
    /// Tell Windows who the app is, so that a <c>heracles://</c> link starts this
    /// copy and reminders are delivered under its name, and hand the reminders
    /// over. All of it is the current user's own (under <c>HKCU</c>), and needs no
    /// administrator.
    ///
    /// A run on a test library does none of it: it must not take the user's
    /// links for itself, nor put a test's reminders on the screen.
    /// </summary>
    private void RegisterWithWindows(AppModel model)
    {
        if (AppPaths.IsTestLibrary) return;
        // Windows can refuse any of this (a locked-down registry, notifications
        // switched off by policy). The app is no worse for it than before it
        // had reminders, so it says so in the log and goes on.
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(LinkRegistration.ClassesKey);
            reminders = WindowsIntegration.Register(classes, Environment.ProcessPath, model, () => new ToastCenter(AppIdentity.Id));
            Log.Info(reminders is null ? "windows: links registered, reminders not" : "windows: links and reminders registered");
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            Log.Error("windows: links and reminders could not be registered", error);
        }
    }

    /// <summary>Open a window on the library, as it was saved or as new.</summary>
    internal MainWindow? OpenWindow(SavedWindow? saved = null, string? noteId = null)
    {
        if (Model is null) return null;
        var window = new MainWindow(Model);
        if (saved is not null) window.Restore(saved);
        if (noteId is not null) window.Workspace?.Open(noteId);

        windows.Add(window);
        frontmost = window;
        window.Activated += (_, _) => frontmost = window;
        SavedWindow? last = null;
        window.Closing += (_, _) => last = window.Capture();
        window.Closed += (_, _) =>
        {
            windows.Remove(window);
            if (ReferenceEquals(frontmost, window)) frontmost = windows.LastOrDefault();
            Log.Info($"window: closed ({windows.Count} open)");
            // Closing one window of several forgets it. Closing the last one
            // ends the app, and that window is the one to come back to.
            if (!exiting) SaveWindows(windows.Count > 0 || last is null ? windows.Select(open => open.Capture()) : [last]);
        };
        window.Show();
        Log.Info($"window: opened ({windows.Count} open)");
        return window;
    }

    /// <summary>End the app from the menu: every window that is open comes back next time.</summary>
    internal void ExitApp()
    {
        // Nothing typed is lost on quit: every editor saves before anything closes.
        Model?.SaveEverything();
        SaveWindows(windows.Select(window => window.Capture()));
        exiting = true;
        Shutdown();
    }

    private static void SaveWindows(IEnumerable<SavedWindow> states)
    {
        try { WindowStates.Save(AppPaths.WindowsPath, states); }
        catch (IOException error) { Log.Error("windows: could not be saved", error); }
        catch (UnauthorizedAccessException error) { Log.Error("windows: could not be saved", error); }
    }

    /// <summary>Another copy of the app was started. It has gone again and left its arguments.</summary>
    private void OnSecondLaunch(string[] args)
    {
        Log.Info($"instance: second launch: {string.Join(" ", args)}");
        if (Model is null) return;
        if (args.Contains("--new-window"))
        {
            OpenWindow();
        }
        else if (args.Select(AppLink.Parse).FirstOrDefault(link => link is not null) is { } link)
        {
            Model.PendingLink = link;
        }
        BringForward(frontmost);
    }

    private static void BringForward(Window? window)
    {
        if (window is null) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>A link opens in the window that is in front.</summary>
    private void TakePendingLink()
    {
        if (Model?.PendingLink is not { } link) return;
        Model.PendingLink = null;
        (frontmost ?? windows.FirstOrDefault())?.Workspace?.Follow(link);
        BringForward(frontmost);
    }

    private void ShowError()
    {
        // Taken now: a later success clears it before the dialog can ask.
        if (Model?.LastError is not { } text) return;
        Log.Info($"error shown: {text}");
        // Shown once the action that failed has returned, not in the middle of it.
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => Prompts.Error(frontmost, text));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (installing)
        {
            base.OnExit(e);
            return;
        }
        // Whatever way the app is ending, an editor may still hold typing.
        Model?.SaveEverything();
        Model?.Dispose();
        Store?.Dispose();
        instance?.Dispose();
        // Last of all, once what was typed is saved and the library closed:
        // an update that was fetched is started, to take this copy's place.
        UpdateCenter.Shared.InstallOnExit();
        Log.Info("exit");
        base.OnExit(e);
    }
}
