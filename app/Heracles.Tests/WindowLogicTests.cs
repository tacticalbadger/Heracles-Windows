using Heracles.Model;
using Heracles.Notes;
using Heracles.Sidebar;
using Heracles.Workspaces;

namespace Heracles.Tests;

public sealed class ReadOnlyDocumentTests
{
    private static string Doc(string content) => $$"""{"type":"doc","content":[{{content}}]}""";

    private static string Paragraph(string text) =>
        $$"""{"type":"paragraph","content":[{"type":"text","text":"{{text}}"}]}""";

    private static string Item(string text, string more = "", string attrs = "") =>
        $$"""{"type":"listItem"{{attrs}},"content":[{{Paragraph(text)}}{{more}}]}""";

    [Fact]
    public void AParagraphIsItsText()
    {
        Assert.Equal("Pack the lantern.", ReadOnlyDocument.PlainText(Doc(Paragraph("Pack the lantern."))));
    }

    [Fact]
    public void BlocksAreLinesOfTheirOwn()
    {
        var json = Doc(
            """{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Trip"}]},"""
            + Paragraph("First") + "," + Paragraph("Second"));

        Assert.Equal("Trip\nFirst\nSecond", ReadOnlyDocument.PlainText(json));
    }

    [Fact]
    public void MarksAndLineBreaksInsideABlockAreRead()
    {
        var json = Doc(
            """{"type":"paragraph","content":[{"type":"text","text":"bold","marks":[{"type":"bold"}]},{"type":"text","text":" and"},{"type":"hardBreak"},{"type":"text","text":"more"}]}""");

        Assert.Equal("bold and\nmore", ReadOnlyDocument.PlainText(json));
    }

    [Fact]
    public void ABulletListIsIndentedByDepth()
    {
        var nested = $$""",{"type":"bulletList","content":[{{Item("inner")}}]}""";
        var json = Doc($$"""{"type":"bulletList","content":[{{Item("outer", nested)}},{{Item("next")}}]}""");

        Assert.Equal("• outer\n  • inner\n• next", ReadOnlyDocument.PlainText(json));
    }

    [Fact]
    public void ANumberedListCounts()
    {
        var json = Doc($$"""{"type":"orderedList","content":[{{Item("one")}},{{Item("two")}}]}""");

        Assert.Equal("1. one\n2. two", ReadOnlyDocument.PlainText(json));
    }

    [Fact]
    public void ATaskListShowsWhatIsDone()
    {
        var done = """{"type":"taskItem","attrs":{"checked":true},"content":[""" + Paragraph("done") + "]}";
        var open = """{"type":"taskItem","attrs":{"checked":false},"content":[""" + Paragraph("open") + "]}";
        var json = Doc($$"""{"type":"taskList","content":[{{done}},{{open}}]}""");

        Assert.Equal("☑ done\n☐ open", ReadOnlyDocument.PlainText(json));
    }

    [Fact]
    public void ACodeBlockIsItsCode()
    {
        var json = Doc("""{"type":"codeMirrorBlock","attrs":{"language":"rust","code":"fn main() {}\n// done"}}""");

        Assert.Equal("fn main() {}\n// done", ReadOnlyDocument.PlainText(json));
    }

    [Fact]
    public void BlocksThatHoldBlocksShowWhatIsInThem()
    {
        var json = Doc(
            """{"type":"blockquote","content":[""" + Paragraph("quoted") + "]},"
            + """{"type":"columnBlock","content":[{"type":"column","content":[""" + Paragraph("left") + """]},{"type":"column","content":[""" + Paragraph("right") + "]}]}");

        Assert.Equal("quoted\nleft\nright", ReadOnlyDocument.PlainText(json));
    }

