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
  reorderNote
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
  ipcMain.handle('notes:create', (_event, payload) => createNote(payload));
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
