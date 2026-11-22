using Heracles.Editor;
using static Heracles.Tests.Editor.EditorTestSupport;

namespace Heracles.Tests.Editor;

public sealed class JsonValueTests
{
    [Fact]
    public void ReadsAndWritesEveryKindOfValue()
    {
        var value = JsonValue.Parse("""{"a":[1,2.5,true,null,"x"],"b":{"c":false}}""")!;
        Assert.Equal(
            new[] { JsonValue.Of(1), JsonValue.Of(2.5), JsonValue.Of(true), JsonValue.Null, JsonValue.Of("x") },
            value["a"]?.ArrayValue);
        Assert.Equal(JsonValue.Of(false), value["b"]?["c"]);
        // Keys are written in order, and whole numbers without a fraction.
        Assert.Equal("""{"a":[1,2.5,true,null,"x"],"b":{"c":false}}""", value.Text);
    }

    [Fact]
    public void TellsABooleanFromANumber()
    {
        var value = JsonValue.Parse("""{"checked":true,"level":1}""")!;
        Assert.Equal(JsonValue.Of(true), value["checked"]);
        Assert.Equal(JsonValue.Of(1), value["level"]);
        Assert.Equal(1, value["level"]?.IntValue);
        Assert.Null(value["checked"]?.IntValue);
    }

    [Fact]
    public void LeavesSlashesAlone()
    {
        Assert.Equal("\"heracles-image://local/ab\"", JsonValue.Of("heracles-image://local/ab").Text);
    }

    [Fact]
    public void RefusesTextThatIsNotJSON()
    {
        Assert.Null(JsonValue.Parse("{nope"));
    }

    [Fact]
    public void KeysAreWrittenInOrderWhateverOrderTheyCameIn()
    {
        Assert.Equal("""{"a":1,"b":2}""", JsonValue.Parse("""{"b":2,"a":1}""")!.Text);
        Assert.Equal(JsonValue.Parse("""{"b":2,"a":1}"""), JsonValue.Parse("""{"a":1,"b":2}"""));
    }

    [Fact]
    public void LettersOutsideAsciiAreWrittenAsTheyAre()
    {
        Assert.Equal("\"café 🔥\"", JsonValue.Of("café 🔥").Text);
        Assert.Equal(JsonValue.Of("café 🔥"), JsonValue.Parse("\"café 🔥\""));
    }
}

public sealed class RichTextTests
{
    private static readonly TextStyle Bold = new(Bold: true);

    [Fact]
    public void PlainTextIsOneSpan()
    {
        var text = new RichText("Hello");
        Assert.Equal("[Hello]", Shape(text));
        Assert.Equal(5, text.Length);
        Assert.Equal("Hello", text.String);
        Assert.True(new RichText().IsEmpty);
        Assert.Empty(new RichText("").Spans);
    }

    [Fact]
    public void LengthsAreCountedTheWayTextViewsCountThem()
    {
        // An emoji is two UTF-16 units, which is what text boxes count.
        Assert.Equal(4, new RichText("a😀b").Length);
    }

    [Fact]
    public void NeighbouringSpansWithTheSameStyleBecomeOne()
    {
        var text = new RichText("Hel") + new RichText("lo") + new RichText(" bold", Bold) + new RichText("er", Bold);
        Assert.Equal("[Hello][b: bolder]", Shape(text));
    }

    [Fact]
    public void ReadsInlineContentFromADocument()
    {
        var text = RichText.FromInline(Items(
            """
            [{"type":"text","text":"Pack the "},
             {"type":"text","text":"lantern","marks":[{"type":"bold"},{"type":"italic"}]},
             {"type":"hardBreak"},
             {"type":"text","text":"site","marks":[{"type":"link","attrs":{"href":"https://example.com","target":"_blank"}}]},
             {"type":"text","text":"x","marks":[{"type":"code"},{"type":"strike"}]}]
            """));
        Assert.Equal("[Pack the ][bi:lantern][\n][<https://example.com>:site][sc:x]", Shape(text));
    }

    [Fact]
    public void WritesInlineContentBackAsItWasStored()
    {
        var stored = Items(
            """
            [{"type":"text","text":"Pack the "},
             {"type":"text","text":"lantern","marks":[{"type":"bold"},{"type":"italic"}]},
             {"type":"hardBreak"},
             {"type":"text","text":"site","marks":[{"type":"link","attrs":{"href":"https://example.com","target":"_blank"}}]}]
            """);
        Assert.Equal(stored, RichText.FromInline(stored).InlineJson);
    }