    [Fact]
    public void WhatIsNotKnownIsLeftOut()
    {
        var json = Doc("""{"type":"somethingNew","attrs":{"x":1}},""" + Paragraph("kept") + """,{"type":"image","attrs":{"src":"heracles-image://local/abc"}}""");

        Assert.Equal("kept", ReadOnlyDocument.PlainText(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"type":"paragraph"}""")]
    [InlineData("""{"type":"doc"}""")]
    public void WhatIsNotADocumentIsNoText(string json)
    {
        Assert.Equal("", ReadOnlyDocument.PlainText(json));
    }
}

public sealed class TabDragTests
{
    [Fact]
    public void ATabBeingDraggedIsCarriedAsText()
    {
        var drag = new TabDrag("pane-1", "note-9");

        Assert.Equal("heracles-tab:pane-1|note-9", drag.Text);
        Assert.Equal(drag, TabDrag.Parse(drag.Text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pane-1|note-9")]
    [InlineData("heracles-tab:only-one-part")]
    [InlineData("heracles-tab:|note")]
    [InlineData("heracles-tab:pane|")]
    public void OtherTextIsNotATab(string? text)
    {
        Assert.Null(TabDrag.Parse(text));
    }
}

public sealed class SidebarRowsTests
{
    private static Note Note(
        string id, NoteKind kind = NoteKind.Page, string? parent = null, long at = 0,
        bool favorited = false, bool archived = false, bool trashed = false, string? title = null) =>
        new(
            Id: id, Kind: kind, ParentId: parent, Title: title ?? id, Icon: null, Position: at,
            Favorited: favorited, Archived: archived, Trashed: trashed,
            CreatedAt: "2026-01-01T00:00:00.000Z", UpdatedAt: "2026-01-01T00:00:00.000Z");

    private static readonly IReadOnlySet<string> NothingCollapsed = new HashSet<string>();

    private static string Outline(IEnumerable<SidebarRow> rows) =>
        string.Join("\n", rows.Select(row => new string(' ', row.Depth * 2) + (row.IsHeader ? "# " : "") + row.Key));

    private static Library Sample() => new(
    [
        Note("nb", NoteKind.Notebook, title: "Work"),
        Note("plans", NoteKind.Section, parent: "nb", at: 0),
        Note("trip", parent: "plans", at: 0, favorited: true),
        Note("budget", parent: "plans", at: 1),
        Note("old", parent: "nb", archived: true),
        Note("scrap", parent: "nb", trashed: true),
    ]);

    [Fact]
    public void TheRowsComeInTheOrderTheSidebarShows()
    {
        var rows = SidebarRows.For(Sample(), NothingCollapsed);

        Assert.Equal(
            """
            # section:favorites
              favorite:trip
            # notebook:nb
              note:plans
                note:trip
                note:budget
            # section:archive
              archived:old
            # section:trash
              trashed:scrap
            """.ReplaceLineEndings("\n"),
            Outline(rows));
    }

    [Fact]
    public void SectionsWithNothingInThemAreLeftOut()
    {
        var rows = SidebarRows.For(new Library([Note("nb", NoteKind.Notebook), Note("page", parent: "nb")]), NothingCollapsed);

        Assert.Equal("# notebook:nb\n  note:page", Outline(rows));
    }

    [Fact]
    public void ACollapsedNoteHidesWhatIsUnderIt()
    {
        var rows = SidebarRows.For(Sample(), new HashSet<string> { "plans" });

        Assert.DoesNotContain(rows, row => row.Key is "note:trip" or "note:budget");
        var plans = Assert.Single(rows, row => row.Key == "note:plans");
        Assert.True(plans.HasChildren);
        Assert.False(plans.IsExpanded);
        // The favorite is still listed where favorites are.
        Assert.Contains(rows, row => row.Key == "favorite:trip");
    }

    [Fact]
    public void ARowSaysWhetherItCanBeOpenedOut()
    {
        var rows = SidebarRows.For(Sample(), NothingCollapsed);

        Assert.True(Assert.Single(rows, row => row.Key == "note:plans").IsExpanded);
        Assert.False(Assert.Single(rows, row => row.Key == "note:budget").HasChildren);
    }

    [Fact]
    public void TitlesAreShownAsTheyAreNamedElsewhere()
    {
        var longTitle = new string('x', 300);
        var library = new Library(
        [
            Note("nb", NoteKind.Notebook, title: "Work"),
            Note("empty", parent: "nb", at: 0, title: ""),
            Note("long", parent: "nb", at: 1, title: longTitle),
        ]);

        var rows = SidebarRows.For(library, NothingCollapsed);

        Assert.Equal("Work", Assert.Single(rows, row => row.Key == "notebook:nb").Title);
        Assert.Equal("Untitled", Assert.Single(rows, row => row.Key == "note:empty").Title);
        // Kept whole here: cutting it to fit is the view's business.
        Assert.Equal(longTitle, Assert.Single(rows, row => row.Key == "note:long").Title);
    }

    [Fact]
    public void TheRowOfANoteIsFoundByItsId()
    {
        var rows = SidebarRows.For(Sample(), NothingCollapsed);

        // The tree's row, not the favorite's: that is the one the sidebar highlights.
        Assert.Equal("note:trip", SidebarRows.RowFor(rows, "trip")?.Key);
        Assert.Equal("archived:old", SidebarRows.RowFor(rows, "old")?.Key);
        Assert.Null(SidebarRows.RowFor(rows, "missing"));
        Assert.Null(SidebarRows.RowFor(rows, null));
    }
}

public sealed class PaneSharesTests
{
    private static void Same(double[] expected, IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var index = 0; index < expected.Length; index++) Assert.Equal(expected[index], actual[index], 6);
    }

    [Fact]
    public void SharesWithRoomAreLeftAsTheyAre()
    {
        Same([0.5, 0.5], PaneHost.Shares([0.5, 0.5]));
        Same([0.3, 0.7], PaneHost.Shares([0.3, 0.7]));
    }

    [Fact]
    public void NoPaneIsDraggedSmallerThanTheSmallestShare()
    {
        Same([0.12, 0.88], PaneHost.Shares([0.05, 0.95]));
        Same([0.88, 0.12], PaneHost.Shares([1.0, 0.0]));
    }

    [Fact]
    public void WhatSmallPanesAreGivenComesFromTheOnesWithRoom()
    {
        var shares = PaneHost.Shares([0.02, 0.03, 0.95]);

        Same([0.12, 0.12, 0.76], shares);
        Assert.Equal(1.0, shares.Sum(), 6);
    }

    [Fact]
    public void TooManyPanesForTheRuleKeepWhatTheyHave()
    {
        var nine = Enumerable.Repeat(1.0 / 9, 9).ToArray();

        Same(nine, PaneHost.Shares(nine));
    }
}
