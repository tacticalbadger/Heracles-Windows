namespace Heracles.Tests;

public sealed class WindowStatesTests
{
    [Fact]
    public void StatesSurviveBeingSavedAndRestored()
    {
        SavedWindow[] states =
        [
            new(100, 80, 1200, 800, false, """{"focused":"p","root":{"leaf":{"id":"p","tabs":[],"active":null}}}"""),
            new(-1800, 40, 900, 700, true, null),
        ];

        var restored = WindowStates.Deserialize(WindowStates.Serialize(states));

        Assert.Equal(states, restored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""[{"Width":"x"}]""")]
    [InlineData("[null]")]
    public void WhatIsNotAListOfWindowsIsNoWindows(string? json)
    {
        Assert.Empty(WindowStates.Deserialize(json));
    }

    [Fact]
    public void AWindowTooSmallToUseComesBackUsable()
    {
        var restored = WindowStates.Deserialize(WindowStates.Serialize([new SavedWindow(10, 10, 100, 50, false, null)]));

        var state = Assert.Single(restored);
        Assert.Equal(400, state.Width);
        Assert.Equal(300, state.Height);
    }

    [Fact]
    public void TheFileIsWrittenAndReadBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"heracles-tests-{Guid.NewGuid()}");
        var path = Path.Combine(folder, "nested", "windows.json");
        try
        {
            Assert.Empty(WindowStates.Load(path)); // no file yet

            WindowStates.Save(path, [new SavedWindow(1, 2, 800, 600, false, null)]);
            Assert.Equal([new SavedWindow(1, 2, 800, 600, false, null)], WindowStates.Load(path));

            File.WriteAllText(path, "damaged");
            Assert.Empty(WindowStates.Load(path)); // ignored, not fatal
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { }
        }
    }
}
