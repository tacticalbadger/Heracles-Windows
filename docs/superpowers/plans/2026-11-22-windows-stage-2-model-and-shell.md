# Heracles for Windows, Stage 2: Model and Shell — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows app you can browse a library with: sidebar, panes and tabs, several windows kept in step, Quick Open and the note actions, with a note's content shown as read-only text until the editor exists.

**Architecture:** The Mac app's model layer is translated to C# file for file (`AppLink`, `Library`, `PaneTree`, `AppModel`, `Workspace`), each with the Mac's tests translated first. One `AppModel` per process holds the library and follows the core's change feed; each window has a `Workspace` with an immutable `PaneTree`. Views are WPF, built in code where they are recursive (panes) and in XAML where they are fixed (the window).

**Tech Stack:** C#, WPF on .NET 10, xunit, `heracles-core` through the generated bindings, `Emoji.Wpf` 0.3.4 for colour emoji in icons.

**Spec:** `docs/superpowers/specs/2026-11-22-native-windows-app-design.md` (stage 2 of "Order of work"). `CLAUDE.md` holds the decisions and the spike findings this plan relies on.

## Global Constraints

- The Mac app at `..\Heracles` (commit `5dd0252`) is the behavioural reference. Each C# type keeps the Mac type's name and members where C# allows. Mac sources: `macos/Sources/HeraclesApp/`; Mac tests: `macos/Tests/HeraclesAppTests/`.
- "Translate the Mac tests" means: one C# test per Swift `@Test`, the same name in PascalCase, the same inputs and expected values. No Mac test is dropped without a line in the ledger saying why.
- Commit locally at the end of each task, as `tacticalbadger`, message ending `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Do not push.
- Update `CLAUDE.md` ("Layout", "Parity", "Current state") in every commit.
- Never run the app on the real library in a test: `tools\e2e\launch.ps1` only. Testing aids (`--e2e-seed`) do nothing unless `HERACLES_DATA_DIR` is set.
- The owner may be at the PC: drive the app with `ui.ps1 invoke|select|setvalue|expand`, not real clicks or keys, and do not write to the clipboard in a test.
- Build output is captured through a child shell (`cmd /c "powershell -File tools\build.ps1 … > log 2>&1"`). After an edit that seems to have had no effect, suspect the clock and rebuild with `tools\build.ps1 clean`.
- Namespaces: `Heracles.Model`, `Heracles.Workspaces`, `Heracles.Sidebar`, `Heracles.Notes`, `Heracles.Support`. The generated types are `Heracles.Core`.
- Shortcuts are the Mac's with Ctrl for Command. Light and dark follow Windows.
- Colour emoji come from `Emoji.Wpf` in icons and read-only text (owner's decision). Nothing in this stage edits text with emoji in it.

## Review Focus

1. **A layout saved by an earlier session names notes that are gone, or the file is damaged.** Expected: the window opens with what still exists; a damaged file is ignored, not fatal. Task 6.
2. **A note open in two windows is deleted in one.** Expected: its tab leaves the other window at once. Tasks 4, 5, 8.
3. **A second copy of the app is started while one runs**, with or without a link. Expected: no second process stays; the running app comes forward and follows the link. Task 6.
4. **A title that is empty, very long, or contains line breaks.** Expected: "Untitled" for empty; one line, cut with an ellipsis, in the sidebar, tabs and Quick Open. Task 7.
5. **A change arrives from the core on another thread while a window is closing.** Expected: no exception; the change is applied or dropped. Task 4.

---

### Task 1: Links (`Model/Navigation.cs`)

**Files:** Create `app/Heracles/Model/Navigation.cs`; Test `app/Heracles.Tests/NavigationTests.cs`.

**Interfaces:**
- Produces: `enum NavigationDecision { OpenExternally, OpenInApp, Deny }`; `static class NavigationPolicy { static NavigationDecision Decide(Uri? url); static NavigationDecision Decide(string? text); }`; `sealed record AppLink(string NoteId, string? BlockId) { const string Scheme = "heracles"; static AppLink? Parse(Uri url); static AppLink? Parse(string? text); string Url { get; } }`.

- [ ] **Step 1:** Translate `NavigationTests.swift` (both suites, 7 tests) into `NavigationTests.cs`. Add: `Decide("not a url")` and `Decide("")` are `Deny`; `AppLink.Parse("heracles://note/abc#block=")` has `BlockId == null`.
- [ ] **Step 2:** Run `dotnet test`; expected: does not compile (`AppLink` missing).
- [ ] **Step 3:** Implement. `Decide(string)` parses with `Uri.TryCreate(text, UriKind.Absolute, …)`. A note link is scheme `heracles`, host `note`, exactly one non-empty path segment; the block comes from a fragment that starts `block=`. `Url` is `heracles://note/<id>` with `#block=<id>` when there is one.
- [ ] **Step 4:** Run `tools\build.ps1 test`; expected: all pass. Commit: `Links: AppLink and the navigation policy`.

