using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace Heracles.Updates;

internal enum UpdateCheckKind
{
    /// <summary>The feed names a build newer than this one, with an archive for this processor.</summary>
    Found,
    NothingNewer,
    /// <summary>The look could not be finished, or what it found was refused. The reason says which.</summary>
    Stopped,
}

/// <summary>What a look at the feed came to.</summary>
internal sealed record UpdateCheckResult(UpdateCheckKind Kind, UpdateManifest? Manifest = null, UpdateArchive? Archive = null, string? Reason = null);

/// <summary>
/// Looks for a newer version and fetches it: the part of updating that
/// talks to the web. It reads the feed, believes it only if the update key
/// signed it, downloads the archive the feed names for this processor,
/// takes it only if it is byte for byte the one the feed describes, and
/// unpacks it into a folder of the app's own. Putting it in the app's
/// place is <see cref="UpdateInstaller"/>'s business, once the app has quit.
///
/// A look sends the app's version and processor (in the request's
/// <c>User-Agent</c>), and nothing about the library.
///
/// Each thing that happens is said once through <c>log</c>, in the Mac
/// app's words where it has the same thing to say: the update rehearsal
/// reads them, and so can anyone looking at the log.
/// </summary>
internal sealed class Updater : IDisposable
{
    public const string ArchiveImproperlySigned = "The update is improperly signed";

    private const string PartName = "download.part";
    private const string StagedName = UpdateInstaller.StagedName;

    private static readonly TimeSpan FeedPatience = TimeSpan.FromSeconds(30);
    // How long a download may bring nothing before it is given up.
    private static readonly TimeSpan DownloadPatience = TimeSpan.FromSeconds(60);

    private readonly UpdateSettings settings;
    private readonly string? feedForThisRun;
    private readonly string folder;
    private readonly AppVersion own;
    private readonly string architecture;
    private readonly Action<string> log;
    private readonly HttpClient web;

