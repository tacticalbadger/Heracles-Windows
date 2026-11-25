namespace Heracles.Tests;

public sealed class SingleInstanceTests
{
    private static string UniqueName() => $"heracles-tests-{Guid.NewGuid()}";

    public static TheoryData<string[]> Arguments => new()
    {
        Array.Empty<string>(),
        new[] { "--new-window" },
        new[] { "heracles://note/abc#block=b1", "a path with spaces\\and \"quotes\"", "línea\nnueva" },
    };

    [Theory]
    [MemberData(nameof(Arguments))]
    public void ArgumentsSurviveTheCrossing(string[] args)
    {
        var line = SingleInstance.Encode(args);

        Assert.DoesNotContain('\n', line);
        Assert.Equal(args, SingleInstance.Decode(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1, 2]")]
    public void WhatIsNotArgumentsIsNoArguments(string line)
    {
        Assert.Empty(SingleInstance.Decode(line));
    }

    [Fact]
    public void OnlyTheFirstToClaimANameHoldsIt()
    {
        var name = UniqueName();
        using var first = SingleInstance.Claim(name, _ => { });
        using var second = SingleInstance.Claim(name, _ => { });

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void ANameIsFreeAgainOnceItsHolderIsGone()
    {
        var name = UniqueName();
        SingleInstance.Claim(name, _ => { })?.Dispose();

        using var again = SingleInstance.Claim(name, _ => { });
        Assert.NotNull(again);
    }

    [Fact]
    public async Task ASecondLaunchHandsItsArgumentsToTheHolder()
    {
        var name = UniqueName();
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var holder = SingleInstance.Claim(name, args => received.TrySetResult(args));
        Assert.NotNull(holder);

        Assert.True(SingleInstance.Send(name, ["heracles://note/abc", "--new-window"], TimeSpan.FromSeconds(5)));

        var args = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["heracles://note/abc", "--new-window"], args);
    }

    [Fact]
    public void TheHolderCanBeReachedAsSoonAsItHoldsTheName()
    {
        // No waiting for the holder to get round to listening: by the time
        // the name is held, a later copy finds somebody to hand over to. (It
        // used to start listening a moment later, on a borrowed thread, and
        // on a busy machine a later copy could give up before it had.)
        for (var round = 0; round < 50; round++)
        {
            var name = UniqueName();
            using var holder = SingleInstance.Claim(name, _ => { });

            // Asked of Windows, which lists the pipes there are: no timing in it.
            var pipes = Directory.GetFiles(@"\\.\pipe\").Select(Path.GetFileName);
            Assert.True(pipes.Contains(SingleInstance.PipeName(name)), $"round {round}: nobody is listening yet");
        }
    }

    [Fact]
    public async Task TheHolderKeepsListeningAfterOneLaunch()
    {
        var name = UniqueName();
        var seen = new List<string>();
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var holder = SingleInstance.Claim(name, args =>
        {
            lock (seen)
            {
                seen.Add(string.Join(" ", args));
                if (seen.Count == 2) both.TrySetResult();
            }
        });

        Assert.True(SingleInstance.Send(name, ["one"], TimeSpan.FromSeconds(5)));
        Assert.True(SingleInstance.Send(name, ["two"], TimeSpan.FromSeconds(5)));

        await both.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["one", "two"], seen);
    }

    [Fact]
    public void SendingToANameNobodyHoldsFailsInsteadOfWaitingForever()
    {
        var started = DateTime.UtcNow;

        Assert.False(SingleInstance.Send(UniqueName(), ["--new-window"], TimeSpan.FromMilliseconds(300)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TwoLibrariesDoNotShareAName()
    {
        using var one = SingleInstance.Claim(@"C:\Data\One", _ => { });
        using var other = SingleInstance.Claim(@"C:\Data\Two", _ => { });
        using var sameInOtherCase = SingleInstance.Claim(@"c:\data\ONE", _ => { });

        Assert.NotNull(one);
        Assert.NotNull(other);
        Assert.Null(sameInOtherCase); // Windows paths ignore case
    }
}
