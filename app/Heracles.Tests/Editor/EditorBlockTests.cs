using Heracles.Editor;
using static Heracles.Tests.Editor.EditorTestSupport;

namespace Heracles.Tests.Editor;

/// <summary>Reading a stored document into blocks, and writing blocks back.</summary>
public sealed class EditorBlockTests
{
    private const string Everything =
        """
        {"type":"doc","content":[
          {"type":"heading","attrs":{"blockId":"h1","level":2},"content":[{"type":"text","text":"Trip plan"}]},
          {"type":"paragraph","attrs":{"blockId":"p1"},"content":[
            {"type":"text","text":"Pack the "},{"type":"text","text":"lantern","marks":[{"type":"bold"}]}]},
          {"type":"bulletList","content":[
            {"type":"listItem","attrs":{"blockId":"b1"},"content":[
              {"type":"paragraph","attrs":{"blockId":"b1p"},"content":[{"type":"text","text":"Tent"}]},
              {"type":"bulletList","content":[
                {"type":"listItem","attrs":{"blockId":"b2"},"content":[
                  {"type":"paragraph","attrs":{"blockId":"b2p"},"content":[{"type":"text","text":"Pegs"}]}]}]},
              {"type":"paragraph","attrs":{"blockId":"b1q"},"content":[{"type":"text","text":"More about the tent"}]}]},
            {"type":"listItem","attrs":{"blockId":"b3"},"content":[
              {"type":"paragraph","attrs":{"blockId":"b3p"},"content":[{"type":"text","text":"Stove"}]}]}]},
          {"type":"orderedList","attrs":{"start":3},"content":[
            {"type":"listItem","attrs":{"blockId":"n1"},"content":[
              {"type":"paragraph","attrs":{"blockId":"n1p"},"content":[{"type":"text","text":"Third"}]}]}]},
          {"type":"taskList","content":[
            {"type":"taskItem","attrs":{"blockId":"t1","checked":true},"content":[
              {"type":"paragraph","attrs":{"blockId":"t1p"},"content":[{"type":"text","text":"Book ferry"}]}]},
            {"type":"taskItem","attrs":{"blockId":"t2","checked":false},"content":[
              {"type":"paragraph","attrs":{"blockId":"t2p"}}]}]},
          {"type":"blockquote","attrs":{"blockId":"q1"},"content":[
            {"type":"paragraph","attrs":{"blockId":"q1p"},"content":[{"type":"text","text":"Quoted"}]}]},
          {"type":"codeMirrorBlock","attrs":{"blockId":"c1","language":"rust","code":"fn main() {}"}},
          {"type":"calloutBlock","attrs":{"blockId":"k1","emoji":"🔥"},"content":[
            {"type":"paragraph","attrs":{"blockId":"k1p"},"content":[{"type":"text","text":"Hot"}]}]},
          {"type":"toggleBlock","attrs":{"blockId":"g1","open":false},"content":[
            {"type":"paragraph","attrs":{"blockId":"g1p"},"content":[{"type":"text","text":"Summary"}]},
            {"type":"paragraph","attrs":{"blockId":"g1q"},"content":[{"type":"text","text":"Hidden"}]}]},
          {"type":"columnBlock","content":[
            {"type":"column","attrs":{"width":2},"content":[
              {"type":"paragraph","attrs":{"blockId":"l1"},"content":[{"type":"text","text":"Left"}]}]},
            {"type":"column","attrs":{"width":1},"content":[
              {"type":"paragraph","attrs":{"blockId":"r1"},"content":[{"type":"text","text":"Right"}]}]}]},
          {"type":"horizontalRule","attrs":{"blockId":"d1"}},
          {"type":"image","attrs":{"blockId":"i1","src":"heracles-image://local/abc","alt":"Map","title":null}}
        ]}
        """;

    private static EditorDocument Read(string json)
    {
        var document = EditorDocument.FromJson(json, BlockIds.Sequential());
        Assert.NotNull(document);
        return document;
    }

