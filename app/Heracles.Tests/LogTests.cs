namespace Heracles.Tests;

public sealed class LogTests
{
    [Fact]
    public void AnEntryIsOneLine()
    {
        var line = Log.Line(new DateTime(2026, 11, 22, 9, 5, 7, DateTimeKind.Utc), "info", "a\r\nb");

        Assert.Equal("2026-11-22T09:05:07Z info a b", line);
    }
}
