# Test fixtures

## `legacy.sqlite`

A small library as the Electron app kept it (`heracles.sqlite`, with the
schema its `electron/db.cjs` ended up with after all of its migrations),
for the tests of the first-start import to bring across. It is the same
shape as the libraries `core/tests/legacy_import.rs` builds for the core's
own tests, and was written by a throwaway Rust test using those same
statements.

What it holds:

- a notebook `Old notebook`, with a section `Old plans`;
- a page `Old trip` in the section: a heading, a paragraph with a bold
  word, a ticked task, and a one-pixel picture held in the page as a
  `data:` address; it has an icon and is a favorite;
- a page `Ferry times` under `Old trip`, archived;
- a page `Old scrap`, in the trash;
- a todo list `Old packing` with two columns (`Up Next`, `Done`), three
  tasks (`Old tent` with a description, a priority, a label, a due date and
  a place; `Old stove`, done; `Old pegs`, a subtask of the tent) and one
  comment;
- a page `Lost page` whose parent does not exist, which the import puts in
  the first notebook and reports as adjusted.

So an import reports 7 notes, 3 tasks, 2 columns, 1 comment and 1 image,
and at least one line of things adjusted.

The file is never changed by a test: each copies it to a folder of its own
first.
