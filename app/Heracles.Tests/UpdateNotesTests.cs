using Heracles.Updates;

namespace Heracles.Tests;

/// <summary>
/// Release notes are written as Markdown (docs\release-notes), and the
/// update window draws them as text: the little of Markdown they use.
/// </summary>
public sealed class UpdateNotesTests
{
    private static NoteRun Plain(string text) => new(text, Bold: false, Code: false);

    private static NoteRun Bold(string text) => new(text, Bold: true, Code: false);

    private static NoteRun Code(string text) => new(text, Bold: false, Code: true);

    [Fact]
    public void AListItemIsAPointWithItsMarks()
    {
        var line = Assert.Single(UpdateNotes.Read("- Heracles now **updates itself**. Run `Heracles.exe` from the copy."));

        Assert.Equal(NoteLineKind.Point, line.Kind);
        Assert.Equal(
            [Plain("Heracles now "), Bold("updates itself"), Plain(". Run "), Code("Heracles.exe"), Plain(" from the copy.")],
            line.Runs.ToArray());
    }

    [Fact]
    public void EachItemIsALineOfItsOwn()
    {
        var lines = UpdateNotes.Read("- One.\n- Two.\r\n* Three.");

        Assert.Equal(["One.", "Two.", "Three."], lines.Select(line => line.Text));
        Assert.All(lines, line => Assert.Equal(NoteLineKind.Point, line.Kind));
    }

    [Fact]
    public void AnItemThatRunsOnToTheNextLineIsOneItem()
    {
        var lines = UpdateNotes.Read("- Keep the folder\n  where it is.\n- Two.");

        Assert.Equal(["Keep the folder where it is.", "Two."], lines.Select(line => line.Text));
    }

    [Fact]
    public void WordsThatAreNotAListAreParagraphs()
    {
        var lines = UpdateNotes.Read("The same app,\nas a disc image.\n\nNothing else changed.");

        Assert.Equal(["The same app, as a disc image.", "Nothing else changed."], lines.Select(line => line.Text));
        Assert.All(lines, line => Assert.Equal(NoteLineKind.Paragraph, line.Kind));
    }

    [Fact]
    public void AHeadingIsAHeading()
    {
        var lines = UpdateNotes.Read("## In this version\n- One.");

        Assert.Equal([NoteLineKind.Heading, NoteLineKind.Point], lines.Select(line => line.Kind));
        Assert.Equal("In this version", lines[0].Text);
    }

    [Fact]
    public void ALinkShowsItsWords()
    {
        // The window opens nothing: there is no web view in the app, and a
        // feed must not be able to send anyone anywhere.
        var line = Assert.Single(UpdateNotes.Read("See [the release page](https://example.com/releases) for more."));

        Assert.Equal("See the release page for more.", line.Text);
    }

    [Theory]
    [InlineData("2 ** 3 is 8")]
    [InlineData("a ` in the middle")]
    [InlineData("[not a link]")]
    public void MarksThatAreNotClosedAreLeftAsTyped(string text)
    {
        var line = Assert.Single(UpdateNotes.Read(text));

        Assert.Equal(text, line.Text);
        Assert.Equal([Plain(text)], line.Runs.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void NoNotesAreNoLines(string text)
    {
        Assert.Empty(UpdateNotes.Read(text));
    }

    [Fact]
    public void WhatLooksLikeMarkupOfAnotherKindIsOnlyText()
    {
        // Notes come from the feed. Whatever is in them is shown as words.
        var line = Assert.Single(UpdateNotes.Read("<script>alert(1)</script> <b>bold</b>"));

        Assert.Equal("<script>alert(1)</script> <b>bold</b>", line.Text);
    }
}
