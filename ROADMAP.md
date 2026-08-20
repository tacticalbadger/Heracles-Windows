# Roadmap

Reconstructed 2026-08-19 after the previous roadmap was lost. Sources: the goals
in [`docs/architecture.md`](./docs/architecture.md), the current state of the
code, and issues found during a full code review (fixed items are recorded in
[`CHANGELOG.md`](./CHANGELOG.md)).

## Known gaps (small, well-understood)

- **Archive doesn't cascade.** Archiving a page hides its children (they aren't
  archived themselves, and their parent no longer renders in the tree). They
  come back on unarchive, so nothing is lost — but they're invisible meanwhile.
  Decide: cascade `archived` down like trash now does, or render an archived
  page's subtree inside the Archived section.
- **`heracles://` links go nowhere.** "Copy link" writes `heracles://note/<id>`
  and block links to the clipboard, but no custom protocol is registered with
  the OS and nothing parses those links on the receiving end. Needs
  `app.setAsDefaultProtocolClient` + an open-url handler that focuses/opens the
  note (and scrolls to the block for `#block=` links).
- **Duplicate copies one note (plus its board), not the child subtree.** Pages
  nested under the duplicated page are not copied. Task comments are also not
  copied on board duplication.
- **Reminders never fire.** Tasks store reminder timestamps but no scheduler or
  OS notification exists. Electron's `Notification` API + a main-process timer
  would cover it.
- **Code blocks always highlight as JavaScript.** Only
  `@codemirror/lang-javascript` is installed; the stored `language` attribute is
  ignored for highlighting. Add a language picker + lazy-loaded language packs.
- **Same note in two panes fights over the cursor.** The non-editing pane is
  refreshed with `setContent`, which resets its selection. Fine for now; a
  shared editor state (or transaction forwarding) would fix it properly.
- **No export.** Import exists for Markdown/HTML; there is no way to get a page
  back out. Markdown export is the natural first target (images as attachments
  or data URLs are the open question).

## Medium-term

- **Image storage.** Images embed as base64 data URLs inside note HTML, so a
  screenshot-heavy page makes every list/search/save carry megabytes. Move
  image payloads to a separate table (or files in `userData`) and reference
  them by id; migrate existing data URLs lazily.
- **Full-text search.** ⌘K and the sidebar filter do substring matching over
  stripped text in the renderer. SQLite FTS5 over title + stripped content
  would scale better and enable ranking.
- **Windows release signing.** Builds work; Authenticode signing (see
  [`windowsInstructions.md`](./windowsInstructions.md)) is still needed for
  auto-update to apply on Windows.
- **Renderer bundle splitting.** The renderer is a single ~1.1 MB chunk; Vite
  warns about it. CodeMirror and the Kanban board are natural lazy-load seams.
- **Tests.** `npm test` now runs a functional test of the DB layer's cascade
  behavior (`scripts/db-cascade-test.cjs`). Grow this: paneTreeOps is pure and
  trivially testable; import/paste converters are DOM-only.

## Long-term (from the original architecture goals)

- Cloud sync (the schema is local-first; sync was always "later").
- Office 365 calendar integration.
- Slack notifications.
- Command palette beyond the note switcher (actions, not just navigation).