    [Fact]
    public void ReadsEveryKindOfBlock()
    {
        var document = Read(Everything);
        // The columns and their rows have no stored ids. Give them fixed ones to print.
        var row = document.Blocks.Single(block => block.Kind == BlockKind.Columns);
        row.Id = "cols";
        row.Children[0].Id = "left";
        row.Children[1].Id = "right";
        Assert.Equal(
            Lines(
                """
                h2#h1 Trip plan
                p#p1 Pack the lantern
                bullet#b1 Tent
                  bullet#b2 Pegs
                  p#b1q More about the tent
                bullet#b3 Stove
                numbered#n1 Third
                done#t1 Book ferry
                todo#t2
                quote#q1
                  p#q1p Quoted
                code#c1 fn main() {}
                callout#k1
                  p#k1p Hot
                toggle(closed)#g1
                  p#g1p Summary
                  p#g1q Hidden
                columns#cols
                  column#left
                    p#l1 Left
                  column#right
                    p#r1 Right
                divider#d1
                image#i1
                """),
            Outline(document));
        Assert.Equal("[Pack the ][b:lantern]", Shape(document.Blocks[1].Text));
        Assert.Equal("b1p", document.Blocks[2].ParagraphId);
        Assert.Equal("rust", document.Block("c1")?.Language);
        Assert.Equal("🔥", document.Block("k1")?.Emoji);
        Assert.Equal(2, document.Block("left")?.Width);
        Assert.Equal(JsonValue.Of("Map"), document.Block("i1")?.Attrs["alt"]);
        Assert.Equal(JsonValue.Of(3), document.Block("n1")?.ListAttrs["start"]);
    }

    [Fact]
    public void WritesADocumentBackAsItWasStored()
    {
        Assert.True(SameJson(Read(Everything).Json, Everything));
    }

    [Fact]
    public void TwoListsInARowStayTwoLists()
    {
        const string stored =
            """
            {"type":"doc","content":[
              {"type":"bulletList","content":[{"type":"listItem","attrs":{"blockId":"a"},"content":[
                {"type":"paragraph","attrs":{"blockId":"ap"},"content":[{"type":"text","text":"A"}]}]}]},
              {"type":"bulletList","content":[{"type":"listItem","attrs":{"blockId":"b"},"content":[
                {"type":"paragraph","attrs":{"blockId":"bp"},"content":[{"type":"text","text":"B"}]}]}]}]}
            """;
        var document = Read(stored);
        Assert.Equal("bullet#a A\n^bullet#b B", Outline(document));
        Assert.True(SameJson(document.Json, stored));
    }

    [Fact]
    public void ListItemsOfOneKindAreWrittenAsOneList()
    {
        var document = Doc(Bullet("a", "A"), Bullet("b", "B"), Todo("c", "C"), P("d", "D"), Bullet("e", "E"));
        Assert.True(SameJson(
            document.Json,
            """
            {"type":"doc","content":[
              {"type":"bulletList","content":[
                {"type":"listItem","attrs":{"blockId":"a"},"content":[{"type":"paragraph","attrs":{"blockId":"a-p"},"content":[{"type":"text","text":"A"}]}]},
                {"type":"listItem","attrs":{"blockId":"b"},"content":[{"type":"paragraph","attrs":{"blockId":"b-p"},"content":[{"type":"text","text":"B"}]}]}]},
              {"type":"taskList","content":[
                {"type":"taskItem","attrs":{"blockId":"c","checked":false},"content":[{"type":"paragraph","attrs":{"blockId":"c-p"},"content":[{"type":"text","text":"C"}]}]}]},
              {"type":"paragraph","attrs":{"blockId":"d"},"content":[{"type":"text","text":"D"}]},
              {"type":"bulletList","content":[
                {"type":"listItem","attrs":{"blockId":"e"},"content":[{"type":"paragraph","attrs":{"blockId":"e-p"},"content":[{"type":"text","text":"E"}]}]}]}]}
            """));
    }

    [Fact]
    public void AListItemsChildrenAreWrittenInsideIt()
    {
        var document = Doc(Numbered("a", "A", P("x", "more"), Numbered("b", "B")));
        Assert.True(SameJson(
            document.Json,
            """
            {"type":"doc","content":[{"type":"orderedList","content":[
              {"type":"listItem","attrs":{"blockId":"a"},"content":[
                {"type":"paragraph","attrs":{"blockId":"a-p"},"content":[{"type":"text","text":"A"}]},
                {"type":"paragraph","attrs":{"blockId":"x"},"content":[{"type":"text","text":"more"}]},
                {"type":"orderedList","content":[{"type":"listItem","attrs":{"blockId":"b"},"content":[
                  {"type":"paragraph","attrs":{"blockId":"b-p"},"content":[{"type":"text","text":"B"}]}]}]}]}]}]}
            """));
    }

    [Fact]
    public void BlocksWithoutIdsAreGivenThem()
    {
        var document = Read(
            """
            {"type":"doc","content":[
              {"type":"paragraph","content":[{"type":"text","text":"A"}]},
              {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph"}]}]}]}
            """);
        Assert.Equal("p#n1 A\nbullet#n2", Outline(document));
        Assert.Equal("n3", document.Blocks[1].ParagraphId);
    }

