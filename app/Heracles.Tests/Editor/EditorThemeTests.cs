using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Heracles.Editor;
using static Heracles.Tests.Editor.EditorTestSupport;
using Block = Heracles.Editor.Block;

namespace Heracles.Tests.Editor;

/// <summary>A block's text as a text box shows it, and read back from one.</summary>
public sealed class EditorThemeTests
{
    private static Paragraph Shown(Block block)
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.AddRange(EditorTheme.Inlines(block, emoji: false));
        return paragraph;
    }

    private static Inline At(Paragraph paragraph, int index) => paragraph.Inlines.ElementAt(index);

    [Fact]
    public void ABlocksTextSurvivesTheTripThroughATextBox() => Sta.Run(() =>
    {
        var block = P("a", "plain bold italic code link struck");
        block.Text = block.Text.Set(TextMark.Bold, true, 6, 10).Set(TextMark.Italic, true, 11, 17)
            .Set(TextMark.Code, true, 18, 22)
            .SetLink(new TextLink("https://example.com", JsonValue.ObjectOf(("target", JsonValue.Of("_blank")))), 23, 27)
            .Set(TextMark.Strike, true, 28, 34);
        block.Text += RichText.FromInline([JsonValue.Parse("""{"type":"mention","attrs":{"id":"u1"}}""")!]);
        // A line break keeps the marks of the text it is in.
        block.Text += new RichText("two\nlines", new TextStyle(Bold: true));

        Assert.Equal(block.Text, EditorTheme.RichTextFrom(Shown(block)));
    });

    [Fact]
    public void MarksShowAsFontsAndLines() => Sta.Run(() =>
    {
        var block = P("a", "bold italic code struck");
        block.Text = block.Text.Set(TextMark.Bold, true, 0, 4).Set(TextMark.Italic, true, 5, 11)
            .Set(TextMark.Code, true, 12, 16).Set(TextMark.Strike, true, 17, 23);
        var shown = Shown(block);
        // bold, a space, italic, a space, code, a space, struck
        Assert.Equal(FontWeights.Bold, At(shown, 0).FontWeight);
        Assert.Equal(FontStyles.Italic, At(shown, 2).FontStyle);
        Assert.Same(EditorTheme.Mono, At(shown, 4).FontFamily);
        Assert.Contains(TextDecorations.Strikethrough[0], At(shown, 6).TextDecorations);
        Assert.True(At(shown, 0).TextDecorations is null or { Count: 0 });
        Assert.NotEqual(FontWeights.Bold, At(shown, 1).FontWeight);
    });

    [Fact]
    public void ALinkIsUnderlinedAndKeepsItsAddress() => Sta.Run(() =>
    {
        var block = P("a", "see the site");
        block.Text = block.Text.SetLink(new TextLink("https://example.com"), 8, 12);
        var shown = Shown(block);
        Assert.Contains(TextDecorations.Underline[0], At(shown, 1).TextDecorations);
        Assert.Equal("https://example.com", (At(shown, 1).GetValue(EditorTheme.MarksProperty) as TextStyle)?.Link?.Href);
    });

    [Fact]
    public void FontsAndColoursFromElsewhereAreNotMarks() => Sta.Run(() =>
    {
        var foreign = new Paragraph();
        foreign.Inlines.Add(new Bold(new Run("pasted") { FontSize = 30, Foreground = Brushes.Red }));
        Assert.Equal("[pasted]", Shape(EditorTheme.RichTextFrom(foreign)));
    });

    [Fact]
    public void AFinishedTaskIsStruckThrough() => Sta.Run(() =>
    {
        var shown = Shown(Todo("t", "Done", true));
        Assert.Contains(TextDecorations.Strikethrough[0], At(shown, 0).TextDecorations);
        // That is how it looks, not a mark on the text.
        Assert.Equal("[Done]", Shape(EditorTheme.RichTextFrom(shown)));
    });

    [Fact]
    public void CodeIsReadBackAsItsWordsAlone() => Sta.Run(() =>
    {
        var shown = Shown(Code("c", "if x {\n  y()\n}"));
        Assert.Equal(5, shown.Inlines.Count);
        Assert.Equal("[if x {\n  y()\n}]", Shape(EditorTheme.RichTextFrom(shown, isCode: true)));
    });

    [Fact]
    public void EveryKindOfLineBreakBecomesTheOneThatIsStored() => Sta.Run(() =>
    {
        // The Unicode line and paragraph separators, by number: written in
        // the source as characters they would end the line they are on.
        var line = (char)0x2028;
        var paragraph = (char)0x2029;
        Assert.Equal("a\nb\nc\nd\ne", EditorTheme.Clean($"a\r\nb\rc{line}d{paragraph}e"));
        var odd = new Paragraph();
        odd.Inlines.Add(new Run($"one{line}two"));
        Assert.Equal("[one\ntwo]", Shape(EditorTheme.RichTextFrom(odd)));
    });

    [Fact]
    public void StaticTextDrawsAnEmojiAsAnElementOfItsOwn() => Sta.Run(() =>
    {
        var block = P("a", "Pack the lantern 💡 and go");
        block.Text = block.Text.Set(TextMark.Bold, true, 0, 4);
        var inlines = EditorTheme.Inlines(block, emoji: true).ToList();
        var emoji = Assert.Single(inlines.OfType<global::Emoji.Wpf.EmojiInline>());
        Assert.Equal("💡", emoji.Text);
        // The text is all still there, in order, when it is read back.
        var paragraph = new Paragraph();
        paragraph.Inlines.AddRange(inlines);
        Assert.Equal(block.Text, EditorTheme.RichTextFrom(paragraph));
        // A text box is given the emoji as text: it counts characters.
        Assert.Empty(EditorTheme.Inlines(block, emoji: false).OfType<global::Emoji.Wpf.EmojiInline>());
    });

    [Fact]
    public void HeadingsAreLargerAndSetApart() => Sta.Run(() =>
    {
        Assert.True(EditorTheme.FontFor(H(1, "h")).Size > EditorTheme.FontFor(H(2, "h")).Size);
        Assert.True(EditorTheme.FontFor(H(3, "h")).Size > EditorTheme.FontFor(P("p")).Size);
        Assert.Equal(EditorTheme.BodySize, EditorTheme.FontFor(Bullet("b")).Size);
        Assert.Same(EditorTheme.Mono, EditorTheme.FontFor(Code("c")).Family);
        Assert.True(EditorTheme.LineHeight(H(1, "h")) > EditorTheme.LineHeight(P("p")));
        Assert.True(EditorTheme.TopMargin(H(1, "h")) > EditorTheme.TopMargin(H(3, "h")));
        Assert.Equal(0, EditorTheme.TopMargin(P("p")));
    });
}
