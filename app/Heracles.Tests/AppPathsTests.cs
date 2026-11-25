namespace Heracles.Tests;

public sealed class AppPathsTests
{
    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.Where(variable => variable.Name == name).Select(variable => variable.Value).FirstOrDefault();

    [Fact]
    public void WithoutOverridesTheLibraryIsUnderAppData()
    {
        var locations = AppPaths.Resolve(Environment(), @"C:\A", @"C:\L");

        Assert.Equal(@"C:\A\Heracles", locations.DataDirectory);
        Assert.Equal(@"C:\L\Heracles\heracles.log", locations.LogPath);
    }

    [Fact]
    public void TheOverridesWin()
    {
        var locations = AppPaths.Resolve(
            Environment(("HERACLES_DATA_DIR", @"D:\x"), ("HERACLES_LOG_PATH", @"D:\y\log.txt")), @"C:\A", @"C:\L");

        Assert.Equal(@"D:\x", locations.DataDirectory);
        Assert.Equal(@"D:\y\log.txt", locations.LogPath);
    }

    [Fact]
    public void ALibraryPutSomewhereElseIsATestLibrary()
    {
        // Such a run must not take the user's links or put reminders on the screen.
        Assert.False(AppPaths.Resolve(Environment(), @"C:\A", @"C:\L").IsTestLibrary);
        Assert.True(AppPaths.Resolve(Environment(("HERACLES_DATA_DIR", @"D:\x")), @"C:\A", @"C:\L").IsTestLibrary);
        Assert.False(AppPaths.Resolve(Environment(("HERACLES_DATA_DIR", "")), @"C:\A", @"C:\L").IsTestLibrary);
        Assert.False(AppPaths.Resolve(Environment(("HERACLES_LOG_PATH", @"D:\y\log.txt")), @"C:\A", @"C:\L").IsTestLibrary);
    }

    [Fact]
    public void AnEmptyOverrideIsIgnored()
    {
        var locations = AppPaths.Resolve(Environment(("HERACLES_DATA_DIR", "")), @"C:\A", @"C:\L");

        Assert.Equal(@"C:\A\Heracles", locations.DataDirectory);
    }

    [Fact]
    public void WhatTheAppKeepsAboutUpdatesIsThisPcsOwn()
    {
        // Beside the log, not in the library: it is about this copy of the app.
        Assert.Equal(@"C:\L\Heracles\updates", AppPaths.Resolve(Environment(), @"C:\A", @"C:\L").UpdatesDirectory);
    }

    [Fact]
    public void ATestLibraryKeepsItsUpdatesToItself()
    {
        // A rehearsal of an update must not touch what the real app
        // remembers, nor leave a download where the real app would find it.
        var locations = AppPaths.Resolve(Environment(("HERACLES_DATA_DIR", @"D:\x")), @"C:\A", @"C:\L");

        Assert.Equal(@"D:\x\updates", locations.UpdatesDirectory);
    }
}
