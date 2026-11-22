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
    public void AnEmptyOverrideIsIgnored()
    {
        var locations = AppPaths.Resolve(Environment(("HERACLES_DATA_DIR", "")), @"C:\A", @"C:\L");

        Assert.Equal(@"C:\A\Heracles", locations.DataDirectory);
    }
}
