using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Heracles.Updates;

/// <summary>What a feed says of the newest version, once its signature has held.</summary>
/// <param name="Version">The version people see, "0.3.1".</param>
/// <param name="Build">The build number. A build is newer when this is higher, whatever the version says.</param>
/// <param name="Notes">What changed, in Markdown.</param>
/// <param name="Archives">The app for each processor, by name ("arm64", "x64").</param>
internal sealed record UpdateManifest(string Version, int Build, string Notes, IReadOnlyDictionary<string, UpdateArchive> Archives);

/// <summary>One processor's app, zipped: where it is, and what it must weigh and hash to.</summary>
internal sealed record UpdateArchive(string Url, long Size, string Sha256);

/// <summary>
/// A feed or an archive that is not the developer's, or that cannot be read
/// or acted on. The message says which, in words fit to show and to log.
/// </summary>
internal sealed class UpdateRefusedException(string message) : Exception(message);

/// <summary>
/// The check every update rests on: is this the update key's own signature
/// of exactly these bytes. The key is an ECDSA key on the curve P-256, the
/// signature is over the SHA-256 of the bytes, and both are what Windows
/// itself computes: nothing here is a library of the app's.
/// </summary>
internal static class UpdateSignature
{
    /// <param name="publicKey">The key's public half: the point's two coordinates, 32 bytes each, in base64.</param>
    /// <param name="signature">The signature's two numbers, 32 bytes each, in base64.</param>
    /// <returns>False as well for a key or a signature that cannot be read.</returns>
    public static bool IsValid(string publicKey, string signature, ReadOnlySpan<byte> data)
    {
        if (Bytes(publicKey) is not { Length: 64 } point || Bytes(signature) is not { Length: 64 } signed) return false;
        try
        {
            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[..32], Y = point[32..] },
            });
            return key.VerifyData(data, signed, HashAlgorithmName.SHA256);
        }
        // A key that is no point on the curve. Windows says "the parameter is
        // incorrect", and .NET passes that on as a curve the platform lacks.
        catch (Exception error) when (error is CryptographicException or PlatformNotSupportedException) { return false; }
    }

    private static byte[]? Bytes(string base64)
    {
        try { return Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
    }
}

/// <summary>Where an update may be fetched from.</summary>
internal static class UpdateAddress
{
    /// <summary>
    /// Over HTTPS, from anywhere; over plain HTTP only from this machine,
    /// which is where a rehearsal serves its feed.
    /// </summary>
    public static bool IsAllowed(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }
}

/// <summary>
/// The feed: one small file on the newest GitHub release, which says what
/// that release is and carries the developer's signature of what it says.
///
/// <code>{"format":1,"manifest":"&lt;base64&gt;","signature":"&lt;base64&gt;"}</code>
///
/// The manifest is JSON, kept as the exact bytes that were signed, so that
/// nothing has to be put back in order before the signature is checked.
/// Nothing in it is read until the signature has held: whoever controls
/// the place the feed is served from can withhold an update, and cannot
/// supply one. The archives are named in the manifest with their size and
/// SHA-256, so the signature covers them too.
/// </summary>
internal static partial class UpdateFeed
{
    /// <summary>The form of feed this version reads.</summary>
    public const int Format = 1;

    /// <summary>More than a feed can ever be. One is a few thousand bytes.</summary>
    public const int MaximumLength = 1 << 20;

    /// <summary>The Mac app's words (Sparkle's) for the same refusal.</summary>
    public const string ImproperlySigned = "The update feed is improperly signed";

    public const string Unreadable = "The update feed cannot be read";

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+-]{0,31}$")]
    private static partial Regex VersionForm();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex ChecksumForm();

    /// <summary>The name a processor's archive goes by in a feed, and in a package's file name.</summary>
    public static string ArchitectureName(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X64 => "x64",
        _ => architecture.ToString().ToLowerInvariant(),
    };

    /// <summary>What a feed says, if the key signed it.</summary>
    /// <exception cref="UpdateRefusedException">It is not a feed, the key did not sign it, or it does not say enough.</exception>
    public static UpdateManifest Read(byte[] feed, string publicKey)
    {
        if (feed.Length > MaximumLength) throw new UpdateRefusedException(Unreadable);
        byte[] said;
        string? signature;
        try
        {
            using var envelope = JsonDocument.Parse(feed);
            var root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("manifest", out var manifest) || manifest.ValueKind != JsonValueKind.String
                || !manifest.TryGetBytesFromBase64(out said!))
            {
                throw new UpdateRefusedException(Unreadable);
            }
            signature = root.TryGetProperty("signature", out var signed) && signed.ValueKind == JsonValueKind.String ? signed.GetString() : null;
            // Judged before the form: what is not signed is not believed
            // about anything, its own form included.
            if (string.IsNullOrEmpty(signature) || !UpdateSignature.IsValid(publicKey, signature, said))
            {
                throw new UpdateRefusedException(ImproperlySigned);
            }
            if (!root.TryGetProperty("format", out var format) || !format.TryGetInt32(out var number) || number != Format)
            {
                throw new UpdateRefusedException("The update feed is of a form this version of Heracles does not know");
            }
        }
        catch (JsonException) { throw new UpdateRefusedException(Unreadable); }

        try { return Manifest(said); }
        catch (JsonException) { throw new UpdateRefusedException("The update feed does not say what the update is"); }
    }

    private static UpdateManifest Manifest(byte[] said)
    {
        using var document = JsonDocument.Parse(said);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();

        var version = Text(root, "version");
        if (version is null || !VersionForm().IsMatch(version)) throw new UpdateRefusedException("The update feed names no version");
        if (!root.TryGetProperty("build", out var buildValue) || buildValue.ValueKind != JsonValueKind.Number
            || !buildValue.TryGetInt32(out var build) || build <= 0)
        {
            throw new UpdateRefusedException("The update feed names no build number");
        }
        if (!root.TryGetProperty("archives", out var archivesValue) || archivesValue.ValueKind != JsonValueKind.Object)
        {
            throw new UpdateRefusedException("The update feed names no archives");
        }

        var archives = new Dictionary<string, UpdateArchive>(StringComparer.Ordinal);
        foreach (var entry in archivesValue.EnumerateObject())
        {
            var archive = entry.Value;
            if (archive.ValueKind != JsonValueKind.Object) throw new JsonException();
            var url = Text(archive, "url");
            var sum = Text(archive, "sha256");
            if (url is null || !UpdateAddress.IsAllowed(url)
                || sum is null || !ChecksumForm().IsMatch(sum)
                || !archive.TryGetProperty("size", out var sizeValue) || sizeValue.ValueKind != JsonValueKind.Number
                || !sizeValue.TryGetInt64(out var size) || size <= 0)
            {
                throw new UpdateRefusedException($"The update feed does not say enough about the archive for {entry.Name}");
            }
            archives[entry.Name] = new UpdateArchive(url, size, sum.ToLowerInvariant());
        }
        return new UpdateManifest(version, build, Text(root, "notes") ?? "", archives);
    }

    private static string? Text(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
