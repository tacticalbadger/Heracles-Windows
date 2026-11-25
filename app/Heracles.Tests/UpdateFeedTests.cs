using System.Security.Cryptography;
using System.Text;
using Heracles.Updates;

namespace Heracles.Tests;

/// <summary>
/// An update key made for a test, and what the release scripts do with the
/// real one: sign a feed. The app itself never signs anything.
/// </summary>
internal sealed class TestUpdateKey : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The public half as the app is given it: the point's two coordinates, base64.</summary>
    public string Public
    {
        get
        {
            var point = key.ExportParameters(false).Q;
            return Convert.ToBase64String([.. point.X!, .. point.Y!]);
        }
    }

    public string Sign(byte[] data) => Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256));

    /// <summary>A feed as the release scripts write it: what it says, and the signature of exactly those bytes.</summary>
    public byte[] Feed(string manifest)
    {
        var bytes = Encoding.UTF8.GetBytes(manifest);
        return Encoding.UTF8.GetBytes(
            $$"""{"format":1,"manifest":"{{Convert.ToBase64String(bytes)}}","signature":"{{Sign(bytes)}}"}""");
    }

    public void Dispose() => key.Dispose();
}

public sealed class UpdateSignatureTests
{
    [Fact]
    public void ASignatureMadeByTheReleaseScriptsHolds()
    {
        // Made by Windows PowerShell, which is what signs a release
        // (tools\update-signing.ps1), not by the code under test.
        const string publicKey = "oksJc8gZZPXyOts86dZQftgbIPQU7FpOqlb2GP9W+sbYjfFMTR6UQtqVxK2x3wH1Hmy6nPpFZBYQ+qE0GySRJA==";
        const string signature = "aJWtrby9ixTvYxijtm2mzmKSHmQrk/GQKVWxGk0U2MEWR2SpDr9uObBltlvrbqMdYDC2zelxoQyfsnBQTEjxRA==";
        var message = Encoding.UTF8.GetBytes("""{"version":"9.9.2","build":99902}""");

        Assert.True(UpdateSignature.IsValid(publicKey, signature, message));
        message[3] ^= 1;
        Assert.False(UpdateSignature.IsValid(publicKey, signature, message));
    }

