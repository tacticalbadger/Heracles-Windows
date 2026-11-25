using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Heracles.Updates;

/// <summary>
/// Updates over the air: the app's end of them.
///
/// Once a day, and when asked from the Help menu, the app reads a feed (a
/// file on the newest GitHub release). What it finds there is offered with
/// its release notes, and fetched and installed when the user agrees.
/// Installing quits the app the way Exit does, so every editor saves first.
///
/// Nothing is taken on the feed's word: the feed has to carry a signature
/// made with the update key, whose public half is beside the program, and
/// the archive has to be the one the feed describes (<see cref="UpdateFeed"/>,
/// <see cref="Updater"/>). Whoever controls the place the files are served
/// from can withhold an update and cannot supply one.
///
/// Not every run looks: see <see cref="UpdatePolicy"/>. What the Mac app does
/// with Sparkle, this does with code of the app's own, because nothing like
/// Sparkle fits an app that has no installer.
/// </summary>
internal sealed class UpdateCenter
{
    public static UpdateCenter Shared { get; } = new();

    // The daily look is not timed across days: the timer comes round at
    // least this often and asks whether a look is due, so a PC that slept,
    // or a clock that was put right, does not throw it out.
    private static readonly TimeSpan LongestWait = TimeSpan.FromHours(1);
    private static readonly TimeSpan AfterStart = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AfterALook = TimeSpan.FromMinutes(1);

    private Updater? updater;
    private UpdatePreferences preferences = new();
    private string preferencesPath = "";
    private DispatcherTimer? timer;
    private Task cleanup = Task.CompletedTask;
    private Func<Window?> owner = () => null;
    private Action quit = () => { };
    private bool looking;
    private UpdateWindow? window;
    private CancellationTokenSource? fetching;
    private (string Folder, string Version)? ready;
    private bool restartAfterInstall;

    /// <summary>Whether this run looks for updates at all.</summary>
    public bool IsActive => updater is not null;

    /// <summary>False while a look is under way or an update is on offer, and in a run that does not look.</summary>
    public bool CanCheckForUpdates => IsActive && !looking && window is null;

    /// <summary>The daily look. On unless the user switches it off.</summary>
    public bool ChecksAutomatically
    {
        get => IsActive && preferences.Automatic;
        set
        {
            if (!IsActive || preferences.Automatic == value) return;
            preferences = preferences with { Automatic = value };
            Save();
            Arm(AfterStart);
        }
    }

    /// <summary>
    /// Start looking for updates, unless this run has no business looking.
    /// </summary>
    /// <param name="owner">The window an offer is shown over.</param>
    /// <param name="quit">Ends the app as its Exit does: every editor saves first.</param>
    public void Start(IReadOnlyList<string> arguments, Func<Window?> owner, Action quit)
    {
        if (updater is not null) return;
        this.owner = owner;
        this.quit = quit;
        var appFolder = AppContext.BaseDirectory;
        var updates = AppPaths.UpdatesDirectory;
        // Whatever an update left for later goes now: the old app kept
        // aside, the folder the update was unpacked into. Off this thread,
        // and before any look, so that it can never take a new download.
        cleanup = Task.Run(() =>
        {
            // The copy that installed an update may still be closing, with
            // the folder it ran from in use: a few tries, a moment apart.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                UpdateInstaller.RemoveLeftovers(appFolder, updates);
                if (!UpdateInstaller.HasLeftovers(appFolder, updates)) return;
                Thread.Sleep(TimeSpan.FromSeconds(2));
            }
        });

        var settings = UpdateSettings.Load(appFolder);
        var policy = UpdatePolicy.Decide(arguments, Environment.GetEnvironmentVariable, settings);
        if (!policy.IsOn || settings is null)
        {
            Log.Info("updates: off: " + policy.WhyOff);
            return;
        }
        updater = new Updater(
            settings, policy.Feed, updates, AppVersion.Current,
            UpdateFeed.ArchitectureName(RuntimeInformation.ProcessArchitecture), line => Log.Info("updates: " + line));
        preferencesPath = Path.Combine(updates, UpdatePreferences.FileName);
        preferences = UpdatePreferences.Load(preferencesPath);
        Log.Info($"updates: on, reading {updater.FeedAddress}");

