using System.Globalization;

namespace Heracles.Updates;

/// <summary>What the copy that installs an update is asked to do.</summary>
/// <param name="Source">The folder the new app was unpacked into, which is where the installing copy runs from.</param>
/// <param name="Target">The folder the app lives in: the one to replace.</param>
/// <param name="ProcessId">The running app, which has to have quit first.</param>
/// <param name="Restart">Whether to start the app again afterwards.</param>
/// <param name="Version">What is being installed, for the log.</param>
internal sealed record InstallRequest(string Source, string Target, int ProcessId, bool Restart, string Version);

/// <param name="WhyNot">For an update that was not installed, the reason. The app is then as it was.</param>
internal sealed record InstallOutcome(bool Installed, string? WhyNot);

/// <summary>
/// Puts a fetched update in the app's place.
///
/// A program cannot replace its own files while it runs, so the app does
/// not try. When it quits it starts the new version from the folder the
/// update was unpacked into, with <c>--install-update</c>. That copy waits
/// for the old app to be gone, copies itself beside the app's folder, and
/// then swaps the two folders: two renames, with the long work done before
/// either. The old folder is kept aside until the new app has started, and
/// if anything fails it is put back.
///
/// Every folder this touches is one nothing is running from: the app's own
/// once the app has quit, and two beside it that are the updater's own
/// (<see cref="IncomingSuffix"/>, <see cref="PreviousSuffix"/>). A folder
/// with one of those names that the updater did not make is somebody's,
/// and is never removed: the updater's own carry a marker file.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>On the command line of the copy that installs.</summary>
    public const string Argument = "--install-update";

    public const string Program = "Heracles.exe";

    /// <summary>The new app, beside the app's folder, until it takes its place.</summary>
    public const string IncomingSuffix = ".update-new";

    /// <summary>The app as it was, beside the new one, until the new one has started.</summary>
    public const string PreviousSuffix = ".update-old";

    /// <summary>The file that says a folder is the updater's own to remove.</summary>
    public const string Marker = ".heracles-update";

    /// <summary>The folder, in the app's folder for updates, that a fetched update is unpacked into.</summary>
    public const string StagedName = "staged";

    /// <summary>Whether two paths name one folder, however they are cased and whether or not they end in a separator.</summary>
    private static bool SameFolder(string one, string other)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(one)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(other)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    // The command line

    public static string[] Arguments(InstallRequest request)
    {
        List<string> arguments =
        [
            Argument,
            "--pid", request.ProcessId.ToString(CultureInfo.InvariantCulture),
            "--target", request.Target,
            "--version", request.Version,
        ];
        if (request.Restart) arguments.Add("--restart");
        return [.. arguments];
    }

    /// <summary>The request on a command line, or null when the command line is anything else.</summary>
    /// <param name="source">The folder the copy reading this runs from.</param>
    public static InstallRequest? Parse(IReadOnlyList<string> arguments, string source)
    {
        if (!arguments.Contains(Argument)) return null;
        string? After(string name)
        {
            var at = arguments.ToList().IndexOf(name);
            return at >= 0 && at + 1 < arguments.Count ? arguments[at + 1] : null;
        }
        if (!int.TryParse(After("--pid"), NumberStyles.None, CultureInfo.InvariantCulture, out var processId)) return null;
        if (After("--target") is not { Length: > 0 } target || !Path.IsPathFullyQualified(target)) return null;
        if (After("--version") is not { Length: > 0 } version) return null;
        return new InstallRequest(source, target, processId, arguments.Contains("--restart"), version);
    }

    // Whether this copy can be replaced at all

    /// <summary>
    /// Why the app in a folder cannot replace itself, in words for its user,
    /// or null when it can. A copy run from a disc, or from somewhere Windows
    /// keeps it from writing, is told so before anything is downloaded.
    /// </summary>
    public static string? WhyNotHere(string appFolder)
    {
        const string elsewhere = " Copy the Heracles folder to a place of your own on this PC, and run it from there.";
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appFolder)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "Heracles cannot tell which folder it is in, so it cannot update itself.";
        }
        if (!Directory.Exists(full)) return "Heracles cannot find the folder it is in, so it cannot update itself.";
        if (Path.GetDirectoryName(full) is not { Length: > 0 } parent)
        {
            return "Heracles is at the top of a drive, where it cannot update itself." + elsewhere;
        }
        // The swap needs a folder made beside the app's, and the app's own
        // moved: tried with a folder and a file nobody will miss.
        var probe = Path.Combine(parent, Path.GetFileName(full) + ".update-probe-" + Guid.NewGuid().ToString("N"));
        var inside = Path.Combine(full, Marker + "-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            File.WriteAllBytes(inside, []);
            File.Delete(inside);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "Heracles cannot update itself where it is: Windows does not let it change this folder." + elsewhere;
        }
    }

    // Installing

    /// <summary>
    /// Replace the app in <see cref="InstallRequest.Target"/> with the one in
    /// <see cref="InstallRequest.Source"/>. Either the new app is in place
    /// afterwards, or the old one is exactly as it was.
    /// </summary>
    /// <param name="updatesFolder">The app's own folder for updates, which is where a fetched one is unpacked.</param>
    /// <param name="appHasQuit">Waits for the running app to be gone, and says whether it went.</param>
    /// <param name="tries">How often a rename that Windows refuses is tried: something may hold a file for a moment.</param>
    public static InstallOutcome Install(
        InstallRequest request, string updatesFolder, Func<int, bool> appHasQuit, Action<string> log, int tries = 20, TimeSpan? pause = null)
    {
        var wait = pause ?? TimeSpan.FromMilliseconds(500);
        var target = Path.TrimEndingDirectorySeparator(request.Target);
        var incoming = target + IncomingSuffix;
        var previous = target + PreviousSuffix;
        var marker = Path.Combine(target, Marker);

        log($"installing {request.Version}");
        InstallOutcome Stop(string why)
        {
            log("stopped: " + why);
            return new InstallOutcome(false, why);
        }

        // The request comes on a command line, and anybody who can start a
        // program can write one: Windows starts the app with a link as its
        // argument, and a link can be made to say anything. So the request
        // is not taken at its word. Only the copy that the updater itself
        // unpacked (after checking it against the signed feed) may replace
        // anything, and the only thing it may replace is a folder that
        // holds the app. The app as it is installed, started with this
        // request, does nothing.
        if (!SameFolder(request.Source, Path.Combine(updatesFolder, StagedName)))
        {
            return Stop("this copy is not one that was fetched as an update, so it replaces nothing");
        }
        if (!File.Exists(Path.Combine(request.Source, Program))) return Stop($"the update holds no {Program}");
        if (!Directory.Exists(target)) return Stop("the folder Heracles was in is no longer there");
        if (SameFolder(target, request.Source)) return Stop("the folder to replace is the one the update is in");
        if (!File.Exists(Path.Combine(target, Program)))
        {
            return Stop($"the folder to replace does not hold Heracles ({Program}), so it is left alone");
        }
        if (!appHasQuit(request.ProcessId)) return Stop("Heracles did not quit, so nothing was changed");

        try
        {
            // The updater's own leftovers go. Anything else with these names
            // is somebody's, and stops the install where it stands.
            foreach (var inTheWay in new[] { incoming, previous })
            {
                if (!Directory.Exists(inTheWay) && !File.Exists(inTheWay)) continue;
                if (!IsOurs(inTheWay))
                {
                    return Stop($"a folder named {Path.GetFileName(inTheWay)} is in the way, and it is not one the updater made");
                }
                DeleteFolder(inTheWay);
            }

            // The long part first, while the app's folder is untouched.
            Directory.CreateDirectory(incoming);
            File.WriteAllBytes(Path.Combine(incoming, Marker), []);
            CopyFolder(request.Source, incoming);
            File.Delete(Path.Combine(incoming, Marker));
            // The old folder is marked while it is still in place, so that
            // once aside it is known for the updater's.
            File.WriteAllBytes(marker, []);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Quietly(() => DeleteFolder(incoming));
            Quietly(() => File.Delete(marker));
            return Stop("the update could not be copied into place: " + error.Message);
        }

        // Two renames: the old folder aside, the new one in.
        if (Retry(() => Directory.Move(target, previous), tries, wait) is { } held)
        {
            Quietly(() => File.Delete(marker));
            Quietly(() => DeleteFolder(incoming));
            return Stop("the folder Heracles is in could not be replaced: " + held.Message);
        }
        if (Retry(() => Directory.Move(incoming, target), tries, wait) is { } refused)
        {
            // Back as it was.
            Retry(() => Directory.Move(previous, target), tries, wait);
            Quietly(() => File.Delete(marker));
            Quietly(() => DeleteFolder(incoming));
            return Stop("the folder Heracles is in could not be replaced: " + refused.Message);
        }
        log($"installed {request.Version}");
        return new InstallOutcome(true, null);
    }

    /// <summary>
    /// Remove what an install left for later: the old app kept aside, the
    /// folder the update was unpacked into, a download that never finished.
    /// Done by the app when it starts. Nothing here can fail the app: what
    /// cannot be removed now is tried again next time.
    /// </summary>
    public static void RemoveLeftovers(string appFolder, string updatesFolder)
    {
        var target = Path.TrimEndingDirectorySeparator(appFolder);
        foreach (var beside in new[] { target + PreviousSuffix, target + IncomingSuffix })
        {
            if (IsOurs(beside)) Quietly(() => DeleteFolder(beside));
        }
        Quietly(() => DeleteFolder(Path.Combine(updatesFolder, StagedName)));
        Quietly(() => File.Delete(Path.Combine(updatesFolder, "download.part")));
    }

    /// <summary>
    /// Whether anything is still there for <see cref="RemoveLeftovers"/> to
    /// remove. The copy that installed an update may still be closing, and
    /// its folder cannot go until it has.
    /// </summary>
    public static bool HasLeftovers(string appFolder, string updatesFolder)
    {
        var target = Path.TrimEndingDirectorySeparator(appFolder);
        return IsOurs(target + PreviousSuffix) || IsOurs(target + IncomingSuffix)
            || Directory.Exists(Path.Combine(updatesFolder, StagedName))
            || File.Exists(Path.Combine(updatesFolder, "download.part"));
    }

    // Folders

    private static bool IsOurs(string folder) => Directory.Exists(folder) && File.Exists(Path.Combine(folder, Marker));

    /// <summary>Remove a folder and all in it, read-only files too. No fault if it is not there.</summary>
    internal static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        // Files copied off a disc are read-only, and Windows will not
        // delete those until they are told otherwise.
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(folder, recursive: true);
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var folder in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
        }
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: false);
        }
    }

    /// <returns>Null once the work went through, or what stopped it on the last try.</returns>
    private static Exception? Retry(Action work, int tries, TimeSpan pause)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < Math.Max(1, tries); attempt++)
        {
            if (attempt > 0) Thread.Sleep(pause);
            try
            {
                work();
                return null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { last = error; }
        }
        return last;
    }

    private static void Quietly(Action work)
    {
        try { work(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
