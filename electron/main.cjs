const { app, BrowserWindow, ipcMain } = require('electron');
const path = require('path');
const {
  initDatabase,
  listNotes,
  getNote,
  createNote,
  updateNote,
  archiveNote,
  trashNote,
  restoreNote,
  deleteNote,
  emptyTrash,
  setNoteParent,
  setNoteFavorited,
  reorderNote,
  listColumns,
  createColumn,
  updateColumn,
  deleteColumn,
  reorderColumns,
  listTasks,
  createTask,
  updateTask,
  toggleTaskComplete,
  moveTask,
  deleteTask,
  listComments,
  createComment,
  updateComment,
  deleteComment,
  seedDefaultColumns
} = require('./db.cjs');

let mainWindow;
const extraWindows = new Set();

const createWindow = ({ initialNoteId } = {}) => {
  const win = new BrowserWindow({
    width: 1200,
    height: 800,
    minWidth: 900,
    minHeight: 600,
    backgroundColor: '#0e0f12',
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  const hashSuffix = initialNoteId ? `#note=${encodeURIComponent(initialNoteId)}` : '';

  if (!app.isPackaged) {
    win.loadURL(`http://localhost:5173/${hashSuffix}`);
    win.webContents.openDevTools({ mode: 'detach' });
  } else {
    win.loadFile(path.join(__dirname, '../dist/renderer/index.html'), {
      hash: hashSuffix.replace(/^#/, '')
    });
  }

  if (!mainWindow) {
    mainWindow = win;
    mainWindow.on('closed', () => {
      mainWindow = null;
    });
  } else {
    extraWindows.add(win);
    win.on('closed', () => {
      extraWindows.delete(win);
    });
  }

  return win;
};

app.whenReady().then(() => {
  initDatabase(app.getPath('userData'));

  ipcMain.handle('notes:list', () => listNotes());
  ipcMain.handle('notes:get', (_event, id) => getNote(id));
  ipcMain.handle('notes:create', (_event, payload) => {
    const created = createNote(payload);
    // Seed default kanban columns for fresh todo lists.
    if (created && created.kind === 'todolist') {
      seedDefaultColumns(created.id);
    }
    return created;
  });
  ipcMain.handle('notes:update', (_event, payload) => updateNote(payload));
  ipcMain.handle('notes:archive', (_event, payload) => archiveNote(payload));
  ipcMain.handle('notes:trash', (_event, payload) => trashNote(payload));
  ipcMain.handle('notes:restore', (_event, payload) => restoreNote(payload));
  ipcMain.handle('notes:delete', (_event, payload) => deleteNote(payload));
  ipcMain.handle('notes:emptyTrash', () => emptyTrash());
  ipcMain.handle('notes:setParent', (_event, payload) => setNoteParent(payload));
  ipcMain.handle('notes:setFavorited', (_event, payload) => setNoteFavorited(payload));
  ipcMain.handle('notes:reorder', (_event, payload) => reorderNote(payload));
  ipcMain.handle('notes:openWindow', (_event, payload) => {
    createWindow({ initialNoteId: payload?.id });
    return { success: true };
  });

  // ── Todo lists / kanban ────────────────────────────────────────────────
  ipcMain.handle('tasks:listColumns', (_event, payload) => listColumns(payload?.todoListId));
  ipcMain.handle('tasks:createColumn', (_event, payload) => createColumn(payload));
  ipcMain.handle('tasks:updateColumn', (_event, payload) => updateColumn(payload));
  ipcMain.handle('tasks:deleteColumn', (_event, payload) => deleteColumn(payload));
  ipcMain.handle('tasks:reorderColumns', (_event, payload) => reorderColumns(payload));
  ipcMain.handle('tasks:list', (_event, payload) => listTasks(payload?.todoListId));
  ipcMain.handle('tasks:create', (_event, payload) => createTask(payload));
  ipcMain.handle('tasks:update', (_event, payload) => updateTask(payload));
  ipcMain.handle('tasks:toggleComplete', (_event, payload) => toggleTaskComplete(payload));
  ipcMain.handle('tasks:move', (_event, payload) => moveTask(payload));
  ipcMain.handle('tasks:delete', (_event, payload) => deleteTask(payload));
  ipcMain.handle('tasks:listComments', (_event, payload) => listComments(payload?.taskId));
  ipcMain.handle('tasks:createComment', (_event, payload) => createComment(payload));
  ipcMain.handle('tasks:updateComment', (_event, payload) => updateComment(payload));
  ipcMain.handle('tasks:deleteComment', (_event, payload) => deleteComment(payload));

  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});
