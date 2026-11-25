using Heracles.Updates;

namespace Heracles.Tests;

/// <summary>
/// Whether a run looks for updates, and where. A run that should not and
/// does would offer the published app to a trial copy or a test. The Mac's
/// <c>UpdatePolicyTests</c>, with what Windows adds.
/// </summary>
public sealed class UpdatePolicyTests
{
    /// <summary>What a packaged build carries beside its program.</summary>
    private static readonly UpdateSettings Ready = new("https://example.com/update-feed.json", "key");

    private static readonly (string, string)[] Trial = [("HERACLES_DATA_DIR", @"C:\x")];

    private static UpdatePolicy Decide(string[] arguments, (string Name, string Value)[]? environment = null, UpdateSettings? settings = null, bool bare = false) =>
        UpdatePolicy.Decide(
            arguments,
            name => (environment ?? []).Where(variable => variable.Name == name).Select(variable => variable.Value).FirstOrDefault(),
            bare ? null : settings ?? Ready);

    [Fact]
    public void AnEverydayRunLooksAtTheBuiltInFeed()
    {
        Assert.Equal(UpdatePolicy.On(null), Decide([]));
        Assert.True(Decide([]).IsOn);
    }

    [Fact]
    public void ABuildWithoutFeedOrKeyDoesNotLook()
    {
        // A build made for trying things out has no such file beside it.
        Assert.False(Decide([], bare: true).IsOn);
    }

    [Fact]
    public void ATrialLibraryDoesNotLook()
    {
        Assert.False(Decide([], Trial).IsOn);
    }

    [Fact]
    public void AnEmptyDataDirIsAnEverydayRun()
    {
        Assert.Equal(UpdatePolicy.On(null), Decide([], [("HERACLES_DATA_DIR", "")]));
    }

    [Fact]
    public void ARehearsalNamesItsOwnFeed()
    {
        Assert.Equal(
            UpdatePolicy.On("http://127.0.0.1:1/a.json"),
            Decide(["--updater-feed", "http://127.0.0.1:1/a.json"], Trial));
    }

    [Fact]
    public void AnEverydayRunTakesNoFeedFromTheCommandLine()
    {
        Assert.Equal(UpdatePolicy.On(null), Decide(["--updater-feed", "http://evil.example/a.json"]));
    }

    [Fact]
    public void AFeedFlagWithoutAValueIsIgnored()
    {
        Assert.False(Decide(["--updater-feed"], Trial).IsOn);
    }

    [Fact]
    public void ARehearsalStillNeedsAKey()
    {
        Assert.False(Decide(["--updater-feed", "http://127.0.0.1:1/a.json"], Trial, bare: true).IsOn);
    }

    [Fact]
    public void ARunThatDoesNotLookSaysWhy()
    {
        // The reason goes in the log: "why is this copy not updating" has an answer.
        Assert.Contains("HERACLES_DATA_DIR", Decide([], Trial).WhyOff);
        Assert.Contains(UpdateSettings.FileName, Decide([], bare: true).WhyOff);
        Assert.Equal("", Decide([]).WhyOff);
    }
}

/// <summary>The file beside the program that says where updates come from.</summary>
public sealed class UpdateSettingsTests
{
    [Fact]
    public void TheFeedAndTheKeyAreRead()
    {
        var settings = UpdateSettings.Parse("""{"feed":"https://example.com/f.json","key":"abc="}""");
        Assert.Equal(new UpdateSettings("https://example.com/f.json", "abc="), settings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"feed":"https://example.com/f.json"}""")]
    [InlineData("""{"key":"abc="}""")]
    [InlineData("""{"feed":"","key":"abc="}""")]
    [InlineData("""{"feed":"https://example.com/f.json","key":"  "}""")]
    [InlineData("""{"feed":7,"key":"abc="}""")]
    public void AFileThatDoesNotNameBothIsNoSettings(string text)
    {
        Assert.Null(UpdateSettings.Parse(text));
    }

    [Fact]
    public void AFolderWithoutTheFileHasNoSettings()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"heracles-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Null(UpdateSettings.Load(folder));
            File.WriteAllText(Path.Combine(folder, "Heracles.update.json"), """{"feed":"https://example.com/f.json","key":"abc="}""");
            Assert.Equal("abc=", UpdateSettings.Load(folder)?.Key);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}

/// <summary>The version a build says it is, which is what an update is measured against.</summary>
public sealed class AppVersionTests
{
    [Fact]
    public void TheBuildNumberIsTheLastPartOfTheFileVersion()
    {
        Assert.Equal(new AppVersion("0.3.0", 52), AppVersion.Parse("0.3.0.52"));
        Assert.Equal(new AppVersion("9.9.1", 99901), AppVersion.Parse("9.9.1.99901"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.3.0")]
    [InlineData("0.3.0.x")]
    public void AVersionThatCannotBeReadIsBuildNought(string? text)
    {
        // Build 0 is older than anything published, and is what a build made by hand says.
        Assert.Equal(0, AppVersion.Parse(text).Build);
    }

    [Fact]
    public void ThisBuildKnowsItsOwn()
    {
        Assert.False(string.IsNullOrEmpty(AppVersion.Current.Version));
        Assert.True(AppVersion.Current.Build >= 0);
    }
}