        timer = new DispatcherTimer();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (preferences.IsCheckDue(DateTimeOffset.UtcNow)) _ = Look(manual: false);
            else Arm(AfterALook);
        };
        Arm(AfterStart);
    }

    /// <summary>Set the timer for the next daily look, or stop it when the daily look is off.</summary>
    private void Arm(TimeSpan atLeast)
    {
        if (timer is null) return;
        timer.Stop();
        if (!preferences.Automatic) return;
        var wait = preferences.UntilNextCheck(DateTimeOffset.UtcNow);
        if (wait > LongestWait) wait = LongestWait;
        timer.Interval = wait > atLeast ? wait : atLeast;
        timer.Start();
    }

    private void Save()
    {
        try { preferences.Save(preferencesPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error("updates: what was chosen could not be kept", error);
        }
    }

    /// <summary>Help ▸ Check for Updates…: looks now and says what it found.</summary>
    public void CheckForUpdates()
    {
        if (CanCheckForUpdates) _ = Look(manual: true);
    }

    /// <summary>
    /// One look at the feed. The daily one says nothing unless there is an
    /// update; one asked for from the menu always answers.
    /// </summary>
    private async Task Look(bool manual)
    {
        if (updater is null || looking) return;
        looking = true;
        try
        {
            await cleanup;
            var result = await updater.CheckAsync();
            preferences = preferences with { LastCheck = DateTimeOffset.UtcNow };
            Save();
            switch (result.Kind)
            {
                case UpdateCheckKind.Found when result is { Manifest: { } manifest, Archive: { } archive }:
                    if (!manual && manifest.Build == preferences.SkippedBuild)
                    {
                        Log.Info($"updates: {manifest.Version} was skipped, so nothing is said of it");
                        break;
                    }
                    Offer(manifest, archive, asked: manual);
                    break;
                case UpdateCheckKind.NothingNewer when manual:
                    Prompts.Message(owner(), "Heracles is up to date", $"Heracles {AppVersion.Current.Version} is the newest version there is.");
                    break;
                case UpdateCheckKind.Stopped when manual:
                    Prompts.Message(owner(), "Heracles could not look for updates", (result.Reason ?? "Something went wrong") + ".");
                    break;
            }
        }
        // A look must never take the app down with it.
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
        {
            Log.Error("updates: the look failed", error);
        }
        finally
        {
            looking = false;
            Arm(AfterALook);
        }
    }

    /// <summary>Show what was found, and do what is chosen.</summary>
    /// <param name="asked">Whether the look was asked for from the menu, and not the daily one.</param>
    private void Offer(UpdateManifest manifest, UpdateArchive archive, bool asked)
    {
        if (window is not null)
        {
            window.Activate();
            return;
        }
        var offer = new UpdateWindow(manifest, AppVersion.Current, UpdateInstaller.WhyNotHere(AppContext.BaseDirectory));
        window = offer;
        // An offer nobody asked for does not take the keyboard: whoever is
        // typing in a note goes on typing in it, and Enter does not land on
        // "Install Update".
        offer.ShowActivated = asked;
        if (owner() is { IsVisible: true } over) offer.Owner = over;
        else offer.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // Fetched already, and still waiting for the app to quit.
        if (ready is { } waiting && waiting.Version == manifest.Version) offer.ShowReady();

        offer.InstallChosen += () => _ = Fetch(offer, manifest, archive);
        offer.CancelChosen += () => fetching?.Cancel();
        offer.SkipChosen += () =>
        {
            preferences = preferences with { SkippedBuild = manifest.Build };
            Save();
            Log.Info($"updates: {manifest.Version} is to be skipped");
            offer.Close();
        };
        offer.RestartChosen += () =>
        {
            restartAfterInstall = true;
            offer.Close();
            quit();
        };
        offer.Closed += (_, _) =>
        {
            // A download nobody is waiting for is called off.
            fetching?.Cancel();
            if (ReferenceEquals(window, offer)) window = null;
        };
        offer.Show();
    }

    private async Task Fetch(UpdateWindow offer, UpdateManifest manifest, UpdateArchive archive)
    {
        if (updater is null || fetching is not null) return;
        using var called = new CancellationTokenSource();
        fetching = called;
        offer.ShowFetching();
        try
        {
            var folder = await updater.FetchAsync(manifest, archive, offer.Progress, called.Token);
            ready = (folder, manifest.Version);
            offer.ShowReady();
        }
        catch (OperationCanceledException)
        {
            Log.Info($"updates: the download of {manifest.Version} was called off");
            offer.ShowOffer();
        }
        catch (UpdateRefusedException refused)
        {
            offer.ShowProblem($"The update was not installed. {refused.Message}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error($"updates: stopped: {manifest.Version} could not be unpacked", error);
            offer.ShowProblem($"The update was not installed. {error.Message}");
        }
        finally
        {
            fetching = null;
        }
    }

    /// <summary>
    /// The app is ending. If an update was fetched and checked, the new
    /// version is started from where it was unpacked, to put itself in this
    /// copy's place once this copy is gone (<see cref="UpdateInstaller"/>).
    /// </summary>
    public void InstallOnExit()
    {
        try { fetching?.Cancel(); }
        catch (ObjectDisposedException) { }
        if (ready is not { } update) return;
        ready = null;
        var request = new InstallRequest(
            update.Folder, Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Environment.ProcessId, restartAfterInstall, update.Version);
        try
        {
            var start = new ProcessStartInfo(Path.Combine(update.Folder, UpdateInstaller.Program))
            {
                UseShellExecute = false,
                WorkingDirectory = update.Folder,
            };
            foreach (var argument in UpdateInstaller.Arguments(request)) start.ArgumentList.Add(argument);
            using var installer = Process.Start(start);
            Log.Info($"updates: {update.Version} was handed to its installer");
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Log.Error($"updates: stopped: the installer of {update.Version} could not be started", error);
        }
    }

    /// <summary>
    /// What the copy started with <c>--install-update</c> does: wait for the
    /// app to be gone, put itself in its place, and start it again if asked.
    /// </summary>
    /// <returns>Whether the update was installed.</returns>
    public static bool RunInstaller(InstallRequest request)
    {
        var outcome = UpdateInstaller.Install(request, AppPaths.UpdatesDirectory, WaitForExit, line => Log.Info("updates: " + line));
        if (request.Restart)
        {
            // The new app, or the old one where the install did not go through.
            try
            {
                using var app = Process.Start(new ProcessStartInfo(Path.Combine(request.Target, UpdateInstaller.Program))
                {
                    UseShellExecute = false,
                    WorkingDirectory = request.Target,
                });
            }
            catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
            {
                Log.Error("updates: Heracles could not be started again", error);
            }
        }
        return outcome.Installed;
    }

    /// <summary>Wait, a minute at most, for a process to be gone.</summary>
    private static bool WaitForExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WaitForExit(60_000);
        }
        // No such process: it has gone already.
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (Win32Exception) { return false; }
    }
}