    /// <param name="feedForThisRun">A feed named for this run (a rehearsal's), or null for the one in the settings.</param>
    /// <param name="folder">The app's own folder for updates: what is downloaded and unpacked goes here.</param>
    /// <param name="own">The version this copy is.</param>
    /// <param name="architecture">The processor this copy is for, as a feed names it.</param>
    public Updater(UpdateSettings settings, string? feedForThisRun, string folder, AppVersion own, string architecture, Action<string> log)
    {
        this.settings = settings;
        this.feedForThisRun = feedForThisRun;
        this.folder = folder;
        this.own = own;
        this.architecture = architecture;
        this.log = log;
        web = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            ConnectTimeout = TimeSpan.FromSeconds(20),
        })
        {
            // Each request has its own limit: a feed is small, an archive is not.
            Timeout = Timeout.InfiniteTimeSpan,
        };
        web.DefaultRequestHeaders.UserAgent.ParseAdd($"Heracles/{Token(own.Version)} (Windows; {Token(architecture)})");
    }

    /// <summary>The feed this copy reads.</summary>
    public string FeedAddress => feedForThisRun ?? settings.Feed;

    /// <summary>Where a fetched update is unpacked.</summary>
    public string StagedFolder => Path.Combine(folder, StagedName);

    public void Dispose() => web.Dispose();

    private static string Token(string text) => new([.. text.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')]);

    // Looking

    /// <summary>Read the feed, and say whether it names something newer than this copy.</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancel = default)
    {
        UpdateCheckResult Stopped(string reason)
        {
            log("stopped: " + reason);
            return new UpdateCheckResult(UpdateCheckKind.Stopped, Reason: reason);
        }

        var address = FeedAddress;
        if (!UpdateAddress.IsAllowed(address)) return Stopped("The update feed's address is not one that updates are taken from");

        byte[] feed;
        try { feed = await FetchFeed(address, cancel).ConfigureAwait(false); }
        catch (UpdateRefusedException refused) { return Stopped(refused.Message); }

        UpdateManifest manifest;
        try { manifest = UpdateFeed.Read(feed, settings.Key); }
        catch (UpdateRefusedException refused) { return Stopped(refused.Message); }

        // The build number decides, as on the Mac: an older feed served
        // again offers nothing to a copy that is past it.
        if (manifest.Build <= own.Build)
        {
            log("nothing newer in the feed");
            return new UpdateCheckResult(UpdateCheckKind.NothingNewer, manifest);
        }
        if (!manifest.Archives.TryGetValue(architecture, out var archive))
        {
            return Stopped($"The update feed has nothing for this PC's processor ({architecture})");
        }
        log($"found {manifest.Version} ({manifest.Build})");
        return new UpdateCheckResult(UpdateCheckKind.Found, manifest, archive);
    }

    private async Task<byte[]> FetchFeed(string address, CancellationToken cancel)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(FeedPatience);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            // The newest feed, not one a cache on the way has kept.
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await web.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateRefusedException($"The update feed could not be fetched: the server answered {(int)response.StatusCode}");
            }
            await using var body = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            using var kept = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(buffer, limit.Token).ConfigureAwait(false)) > 0)
            {
                // Read no further than a feed can be long.
                if (kept.Length + read > UpdateFeed.MaximumLength) throw new UpdateRefusedException(UpdateFeed.Unreadable);
                kept.Write(buffer, 0, read);
            }
            return kept.ToArray();
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new UpdateRefusedException("The update feed could not be fetched: the server did not answer in time");
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            throw new UpdateRefusedException("The update feed could not be fetched: " + error.Message);
        }
    }

    // Fetching

    /// <summary>
    /// Download the archive a feed named, take it only if it is the one the
    /// feed described, and unpack it.
    /// </summary>
    /// <returns>The folder that now holds the new app.</returns>
    /// <exception cref="UpdateRefusedException">
    /// It could not be downloaded, it is not the archive the feed described,
    /// or it does not hold the app. Nothing of it is left behind.
    /// </exception>
    public async Task<string> FetchAsync(UpdateManifest manifest, UpdateArchive archive, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        var part = Path.Combine(folder, PartName);
        var staged = StagedFolder;
        try
        {
            cancel.ThrowIfCancellationRequested();
            Directory.CreateDirectory(folder);
            // Whatever an update that was never installed left here.
            UpdateInstaller.DeleteFolder(staged);
            File.Delete(part);

            string sum;
            long length;
            try { (length, sum) = await Download(archive, part, progress, cancel).ConfigureAwait(false); }
            catch (UpdateRefusedException refused)
            {
                log($"stopped: could not download {manifest.Version}: {refused.Message}");
                throw new UpdateRefusedException("The update could not be downloaded: " + refused.Message);
            }
            log($"downloaded {manifest.Version}");

            // The feed is signed and names the archive's length and SHA-256,
            // so an archive that matches both is one the developer signed
            // for. Anything else is not, whoever served it.
            if (length != archive.Size || !string.Equals(sum, archive.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Refused(ArchiveImproperlySigned);
            }

            try { Unpack(part, staged, cancel); }
            catch (InvalidDataException) { throw Refused("The update's archive cannot be opened"); }
            catch (UpdateRefusedException refused) { throw Refused(refused.Message); }
            if (!File.Exists(Path.Combine(staged, UpdateInstaller.Program)))
            {
                throw Refused($"The update's archive does not hold {UpdateInstaller.Program}");
            }
            File.Delete(part);
            progress?.Report(1.0);
            log($"{manifest.Version} is verified and will be installed when the app quits");
            return staged;
        }
        catch
        {
            // Refused, failed or called off: nothing half-fetched stays.
            try
            {
                UpdateInstaller.DeleteFolder(staged);
                File.Delete(part);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }

        UpdateRefusedException Refused(string why)
        {
            log("stopped: " + why);
            return new UpdateRefusedException(why);
        }
    }

    /// <summary>The archive, into a file. Its length and SHA-256 are worked out as it arrives.</summary>
    private async Task<(long Length, string Sha256)> Download(UpdateArchive archive, string part, IProgress<double>? progress, CancellationToken cancel)
    {
        if (!UpdateAddress.IsAllowed(archive.Url)) throw new UpdateRefusedException("its address is not one that updates are taken from");
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        patience.CancelAfter(DownloadPatience);
        try
        {
            using var response = await web.GetAsync(archive.Url, HttpCompletionOption.ResponseHeadersRead, patience.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new UpdateRefusedException($"the server answered {(int)response.StatusCode}");
            await using var body = await response.Content.ReadAsStreamAsync(patience.Token).ConfigureAwait(false);
            await using var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long length = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, patience.Token).ConfigureAwait(false)) > 0)
            {
                patience.CancelAfter(DownloadPatience);
                length += read;
                // More than the feed said there would be: it is not the
                // archive, and there is no call to fill the disk finding
                // out how much more.
                if (length > archive.Size) break;
                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), patience.Token).ConfigureAwait(false);
                progress?.Report(Math.Min(1.0, (double)length / archive.Size));
            }
            return (length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new UpdateRefusedException("the server stopped answering");
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            throw new UpdateRefusedException(error.Message);
        }
    }

    /// <summary>Unpack an archive into a folder, and nowhere else.</summary>
    private static void Unpack(string archive, string into, CancellationToken cancel)
    {
        var root = Path.GetFullPath(into);
        var inside = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            cancel.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            // A name with ".." in it, or a path of its own, would land
            // somewhere else. The archive is signed for; this is so that a
            // mistake in making one cannot write over anything.
            if (!target.StartsWith(inside, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateRefusedException("The update's archive holds a file that would land outside its folder");
            }
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }
}
