# Heracles for Windows, Stage 3: The Editor's Document Model — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Mac editor's document model in plain C#, with the Mac's tests passing: a note as a tree of blocks, read from and written to the stored JSON, and every edit that changes a note's shape as a function on that model.

**Architecture:** A file-for-file translation of `macos/Sources/HeraclesApp/Editor/` (the model files only: `JSONValue`, `RichText`, `EditorBlock`, `EditorSelection`, `EditorDocument+Tree`, `EditorDocument+Editing`) into `app/Heracles/Editor/`, with no WPF types in it. The Mac's tests are translated first and decide every behaviour.

**Tech Stack:** C#, .NET 10, xunit, `System.Text.Json`.

**Spec:** `docs/superpowers/specs/2026-11-22-native-windows-app-design.md` (stage 3 of "Order of work"; "The editor: the document model").

## Global Constraints

- The Mac sources at `..\Heracles` (commit `5dd0252`) decide behaviour. One C# test per Swift `@Test`, same name in PascalCase, same inputs and expected text. A Mac test that cannot be translated gets a line in the ledger saying why.
- Namespace `Heracles.Editor`. No `System.Windows` type appears in it.
- Text offsets and lengths are UTF-16 units, as on the Mac. A line break inside a block is `\n`.
- How Swift's value types are carried over (these are this plan's decisions):
  - `RichText`, `TextStyle`, `TextLink`, `JsonValue` and `EditorSelection` are **immutable**, with structural equality. A Swift `mutating func` on `RichText` becomes a method that returns a new one.
  - `Block` and `EditorDocument` are **mutable classes**, because the Mac's edits are written as changes in place. Each has `Clone()` (deep) and `SameAs(other)` (structural). Wherever the Swift copies a block or a document by assigning it, the C# calls `Clone()`.
  - `BlockIDs`, passed `inout` on the Mac, is the class `BlockIds`.
  - A Swift `Range<Int>` is a pair of `int start, int end` (end exclusive).
