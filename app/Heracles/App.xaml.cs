using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Heracles.Model;

namespace Heracles;

public partial class App : Application
{
    private readonly List<MainWindow> windows = [];
    private SingleInstance? instance;
    private MainWindow? frontmost;
    private bool exiting;

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
        Log.Info($"Heracles {VersionText}");

        // Logged, not swallowed: the app still stops, and the log says why.
        DispatcherUnhandledException += (_, args) => Log.Error("unhandled exception", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("unhandled exception (fatal): " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("unobserved task exception", args.Exception);
            args.SetObserved();
        };

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
            return;
        }

        Model.PendingLinkChanged += TakePendingLink;
        Model.ErrorChanged += ShowError;

        var saved = WindowStates.Load(AppPaths.WindowsPath);
        if (saved.Count == 0) OpenWindow();
        foreach (var window in saved) OpenWindow(window);

        if (e.Args.Select(AppLink.Parse).FirstOrDefault(link => link is not null) is { } link) Model.PendingLink = link;
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
        // Whatever way the app is ending, an editor may still hold typing.
        Model?.SaveEverything();
        Model?.Dispose();
        Store?.Dispose();
        instance?.Dispose();
        Log.Info("exit");
        base.OnExit(e);
    }
}