    [Fact]
    public void OnlyTheKeysOwnSignatureOfExactlyTheseBytesHolds()
    {
        using var key = new TestUpdateKey();
        using var other = new TestUpdateKey();
        var message = Encoding.UTF8.GetBytes("what is signed");

        Assert.True(UpdateSignature.IsValid(key.Public, key.Sign(message), message));
        Assert.False(UpdateSignature.IsValid(key.Public, other.Sign(message), message));
        Assert.False(UpdateSignature.IsValid(other.Public, key.Sign(message), message));
        Assert.False(UpdateSignature.IsValid(key.Public, key.Sign(message), Encoding.UTF8.GetBytes("what is signed.")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !")]
    [InlineData("AAAA")]
    public void AKeyOrASignatureThatCannotBeReadHoldsNothing(string broken)
    {
        using var key = new TestUpdateKey();
        var message = Encoding.UTF8.GetBytes("what is signed");

        Assert.False(UpdateSignature.IsValid(broken, key.Sign(message), message));
        Assert.False(UpdateSignature.IsValid(key.Public, broken, message));
    }

    [Fact]
    public void AKeyThatIsNoPointOnTheCurveHoldsNothing()
    {
        using var key = new TestUpdateKey();
        var message = Encoding.UTF8.GetBytes("what is signed");
        var nonsense = Convert.ToBase64String(Enumerable.Repeat((byte)7, 64).ToArray());

        Assert.False(UpdateSignature.IsValid(nonsense, key.Sign(message), message));
    }
}

public sealed class UpdateFeedTests : IDisposable
{
    private readonly TestUpdateKey key = new();

    public void Dispose() => key.Dispose();

    private const string Sum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Manifest(
        string version = "0.3.1", string build = "53", string notes = "- A fix.",
        string url = "https://example.com/Heracles-0.3.1-win-arm64.zip", string size = "1000", string sum = Sum) =>
        $$$"""
        {"version":"{{{version}}}","build":{{{build}}},"notes":"{{{notes}}}",
         "archives":{"arm64":{"url":"{{{url}}}","size":{{{size}}},"sha256":"{{{sum}}}"},
                     "x64":{"url":"https://example.com/Heracles-0.3.1-win-x64.zip","size":2000,"sha256":"{{{Sum}}}"} } }
        """;

    [Fact]
    public void ASignedFeedSaysWhatTheNewestVersionIs()
    {
        var manifest = UpdateFeed.Read(key.Feed(Manifest()), key.Public);

        Assert.Equal("0.3.1", manifest.Version);
        Assert.Equal(53, manifest.Build);
        Assert.Equal("- A fix.", manifest.Notes);
        Assert.Equal(["arm64", "x64"], manifest.Archives.Keys.Order());
        Assert.Equal(new UpdateArchive("https://example.com/Heracles-0.3.1-win-arm64.zip", 1000, Sum), manifest.Archives["arm64"]);
        Assert.Equal(2000, manifest.Archives["x64"].Size);
    }

    [Fact]
    public void AFeedAlteredAfterItWasSignedIsRefused()
    {
        // What it says is changed and the signature left: the release notes
        // now say something else.
        var said = Encoding.UTF8.GetBytes(Manifest());
        var signature = key.Sign(said);
        var altered = Encoding.UTF8.GetBytes(Manifest(notes: "- From somewhere else."));
        var feed = Encoding.UTF8.GetBytes(
            $$"""{"format":1,"manifest":"{{Convert.ToBase64String(altered)}}","signature":"{{signature}}"}""");

        var refused = Assert.Throws<UpdateRefusedException>(() => UpdateFeed.Read(feed, key.Public));
        Assert.Equal(UpdateFeed.ImproperlySigned, refused.Message);
    }

    [Fact]
    public void AFeedSignedWithAnotherKeyIsRefused()
    {
        using var other = new TestUpdateKey();

        var refused = Assert.Throws<UpdateRefusedException>(() => UpdateFeed.Read(other.Feed(Manifest()), key.Public));
        Assert.Equal(UpdateFeed.ImproperlySigned, refused.Message);
    }

    [Fact]
    public void AFeedWithNoSignatureIsRefused()
    {
        var said = Convert.ToBase64String(Encoding.UTF8.GetBytes(Manifest()));
        foreach (var feed in new[]
        {
            $$"""{"format":1,"manifest":"{{said}}"}""",
            $$"""{"format":1,"manifest":"{{said}}","signature":""}""",
            $$"""{"format":1,"manifest":"{{said}}","signature":7}""",
        })
        {
            var refused = Assert.Throws<UpdateRefusedException>(() => UpdateFeed.Read(Encoding.UTF8.GetBytes(feed), key.Public));
            Assert.Equal(UpdateFeed.ImproperlySigned, refused.Message);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Not found</html>")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"format":1,"signature":"AAAA"}""")]
    [InlineData("""{"format":1,"manifest":"not base64 !","signature":"AAAA"}""")]
    public void WhatIsNotAFeedIsRefused(string text)
    {
        Assert.Throws<UpdateRefusedException>(() => UpdateFeed.Read(Encoding.UTF8.GetBytes(text), key.Public));
    }

    [Fact]
    public void AFeedOfAFormThisVersionDoesNotKnowIsRefused()
    {
        var said = Encoding.UTF8.GetBytes(Manifest());
        var feed = $$"""{"format":2,"manifest":"{{Convert.ToBase64String(said)}}","signature":"{{key.Sign(said)}}"}""";

        var refused = Assert.Throws<UpdateRefusedException>(() => UpdateFeed.Read(Encoding.UTF8.GetBytes(feed), key.Public));
        Assert.NotEqual(UpdateFeed.ImproperlySigned, refused.Message);
    }

    [Fact]
    public void AFeedLongerThanAFeedCanBeIsRefusedUnread()
    {
        var feed = new byte[UpdateFeed.MaximumLength + 1];
        Array.Fill(feed, (byte)' ');

        Assert.Throws<UpdateRefusedException>(() => UpdateFeed.Read(feed, key.Public));
    }

    public static TheoryData<string> ManifestsThatSayTooLittle() => new()
    {
        "[]",
        "{}",
        // No version, or one that is not a version.
        Manifest(version: ""),
        Manifest(version: "0.3.1\\nnewer"),
        // No build number that can be compared.
        Manifest(build: "0"),
        Manifest(build: "-4"),
        Manifest(build: "\"53\""),
        Manifest(build: "53.5"),
        // An archive with no size, no checksum, or an address that may not be fetched.
        Manifest(size: "0"),
        Manifest(size: "-1"),
        Manifest(sum: "abc"),
        Manifest(sum: "g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
        Manifest(url: ""),
        Manifest(url: "http://example.com/Heracles.zip"),
        Manifest(url: "file:///C:/Heracles.zip"),
        Manifest(url: "Heracles.zip"),
        """{"version":"0.3.1","build":53,"notes":"","archives":[]}""",
        """{"version":"0.3.1","build":53,"notes":""}""",
    };

    [Theory]
    [MemberData(nameof(ManifestsThatSayTooLittle))]
    public void ASignedFeedThatDoesNotSayEnoughIsRefused(string manifest)
    {
        // Signed by the right key, and still not something to act on.
        var refused = Assert.Throws<UpdateRefusedException>(() => UpdateFeed.Read(key.Feed(manifest), key.Public));
        Assert.NotEqual(UpdateFeed.ImproperlySigned, refused.Message);
    }

    [Fact]
    public void WhatAFeedSaysBeyondWhatIsKnownIsLeftAlone()
    {
        // A later release may say more. This version reads what it knows.
        var manifest = """
            {"version":"0.4.0","build":60,"notes":"","minimumWindows":"10.0.19041","archives":
             {"arm64":{"url":"https://example.com/a.zip","size":5,"sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","extra":true},
              "riscv":{"url":"https://example.com/r.zip","size":5,"sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}}}
            """;

        var read = UpdateFeed.Read(key.Feed(manifest), key.Public);
        Assert.Equal(60, read.Build);
        Assert.Equal(["arm64", "riscv"], read.Archives.Keys.Order());
    }

    [Fact]
    public void NotesAreOptional()
    {
        var manifest = """
            {"version":"0.4.0","build":60,"archives":
             {"x64":{"url":"https://example.com/a.zip","size":5,"sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}}}
            """;

        Assert.Equal("", UpdateFeed.Read(key.Feed(manifest), key.Public).Notes);
    }

    [Theory]
    [InlineData("https://github.com/x/y/releases/latest/download/update-feed.json", true)]
    [InlineData("HTTPS://EXAMPLE.COM/a.zip", true)]
    // A rehearsal's feed, on this machine only.
    [InlineData("http://127.0.0.1:8498/feed/update-feed.json", true)]
    [InlineData("http://localhost:8498/a.zip", true)]
    [InlineData("http://example.com/a.zip", false)]
    [InlineData("http://127.0.0.1.example.com/a.zip", false)]
    [InlineData("ftp://example.com/a.zip", false)]
    [InlineData("file:///C:/a.zip", false)]
    [InlineData("a.zip", false)]
    [InlineData("", false)]
    public void UpdatesComeOverHttpsOrFromThisMachine(string address, bool allowed)
    {
        Assert.Equal(allowed, UpdateAddress.IsAllowed(address));
    }

    [Fact]
    public void AProcessorHasAName()
    {
        Assert.Equal("arm64", UpdateFeed.ArchitectureName(System.Runtime.InteropServices.Architecture.Arm64));
        Assert.Equal("x64", UpdateFeed.ArchitectureName(System.Runtime.InteropServices.Architecture.X64));
    }
}

public sealed class UpdatePreferencesTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"heracles-tests-{Guid.NewGuid()}");

    private string File_ => Path.Combine(folder, "updates", "preferences.json");

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    private static readonly DateTimeOffset Noon = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACopyThatHasNeverLookedLooksEveryDayAndIsDueNow()
    {
        var preferences = UpdatePreferences.Load(File_);

        Assert.True(preferences.Automatic);
        Assert.Null(preferences.LastCheck);
        Assert.Null(preferences.SkippedBuild);
        Assert.True(preferences.IsCheckDue(Noon));
    }

    [Fact]
    public void WhatIsChosenIsKept()
    {
        new UpdatePreferences(Automatic: false, LastCheck: Noon, SkippedBuild: 53).Save(File_);

        Assert.Equal(new UpdatePreferences(false, Noon, 53), UpdatePreferences.Load(File_));
    }

    [Fact]
    public void AFileThatCannotBeReadIsAsIfThereWereNone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, "{ not json");

        Assert.Equal(new UpdatePreferences(), UpdatePreferences.Load(File_));
    }

