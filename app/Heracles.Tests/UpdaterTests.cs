using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Heracles.Updates;

namespace Heracles.Tests;

/// <summary>
/// Looking for an update and fetching one, against a web server of the
/// test's own on this machine, with a key made for the test. Nothing here
/// is the real feed or the real key.
/// </summary>
public sealed class UpdaterTests : IDisposable
{
    private readonly TestUpdateKey key = new();
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"heracles-tests-{Guid.NewGuid()}");
    private readonly List<string> log = [];
    private readonly Dictionary<string, (string Status, byte[] Body)> pages = [];
    private readonly LocalWeb web;

    public UpdaterTests()
    {
        web = new LocalWeb(path => pages.TryGetValue(path, out var page)
            ? (page.Status, "application/octet-stream", page.Body, null)
            : ("404 Not Found", "text/plain", Encoding.ASCII.GetBytes("Not Found"), null));
    }

    public void Dispose()
    {
        web.Dispose();
        key.Dispose();
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    private string Updates => Path.Combine(folder, "updates");

    private Updater Updater(int ownBuild = 52, string? feed = null, string? feedForThisRun = null, string architecture = "arm64") =>
        new(new UpdateSettings(feed ?? web.Address("feed/update-feed.json"), key.Public), feedForThisRun, Updates,
            new AppVersion("0.3.0", ownBuild), architecture, log.Add);

    /// <summary>The app as an update carries it: a zip of the folder's files.</summary>
    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        return stream.ToArray();
    }

    private static readonly byte[] App = Zip(("Heracles.exe", "the new program"), ("Heracles.dll", "its code"), ("en/words.txt", "words"));

    /// <summary>Put a feed and its archive on the server, as a release has them.</summary>
    private void Publish(int build = 53, string version = "0.3.1", byte[]? archive = null, byte[]? served = null, string architecture = "arm64")
    {
        archive ??= App;
        var sum = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var manifest = $$$"""
            {"version":"{{{version}}}","build":{{{build}}},"notes":"- What changed.",
             "archives":{"{{{architecture}}}":{"url":"{{{web.Address("feed/Heracles.zip")}}}","size":{{{archive.Length}}},"sha256":"{{{sum}}}"} } }
            """;
        pages["/feed/update-feed.json"] = ("200 OK", key.Feed(manifest));
        pages["/feed/Heracles.zip"] = ("200 OK", served ?? archive);
    }

    private List<string> Asked()
    {
        lock (web.Asked) return [.. web.Asked];
    }

    // Looking

    [Fact]
    public async Task ANewerVersionInTheFeedIsFound()
    {
        Publish(build: 53);

        var result = await Updater(ownBuild: 52).CheckAsync();

        Assert.Equal(UpdateCheckKind.Found, result.Kind);
        Assert.Equal("0.3.1", result.Manifest?.Version);
        Assert.Equal(53, result.Manifest?.Build);
        Assert.Equal("- What changed.", result.Manifest?.Notes);
        Assert.Equal(App.Length, result.Archive?.Size);
        Assert.Equal(["found 0.3.1 (53)"], log);
        // Looking fetches the feed and nothing else.
        Assert.Equal(["/feed/update-feed.json"], Asked());
    }

    [Theory]
    [InlineData(53)]
    [InlineData(54)]
    [InlineData(9000)]
    public async Task ACopyThatIsAsNewOrNewerIsOfferedNothing(int ownBuild)
    {
        // The build number decides, not the version: an older feed served
        // again offers nothing to a copy that is past it.
        Publish(build: 53);

        var result = await Updater(ownBuild).CheckAsync();

        Assert.Equal(UpdateCheckKind.NothingNewer, result.Kind);
        Assert.Equal(["nothing newer in the feed"], log);
    }

    [Fact]
    public async Task AFeedAlteredAfterItWasSignedStopsTheLookAndSaysWhy()
    {
        Publish();
        var feed = Encoding.UTF8.GetString(pages["/feed/update-feed.json"].Body);
        // One character of what it says, changed after it was signed.
        var at = feed.IndexOf("\"manifest\":\"", StringComparison.Ordinal) + 20;
        var altered = feed[..at] + (feed[at] == 'A' ? 'B' : 'A') + feed[(at + 1)..];
        pages["/feed/update-feed.json"] = ("200 OK", Encoding.UTF8.GetBytes(altered));

        var result = await Updater().CheckAsync();

        Assert.Equal(UpdateCheckKind.Stopped, result.Kind);
        Assert.Equal("The update feed is improperly signed", result.Reason);
        Assert.Equal(["stopped: The update feed is improperly signed"], log);
        Assert.Null(result.Manifest);
        // Nothing is fetched on the word of such a feed.
        Assert.DoesNotContain("/feed/Heracles.zip", Asked());
    }

    [Fact]
    public async Task AFeedSignedWithAnotherKeyStopsTheLook()
    {
        Publish();
        using var other = new TestUpdateKey();
        var updater = new Updater(new UpdateSettings(web.Address("feed/update-feed.json"), other.Public), null, Updates,
            new AppVersion("0.3.0", 52), "arm64", log.Add);

        var result = await updater.CheckAsync();

        Assert.Equal(UpdateCheckKind.Stopped, result.Kind);
        Assert.Equal("The update feed is improperly signed", result.Reason);
    }

    [Fact]
    public async Task AFeedThatIsNotThereStopsTheLook()
    {
        var result = await Updater().CheckAsync();

        Assert.Equal(UpdateCheckKind.Stopped, result.Kind);
        Assert.Contains("could not be fetched", result.Reason);
        Assert.Contains("404", result.Reason);
        Assert.StartsWith("stopped: ", Assert.Single(log));
    }

    [Fact]
    public async Task APlaceThatDoesNotAnswerStopsTheLook()
    {
        // A port nothing listens on: taken, and given back.
        var taken = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        taken.Start();
        var port = ((System.Net.IPEndPoint)taken.LocalEndpoint).Port;
        taken.Stop();

        var result = await Updater(feed: $"http://127.0.0.1:{port}/update-feed.json").CheckAsync();

        Assert.Equal(UpdateCheckKind.Stopped, result.Kind);
        Assert.Contains("could not be fetched", result.Reason);
    }

    [Fact]
    public async Task AFeedWithNothingForThisProcessorStopsTheLook()
    {
        Publish(architecture: "x64");

        var result = await Updater(architecture: "arm64").CheckAsync();

        Assert.Equal(UpdateCheckKind.Stopped, result.Kind);
        Assert.Contains("arm64", result.Reason);
    }

    [Fact]
    public async Task AFeedNamedForTheRunIsTheOneRead()
    {
        Publish();
        pages["/rehearsal/update-feed.json"] = pages["/feed/update-feed.json"];
        pages.Remove("/feed/update-feed.json");

        var result = await Updater(feed: "https://example.invalid/never-asked.json", feedForThisRun: web.Address("rehearsal/update-feed.json")).CheckAsync();

        Assert.Equal(UpdateCheckKind.Found, result.Kind);
        Assert.Equal(["/rehearsal/update-feed.json"], Asked());
    }

    [Fact]
    public async Task AFeedAtAnAddressUpdatesAreNotTakenFromIsNeverAsked()
    {
        var result = await Updater(feed: "http://example.invalid/update-feed.json").CheckAsync();

        Assert.Equal(UpdateCheckKind.Stopped, result.Kind);
        Assert.Contains("address", result.Reason);
        Assert.Empty(Asked());
    }

    // Fetching

    private async Task<(UpdateManifest Manifest, UpdateArchive Archive, Updater Updater)> Found()
    {
        var updater = Updater();
        var result = await updater.CheckAsync();
        Assert.Equal(UpdateCheckKind.Found, result.Kind);
        log.Clear();
        return (result.Manifest!, result.Archive!, updater);
    }

    [Fact]
    public async Task TheArchiveIsFetchedCheckedAndUnpacked()
    {
        Publish();
        var (manifest, archive, updater) = await Found();
        var progress = new List<double>();

        var staged = await updater.FetchAsync(manifest, archive, new Collect(progress));

        Assert.Equal(Path.Combine(Updates, "staged"), staged);
        Assert.Equal("the new program", File.ReadAllText(Path.Combine(staged, "Heracles.exe")));
        Assert.Equal("its code", File.ReadAllText(Path.Combine(staged, "Heracles.dll")));
        Assert.Equal("words", File.ReadAllText(Path.Combine(staged, "en", "words.txt")));
        Assert.Equal(["downloaded 0.3.1", "0.3.1 is verified and will be installed when the app quits"], log);
        Assert.Equal(1.0, progress.Last());
        Assert.All(progress, share => Assert.InRange(share, 0.0, 1.0));
        // What was downloaded is not kept once it is unpacked.
        Assert.Equal(["staged"], Directory.GetFileSystemEntries(Updates).Select(Path.GetFileName));
    }

    private sealed class Collect(List<double> into) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (into) into.Add(value);
        }
    }

    [Fact]
    public async Task AnArchiveAlteredAfterItWasSignedIsRefusedAndNothingIsUnpacked()
    {
        // Four bytes in its middle, the same length, the same name.
        var altered = (byte[])App.Clone();
        for (var index = 0; index < 4; index++) altered[altered.Length / 2 + index] ^= 0x5a;
        Publish(served: altered);
        var (manifest, archive, updater) = await Found();

        var refused = await Assert.ThrowsAsync<UpdateRefusedException>(() => updater.FetchAsync(manifest, archive));

        Assert.Equal("The update is improperly signed", refused.Message);
        Assert.Equal(["downloaded 0.3.1", "stopped: The update is improperly signed"], log);
        Assert.False(Directory.Exists(Path.Combine(Updates, "staged")));
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(4096)]
    public async Task AnArchiveOfAnotherLengthIsRefused(int difference)
    {
        byte[] served = difference > 0 ? [.. App, .. new byte[difference]] : App[..^1];
        Publish(served: served);
        var (manifest, archive, updater) = await Found();

        var refused = await Assert.ThrowsAsync<UpdateRefusedException>(() => updater.FetchAsync(manifest, archive));

        Assert.Equal("The update is improperly signed", refused.Message);
        Assert.False(Directory.Exists(Path.Combine(Updates, "staged")));
    }

    [Fact]
    public async Task AnArchiveThatIsNotThereStopsTheFetch()
    {
        Publish();
        var (manifest, archive, updater) = await Found();
        pages.Remove("/feed/Heracles.zip");

        var refused = await Assert.ThrowsAsync<UpdateRefusedException>(() => updater.FetchAsync(manifest, archive));

        Assert.Contains("could not be downloaded", refused.Message);
        Assert.StartsWith("stopped: could not download 0.3.1", Assert.Single(log));
        Assert.Empty(Directory.GetFileSystemEntries(Updates));
    }

    [Fact]
    public async Task AnArchiveWhoseFilesWouldLandOutsideTheFolderIsRefused()
    {
        // Signed and all: the unpacking still writes nowhere but its folder.
        Publish(archive: Zip(("Heracles.exe", "the new program"), ("../outside.txt", "out")));
        var (manifest, archive, updater) = await Found();

        await Assert.ThrowsAsync<UpdateRefusedException>(() => updater.FetchAsync(manifest, archive));

        Assert.False(File.Exists(Path.Combine(Updates, "outside.txt")));
        Assert.False(File.Exists(Path.Combine(folder, "outside.txt")));
        Assert.False(Directory.Exists(Path.Combine(Updates, "staged")));
    }

    [Fact]
    public async Task AnArchiveWithoutTheAppInItIsRefused()
    {
        Publish(archive: Zip(("README.txt", "no program here")));
        var (manifest, archive, updater) = await Found();

        var refused = await Assert.ThrowsAsync<UpdateRefusedException>(() => updater.FetchAsync(manifest, archive));

        Assert.Contains("Heracles.exe", refused.Message);
        Assert.False(Directory.Exists(Path.Combine(Updates, "staged")));
    }

    [Fact]
    public async Task WhatAnEarlierFetchLeftBehindIsReplaced()
    {
        Publish();
        var (manifest, archive, updater) = await Found();
        Directory.CreateDirectory(Path.Combine(Updates, "staged"));
        File.WriteAllText(Path.Combine(Updates, "staged", "stale.txt"), "from an update that was never installed");
        File.WriteAllText(Path.Combine(Updates, "download.part"), "half an archive");

        var staged = await updater.FetchAsync(manifest, archive);

        Assert.False(File.Exists(Path.Combine(staged, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(staged, "Heracles.exe")));
        Assert.Equal(["staged"], Directory.GetFileSystemEntries(Updates).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AFetchThatIsCalledOffLeavesNothing()
    {
        Publish();
        var (manifest, archive, updater) = await Found();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updater.FetchAsync(manifest, archive, null, cancel.Token));

        Assert.True(!Directory.Exists(Updates) || Directory.GetFileSystemEntries(Updates).Length == 0);
    }
}

/// <summary>Putting a fetched update in the app's place, with folders of the test's own.</summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"heracles-tests-{Guid.NewGuid()}");
    private readonly List<string> log = [];

    private string Target => Path.Combine(folder, "Programs", "Heracles");

    private string Staged => Path.Combine(folder, "updates", "staged");

    private string Previous => Target + UpdateInstaller.PreviousSuffix;

    private string Incoming => Target + UpdateInstaller.IncomingSuffix;

    public UpdateInstallerTests()
    {
        Directory.CreateDirectory(Path.Combine(Target, "en"));
        File.WriteAllText(Path.Combine(Target, "Heracles.exe"), "the old program");
        File.WriteAllText(Path.Combine(Target, "gone-in-the-new-version.dll"), "old code");
        File.WriteAllText(Path.Combine(Target, "en", "words.txt"), "old words");
        Directory.CreateDirectory(Path.Combine(Staged, "en"));
        File.WriteAllText(Path.Combine(Staged, "Heracles.exe"), "the new program");
        File.WriteAllText(Path.Combine(Staged, "en", "words.txt"), "new words");
    }

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    private InstallRequest Request(bool restart = true) => new(Staged, Target, ProcessId: 4242, restart, "0.3.1");

    private string Updates => Path.Combine(folder, "updates");

    private InstallOutcome Install(bool appHasQuit = true, InstallRequest? request = null) =>
        UpdateInstaller.Install(request ?? Request(), Updates, _ => appHasQuit, log.Add, tries: 3, pause: TimeSpan.FromMilliseconds(10));

    // Who may install, and over what. The request comes on a command line,
    // and a command line can be written by anybody who can start a program:
    // a link handed to Windows starts the app with the link as its argument.

    [Fact]
    public void ACopyThatWasNotFetchedAsAnUpdateInstallsNothing()
    {
        // The app as it is installed somewhere, started with the request:
        // not the copy the updater unpacked, so not one that may replace anything.
        var elsewhere = Path.Combine(folder, "Elsewhere", "Heracles");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "Heracles.exe"), "some copy of the program");

        var outcome = Install(request: Request() with { Source = elsewhere });

        Assert.False(outcome.Installed);
        Assert.Contains("not one that was fetched as an update", outcome.WhyNot);
        Assert.Equal("the old program", File.ReadAllText(Path.Combine(Target, "Heracles.exe")));
        Assert.False(Directory.Exists(Previous));
        Assert.False(Directory.Exists(Incoming));
    }

    [Fact]
    public void TheAppAsItIsInstalledCannotBeMadeToReplaceItself()
    {
        var outcome = Install(request: Request() with { Source = Target });

        Assert.False(outcome.Installed);
        Assert.Equal(3, FilesOf(Target).Length);
    }

    [Fact]
    public void AFolderThatDoesNotHoldTheAppIsNeverReplaced()
    {
        // Somebody's documents, named as the folder to replace.
        var theirs = Path.Combine(folder, "Documents");
        Directory.CreateDirectory(theirs);
        File.WriteAllText(Path.Combine(theirs, "thesis.docx"), "years of work");

        var outcome = Install(request: Request() with { Target = theirs });

        Assert.False(outcome.Installed);
        Assert.Contains("does not hold Heracles", outcome.WhyNot);
        Assert.Equal(["thesis.docx"], FilesOf(theirs));
        Assert.Equal("years of work", File.ReadAllText(Path.Combine(theirs, "thesis.docx")));
        Assert.False(Directory.Exists(theirs + UpdateInstaller.PreviousSuffix));
        Assert.False(Directory.Exists(theirs + UpdateInstaller.IncomingSuffix));
    }

    [Fact]
    public void TheFolderTheUpdateWasUnpackedIntoIsNeverTheOneReplaced()
    {
        var outcome = Install(request: Request() with { Target = Staged });

        Assert.False(outcome.Installed);
        Assert.Equal("the new program", File.ReadAllText(Path.Combine(Staged, "Heracles.exe")));
    }

    private static string[] FilesOf(string root) =>
        [.. Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(root, file)).Order(StringComparer.Ordinal)];

    [Fact]
    public void TheNewAppTakesTheOldOnesPlace()
    {
        var outcome = Install();

        Assert.True(outcome.Installed, outcome.WhyNot);
        Assert.Equal(["Heracles.exe", Path.Combine("en", "words.txt")], FilesOf(Target));
        Assert.Equal("the new program", File.ReadAllText(Path.Combine(Target, "Heracles.exe")));
        Assert.Equal("new words", File.ReadAllText(Path.Combine(Target, "en", "words.txt")));
        Assert.Equal(["installing 0.3.1", "installed 0.3.1"], log);
    }

    [Fact]
    public void TheOldAppIsKeptAsideMarkedAsTheUpdatersOwn()
    {
        Install();

        Assert.Equal("the old program", File.ReadAllText(Path.Combine(Previous, "Heracles.exe")));
        Assert.True(File.Exists(Path.Combine(Previous, UpdateInstaller.Marker)));
        Assert.False(Directory.Exists(Incoming));
    }

    [Fact]
    public void WhatWasFetchedIsLeftWhereItWas()
    {
        // The copy that does the installing runs from there.
        Install();

        Assert.Equal("the new program", File.ReadAllText(Path.Combine(Staged, "Heracles.exe")));
    }

    [Fact]
    public void NothingIsTouchedWhileTheAppIsStillRunning()
    {
        var outcome = Install(appHasQuit: false);

        Assert.False(outcome.Installed);
        Assert.Contains("did not quit", outcome.WhyNot);
        Assert.Equal("the old program", File.ReadAllText(Path.Combine(Target, "Heracles.exe")));
        Assert.False(Directory.Exists(Previous));
        Assert.False(Directory.Exists(Incoming));
        Assert.Equal("stopped: " + outcome.WhyNot, log.Last());
    }

    [Fact]
    public void WhatAnEarlierUpdateKeptAsideIsClearedAwayFirst()
    {
        Directory.CreateDirectory(Previous);
        File.WriteAllText(Path.Combine(Previous, UpdateInstaller.Marker), "");
        File.WriteAllText(Path.Combine(Previous, "Heracles.exe"), "the program before last");

        var outcome = Install();

        Assert.True(outcome.Installed, outcome.WhyNot);
        Assert.Equal("the old program", File.ReadAllText(Path.Combine(Previous, "Heracles.exe")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFolderInTheWayThatIsNotTheUpdatersIsLeftAloneAndNothingIsInstalled(bool previous)
    {
        // Somebody's own folder, which happens to have the name.
        var theirs = previous ? Previous : Incoming;
        Directory.CreateDirectory(theirs);
        File.WriteAllText(Path.Combine(theirs, "thesis.docx"), "years of work");

        var outcome = Install();

        Assert.False(outcome.Installed);
        Assert.Contains(Path.GetFileName(theirs), outcome.WhyNot);
        Assert.Equal("years of work", File.ReadAllText(Path.Combine(theirs, "thesis.docx")));
        Assert.Equal("the old program", File.ReadAllText(Path.Combine(Target, "Heracles.exe")));
        Assert.Equal(["Heracles.exe", Path.Combine("en", "words.txt"), "gone-in-the-new-version.dll"], FilesOf(Target));
    }

    [Fact]
    public void WhenTheOldAppCannotBeMovedAsideItStaysAsItWas()
    {
        // Something still has a file of the app's open.
        using (new FileStream(Path.Combine(Target, "Heracles.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = Install();

            Assert.False(outcome.Installed);
            Assert.Contains("could not be replaced", outcome.WhyNot);
        }
        Assert.Equal("the old program", File.ReadAllText(Path.Combine(Target, "Heracles.exe")));
        Assert.Equal(3, FilesOf(Target).Length);
        Assert.False(Directory.Exists(Previous));
        Assert.False(Directory.Exists(Incoming));
    }

    [Fact]
    public void AnUpdateWithNoAppInItIsNotInstalled()
    {
        File.Delete(Path.Combine(Staged, "Heracles.exe"));

        var outcome = Install();

        Assert.False(outcome.Installed);
        Assert.Equal("the old program", File.ReadAllText(Path.Combine(Target, "Heracles.exe")));
    }

    [Fact]
    public void WhatAnInstallLeftBehindIsClearedAwayAtTheNextStart()
    {
        Install();
        Assert.True(Directory.Exists(Previous));
        File.WriteAllText(Path.Combine(folder, "updates", "download.part"), "half an archive");
        File.WriteAllText(Path.Combine(folder, "updates", "preferences.json"), "{}");
        Assert.True(UpdateInstaller.HasLeftovers(Target, Path.Combine(folder, "updates")));

        UpdateInstaller.RemoveLeftovers(Target, Path.Combine(folder, "updates"));

        Assert.False(UpdateInstaller.HasLeftovers(Target, Path.Combine(folder, "updates")));
        Assert.False(Directory.Exists(Previous));
        Assert.False(Directory.Exists(Staged));
        Assert.False(File.Exists(Path.Combine(folder, "updates", "download.part")));
        // What the app remembers about updates is not a leftover.
        Assert.True(File.Exists(Path.Combine(folder, "updates", "preferences.json")));
        Assert.Equal("the new program", File.ReadAllText(Path.Combine(Target, "Heracles.exe")));
    }

    [Fact]
    public void AnOldAppWhoseFilesCannotBeWrittenIsStillClearedAway()
    {
        // As they are when the folder was copied off a disc.
        foreach (var file in Directory.GetFiles(Target, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        }

        Assert.True(Install().Installed);
        UpdateInstaller.RemoveLeftovers(Target, Path.Combine(folder, "updates"));

        Assert.False(Directory.Exists(Previous));
    }

    [Fact]
    public void ClearingAwayLeavesAFolderThatIsNotTheUpdatersAlone()
    {
        Directory.CreateDirectory(Previous);
        File.WriteAllText(Path.Combine(Previous, "thesis.docx"), "years of work");

        UpdateInstaller.RemoveLeftovers(Target, Path.Combine(folder, "updates"));

        Assert.Equal("years of work", File.ReadAllText(Path.Combine(Previous, "thesis.docx")));
        // Nor is it something still waiting to be cleared away.
        Assert.False(UpdateInstaller.HasLeftovers(Target, Path.Combine(folder, "updates")));
    }

    [Fact]
    public void ClearingAwayWhereThereIsNothingIsNoFault()
    {
        UpdateInstaller.RemoveLeftovers(Path.Combine(folder, "nowhere", "Heracles"), Path.Combine(folder, "no-updates"));
    }

    [Fact]
    public void TheRequestCrossesTheCommandLineAsItWas()
    {
        var request = new InstallRequest(@"C:\Users\jdoe\AppData\Local\Heracles\updates\staged", @"D:\My Apps\Heracles", 4242, Restart: true, "0.3.1");

        Assert.Equal(request, UpdateInstaller.Parse(UpdateInstaller.Arguments(request), request.Source));
        var quiet = request with { Restart = false };
        Assert.Equal(quiet, UpdateInstaller.Parse(UpdateInstaller.Arguments(quiet), quiet.Source));
    }

    [Theory]
    [InlineData]
    [InlineData("--new-window")]
    [InlineData("heracles://note/abc")]
    [InlineData("--install-update")]
    [InlineData("--install-update", "--target", @"C:\Apps\Heracles")]
    [InlineData("--install-update", "--pid", "x", "--target", @"C:\Apps\Heracles", "--version", "0.3.1")]
    [InlineData("--install-update", "--pid", "12", "--target", "Heracles", "--version", "0.3.1")]
    public void AnythingElseOnTheCommandLineIsNoRequest(params string[] arguments)
    {
        Assert.Null(UpdateInstaller.Parse(arguments, @"C:\staged"));
    }

    [Fact]
    public void ACopyInAFolderOfItsOwnCanBeReplaced()
    {
        Assert.Null(UpdateInstaller.WhyNotHere(Target));
        // Asking leaves nothing behind.
        Assert.Equal(["Heracles"], Directory.GetFileSystemEntries(Path.GetDirectoryName(Target)!).Select(Path.GetFileName));
    }

    [Fact]
    public void ACopyAtTheTopOfADriveCannotBeReplaced()
    {
        Assert.NotNull(UpdateInstaller.WhyNotHere(Path.GetPathRoot(Environment.SystemDirectory)!));
    }

    [Fact]
    public void ACopyWhoseFolderIsNotThereCannotBeReplaced()
    {
        Assert.NotNull(UpdateInstaller.WhyNotHere(Path.Combine(folder, "nowhere", "Heracles")));
    }
}
