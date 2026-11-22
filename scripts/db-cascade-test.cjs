// Functional test of electron/db.cjs against a throwaway SQLite database,
// plus the window navigation policy in electron/navigation.cjs.
// Run with: npm test
//
// better-sqlite3 is compiled for Electron's ABI (postinstall runs
// electron-builder install-app-deps), so this script re-executes itself under
// Electron's bundled Node runtime instead of the system Node.
if (!process.env.HERACLES_TEST_IN_ELECTRON) {
  const { spawnSync } = require('child_process');
  const electronPath = require('electron'); // path string under plain Node
  const result = spawnSync(electronPath, [__filename], {
    stdio: 'inherit',
    env: { ...process.env, ELECTRON_RUN_AS_NODE: '1', HERACLES_TEST_IN_ELECTRON: '1' }
  });
  process.exit(result.status ?? 1);
}

const fs = require('fs');
const path = require('path');
const os = require('os');

const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'heracles-dbtest-'));
const db = require(path.join(__dirname, '..', 'electron', 'db.cjs'));

let failures = 0;
const check = (label, cond) => {
  console.log(`${cond ? 'PASS' : 'FAIL'}: ${label}`);
  if (!cond) failures += 1;
};

db.initDatabase(dir);

const all0 = db.listNotes();
const notebook = all0.find((n) => n.kind === 'notebook');
check('default notebook created on init', Boolean(notebook));

// Build: notebook > section > page > (child page, todolist with tasks)
const section = db.createNote({ title: 'S', kind: 'section', parentId: notebook.id });
const page = db.createNote({ title: 'P', kind: 'page', parentId: section.id });
const child = db.createNote({ title: 'C', kind: 'page', parentId: page.id });
const todo = db.createNote({ title: 'T', kind: 'todolist', parentId: page.id });
const cols = db.seedDefaultColumns(todo.id);
const task = db.createTask({ todoListId: todo.id, columnId: cols[0].id, title: 'task1' });
db.createTask({ todoListId: todo.id, parentTaskId: task.id, title: 'sub1' });
db.createComment({ taskId: task.id, body: 'hello' });

// 1. trash cascades down
db.trashNote({ id: page.id });
check('trash cascades to child page', db.getNote(child.id).trashed === true);
check('trash cascades to nested todolist', db.getNote(todo.id).trashed === true);
check('section untouched by child trash', db.getNote(section.id).trashed === false);

// 2. restore cascades down
db.restoreNote({ id: page.id });
check('restore cascades to child page', db.getNote(child.id).trashed === false);
check('restore cascades to nested todolist', db.getNote(todo.id).trashed === false);

// 3. restoring a child alone hoists it out of a trashed parent
db.trashNote({ id: page.id });
db.restoreNote({ id: child.id });
const restoredChild = db.getNote(child.id);
check('restored child no longer trashed', restoredChild.trashed === false);
check(
  'restored child hoisted to nearest visible ancestor (section)',
  restoredChild.parentId === section.id
);

// 4. emptyTrash removes trashed subtree + kanban data
db.emptyTrash();
check('trashed page removed', db.getNote(page.id) === undefined);
check('trashed todolist removed', db.getNote(todo.id) === undefined);
check('todolist columns removed', db.listColumns(todo.id).length === 0);
check('todolist tasks removed', db.listTasks(todo.id).length === 0);
check('task comments removed', db.listComments(task.id).length === 0);
check('hoisted child survived emptyTrash', Boolean(db.getNote(child.id)));

// 5. deleteNote cascades subtree + kanban data
const p2 = db.createNote({ title: 'P2', kind: 'page', parentId: section.id });
const t2 = db.createNote({ title: 'T2', kind: 'todolist', parentId: p2.id });
const cols2 = db.seedDefaultColumns(t2.id);
db.createTask({ todoListId: t2.id, columnId: cols2[0].id, title: 'x' });
const delResult = db.deleteNote({ id: p2.id });
check('deleteNote reports subtree ids', delResult.deletedIds.length === 2);
check('deleteNote removed descendant todolist', db.getNote(t2.id) === undefined);
check('deleteNote removed kanban columns', db.listColumns(t2.id).length === 0);
check('deleteNote removed kanban tasks', db.listTasks(t2.id).length === 0);

// 6. last notebook still protected
const lastDel = db.deleteNote({ id: notebook.id });
check('last notebook refuses deletion', lastDel.success === false);

// 7. setNoteParent refuses to create cycles ("Move to…" path)
const a = db.createNote({ title: 'A', kind: 'page', parentId: section.id });
const b = db.createNote({ title: 'B', kind: 'page', parentId: a.id });
const selfMove = db.setNoteParent({ id: a.id, parentId: a.id });
check('setNoteParent refuses self as parent', selfMove.parentId === section.id);
const cycleMove = db.setNoteParent({ id: a.id, parentId: b.id });
check('setNoteParent refuses own descendant as parent', cycleMove.parentId === section.id);
const validMove = db.setNoteParent({ id: b.id, parentId: section.id });
check('setNoteParent still performs valid moves', validMove.parentId === section.id);

// 8. navigation policy: a window may only display the app's own document
const { createNavigationPolicy } = require(path.join(__dirname, '..', 'electron', 'navigation.cjs'));
const indexPath = path.join(os.tmpdir(), 'Heracles App', 'dist', 'renderer', 'index.html');
const indexUrl = require('url').pathToFileURL(indexPath).href;
const packaged = createNavigationPolicy({ isPackaged: true, indexPath });
check('packaged: app index is allowed', packaged.isAppUrl(indexUrl));
check('packaged: app index with a note hash is allowed', packaged.isAppUrl(`${indexUrl}#note=abc`));
check(
  'packaged: another local file is refused',
  !packaged.isAppUrl(require('url').pathToFileURL(path.join(os.tmpdir(), 'evil.html')).href)
);
check('packaged: remote url is refused', !packaged.isAppUrl('https://example.com/index.html'));
check('packaged: malformed url is refused', !packaged.isAppUrl('not a url'));
const dev = createNavigationPolicy({ isPackaged: false, indexPath });
check('dev: vite server is allowed', dev.isAppUrl('http://localhost:5173/#note=abc'));
check('dev: lookalike port is refused', !dev.isAppUrl('http://localhost:51730/'));
check('dev: local file is refused', !dev.isAppUrl(indexUrl));
check('http(s) links count as external', dev.isExternalUrl('https://example.com/a'));
check('javascript: urls are not external', !dev.isExternalUrl('javascript:alert(1)'));

fs.rmSync(dir, { recursive: true, force: true });
console.log(failures === 0 ? '\nALL TESTS PASSED' : `\n${failures} FAILURES`);
process.exit(failures === 0 ? 0 : 1);
