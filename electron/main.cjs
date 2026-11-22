const { app, BrowserWindow, ipcMain, dialog, shell } = require('electron');
const path = require('path');
const fs = require('fs');
const { autoUpdater } = require('electron-updater');
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
const { createNavigationPolicy } = require('./navigation.cjs');

const APP_INDEX_PATH = path.join(__dirname, '../dist/renderer/index.html');

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

  // Navigation policy: the window may only ever display the app itself.
  // External links open in the system browser; everything else is denied,
  // including other local files (a file dropped on the window would otherwise
  // navigate to it). Anything loaded here gets the preload DB bridge.
  const { isAppUrl, isExternalUrl } = createNavigationPolicy({
    isPackaged: app.isPackaged,
    indexPath: APP_INDEX_PATH
  });

  win.webContents.setWindowOpenHandler(({ url }) => {
    if (isExternalUrl(url) && !isAppUrl(url)) {
      shell.openExternal(url);
    }
    return { action: 'deny' };
  });

  win.webContents.on('will-navigate', (event, url) => {
    if (isAppUrl(url)) return;
    event.preventDefault();
    if (isExternalUrl(url)) {
      shell.openExternal(url);
    }
  });

  if (!app.isPackaged) {
    win.loadURL(`http://localhost:5173/${hashSuffix}`);
    win.webContents.openDevTools({ mode: 'detach' });
  } else {
    win.loadFile(APP_INDEX_PATH, {
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

// Background auto-update via electron-updater against the generic release feed
// (Cloudflare R2 bucket baked in by electron-builder's publish config). Downloads
// silently; the renderer shows a "Restart to update" banner once ready.
const setupAutoUpdates = () => {
  if (!app.isPackaged) {
    return; // no update feed in dev
  }

  autoUpdater.autoDownload = true;
  autoUpdater.autoInstallOnAppQuit = true;

  autoUpdater.on('update-downloaded', (info) => {
    for (const win of BrowserWindow.getAllWindows()) {
      win.webContents.send('update:downloaded', { version: info?.version ?? null });
    }
  });

  autoUpdater.on('error', (err) => {
    console.error('[heracles] auto-update error:', err?.message ?? err);
  });

  const check = () => autoUpdater.checkForUpdates().catch(() => {});
  check();
  // Re-check periodically so long-running sessions still pick up releases.
  setInterval(check, 1000 * 60 * 60);
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
  ipcMain.handle('notes:importMarkdown', async (event) => {
    const win = BrowserWindow.fromWebContents(event.sender) ?? mainWindow ?? undefined;
    const result = await dialog.showOpenDialog(win, {
      title: 'Import Markdown',
      buttonLabel: 'Import',
      properties: ['openFile', 'multiSelections'],
      filters: [
        { name: 'Markdown', extensions: ['md', 'markdown', 'mdown', 'mkd', 'mkdn', 'txt'] },
        { name: 'All Files', extensions: ['*'] }
      ]
    });
    if (result.canceled || result.filePaths.length === 0) {
      return { canceled: true, files: [] };
    }
    const files = [];
    for (const filePath of result.filePaths) {
      try {
        const markdown = fs.readFileSync(filePath, 'utf8');
        const base = path.basename(filePath);
        const name = base.replace(/\.(md|markdown|mdown|mkd|mkdn|txt)$/i, '').trim();
        files.push({ name: name || 'Untitled', markdown });
      } catch (err) {
        console.error(`[heracles] Could not read ${filePath}:`, err?.message ?? err);
      }
    }
    return { canceled: false, files };
  });
  ipcMain.handle('notes:importHtml', async (event) => {
    const win = BrowserWindow.fromWebContents(event.sender) ?? mainWindow ?? undefined;
    const result = await dialog.showOpenDialog(win, {
      title: 'Import HTML',
      buttonLabel: 'Import',
      properties: ['openFile', 'multiSelections'],
      filters: [
        { name: 'HTML', extensions: ['html', 'htm'] },
        { name: 'All Files', extensions: ['*'] }
      ]
    });
    if (result.canceled || result.filePaths.length === 0) {
      return { canceled: true, files: [] };
    }
    const files = [];
    for (const filePath of result.filePaths) {
      try {
        const html = fs.readFileSync(filePath, 'utf8');
        const base = path.basename(filePath);
        const name = base.replace(/\.(html|htm)$/i, '').trim();
        files.push({ name: name || 'Untitled', html });
      } catch (err) {
        console.error(`[heracles] Could not read ${filePath}:`, err?.message ?? err);
      }
    }
    return { canceled: false, files };
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

  // ── Auto-update ────────────────────────────────────────────────────────────
  // Quit-and-install when the renderer's "Restart to update" button is clicked.
  ipcMain.handle('update:restart', () => {
    autoUpdater.quitAndInstall();
    return { success: true };
  });
  // Manual re-check (the renderer can trigger this from a menu/setting later).
  ipcMain.handle('update:check', () => {
    if (app.isPackaged) autoUpdater.checkForUpdates().catch(() => {});
    return { success: true };
  });

  createWindow();
  setupAutoUpdates();

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