    [Fact]
    public void TheNextLookIsADayAfterTheLast()
    {
        var preferences = new UpdatePreferences(LastCheck: Noon);

        Assert.False(preferences.IsCheckDue(Noon));
        Assert.False(preferences.IsCheckDue(Noon.AddHours(23.9)));
        Assert.True(preferences.IsCheckDue(Noon.AddHours(24)));
        Assert.True(preferences.IsCheckDue(Noon.AddDays(30)));
    }

    [Fact]
    public void WithTheDailyLookOffNothingIsEverDue()
    {
        Assert.False(new UpdatePreferences(Automatic: false).IsCheckDue(Noon));
        Assert.False(new UpdatePreferences(Automatic: false, LastCheck: Noon).IsCheckDue(Noon.AddDays(30)));
    }

    [Fact]
    public void ALastLookInTheFutureDoesNotStopTheLooking()
    {
        // The clock was put back since: a copy must not wait for a day that
        // has already been.
        Assert.True(new UpdatePreferences(LastCheck: Noon.AddDays(3)).IsCheckDue(Noon));
    }

    [Fact]
    public void HowLongUntilTheNextLook()
    {
        var preferences = new UpdatePreferences(LastCheck: Noon);

        Assert.Equal(TimeSpan.FromHours(18), preferences.UntilNextCheck(Noon.AddHours(6)));
        Assert.Equal(TimeSpan.Zero, preferences.UntilNextCheck(Noon.AddDays(2)));
        Assert.Equal(TimeSpan.Zero, new UpdatePreferences().UntilNextCheck(Noon));
    }
}