    [Fact]
    public void AnIdUsedTwiceIsKeptOnlyByTheFirst()
    {
        var document = Read(
            """
            {"type":"doc","content":[
              {"type":"paragraph","attrs":{"blockId":"same"},"content":[{"type":"text","text":"A"}]},
              {"type":"paragraph","attrs":{"blockId":"same"},"content":[{"type":"text","text":"B"}]}]}
            """);
        Assert.Equal("p#same A\np#n1 B", Outline(document));
    }

    [Fact]
    public void AnEmptyDocumentIsOneEmptyParagraph()
    {
        Assert.Equal("p#n1", Outline(Read("""{"type":"doc"}""")));
        Assert.Equal("p#n1", Outline(Read("""{"type":"doc","content":[]}""")));
    }

    [Fact]
    public void WhatIsNotADocumentIsRefused()
    {
        var ids = BlockIds.Sequential();
        Assert.Null(EditorDocument.FromJson("not json", ids));
        Assert.Null(EditorDocument.FromJson("""{"type":"paragraph"}""", ids));
    }

    [Fact]
    public void ABlockThisEditorDoesNotKnowIsKeptAsStored()
    {
        const string stored =
            """
            {"type":"doc","content":[
              {"type":"table","attrs":{"blockId":"x"},"content":[{"type":"tableRow"}]},
              {"type":"paragraph","attrs":{"blockId":"p","textAlign":"center"},"content":[{"type":"text","text":"A"}]}]}
            """;
        var document = Read(stored);
        Assert.Equal("unknown#x\np#p A", Outline(document));
        // So is an attribute it does not know, on a block it does.
        Assert.True(SameJson(document.Json, stored));
    }

    [Fact]
    public void ACodeBlockStoresItsTextAsAnAttribute()
    {
        var written = JsonValue.Parse(Doc(Code("c", "let x = 1\nlet y = 2", "swift")).Json)!;
        var block = written["content"]!.ArrayValue![0];

        Assert.Equal("codeMirrorBlock", block["type"]?.StringValue);
        Assert.Equal("c", block["attrs"]?["blockId"]?.StringValue);
        Assert.Equal("swift", block["attrs"]?["language"]?.StringValue);
        Assert.Equal("let x = 1\nlet y = 2", block["attrs"]?["code"]?.StringValue);
        Assert.Null(block["content"]);
    }

    [Fact]
    public void AContainerLeftEmptyIsWrittenWithAParagraphSoItStaysValid()
    {
        var written = JsonValue.Parse(Doc(Quote("q")).Json)!;
        var inside = written["content"]?.ArrayValue?[0]["content"]?.ArrayValue;
        Assert.Equal(1, inside?.Count);
        Assert.Equal(JsonValue.Of("paragraph"), inside?[0]["type"]);
    }

    [Fact]
    public void IdsCanBeMadeInSequenceForTests()
    {
        var ids = BlockIds.Sequential("x");
        Assert.Equal("x1", ids.Make());
        Assert.Equal("x2", ids.Make());
        var random = BlockIds.Random();
        var first = random.Make();
        Assert.True(first.Length == 36 && first != random.Make());
        Assert.Equal(first.ToLowerInvariant(), first);
    }

    [Fact]
    public void ACopyChangedLeavesTheOriginalAsItWas()
    {
        var original = Read(Everything);
        var outline = Outline(original);
        var json = original.Json;

        var copy = original.Clone();
        Assert.True(copy.SameAs(original));

        copy.Blocks[1].Text = new RichText("changed");
        copy.Blocks[2].Children.Add(P("added", "new"));
        copy.Block("i1")!.Attrs["alt"] = JsonValue.Of("Chart");
        copy.Block("n1")!.ListAttrs["start"] = JsonValue.Of(9);
        copy.Blocks.RemoveAt(0);

        Assert.Equal(outline, Outline(original));
        Assert.Equal(json, original.Json);
        Assert.False(copy.SameAs(original));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("child")]
    [InlineData("attribute")]
    [InlineData("kind")]
    [InlineData("list start")]
    public void AnyDifferenceMakesTwoBlocksDifferent(string what)
    {
        var block = Bullet("a", "One", P("x", "inside"));
        var other = block.Clone();
        Assert.True(other.SameAs(block));

        switch (what)
        {
            case "text":
                other.Text = new RichText("One", new TextStyle(Bold: true));
                break;
            case "child":
                other.Children[0].Text = new RichText("outside");
                break;
            case "attribute":
                other.Attrs["colour"] = JsonValue.Of("red");
                break;
            case "kind":
                other.Kind = BlockKind.Numbered;
                break;
            case "list start":
                other.StartsList = true;
                break;
        }

        Assert.False(other.SameAs(block));
    }
}
