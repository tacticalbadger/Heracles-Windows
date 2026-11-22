# Heracles

A local-first, hierarchical notebook for macOS: pages in notebooks, a block editor, split panes and kanban boards. Everything is stored on your Mac in one SQLite database.

Heracles is a native Mac app, written in Swift over a Rust core. It replaces the Electron app this project started as. That app is still in the repository and is described in [`docs/electron.md`](./docs/electron.md).

## Status and download

The native app is a preview. The latest build is on the [releases page](https://github.com/tacticalbadger/Heracles/releases): a universal app for macOS 14 or later.

- The build is signed with a Developer ID certificate and notarized by Apple, so it opens like any other downloaded app.
- It does not update itself yet. To install a newer version, replace the app.
- Nobody has used it day to day yet. It passes its automated tests, which cannot move a mouse.
- The first time it starts, it copies an existing Electron library into its own database. See [Data storage](#data-storage).

What is built, what is missing and what has not been checked is recorded in the [rewrite status](./docs/review-and-native-rewrite.md#rewrite-status).

## Features

- **Notebooks, sections and pages** in a sidebar, nested as deep as you like, with favorites, archive and trash. Trashing, restoring and deleting apply to a page and everything under it.
- **Panes and tabs.** Split a window right or down, drag the dividers, and drag tabs between panes. Each window remembers its layout, and any number of windows can be open on the same library.
- **Block editor.** Headings, lists, task lists, quotes, callouts, toggles, code blocks, images and column layouts. Each block's text is a system text view, so typing, selection, spelling and dictation work as they do across the Mac.
- **Markdown as you type**, a slash menu of blocks, and a Format menu with the usual shortcuts.
- **Blocks you can pick up.** Each block has a handle to drag it, make columns with it, duplicate it, turn it into another kind of block, or copy a link to it. Whole blocks can be selected, copied and moved together.
- **Kanban boards.** A todo list is a board with your own columns. Cards carry notes, priority, a due date, a deadline, labels, a location, reminders, comments and subtasks nested to any depth.
- **Reminders** arrive as system notifications, whether or not the app is open.
- **Search** across every note's title and text.
- **Quick Open** (⌘K) to jump to a note by name.
- **Import and export.** Import Markdown and HTML files as pages, with their pictures. Export a note as Markdown with its pictures beside it.
- **Links between notes.** `heracles://` links open a note, or scroll to one block of it.

## How it is built

| Layer | Choice |
|---|---|
| Interface | Swift 6, SwiftUI and AppKit |
| Core | Rust (`heracles-core`): storage, documents, search, import and export |
| Bridge | UniFFI, which generates the Swift bindings to the core |
| Storage | SQLite with full-text search, bundled into the core |
| Editor | Native: a document model in Swift, shown as one system text view (TextKit 2) per block |
| Build | Cargo and Swift Package Manager, run by the scripts in `macos/` |

The core owns the data. Every window reads from it and subscribes to its change feed, which is what keeps two windows showing the same note in step. Documents are stored as blocks in JSON, and pictures are stored once each, outside the notes that use them.

## Building

You need Rust (through [rustup](https://rustup.rs), with the `aarch64-apple-darwin` and `x86_64-apple-darwin` targets) and Xcode.

```bash
macos/build-app.sh              # universal build → build/Heracles.app
macos/build-app.sh --host-only  # this Mac's processor only, faster
```

To run the tests:

```bash
cargo test                           # the core
swift test --package-path macos      # the bindings, the app's model and the editor
build/Heracles.app/Contents/MacOS/Heracles --self-test
```

[`BUILDING.md`](./BUILDING.md#building-the-native-mac-app-in-progress) has the details, including how to sign and publish a release.

## Project structure

```
core/                      heracles-core: the app's data layer (Rust)
├── src/doc/               Block document model, HTML and Markdown conversion
├── src/store/             SQLite schema, notes, boards, search, image store
├── src/transfer.rs        Import and export of files
├── src/legacy.rs          One-time import of the Electron library
├── src/changes.rs         Change feed that keeps every window current
└── tests/                 Behaviour tests, one file per area
macos/                     The Mac app (Swift)
├── build-app.sh           Builds build/Heracles.app
├── build-core.sh          Builds the core and its Swift bindings
├── release.sh             Signs, notarizes and staples a build to distribute
├── Sources/HeraclesCore/  Generated bindings to the core
├── Sources/HeraclesApp/   Model, sidebar, panes, board
│   └── Editor/            The block editor: document model, text views, page
└── Tests/                 Binding, app model and editor tests
tools/bindgen/             UniFFI's Swift bindings generator, version-pinned
electron/, renderer/       The Electron app (see docs/electron.md)
```

## Data storage

The app keeps everything in `~/Library/Application Support/Heracles/`:

| Path | Holds |
|---|---|
| `heracles.db` | Notes, documents, boards and the search index, in SQLite |
| `images/` | Pictures, one file each, named by their content |
| `backups/` | The copy made of an Electron library before it was imported |

The Electron app uses the same folder, with its library in `heracles.sqlite`. The first time the native app starts and finds that file, it backs it up, reads the backup, and copies the library into `heracles.db`. The Electron file is never changed, so the Electron app keeps working. From then on the two apps hold separate copies, and an edit made in one does not appear in the other.

To try the app on a different folder, start it with `HERACLES_DATA_DIR` set:

```bash
HERACLES_DATA_DIR=~/heracles-trial build/Heracles.app/Contents/MacOS/Heracles
```

## Documentation

- [`CHANGELOG.md`](./CHANGELOG.md) — notable changes per release
- [`BUILDING.md`](./BUILDING.md) — build, signing and release guide for both apps
- [`docs/review-and-native-rewrite.md`](./docs/review-and-native-rewrite.md) — the rewrite plan, its status, and the code review that led to it
- [`docs/electron.md`](./docs/electron.md) — the Electron app: features, scripts, releases and architecture notes
- [`docs/architecture.md`](./docs/architecture.md) — the original goals and the Electron design
- [`ROADMAP.md`](./ROADMAP.md) — known gaps in the Electron app and planned work
- [`windowsInstructions.md`](./windowsInstructions.md) — signing and shipping the Windows build

## Sibling repositories

- [Heracles](https://github.com/tacticalbadger/Heracles) — this repository: the native Mac app, and the Electron app's macOS build.
- [Heracles-Windows](https://github.com/tacticalbadger/Heracles-Windows) — the Windows build of the Electron app.

The two repositories share the Electron code and its git history. A change to that code, and to its documentation, belongs in both: commit to one, then merge or cherry-pick into the other. Platform packaging and signing stay in their own repository. The native app is in this repository only.

## License

See `LICENSE`.