- JSON text is written with keys in alphabetical order, whole numbers without a fraction, and slashes and non-ASCII characters left as they are, so the same value always gives the same text.
- Commit locally at the end of each task, as `tacticalbadger`, message ending `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Do not push. Update `CLAUDE.md` in every commit.
- Builds use `tools\build.ps1` or `--no-incremental` (the clock).

## Review Focus

1. **A stored document with something this editor does not know** (a block type, an attribute, a mark, an inline node). Expected: it survives reading and writing untouched. Tasks 1, 2.
2. **Characters outside the basic plane, and joined emoji**, at the edges of a range. Expected: offsets count UTF-16 units and no operation here splits a pair by itself (the views keep carets off the middle). Task 1.
3. **An edit aimed at a block that is not there** (a stale id from a view). Expected: nothing changes and nothing throws. Task 3.
4. **A document reduced to nothing** by removing or moving its last block. Expected: one empty paragraph remains. Task 3.
5. **A change to a copy must not reach the original** (a duplicate, a snapshot for undo). Expected: `Clone()` is deep; changing the copy leaves the original's outline and JSON as they were. Tasks 2, 3.

---

### Task 1: JSON values and rich text

**Files:** Create `app/Heracles/Editor/JsonValue.cs`, `app/Heracles/Editor/RichText.cs`; Test `app/Heracles.Tests/Editor/RichTextTests.cs` (both Mac suites of `RichTextTests.swift`), `app/Heracles.Tests/Editor/EditorTestSupport.cs` (`Shape(RichText)`).

**Interfaces (produces):**
- `abstract record JsonValue` with `static JsonValue? Parse(string text)`, `string Text`, `JsonValue? this[string key]`, `string? StringValue`, `bool? BoolValue`, `double? DoubleValue`, `int? IntValue`, `IReadOnlyList<JsonValue>? ArrayValue`, `IReadOnlyDictionary<string, JsonValue>? ObjectValue`; cases `JsonValue.Null`, `Bool(bool)`, `Number(double)`, `String(string)`, `Array(...)`, `Object(...)`; factories `JsonValue.Of(...)`.
- `enum TextMark { Bold, Italic, Strike, Code }`; `sealed record TextLink(string Href, JsonValue Extra)`; `sealed record TextStyle(bool Bold, bool Italic, bool Strike, bool Code, TextLink? Link, JsonValue Other)` with `static TextStyle Plain`, `bool Has(TextMark)`, `TextStyle With(TextMark, bool)`.
- `sealed class RichText` (immutable): `RichText()`, `RichText(string text, TextStyle? style = null)`, `static RichText FromInline(IEnumerable<JsonValue> nodes)`, `IReadOnlyList<RichText.Span> Spans`, `bool IsEmpty`, `int Length`, `string String`, `operator +`, `RichText Subtext(int start, int end)`, `RichText Replace(int start, int end, RichText replacement)`, `bool Has(TextMark mark, int start, int end)`, `RichText Set(TextMark mark, bool on, int start, int end)`, `RichText SetLink(TextLink? link, int start, int end)`, `(int Start, int End, TextLink Link)? LinkAt(int offset)`, `TextStyle StyleBefore(int offset)`, `IReadOnlyList<JsonValue> InlineJson`, structural `Equals`. `Span(string Text, TextStyle Style, JsonValue? Atom)`; `const string AtomCharacter = "￼"`.

- [ ] **Step 1:** Translate `RichTextTests.swift` (4 + 17 tests). Add: a range ending inside an emoji's pair is taken as asked (`Subtext` of `a😀b` from 0 to 2 has length 2); `Replace` on an empty text; equality of two texts built differently but alike.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement as `JSONValue.swift` and `RichText.swift` do.
- [ ] **Step 4:** `tools\build.ps1 test`; all pass. Commit: `Editor model: JSON values and rich text`.

### Task 2: Blocks, and reading and writing the stored document

**Files:** Create `app/Heracles/Editor/EditorBlock.cs`, `app/Heracles/Editor/EditorSelection.cs`; Test `app/Heracles.Tests/Editor/EditorBlockTests.cs`; extend `EditorTestSupport.cs` (the builders `P`, `H`, `Bullet`, `Numbered`, `Todo`, `Quote`, `Callout`, `Toggle`, `Columns`, `Column`, `Divider`, `Code`, `Image`, `Doc`, `Outline`, `Shape(EditorSelection)`, `SameJson`).

**Interfaces (produces):**
- `sealed class BlockIds { static BlockIds Random(); static BlockIds Sequential(string prefix = "n"); string Make(); }`
- `enum BlockKind { Paragraph, Heading, Bullet, Numbered, Todo, Code, Quote, Callout, Toggle, Columns, Column, Divider, Image, Unknown }`
- `sealed class Block { string Id; BlockKind Kind; RichText Text; List<Block> Children; Dictionary<string, JsonValue> Attrs; string? ParagraphId; bool StartsList; Dictionary<string, JsonValue> ListAttrs; JsonValue? Raw; bool IsListItem, HasText, IsContainer, HoldsBlocks; int Level; bool IsChecked; string Language; string Emoji; bool IsOpen; double Width; string? ImageSource; Block Clone(); bool SameAs(Block other); }`
- `sealed class EditorDocument { List<Block> Blocks; EditorDocument(IEnumerable<Block> blocks); static EditorDocument? FromJson(string json, BlockIds ids); string Json; EditorDocument Clone(); bool SameAs(EditorDocument other); }`
- `abstract record EditorSelection` with `None`, `Text(string Block, int Start, int End)`, `Blocks(IReadOnlyList<string> Ids)` (structural equality), `static EditorSelection Caret(string block, int offset)`.

- [ ] **Step 1:** Translate `EditorBlockTests.swift` (14 tests) and `EditorTestSupport.swift`. Add: a clone changed (text, a child added, an attribute) leaves the original's outline and JSON as they were; `SameAs` is true for a clone and false after any of those changes.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement as `EditorBlock.swift` and `EditorSelection.swift` do.
- [ ] **Step 4:** `tools\build.ps1 test`; all pass. Commit: `Editor model: blocks, and the stored document read and written`.

### Task 3: The tree, and every edit that changes a note's shape

**Files:** Create `app/Heracles/Editor/EditorDocument.Tree.cs`, `app/Heracles/Editor/EditorDocument.Editing.cs`; Test `app/Heracles.Tests/Editor/EditorDocumentTests.cs`.

**Interfaces (produces):** the members of `EditorDocument+Tree.swift` and `EditorDocument+Editing.swift` under the same names in PascalCase, with `inout ids` as a `BlockIds` argument and `Range<Int>` as `int start, int end`; `BlockTarget` and `DropTarget` as the Mac declares them.

- [ ] **Step 1:** Translate `EditorDocumentTests.swift` (98 tests). Add for Review Focus 3 and 4: each edit given an id that is not in the document leaves `Outline` unchanged and does not throw; removing, and moving away, the only block leaves one empty paragraph.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement as the two Swift files do.
- [ ] **Step 4:** `tools\build.ps1 test`; all pass. Commit: `Editor model: the tree and the edits`.

### Task 4: Verify the stage

- [ ] **Step 1:** `tools\build.ps1 clean`, `tools\build.ps1`, `tools\build.ps1 test`, `tools\e2e\check-shell.ps1`; expected: all pass, no warnings.
- [ ] **Step 2:** Confirm `app/Heracles/Editor/` names no `System.Windows` type.
- [ ] **Step 3:** Update `CLAUDE.md` (Layout, Current state, and the rules above about value types under "Decisions"). Commit: `Stage 3 verified: the editor's model`.