### Task 2: The library as the sidebar shows it (`Model/Library.cs`)

**Files:** Create `app/Heracles/Model/Library.cs`; Test `app/Heracles.Tests/LibraryTests.cs`.

**Interfaces:**
- Produces: `sealed record LibraryNode(Note Note, IReadOnlyList<LibraryNode> Children) { string Id }`; `sealed class Library { Library(IEnumerable<Note> notes); IReadOnlyList<LibraryNode> Notebooks; IReadOnlyList<Note> Favorites, Archived, Trashed; Note? Note(string id); IReadOnlyList<Note> Path(string id); Note? Notebook(string containingId); }`; `static class NoteExtensions { static string DisplayTitle(this Note note); }` ("Untitled" when the title is empty or only white space; line breaks become spaces).

- [ ] **Step 1:** Translate `LibraryTests.swift` (7 tests). Add `DisplayTitle` tests: `""` and `"  "` give `Untitled`; `"a\r\nb"` gives `a b`.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement as `Library.swift` does: children by parent among notes neither trashed nor archived, sorted by position, then title (case-insensitive, current culture), then id; built from the notebooks down; favorites and archive by title; trash is the top of each trashed subtree, newest `UpdatedAt` first; `Path` stops on a loop.
- [ ] **Step 4:** Run; all pass. Commit: `Library: the note tree as the sidebar shows it`.

### Task 3: Panes as a value (`Workspaces/PaneTree.cs`)

**Files:** Create `app/Heracles/Workspaces/PaneTree.cs`; Test `app/Heracles.Tests/PaneTreeTests.cs`.

**Interfaces:**
- Produces:
  - `enum SplitAxis { Horizontal, Vertical }`, `enum SplitTarget { Right, Down, Left, Up }`.
  - `abstract record PaneNode { abstract string Id }`; `sealed record PaneLeaf(string Id, ImmutableArray<string> Tabs, string? ActiveTab) : PaneNode`; `sealed record PaneSplit(string Id, SplitAxis Axis, ImmutableArray<PaneNode> Children, ImmutableArray<double> Ratios) : PaneNode`.
  - `sealed class PaneTree` — **immutable**: every change returns a new tree. `PaneTree()`; `PaneNode Root`; `string FocusedLeafId`; `IReadOnlyList<PaneLeaf> Leaves`; `PaneLeaf? Leaf(string id)`; `string? ActiveNoteId`; `PaneTree Focus(string leafId)`; `PaneTree Open(string noteId, string? leafId = null)`; `PaneTree Activate(string noteId, string leafId)`; `PaneTree CloseTab(string noteId, string leafId)`; `PaneTree Split(string leafId, SplitTarget target, string? noteId, out string newLeafId)`; `PaneTree CloseLeaf(string leafId)`; `PaneTree SetRatios(string splitId, IReadOnlyList<double> ratios)`; `PaneTree Remove(IReadOnlySet<string> notes)`; `PaneTree Keep(IReadOnlySet<string> notes)`; `PaneTree MoveTab(string noteId, string sourceId, string destinationId, int index)`; `string ToJson()`; `static PaneTree? FromJson(string? json)` (null for anything that is not a valid tree).

