using System.Reflection;

namespace Heracles;

public partial class App : Application
{
    /// <summary>The library, once it is open. Every window reads from this one store.</summary>
    internal static Store? Store { get; private set; }

    /// <summary>The version and build, as "0.2.0 (build 12)".</summary>
    internal static string VersionText =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

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

        var window = new MainWindow();
        try
        {
            Store = new Store(AppPaths.DataDirectory);
            var count = Store.ListNotes().Length;
            Log.Info($"library: opened, {count} notes");
            window.ShowLibrary(count);
        }
        // Whatever stopped the library opening (the folder cannot be used, the
        // database is damaged, the core's DLL is missing), the window says so.
        catch (Exception error)
        {
            Log.Error($"library: could not be opened: {error.Message}");
            window.ShowStartupError(error.Message);
        }
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Store?.Dispose();
        Log.Info("exit");
        base.OnExit(e);
    }
}
