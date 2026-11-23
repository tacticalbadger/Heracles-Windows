using Heracles.Model;
using Heracles.Workspaces;

namespace Heracles.Tests;

/// <summary>What the app decides about files going in and out: which are offered, and what one is called.</summary>
public sealed class TransferTests
{
    [Fact]
    public void TheImportDialogOffersEveryKindOfFileTheMacTakes()
    {
        Assert.Equal(["md", "markdown", "mdown", "mkd", "mkdn", "txt", "html", "htm"], Transfer.ImportExtensions);

        // A dialog's filter is pairs: what it is called, then what it matches.
        var parts = Transfer.ImportFilter.Split('|');
        Assert.Equal(0, parts.Length % 2);
        Assert.Equal("*.md;*.markdown;*.mdown;*.mkd;*.mkdn;*.txt;*.html;*.htm", parts[1]);
        Assert.Equal("*.*", parts[^1]);
    }

    [Fact]
    public void ExportIsOfferedAsMarkdown()
    {
        var parts = Transfer.ExportFilter.Split('|');
        Assert.Equal(2, parts.Length);
        Assert.Equal("*.md", parts[1]);
    }

    [Theory]
    [InlineData("Trip", "Trip.md")]
    [InlineData("Trip to the coast, 2026", "Trip to the coast, 2026.md")]
    [InlineData("", "Untitled.md")]
    [InlineData("   ", "Untitled.md")]
    [InlineData("  spaced  ", "spaced.md")]
    [InlineData("Ends with a dot.", "Ends with a dot.md")]
    [InlineData("Ends with dots and spaces. . .", "Ends with dots and spaces.md")]
    [InlineData("...", "Untitled.md")]
    [InlineData("Console", "Console.md")]
    public void AFileIsNamedAfterItsNote(string title, string expected)
    {
        Assert.Equal(expected, Transfer.ExportFileName(title));
    }

    [Fact]
    public void WhatWindowsWillNotHaveInAFileNameIsReplaced()
    {
        // Built by character code, so that none is lost on the way into this file.
        var title = string.Join("x", new[] { '/', (char)92, ':', '*', '?', '"', '<', '>', '|' }.Select(c => c.ToString()));
        Assert.Equal("-x-x-x-x-x-x-x-x-.md", Transfer.ExportFileName(title));
        Assert.Equal("two-lines.md", Transfer.ExportFileName("two" + (char)10 + "lines"));
        Assert.Equal("a-tab.md", Transfer.ExportFileName("a" + (char)9 + "tab"));
    }

    [Theory]
    [InlineData("CON", "_CON.md")]
    [InlineData("con", "_con.md")]
    [InlineData("NUL", "_NUL.md")]
    [InlineData("LPT1", "_LPT1.md")]
    [InlineData("COM9", "_COM9.md")]
    [InlineData("COM1.notes", "_COM1.notes.md")]
    [InlineData("COM10", "COM10.md")]
    [InlineData("AUXILIARY", "AUXILIARY.md")]
    public void ANameWindowsKeepsForADeviceIsMadeAnotherName(string title, string expected)
    {
        Assert.Equal(expected, Transfer.ExportFileName(title));
    }

    [Fact]
    public void AVeryLongTitleIsCutShort()
    {
        var name = Transfer.ExportFileName(new string('x', 300));
        Assert.Equal(new string('x', 120) + ".md", name);
    }

    [Fact]
    public void ACutNeverSplitsACharacterInTwo()
    {
        // An emoji is two units of text; the cut falls in the middle of one.
        var lantern = char.ConvertFromUtf32(0x1F4A1);
        var name = Transfer.ExportFileName(new string('x', 119) + lantern + "tail");
        Assert.Equal(new string('x', 119) + ".md", name);
    }

    [Fact]
    public void ACutDoesNotLeaveADotOrASpaceAtTheEnd()
    {
        var name = Transfer.ExportFileName(new string('x', 118) + ". more");
        Assert.Equal(new string('x', 118) + ".md", name);
    }
}

/// <summary>Files in and out, through the workspace, against a real core.</summary>
public sealed class FileTransferTests : IDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly TestLibrary library = new();
    private readonly AppModel model;
    private readonly Workspace workspace;
    private readonly string folder;

    public FileTransferTests()
    {
        model = new AppModel(library.Store);
        workspace = new Workspace(model);
        folder = Path.Combine(library.Folder, "files");
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        model.Dispose();
        library.Dispose();
    }

    private string Written(string name, string text)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void AFileThatCannotBeReadDoesNotStopTheOthers()
    {
        var first = Written("One.md", "# One\n");
        var missing = Path.Combine(folder, "Missing.md");
        var last = Written("Three.md", "# Three\n");

        var imported = workspace.ImportFiles([first, missing, last]);

        Assert.Equal(["One", "Three"], imported.Select(note => note.Title));
        Assert.Equal(imported[1].Id, workspace.Tree.ActiveNoteId);
        // The user is told which one did not come in.
        Assert.NotNull(model.LastError);
        Assert.Contains("Missing.md", model.LastError);
    }

    [Fact]
    public void WhenEveryFileComesInThereIsNothingToSay()
    {
        model.LastError = "something earlier";
        workspace.ImportFiles([Written("One.md", "# One\n")]);
        Assert.Null(model.LastError);
    }

    [Fact]
    public void ImportingNothingChangesNothing()
    {
        var before = model.Library.Notebooks[0].Children.Count;
        Assert.Empty(workspace.ImportFiles([]));
        Assert.Equal(before, model.Library.Notebooks[0].Children.Count);
        Assert.Null(model.LastError);
    }

    [Fact]
    public void AMarkdownFileBringsThePictureBesideIt()
    {
        File.WriteAllBytes(Path.Combine(folder, "map.png"), Png);
        var page = Written("Trip.md", "# Trip\n\n![Map](map.png)\n");

        var note = Assert.Single(workspace.ImportFiles([page]));

        var content = model.Document(note.Id)?.Content;
        Assert.NotNull(content);
        // The picture is in the image store now, and the note points at it there.
        Assert.Contains(CoreApi.ImageUrlScheme() + "://", content);
        Assert.DoesNotContain("map.png", content);
    }

    [Fact]
    public void ANoteExportedAndImportedAgainSaysTheSame()
    {
        var page = workspace.CreateNote(NoteKind.Page);
        Assert.NotNull(page);
        model.Rename(page.Id, "Trip");
        model.SaveDocument(page.Id, TestLibrary.Document("Pack the lantern."), null);
        var destination = Path.Combine(folder, Transfer.ExportFileName("Trip"));

        Assert.True(workspace.ExportActiveNote(destination));
        var back = Assert.Single(workspace.ImportFiles([destination]));

        Assert.Equal("Trip", back.Title);
        Assert.Contains("Pack the lantern.", model.Document(back.Id)?.Content);
    }

    [Fact]
    public void WithNoNoteOpenThereIsNothingToExport()
    {
        Assert.False(workspace.ExportActiveNote(Path.Combine(folder, "Nothing.md")));
        Assert.False(File.Exists(Path.Combine(folder, "Nothing.md")));
    }

    [Fact]
    public void ANoteThatCannotBeWrittenSaysWhy()
    {
        var page = workspace.CreateNote(NoteKind.Page);
        Assert.NotNull(page);
        // A folder stands where the file should go.
        var taken = Path.Combine(folder, "Taken.md");
        Directory.CreateDirectory(taken);

        Assert.False(workspace.ExportActiveNote(taken));
        Assert.NotNull(model.LastError);
    }
}
