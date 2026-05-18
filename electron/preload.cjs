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
    emptyTrash: () => ipcRenderer.invoke('notes:emptyTrash')
  }
});
