# Heracles for Windows, Stage 4: The Editor's Views — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A note can be written in the Windows app: the Mac's block editor, with its behaviour and its tests, over the document model of stage 3.

**Architecture:** The Mac's three layers, file for file under `app/Heracles/Editor/`: `BlockEditorController` (the document, the selection, undo, every command), `BlockEditorView` (the page: block views stacked, and what floats over them) with `BlockViews`, and `BlockTextBox` (one block's text, a `RichTextBox`). `Notes/EditorCoordinator` is the app's end: loading, saving, images, links. The document is the truth; a text box holds one block's text while it is typed in and every change is copied into the document.

**Tech Stack:** C#, .NET 10, WPF, xunit on one STA thread, `Emoji.Wpf` 0.3.4.

**Spec:** `docs/superpowers/specs/2026-11-22-native-windows-app-design.md` ("The editor", "Windows conventions", "Testing"; stage 4 of "Order of work"). Read with `CLAUDE.md`, "Spike findings".

## Global Constraints

- The Mac sources at `..\Heracles` (commit `5dd0252`) decide behaviour: `Editor/BlockEditorController.swift`, `BlockEditorView.swift`, `BlockViews.swift`, `BlockTextView.swift`, `EditorTheme.swift`, `EditorCoordinator.swift`, and `Tests/HeraclesAppTests/EditorControllerTests.swift` (59 tests). One C# test per Swift `@Test` where it can be translated; the ledger says why for each that cannot.
- **One text box, where the caret is** (`CLAUDE.md`, "Views with no window"). Every other block's text is a `TextBlock` with the same runs and emoji in colour. The two must measure the same height at every width: the same line height (`BlockLineHeight`), and 5 px of padding at each side of both.
- **Typing is learned by a diff anchored at the caret** ("Offsets and marks"). The editor holds the typing style itself. After each change it applies the diff to the document and, if the box shows the text otherwise than the document says, redraws the paragraph's inlines and puts the caret back. Nothing is redrawn while an input method is composing.
- A text box's document is always one `Paragraph` of `Run`s and `LineBreak`s. Enter, Shift+Enter, Tab, paste, and every formatting command of the `RichTextBox`'s own are taken by the editor, so WPF never adds a paragraph, a list, or a mark the model does not have. Dragging text out of or into a box is off.
- **Tests never show a window, never call `Focus()` on a shown element, and never touch the system clipboard.** Views are laid out by hand with no window; the controller keeps its own record of the focused text box; copy and paste go through `IDataObject`, and only the glue in the views reaches `Clipboard`.
- Keys are the Mac's with Ctrl for Command and Alt for Option. Enter splits; Shift+Enter is a line break; in code Enter is a new line that keeps the indent and Ctrl+Enter leaves the block; Ctrl+Home and Ctrl+End are the Mac's start and end of document; Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z undo and redo.
- Sizes are the Mac's (`EditorTheme.swift`), a point taken as a device-independent pixel. Colours come from the Fluent theme and must read in light and dark.
- The model files of stage 3 (`JsonValue`, `RichText`, `EditorBlock`, `EditorSelection`, `EditorDocument.*`) still name no `System.Windows` type.
- Commit locally at the end of each task, as `tacticalbadger`, message ending `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Do not push. Update `CLAUDE.md` in every commit. Builds use `tools\build.ps1` (the clock).

## Review Focus

1. **A click on static text.** Expected: the text box takes the block's place without the page moving, and the caret is where the click was. No test can click; the tests call what the mouse handler calls, and Task 6 looks at it on screen.
2. **An input method composing.** Expected: nothing is redrawn under it, and its finished text reaches the document once. Cannot be scripted: the code path is kept small, and it stays under "Not verified".
3. **A note saved elsewhere while this editor has unsaved typing.** Expected: the typing is kept and saved, and the other editor follows. Task 5.
4. **Quitting, or closing a tab, within the 300 ms before a save.** Expected: nothing typed is lost. Task 5.
5. **A view asked about a block that has just gone** (a reload, an undo, another window's save, while a handler is still running). Expected: nothing throws. Tasks 2 to 4.

---

### Task 1: Test support for views, the theme, and the text box

**Files:** Create `app/Heracles/Editor/EditorTheme.cs`, `app/Heracles/Editor/BlockTextBox.cs`; Test `app/Heracles.Tests/Editor/ViewTestSupport.cs`, `EditorThemeTests.cs`, `BlockTextBoxTests.cs`.

**Interfaces (produces):**
- Tests: `Sta.Run(Action test)` (one STA thread for every view test); `Views.Pump()` (run the dispatcher's Background work); `Views.Layout(FrameworkElement root, double width = 800, double height = 600)`.
- `static class EditorTheme`: the Mac's constants (`BodySize` 15, `CodeSize` 13, `ContentWidth` 720, `Gutter` 60, `Indent` 26, `BlockSpacing` 5, `ColumnGap` 40, `TopPadding` 8, `BottomPadding` 140) and `TextPadding` 5; `EditorFont FontFor(Block block)` (`Family`, `Size`, `Weight`); `double LineHeight(Block block)`; `double TopMargin(Block block)`; `IEnumerable<Inline> Inlines(Block block, bool emoji)` (each `Run` carries its `TextStyle` in the attached property `EditorTheme.StyleProperty`, an unknown inline node in `AtomProperty`); `RichText RichTextFrom(Paragraph paragraph, bool isCode = false)`; `TextBlock StaticText(Block block)` and `void Fill(TextBlock text, Block block)`; the brushes `Text`, `Secondary`, `Tertiary`, `Panel`, `Rule`, `Selection`, `Link`, `InlineCode`.
- `sealed class BlockTextBox : RichTextBox`: `string BlockId`; `bool IsCode`; `string? Placeholder`; `void Show(Block block)`; `RichText Read()`; `string Text`; `int Length`; `(int Start, int End) SelectedRange`; `void Select(int start, int end)`; `TextPointer PointerAt(int offset)`; `int OffsetOf(TextPointer pointer)`; `bool CaretIsOnFirstLine`, `CaretIsOnLastLine`; `double CaretX`; `int OffsetNearest(double x, bool onFirstLine)`; `int OffsetAt(Point point)`; `bool IsComposing`; `Func<Key, ModifierKeys, bool>? KeyHandler`.

- [ ] **Step 1:** Tests. `EditorThemeTests`: the Mac's four (`aBlocksTextSurvivesTheTripThroughATextView`, `marksShowAsFontsAndLines`, `fontsAndColorsFromElsewhereAreNotMarks`, `aFinishedTaskIsStruckThrough`), on a `Paragraph`. `BlockTextBoxTests`: every offset of `ab` + bold `cd` + line break + italic `e😀f` round-trips through `PointerAt` and `OffsetOf`; `Select` then `SelectedRange`; the caret's line (first, middle, last of a paragraph that wraps); `OffsetNearest` on the first and last line; a text box and `StaticText` of the same block are the same height (empty, short, wrapping, marks, line breaks, a trailing break, emoji, CJK, one long word, a heading, code; widths 180, 260, 333, 500, 720); text sent by composition arrives after `Pump` and raises `TextChanged`; a box that is not editable takes none.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `tools\build.ps1 app test`; all pass. Commit: `Editor views: the theme and the text box`.

### Task 2: Block views, the page, and an editor that shows a note

**Files:** Create `app/Heracles/Editor/BlockViews.cs`, `BlockEditorView.cs`, `BlockEditorController.cs`; Test `app/Heracles.Tests/Editor/EditorControllerTests.cs` (the harness, and the layout tests).

**Interfaces (produces):**
- `interface IBlockEditorHost { void EditorDidChange(BlockEditorController editor); void EditorDidFocus(BlockEditorController editor); void OpenLink(BlockEditorController editor, string href); string? StoreImage(BlockEditorController editor, byte[] data, string mime); void LoadImage(BlockEditorController editor, string source, Action<ImageSource?> completion); string? DocumentFromHtml(BlockEditorController editor, string html); void CopyLinkToBlock(BlockEditorController editor, string id); }`
- `BlockEditorController(BlockIds? ids = null)`: `EditorDocument Document`; `EditorSelection Selection`; `IBlockEditorHost? Host`; `ScrollViewer ScrollView`; `BlockEditorView Page`; `bool IsEditable`; `bool Load(string json)`; `string Json`; `void Select(EditorSelection selection)`; `BlockTextBox? FocusedText`; `void FocusStart()`, `void FocusEnd()`; `void ScrollToTop()`; `bool Reveal(string blockId)`.
- `abstract class BlockView : FrameworkElement`: `Block Block`; `IReadOnlyList<BlockView> ChildViews`; `void Configure(Block block, int ordinal)`; `virtual BlockTextBox? TextBox`; `virtual double FirstLineHeight`; `double TopMargin`. Subclasses as the Mac's: `TextBlockView` (with `string ShownText`, `BlockTextBox Activate()`, `void Deactivate()`), `CodeBlockView`, `QuoteBlockView`, `CalloutBlockView`, `ToggleBlockView`, `ColumnsBlockView`, `ColumnBlockView`, `DividerBlockView`, `ImageBlockView`, `UnknownBlockView`.
- `BlockEditorView`: `void Reconcile(EditorDocument document)`; `BlockView? BlockViewFor(string id)`; `BlockTextBox? TextBoxFor(string id)` (the live one, or null); `BlockTextBox? EnsureTextBox(string id)`; `Rect? FrameOf(string id)`; `BlockView? BlockViewAt(Point point, ISet<string>? excluding = null, bool nearest = false)`; `DropTarget? DropTargetAt(Point point, IReadOnlyList<string> moving, bool allowColumns = true)`; `IReadOnlyList<string> SelectedBlocks`, `PendingBlocks`; `void Flash(string id)`.
- Tests: `sealed class TestHost : IBlockEditorHost` (records what it is asked); `sealed class EditorHarness(params Block[] blocks)` with `Editor`, `Host`, `Outline`, `Selection`, `Text(id)` (what the block shows), `Focused`, `Caret(id, offset)`, `Type(string)`, `Press(Key key, ModifierKeys modifiers = None)`.

- [ ] **Step 1:** Tests: the Mac's `EditorLayoutTests` (`blocksAreStackedDownThePageInOrder`, `columnsSitSideBySideByTheirWidths`, `theBlockUnderAPointIsFoundByItsRow`, `aCalloutIsFoundAsAWholeFromTheMarginAndItsBlocksFromInside`, `aDropLandsBeforeAfterOrBesideTheBlockUnderIt`, `aLongNoteOpensQuickly`); and for Windows: only the block with the caret has a text box, and moving the caret moves it; giving a block the text box does not change its height or the page's; a view asked for a block that has gone answers null.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement: views and page as the Mac lays them out; of the controller only what shows a note and holds the selection.
- [ ] **Step 4:** `tools\build.ps1 app test`; all pass. Commit: `Editor views: blocks on a page`.

### Task 3: Typing, keys, undo, marks, links, the slash menu

**Files:** Modify `BlockEditorController.cs`, `BlockTextBox.cs`, `BlockEditorView.cs` (the slash menu); Test `EditorControllerTests.cs`.

**Interfaces (produces):** `bool Edit(Func<EditorDocument, BlockIds, EditorSelection?> change, bool undoable = true)`; `bool CanUndo`, `CanRedo`; `void Undo()`, `Redo()`; `void BeginUndoGroup()`; `void InsertText(string text)`; `bool HandleKey(BlockTextBox box, Key key, ModifierKeys modifiers)`; `bool HandleBlockKey(Key key, ModifierKeys modifiers)`; `void Toggle(TextMark mark)`; `void SetLink(string? address)`; `static string? CleanAddress(string address)`; `void EditLink()`; `void TurnCurrentBlock(BlockTarget into)`; `void Apply(EditorFormat format)`; `IReadOnlyList<SlashCommand> SlashCommands`; `void ChooseSlashCommand(int index)`; `void SelectBlocks(IReadOnlyList<string> blocks, bool extending)`; `bool ExtendSelection(string toBlock)`; `void SelectAllBlocks()`; `void DeleteSelectedBlocks()`. `record SlashCommand(string Title, string Detail, string Glyph, BlockTarget Target)` with `All` and `Matching(query)`.

- [ ] **Step 1:** Tests: the Mac's `EditorTypingTests` (12, without the one about the Mac's text engine), `EditorShortcutTests` (12), `EditorNavigationTests` (9). Keys are `Press(Key.Enter)` and so on; a key the editor leaves to the text box is carried out by the matching `EditingCommands` command.
- [ ] **Step 2:** Run; expected: fail (missing members).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `tools\build.ps1 app test`; all pass. Commit: `Editor: typing, keys, undo, marks and the slash menu`.

### Task 4: Block actions, the clipboard, pictures, and the mouse

**Files:** Modify the three editor files; Test `EditorControllerTests.cs`.

**Interfaces (produces):** `void SetChecked(string id, bool isChecked)`; `void SetOpen(string id, bool open)`; `void SetLanguage(string id, string language)`; `void SetEmoji(string id, string emoji)`; `void SetColumnWidths(string id, IReadOnlyList<double> widths)`; `void AddBlock(string after)`; `bool Move(IReadOnlyList<string> blocks, DropTarget target)`; `void Duplicate(string id)`; `void Remove(IReadOnlyList<string> blocks)`; `void Turn(string id, BlockTarget into)`; `const string ClipboardFormat = "com.heracles.notes.blocks"`; `IDataObject? CopySelection()`; `void AddCopiedText(IDataObject data)`; `IDataObject? CutSelection()`; `bool Paste(IDataObject data, bool plainOnly = false)`; `static string? ImageType(string path)`; `bool InsertImages(IReadOnlyList<string> files, DropTarget? target)`; `void ActivateAt(string blockId, Point pointInBlock)` (the first click in static text); the block handle and its menu, the grips between columns, picture files dropped on the page, a drag from one block's text into another.

- [ ] **Step 1:** Tests: the Mac's `EditorBlockActionTests` (6) and `EditorPasteboardTests` (8, each on a `DataObject` of its own); and for Windows: `ActivateAt` puts the caret at the offset under the point; a clipboard in Windows' own HTML form (`Version:`, `StartFragment` markers) is given to the host as its fragment; a bitmap on the clipboard is stored as a PNG.
- [ ] **Step 2:** Run; expected: fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `tools\build.ps1 app test`; all pass. Commit: `Editor: block actions, the clipboard, pictures and the mouse`.

### Task 5: The editor in the app

**Files:** Create `app/Heracles/Notes/EditorCoordinator.cs`; Modify `Notes/NoteDetailView.cs`, `MainWindow.xaml(.cs)` (Edit and Format menus), `App.xaml.cs` (closing and quitting wait for every editor), `Workspaces/PaneLeafView.cs` as needed, `tools/e2e/check-shell.ps1`; Create `tools/e2e/check-editor.ps1`; Test `app/Heracles.Tests/EditorCoordinatorTests.cs`.

**Interfaces (produces):** `EditorCoordinator(AppModel model, Action<Action, TimeSpan>? later = null)`: `BlockEditorController Editor`; `string EditorId`; `Action? OnFocus`; `void Show(string noteId, bool editable, DocumentChange? change)`; `void Reveal(string? blockId, Action done)`; `void Save()`; `string? ShownNoteId`. The page is found by automation as `NoteText:<leafId>` with the note's text as its name, as before, and each block's text as `Block:<id>`.

- [ ] **Step 1:** Tests against a real core in a temporary folder: what is typed is saved after the delay, with the editor's id as its origin; a save from elsewhere replaces the working copy; it does not while this editor has unsaved typing, and this editor's text is what ends up stored; leaving a note saves it at once; `Save` with nothing changed writes nothing; a pasted picture goes into the image store and comes back by `LoadImage`; a link is opened in the app or outside by `NavigationPolicy`.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement, and put the editor in `NoteDetailView` in place of the read-only text.
- [ ] **Step 4:** `tools\build.ps1 app test`, `tools\e2e\check-shell.ps1`, and `tools\e2e\check-editor.ps1` (new: by automation, a note's blocks are there by id, text set in a block is in the stored document after a restart, Undo in the Edit menu takes it back, the Format menu makes a heading). Commit: `The editor in the app: notes can be written`.

### Task 6: Verify the stage

- [ ] **Step 1:** `tools\build.ps1 clean`, `tools\build.ps1`, `tools\build.ps1 test`, both e2e scripts; expected: all pass, no warnings.
- [ ] **Step 2:** `tools\e2e\capture.ps1` of a note holding every kind of block, in dark and in light if both can be had; look at the pictures.
- [ ] **Step 3:** Update `CLAUDE.md` (Layout, the parity checklist, Not verified, Known faults, Current state). Commit: `Stage 4 verified: the editor`.
