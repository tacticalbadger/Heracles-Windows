namespace Heracles.Tests.Editor;

/// <summary>The thread the view tests share: one test on it at a time.</summary>
public sealed class StaTests
{
    [Fact]
    public void ATestThatWaitsDoesNotLetAnotherRunInsideIt()
    {
        var order = new List<string>();
        using var begun = new ManualResetEventSlim();
        // Another test, from another of the runner's threads, as tests of two classes are.
        var other = new Thread(() =>
        {
            begun.Wait();
            Sta.Run(() =>
            {
                lock (order) order.Add("second");
            });
        });
        other.Start();

        Sta.Run(() =>
        {
            lock (order) order.Add("first begins");
            begun.Set();
            // Long enough for the other to ask for the thread. Then this one
            // lets the thread do what it has put off, as every typing test
            // does after each key, and as a test waiting on the web does.
            for (var turn = 0; turn < 5; turn++)
            {
                Thread.Sleep(60);
                Views.Pump();
            }
            lock (order) order.Add("first ends");
        });
        other.Join();

        Assert.Equal(["first begins", "first ends", "second"], order);
    }

    [Fact]
    public void WhatATestThrowsIsThrownToWhoeverRanIt()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => Sta.Run(() => throw new InvalidOperationException("from the views' thread")));
        Assert.Equal("from the views' thread", thrown.Message);
        // And the thread is free again afterwards.
        var ran = false;
        Sta.Run(() => ran = true);
        Assert.True(ran);
    }
}