- [ ] **Step 1:** Translate `PaneTreeTests.swift` (13 tests) with its `shape` helper. The save-and-restore test compares `ToJson()` of both. Add: `FromJson` of `""`, `"not json"`, `"{}"` and a tree whose focused leaf does not exist is `null` for the first three and a tree with a repaired focus for the last.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement as `PaneTree.swift` does, including: a closed active tab hands over to the tab that slides into its place, else the last; a split left with one child is replaced by it; ratios renormalised after pruning; the last pane is emptied, never removed; out-of-range `index` in `MoveTab` means the end. JSON: `{"focused":"<id>","root":<node>}`, a leaf `{"leaf":{"id","tabs","active"}}`, a split `{"split":{"id","axis":"h"|"v","children":[…],"ratios":[…]}}`.
- [ ] **Step 4:** Run; all pass. Commit: `Panes: the pane tree as an immutable value`.

### Task 4: The app's one model (`Model/AppModel.cs`)

**Files:** Create `app/Heracles/Model/AppModel.cs`; Test `app/Heracles.Tests/AppModelTests.cs`, `app/Heracles.Tests/TestLibrary.cs` (a temporary folder with a `Store`, disposed after).

**Interfaces:**
- Consumes: `Library` (Task 2), `AppLink` (Task 1).
- Produces: `readonly record struct DocumentChange(long Revision, string? Origin)`; `sealed class AppModel : IDisposable { AppModel(Store store, Action<Action>? post = null); Store Store; Library Library; string? LastError; AppLink? PendingLink; event Action? Changed; event Action? ErrorChanged; event Action? PendingLinkChanged; Action? AfterChange; DocumentChange? DocumentChangeFor(string noteId); int BoardVersion(string todoListId); T? Attempt<T>(Func<T> work); bool Attempt(Action work); Note? CreateNote(NoteKind kind, string? parentId, string? title = null); void Rename(string id, string title); void SetIcon(string id, string? icon); void SetFavorited(string id, bool value); void SetArchived(string id, bool value); void Trash(string id); void Restore(string id); IReadOnlyList<string> Delete(string id); IReadOnlyList<string> EmptyTrash(); void Move(string id, string? parentId, string? beforeId = null); Note? Duplicate(string id); StoredDocument? Document(string noteId); void SaveDocument(string noteId, string content, string? origin); IReadOnlyList<SearchHit> Search(string query); }`.
- `post` is how a change made on another thread reaches the thread that owns the model. Default: the `SynchronizationContext` current at construction; with none, run in place. A change made on the owning thread is applied before the call that made it returns.

- [ ] **Step 1:** Translate `AppModelTests.swift` (9 tests). The other-thread test passes a `post` that queues: after a write on another thread the library does **not** yet hold the note; after running the queue it does. Add: after `Dispose`, a write to the store raises no event and throws nothing (Review Focus 5).
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement as `AppModel.swift` does. `LastError` is the exception's `Message`; a success clears it. `Search` asks for 50 hits and returns none on error. `Dispose` unsubscribes from the store.
- [ ] **Step 4:** Run; all pass. Commit: `AppModel: one view of the library, kept current by the change feed`.

### Task 5: One window's state (`Workspaces/Workspace.cs`)

**Files:** Create `app/Heracles/Workspaces/Workspace.cs`; Test `app/Heracles.Tests/WorkspaceTests.cs`.