    [Fact]
    public void ALineBreakInsideMarkedTextSplitsTheRun()
    {
        var text = new RichText("one\ntwo", Bold);
        Assert.Equal(
            Items(
                """
                [{"type":"text","text":"one","marks":[{"type":"bold"}]},{"type":"hardBreak"},
                 {"type":"text","text":"two","marks":[{"type":"bold"}]}]
                """),
            text.InlineJson);
        Assert.Equal(Items("""[{"type":"hardBreak"}]"""), new RichText("\n").InlineJson);
    }

    [Fact]
    public void MarksAreWrittenInTheCoresOrder()
    {
        var style = new TextStyle(Bold: true, Italic: true, Strike: true, Link: new TextLink("https://a.example"));
        Assert.Equal(
            Items(
                """
                [{"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"https://a.example"}},
                  {"type":"bold"},{"type":"italic"},{"type":"strike"}]}]
                """),
            new RichText("x", style).InlineJson);
    }

    [Fact]
    public void KeepsMarksAndInlineNodesItDoesNotKnow()
    {
        var stored = Items(
            """
            [{"type":"text","text":"hi","marks":[{"type":"highlight","attrs":{"color":"red"}}]},
             {"type":"mention","attrs":{"id":"u1"}},
             {"type":"text","text":" there"}]
            """);
        var text = RichText.FromInline(stored);
        // An unknown inline node takes one place in the text.
        Assert.Equal(9, text.Length);
        Assert.Equal("[hi][@:￼][ there]", Shape(text));
        Assert.Equal(stored, text.InlineJson);
    }

    [Fact]
    public void DropsEmptyTextRuns()
    {
        Assert.Single(RichText.FromInline(Items("""[{"type":"text","text":""},{"type":"text","text":"a"}]""")).Spans);
    }

    [Fact]
    public void TakesAPieceOfTheText()
    {
        var text = new RichText("Hello ") + new RichText("bold", Bold) + new RichText(" end");
        Assert.Equal("[lo ][b:bo]", Shape(text.Subtext(3, 8)));
        Assert.Equal("", Shape(text.Subtext(0, 0)));
        Assert.Equal("[b:bold]", Shape(text.Subtext(6, 10)));
        // A range past the end is cut to fit.
        Assert.Equal("[ end]", Shape(text.Subtext(10, 99)));
    }

    [Fact]
    public void ReplacesARange()
    {
        var text = new RichText("Hello world");
        text = text.Replace(5, 11, new RichText("!", Bold));
        Assert.Equal("[Hello][b:!]", Shape(text));
        text = text.Replace(0, 0, new RichText(">> "));
        Assert.Equal("[>> Hello][b:!]", Shape(text));
        text = text.Replace(3, 8, new RichText());
        Assert.Equal("[>> ][b:!]", Shape(text));
    }

    [Fact]
    public void TurnsAMarkOnAndOffOverARange()
    {
        var text = new RichText("Hello world");
        text = text.Set(TextMark.Bold, true, 0, 5);
        Assert.Equal("[b:Hello][ world]", Shape(text));
        Assert.True(text.Has(TextMark.Bold, 0, 5));
        Assert.False(text.Has(TextMark.Bold, 0, 6));
        Assert.False(text.Has(TextMark.Bold, 3, 3));
        text = text.Set(TextMark.Italic, true, 3, 8);
        Assert.Equal("[b:Hel][bi:lo][i: wo][rld]", Shape(text));
        text = text.Set(TextMark.Bold, false, 0, 11);
        Assert.Equal("[Hel][i:lo wo][rld]", Shape(text));
    }

    [Fact]
    public void CodeTakesTheOtherMarksAway()
    {
        var text = new RichText("abc", new TextStyle(Bold: true, Link: new TextLink("https://a.example")));
        text = text.Set(TextMark.Code, true, 0, 3);
        Assert.Equal("[c:abc]", Shape(text));
        // And nothing else can be added to code.
        text = text.Set(TextMark.Bold, true, 0, 3);
        Assert.Equal("[c:abc]", Shape(text));
    }

