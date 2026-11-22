const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('heracles', {
  platform: process.platform,
  notes: {
    list: () => ipcRenderer.invoke('notes:list'),
    get: (id) => ipcRenderer.invoke('notes:get', id),
    create: (payload) => ipcRenderer.invoke('notes:create', payload),
    update: (payload) => ipcRenderer.invoke('notes:update', payload),
    archive: (payload) => ipcRenderer.invoke('notes:archive', payload),
    trash: (payload) => ipcRenderer.invoke('notes:trash', payload),
    restore: (payload) => ipcRenderer.invoke('notes:restore', payload),
    remove: (payload) => ipcRenderer.invoke('notes:delete', payload),
    emptyTrash: () => ipcRenderer.invoke('notes:emptyTrash'),
    setParent: (payload) => ipcRenderer.invoke('notes:setParent', payload),
    setFavorited: (payload) => ipcRenderer.invoke('notes:setFavorited', payload),
    reorder: (payload) => ipcRenderer.invoke('notes:reorder', payload),
    openWindow: (payload) => ipcRenderer.invoke('notes:openWindow', payload),
    importMarkdown: () => ipcRenderer.invoke('notes:importMarkdown'),
    importHtml: () => ipcRenderer.invoke('notes:importHtml')
  },
  updates: {
    // Fires when a new version has finished downloading and is ready to install.
    // Returns an unsubscribe function.
    onDownloaded: (callback) => {
      const handler = (_event, info) => callback(info);
      ipcRenderer.on('update:downloaded', handler);
      return () => ipcRenderer.removeListener('update:downloaded', handler);
    },
    restart: () => ipcRenderer.invoke('update:restart'),
    check: () => ipcRenderer.invoke('update:check')
  },
  tasks: {
    listColumns: (payload) => ipcRenderer.invoke('tasks:listColumns', payload),
    createColumn: (payload) => ipcRenderer.invoke('tasks:createColumn', payload),
    updateColumn: (payload) => ipcRenderer.invoke('tasks:updateColumn', payload),
    deleteColumn: (payload) => ipcRenderer.invoke('tasks:deleteColumn', payload),
    reorderColumns: (payload) => ipcRenderer.invoke('tasks:reorderColumns', payload),
    list: (payload) => ipcRenderer.invoke('tasks:list', payload),
    create: (payload) => ipcRenderer.invoke('tasks:create', payload),
    update: (payload) => ipcRenderer.invoke('tasks:update', payload),
    toggleComplete: (payload) => ipcRenderer.invoke('tasks:toggleComplete', payload),
    move: (payload) => ipcRenderer.invoke('tasks:move', payload),
    remove: (payload) => ipcRenderer.invoke('tasks:delete', payload),
    listComments: (payload) => ipcRenderer.invoke('tasks:listComments', payload),
    createComment: (payload) => ipcRenderer.invoke('tasks:createComment', payload),
    updateComment: (payload) => ipcRenderer.invoke('tasks:updateComment', payload),
    deleteComment: (payload) => ipcRenderer.invoke('tasks:deleteComment', payload)
  }
});