**Interfaces:**
- Consumes: `AppModel`, `PaneTree`, `AppLink`.
- Produces: `sealed class Workspace { Workspace(AppModel model); AppModel Model; PaneTree Tree { get; set; } (set raises TreeChanged when the JSON differs); event Action? TreeChanged; bool IsSwitcherShown { get; set; } event Action? SwitcherChanged; Note? ActiveNote; void Open(string noteId, SplitTarget? inSplit = null); void CloseActiveTab(); void SplitFocused(SplitTarget target); void LibraryChanged(); void Follow(AppLink link); string? BlockToReveal(string noteId); void DidReveal(string noteId); Note? CreateNote(NoteKind kind); IReadOnlyList<Note> ImportFiles(IEnumerable<string> paths); bool ExportActiveNote(string destination); IReadOnlyList<Note> SwitcherResults(string query); void TrashActive(); void DuplicateActive(); void ToggleFavoriteActive(); string? LinkToActive(); }`.

- [ ] **Step 1:** Translate `WorkspaceTests.swift` (8 tests). Add: `TreeChanged` fires once per change and not when a change leaves the tree as it was.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement as `Workspace.swift` does. A new note goes inside the open note, beside it when that is a todo list, in the first notebook when nothing is open or the open note is in the trash. `SwitcherResults`: 20 most recently changed notes that are not trashed and not notebooks when the query is blank; else search hits that are not notebooks. File dialogs and the clipboard are the window's business, not this class's.
- [ ] **Step 4:** Run; all pass. Commit: `Workspace: one window's panes and note actions`.

### Task 6: One process, many windows

**Files:** Create `app/Heracles/SingleInstance.cs`, `app/Heracles/Support/WindowStates.cs`; Modify `app/Heracles/App.xaml.cs`; Test `app/Heracles.Tests/SingleInstanceTests.cs`, `app/Heracles.Tests/WindowStatesTests.cs`.

**Interfaces:**
- Produces:
  - `sealed record WindowState(double Left, double Top, double Width, double Height, bool Maximized, string? Panes)`; `static class WindowStates { static IReadOnlyList<WindowState> Load(string path); static void Save(string path, IEnumerable<WindowState> states); static string Serialize(IEnumerable<WindowState>); static IReadOnlyList<WindowState> Deserialize(string? json); }` — `Deserialize` of anything that is not the expected JSON is an empty list; sizes below 400 by 300 are raised to that.
  - `sealed class SingleInstance : IDisposable { static SingleInstance? Claim(string name, Action<string[]> onSecondLaunch); static bool Send(string name, string[] args, TimeSpan timeout); static string Encode(string[] args); static string[] Decode(string line); }` — `Claim` returns null when another process holds the name. The name includes the data folder, so a test run on a fixture never talks to the owner's running app.
  - `App`: `internal static AppModel? Model`; `internal MainWindow OpenWindow(WindowState? state = null, string? noteId = null)`; arguments: a `heracles://` link, `--new-window`, `--e2e-seed`.
  - `AppPaths.WindowsPath` = `<data folder>\windows.json`.
- Log lines: `instance: first`, `instance: handed over to the running app`, `instance: second launch: <args>`, `window: opened (<n> open)`, `window: closed (<n> open)`.

- [ ] **Step 1:** Write the failing tests. `WindowStatesTests`: a round trip of two states; `Deserialize` of `null`, `""`, `"[]"`, `"{"`, `"[{\"Width\":\"x\"}]"` gives an empty list; a state 100 by 50 comes back 400 by 300. `SingleInstanceTests`: `Decode(Encode(args))` returns `args` for `[]`, `["--new-window"]`, and a link with spaces and quotes in it; `Claim` twice with one name gives a holder then `null`; `Send` to a holder delivers the arguments to its callback; `Send` to a name nobody holds returns `false` within the timeout.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement. A named mutex decides who is first; a named pipe (`heracles-<hash of the name>`), current user only, carries one line of arguments per connection. `App.OnStartup`: try `Claim`; if taken, `Send` and exit with code 0; if the send fails, log it and exit 1. The callback is marshalled to the dispatcher: a link sets `Model.PendingLink` and brings the frontmost window forward; `--new-window` opens one; nothing brings the frontmost window forward. Windows are restored from `windows.json` (none saved: one default window); the file is rewritten whenever a window closes or the app exits.
- [ ] **Step 4:** Run `tools\build.ps1 test`; all pass.
- [ ] **Step 5:** Through the harness: launch; launch again with `--new-window`; expected: one `Heracles` process from the build, `ui.ps1 windows` lists two, the log has `instance: handed over` once. Close both through automation; relaunch; expected: two windows again. Then put `not json` in `build\e2e\data\windows.json`, relaunch; expected: one window, no error (Review Focus 1).
- [ ] **Step 6:** Commit: `One process, many windows: single instance and remembered layouts`.