    [Fact]
    public void SetsAndRemovesALink()
    {
        var text = new RichText("see the site now");
        text = text.SetLink(new TextLink("https://example.com"), 8, 12);
        Assert.Equal("[see the ][<https://example.com>:site][ now]", Shape(text));
        Assert.Equal((8, 12), (text.LinkAt(10)?.Start, text.LinkAt(10)?.End));
        Assert.Equal("https://example.com", text.LinkAt(10)?.Link.Href);
        // The caret just after a link still counts as on it, just before does not.
        Assert.Equal((8, 12), (text.LinkAt(12)?.Start, text.LinkAt(12)?.End));
        Assert.Null(text.LinkAt(8));
        Assert.Null(text.LinkAt(3));
        text = text.SetLink(null, 0, 16);
        Assert.Equal("[see the site now]", Shape(text));
    }

    [Fact]
    public void GivesTheStyleNewTypingWouldTake()
    {
        var text = new RichText("plain") + new RichText("bold", Bold);
        Assert.Equal(TextStyle.Plain, text.StyleBefore(5));
        Assert.Equal(Bold, text.StyleBefore(6));
        Assert.Equal(Bold, text.StyleBefore(9));
        // At the very start there is nothing before, so the first character decides.
        Assert.Equal(Bold, new RichText("bold", Bold).StyleBefore(0));
        Assert.Equal(TextStyle.Plain, new RichText().StyleBefore(0));
    }

    [Fact]
    public void TypingAfterALinkDoesNotExtendIt()
    {
        var text = new RichText("site", new TextStyle(Bold: true, Link: new TextLink("https://example.com")));
        Assert.Equal(Bold, text.StyleBefore(4));
        Assert.NotNull(text.StyleBefore(2).Link);
    }

    [Fact]
    public void TextsThatReadTheSameAreEqualHoweverTheyWereBuilt()
    {
        var joined = new RichText("Hel") + new RichText("lo") + new RichText("!", Bold);
        var whole = new RichText("Hello").Replace(5, 5, new RichText("!", Bold));

        Assert.Equal(whole, joined);
        Assert.NotEqual(whole, new RichText("Hello!"));
        // A style with no unknown marks is the same style however it was made.
        Assert.Equal(new TextStyle(), TextStyle.Plain);
        Assert.Equal(new TextLink("https://a.example"), new TextLink("https://a.example"));
    }

    [Fact]
    public void ARangeIsTakenAsAskedEvenInsideAPairOfUnits()
    {
        // Keeping a caret off the middle of an emoji is the view's business.
        var text = new RichText("a😀b");
        Assert.Equal(2, text.Subtext(0, 2).Length);
        Assert.Equal("a😀b", (text.Subtext(0, 2) + text.Subtext(2, 4)).String);
    }

    [Fact]
    public void AnEmptyTextTakesAReplacementAnywhere()
    {
        Assert.Equal("[b:x]", Shape(new RichText().Replace(0, 0, new RichText("x", Bold))));
        Assert.Equal("[x]", Shape(new RichText().Replace(5, 9, new RichText("x"))));
    }
}

public sealed class JsonTextTests
{
    [Fact]
    public void TextThatOnlyLooksLikeAnEscapeIsLeftAlone()
    {
        // A backslash, then the letters of an escape, twice: twelve characters
        // of text, not an emoji. Built from the character code so that no
        // tool between here and the compiler can read it as an escape.
        var backslash = ((char)92).ToString();
        var quote = ((char)34).ToString();
        var value = JsonValue.Of(backslash + "uD83D" + backslash + "uDD25");
        Assert.Equal(12, value.StringValue!.Length);

        // Written, each backslash is doubled and nothing else changes.
        Assert.Equal(quote + backslash + backslash + "uD83D" + backslash + backslash + "uDD25" + quote, value.Text);
        Assert.Equal(value, JsonValue.Parse(value.Text));
    }

    [Fact]
    public void WhatIsWrittenReadsBackTheSame()
    {
        var backslash = ((char)92).ToString();
        var value = JsonValue.ObjectOf(
            ("emoji", JsonValue.Of("🔥 and 🧭")),
            ("quote", JsonValue.Of("say " + (char)34 + "hi" + (char)34)),
            ("path", JsonValue.Of("a" + backslash + "b/c")),
            ("break", JsonValue.Of("one" + (char)10 + "two")),
            ("numbers", JsonValue.ArrayOf([JsonValue.Of(0), JsonValue.Of(-1), JsonValue.Of(1.5), JsonValue.Of(1e20)])));

        Assert.Equal(value, JsonValue.Parse(value.Text));
        Assert.Contains("🔥 and 🧭", value.Text);
        Assert.Contains("[0,-1,1.5,1E+20]", value.Text.ToUpperInvariant());
    }
}
