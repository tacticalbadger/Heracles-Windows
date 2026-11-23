# Heracles for Windows, Stage 6: Import, Export and the Electron Library — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Files come in and go out through the File menu, and a library left by the Electron app is brought across the first time the native app starts, with a summary of what came.

**Architecture:** All three are calls into the core (`Store.ImportFile`, `Store.ExportMarkdown`, `Store.ImportLegacy`); the app supplies the file dialogs, the decision whether to import, and the summary window. What the app decides is in classes with no view in them (`Transfer`, `AppModel.Legacy`), tested against a real core; the dialogs and the window only ask and show.

**Tech Stack:** C#, .NET 10, WPF (`Microsoft.Win32.OpenFileDialog`, `SaveFileDialog`), xunit.

**Spec:** `docs/superpowers/specs/2026-11-22-native-windows-app-design.md` ("Data": first start imports an Electron library; "Import and export"; "Windows conventions": Ctrl+Shift+I, Ctrl+Shift+E; stage 6 of "Order of work"). Links from outside and the rest of the menus, which the design also puts in this stage, were finished in stages 2 and 5: the Mac's menu bar has nothing else the Windows one lacks.

## Global Constraints

- The Mac sources at `..\Heracles` (commit `5dd0252`) decide behaviour: `Workspace.swift` (`chooseFilesToImport`, `chooseExportDestination`), `LegacyImport.swift`, and `ImportSummaryView` in `HeraclesApp.swift`.
- Import takes the Mac's extensions: `md`, `markdown`, `mdown`, `mkd`, `mkdn`, `txt`, `html`, `htm`. Several files at once. Each becomes a page under the place new notes go, and the last one is opened.
- Export writes the open note as Markdown to a file the user names, pictures in a folder beside it (the core's doing). The name offered is the note's title with `.md`, made fit for a Windows file name.
- The Electron app's library is `heracles.sqlite` in the library folder. It is imported only when the new library holds nothing but its first notebook. The core backs the old file up first, never changes it, and refuses a second import; a refusal of that kind is not an error to show.
- The summary says what the Mac's says: how many notes, tasks and images came, that the old library was not changed, where the copy of it is, and each thing that had to be adjusted.
- Tests never show a window or a dialog. The scripted check may drive the file dialogs through UI Automation if they can be driven without real input; if they cannot, it says so and the dialogs go under "Not verified".
- Commit locally at the end of each task, as `tacticalbadger`, message ending `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Do not push. Update `CLAUDE.md` in every commit. Builds use `tools\build.ps1`.

## Review Focus

1. **A title that is not a file name** (`a/b`, `CON`, a trailing dot, 300 characters, nothing at all). Expected: the name offered is one Windows accepts. Task 1.
2. **A file that cannot be imported among several that can.** Expected: the others come in, and the error says which did not. Task 1.
3. **An Electron library that cannot be read** (not a database, or one with other tables). Expected: the new library is left as it was, the app starts, and a message says the old one was not changed. Task 2.
4. **Starting twice.** Expected: the second start imports nothing and shows no summary. Task 2.
5. **A second copy of the app started while the summary is showing.** Expected: it hands over as ever. Not tested; the summary is not modal.

---

### Task 1: Import and export of files

**Files:** Create `app/Heracles/Model/Transfer.cs`; Modify `app/Heracles/MainWindow.xaml.cs` (File ▸ Import…, File ▸ Export as Markdown…); Test `app/Heracles.Tests/TransferTests.cs`.

**Interfaces (produces):**
- `static class Transfer`: `IReadOnlyList<string> ImportExtensions`, `string ImportFilter` (for an open dialog), `string ExportFilter`, `string ExportFileName(string title)`.
- `MainWindow`: `ChooseFilesToImport()`, `ChooseExportDestination()`.

- [ ] **Step 1:** Tests: the filter names every extension once; a file name from a plain title, from one with characters Windows refuses, from a reserved device name, from one that ends in a dot or a space, from an empty one (`Untitled.md`), from a very long one (at most 120 characters before the extension); `Workspace.ImportFiles` with a file that is not there among two that are (two notes, the last opened, an error kept); a Markdown file with a picture beside it comes in with the picture; a note exported and imported again says the same.
- [ ] **Step 2:** Run; expected: does not compile (`Transfer`).
- [ ] **Step 3:** Implement. The dialogs are the system's own; Export is disabled with no note open.
- [ ] **Step 4:** `tools\build.ps1 app test`; all pass. Commit: `Import and export of files, from the File menu`.

### Task 2: The Electron library, on first start

**Files:** Create `app/Heracles/Model/AppModel.Legacy.cs`, `app/Heracles/ImportSummaryWindow.cs`; Modify `app/Heracles/App.xaml.cs`; Create `app/Heracles.Tests/Fixtures/legacy.sqlite` (with a note beside it of what it holds and how it was made), `tools/e2e/check-legacy.ps1`; Modify `tools/e2e/make-fixture.ps1` (`-Legacy` puts the fixture library in the folder); Test `app/Heracles.Tests/LegacyImportTests.cs`.

**Interfaces (produces):**
- `AppModel`: `static string? LegacyDatabase(string dataDirectory)`, `ImportReport? ImportLegacyLibraryIfNeeded(string? database)`.
- `static class ImportSummary`: `string Headline`, `string Counts(ImportReport)`, `string Kept`, `string Adjusted`.
- `ImportSummaryWindow(ImportReport)`: automation ids `ImportSummary`, `ImportSummaryCounts`, `ImportSummaryBackup`, `ImportSummaryRepair:<n>`, `ImportSummaryOk`.
- Log lines: `legacy: imported <n> notes, <n> tasks, <n> images` or `legacy: could not be imported: <reason>`.

- [ ] **Step 1:** Tests against the fixture, copied to a throwaway folder: there is a library to import only when the file is there; an empty library takes it, and the report counts what came; the notes are in the model's library afterwards; a library that already has a note is left alone; a second ask imports nothing and sets no error; a file that is not a database imports nothing, leaves one notebook, and says so in words that say the old library was not changed; the summary's sentences, with one note and with several.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement. The import runs after the model is made and before the first window; the summary is shown over the first window, once.
- [ ] **Step 4:** `check-legacy.ps1`: a fixture with the old library in it starts, shows the summary with its counts and the backup's place, and the sidebar lists the old notes; started again, there is no summary. `tools\build.ps1 app test`, every e2e script. Commit: `The Electron library is imported on first start, with a summary`.

### Task 3: Verify the stage

- [ ] **Step 1:** `tools\build.ps1 clean`, `tools\build.ps1`, `tools\build.ps1 test`, every e2e script; expected: all pass, no warnings.
- [ ] **Step 2:** Update `CLAUDE.md` (Layout, the parity checklist, Not verified, Known faults, Current state). Commit: `Stage 6 verified: import, export and the Electron library`.