### Task 7: The window (sidebar, panes, tabs, Quick Open)

**Files:** Create `app/Heracles/Sidebar/SidebarView.cs`, `Sidebar/NoteIcon.cs`, `Sidebar/SwitcherView.cs`, `Workspaces/PaneNodeView.cs`, `Workspaces/PaneLeafView.cs`, `Workspaces/TabDrag.cs`, `Notes/NoteDetailView.cs`, `Notes/IconPicker.cs`, `Notes/ReadOnlyDocument.cs`, `Support/Prompts.cs`, `Support/E2eSeed.cs`; Modify `MainWindow.xaml(.cs)`, `Heracles.csproj` (`Emoji.Wpf` 0.3.4), `tools/build.ps1` (the package's required files); Test `app/Heracles.Tests/ReadOnlyDocumentTests.cs`, `TabDragTests.cs`, `SidebarRowsTests.cs`.

**Interfaces:**
- Consumes: everything above.
- Produces (the parts with logic, each tested without a window):
  - `static class ReadOnlyDocument { static string PlainText(string documentJson); }` — one line per block, list items indented by depth with `• `, `1. ` or `☐ `/`☑ `, a code block's text as it is, unknown blocks skipped. `""` for anything that is not a document.
  - `sealed record TabDrag(string LeafId, string NoteId) { string Text; static TabDrag? Parse(string? text); }` — prefix `heracles-tab:`, as on the Mac.
  - `static class SidebarRows { static IReadOnlyList<SidebarRow> For(Library library); }` with `sealed record SidebarRow(string Key, string Title, Note? Note, int Depth, bool IsHeader)`: the order the sidebar shows — Favorites header and its notes (only when there are any), each notebook as a header with its tree under it, Archive, Trash.
- Views, with automation ids: `Sidebar` (tree; items `note:<id>`, headers `section:favorites|archive|trash`, `notebook:<id>`), `SidebarSearch`, `SearchResults` (items `hit:<id>`), `NewMenu`, `Pane:<leafId>`, `Tab:<leafId>:<noteId>`, `TabClose:<leafId>:<noteId>`, `NoteTitle:<leafId>`, `NoteIcon:<leafId>`, `NoteText:<leafId>`, `EmptyPane:<leafId>`, `Switcher`, `SwitcherQuery`, `SwitcherResult:<noteId>`, `PromptInput`, `PromptOk`, `PromptCancel`, `ErrorText`, `ErrorOk`; menu items by name.
- Behaviour, all as the Mac views: the sidebar highlights the note in the focused pane; choosing one opens it there, Ctrl-click opens it in a new pane to the right; context menus as `SidebarView.swift` (notebook menu, note menu, trash menu); search replaces the tree with hits and their snippets; tab strip with close buttons, a context menu (Split Right, Split Down, Close Tab, Close Other Tabs, Close Pane), tabs draggable within and between panes; dividers drag, no pane below 12 % of its split; the focused pane's strip is tinted when there is more than one pane; an empty pane says "No Note Open — Choose a note in the sidebar, or press Ctrl+K to find one."; Quick Open is an overlay with a query box, up to 320 px of results with each note's path, arrows move, Enter opens, Ctrl+Enter opens to the right, Escape or a click outside closes; the note view has the icon button (opening the icon picker with the Mac's four groups, a "type one" box and Remove), the title box (committed on Enter and on losing focus; disabled in the trash), and under it the note's text read-only (a todo list says "Boards arrive in a later version."); an error from the model shows a dialog with its text.
- Menu bar and shortcuts: File (New Page Ctrl+N, New Section Ctrl+Shift+N, New To-Do List Ctrl+Shift+T, New Notebook, Quick Open Ctrl+K, Close Tab Ctrl+W, Exit), Note (Add to/Remove from Favorites Ctrl+Shift+F, Duplicate Ctrl+D, Copy Link Ctrl+Shift+C, Move to Trash), View (Split Right Ctrl+\, Split Down Ctrl+Shift+\), Window (New Window), Help (About Heracles: version and build).
- `--e2e-seed`, only with `HERACLES_DATA_DIR` set and only in an empty library: a section "Plans" holding pages "Trip" (icon 🧭, favorited, text "Pack the lantern.") and "Budget", a todo list "Packing", an archived page "Old ideas", a trashed page "Scrap", and a page with an empty title. Logs `seed: created <n> notes`.

- [ ] **Step 1:** Write the failing tests for `ReadOnlyDocument` (a paragraph; two paragraphs; a heading; a bullet list two deep; a numbered list; a task list checked and not; a code block from `attrs.code`; an unknown block skipped; `"not json"` gives `""`), `TabDrag` (round trip; no prefix; one part), and `SidebarRows` (the order above from a library with one favorite, one notebook with a section and two pages, one archived and one trashed note; no Favorites header without favorites; an empty title shows as `Untitled`; a 300-character title is kept whole in the row, the view cuts it).
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement the three, then the views.
- [ ] **Step 4:** Run `tools\build.ps1 test`; all pass. Build the app.
- [ ] **Step 5:** Through the harness on a seeded fixture (`launch.ps1 --e2e-seed`), each checked by automation id or the log:
  1. The sidebar lists Favorites › Trip, the notebook, Plans › Trip and Budget, Packing, Archive › Old ideas, Trash › Scrap, and an `Untitled` row.
  2. `select` `note:<Trip>` opens a tab; `NoteText` holds `Pack the lantern.`; the window title is `Trip`.
  3. `invoke` Split Right; two `Pane:` elements; `select` Budget; the right pane shows it.
  4. `invoke` `TabClose` on Budget; one pane again.
  5. `setvalue SidebarSearch` to `lant`; `hit:<Trip>` appears; clear it; the tree is back.
  6. `invoke` Quick Open; `setvalue SwitcherQuery` to `bud`; `SwitcherResult:<Budget>` exists; `select` it; Budget is open.
  7. `setvalue NoteTitle` to `Budget 2027` and move focus; the sidebar row reads `Budget 2027`.
  8. `invoke` Move to Trash on the open note; its tab is gone; it is under Trash.
  9. Open a second window (`--new-window`); open Trip in both; rename it in one; the other's tab and sidebar row change (Review Focus 2). Delete Scrap for good in one; nothing breaks in the other.
  10. `capture.ps1`; look at the picture in light and in dark: text is legible, the emoji icon is in colour, long and empty titles are cut and named properly (Review Focus 4).
- [ ] **Step 6:** Update `README.md` (Status, what the app does now) and `CLAUDE.md`. Commit: `The window: sidebar, panes and tabs, Quick Open, note actions`.

### Task 8: Verify the stage

- [ ] **Step 1:** `tools\build.ps1 clean`, `tools\build.ps1`, `tools\build.ps1 test`; expected: Rust 203 passed, .NET all passed, no warnings.
- [ ] **Step 2:** `tools\build.ps1 core package` and `-Arch x64`; start each on a seeded fixture; the sidebar is there in both.
- [ ] **Step 3:** Tick what is now true in `CLAUDE.md` "Parity"; list under "Not verified" what was not driven (dragging tabs and dividers with a real mouse, the menus' shortcuts with real keys). Commit: `Stage 2 verified: packages for both processors, notes updated`.
