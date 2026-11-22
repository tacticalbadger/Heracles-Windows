import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent
} from 'react';
import type { Note } from './types/notes';
import { markdownToEditorHtml } from './lib/markdownImport';
import { htmlToEditorHtml } from './lib/htmlImport';
import { stripHtml } from './lib/text';
import { Sidebar, type Section } from './workspace/Sidebar';
import type { SectionSort } from './workspace/SectionMenu';
import { MovePagePicker } from './workspace/MovePagePicker';
import { PaneTree } from './workspace/PaneTree';
import {
  closeLeaf,
  closeTabInLeaf,
  collectLeaves,
  createLeaf,
  ensureLeaf,
  firstLeaf,
  getLeaf,
  isLeaf,
  moveTab,
  moveTabToSplit,
  openTabInLeaf,
  removeNoteFromTree,
  setActiveTab as setActiveTabInTree,
  setSplitRatios,
  splitLeaf,
  type PaneNode,
  type SplitTarget
} from './workspace/paneTreeOps';
import type { ToastTone } from './workspace/EditorPane';

type ToastAction = {
  label: string;
  onAction: () => void;
};

type Toast = {
  id: string;
  message: string;
  tone: ToastTone;
  action?: ToastAction;
};

type SaveState = {
  status: 'idle' | 'saving' | 'saved';
  lastSavedAt?: string;
  noteId?: string;
};

const sortNotes = (items: Note[]) =>
  [...items].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));

const formatDate = (value: string) => {
  const date = new Date(value);
  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
};

const formatTime = (value?: string) => {
  if (!value) return 'Just now';
  const date = new Date(value);
  return date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
};

const App = () => {
  const [theme, setTheme] = useState<'light' | 'dark'>(() => {
    const stored = localStorage.getItem('heracles-theme');
    return stored === 'light' ? 'light' : 'dark';
  });
  const [notes, setNotes] = useState<Note[]>([]);
  const [autoRenameId, setAutoRenameId] = useState<string | null>(null);
  const [paneTree, setPaneTree] = useState<PaneNode>(() => createLeaf());
  const [activeLeafId, setActiveLeafId] = useState<string>(() => firstLeaf(createLeaf()).id);
  const [isSwitcherOpen, setIsSwitcherOpen] = useState(false);
  const [switcherQuery, setSwitcherQuery] = useState('');
  const [switcherIndex, setSwitcherIndex] = useState(0);
  const [saveState, setSaveState] = useState<SaveState>({ status: 'idle' });
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [moveTarget, setMoveTarget] = useState<Note | null>(null);
  const [updateReady, setUpdateReady] = useState<{ version: string | null } | null>(null);
  const [sectionLabels, setSectionLabels] = useState<Partial<Record<Section, string>>>(() => {
    try {
      const stored = localStorage.getItem('heracles-section-labels');
      if (stored) {
        return JSON.parse(stored) as Partial<Record<Section, string>>;
      }
    } catch {
      // ignore
    }
    return {};
  });
  const [sectionSort, setSectionSort] = useState<Record<Section, SectionSort>>(() => {
    try {
      const stored = localStorage.getItem('heracles-section-sort');
      if (stored) {
        const parsed = JSON.parse(stored) as Partial<Record<Section, SectionSort>>;
        return {
          favorites: parsed.favorites ?? 'manual',
          private: parsed.private ?? 'last-edited',
          archive: parsed.archive ?? 'last-edited',
          trash: parsed.trash ?? 'last-edited'
        };
      }
    } catch {
      // ignore corrupt storage
    }
    return {
      favorites: 'manual',
      private: 'last-edited',
      archive: 'last-edited',
      trash: 'last-edited'
    };
  });

  // Derived state: currently active note (from the active leaf's active tab)
  const activeNoteId = useMemo(() => {
    const leaf = getLeaf(paneTree, activeLeafId);
    return leaf?.activeTabId ?? null;
  }, [paneTree, activeLeafId]);

  const switcherInputRef = useRef<HTMLInputElement | null>(null);
  const saveTimers = useRef(new Map<string, number>());
  // Payloads waiting on the save debounce, flushed if the window closes early.
  const pendingSaves = useRef(new Map<string, { content: string; title: string }>());
  const initRan = useRef(false);

  // Server rows can be staler than the editor: a debounced save may still be
  // pending when a metadata update (icon/favorite/archive) or a list refresh
  // returns DB copies. Overlay pending edits so they are never clobbered —
  // without this, replacing state with the DB row makes the editor reset to
  // the older content and silently drop the last ~600ms of typing.
  const mergePendingEdits = useCallback((note: Note): Note => {
    const pending = pendingSaves.current.get(note.id);
    return pending ? { ...note, content: pending.content, title: pending.title } : note;
  }, []);

  const withPendingEdits = useCallback(
    (list: Note[]): Note[] =>
      pendingSaves.current.size === 0 ? list : list.map(mergePendingEdits),
    [mergePendingEdits]
  );

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem('heracles-theme', theme);
  }, [theme]);

  // Show a "Restart to update" banner once a new version has downloaded.
  useEffect(() => {
    if (!window.heracles?.updates?.onDownloaded) return;
    const unsubscribe = window.heracles.updates.onDownloaded((info) =>
      setUpdateReady(info ?? { version: null })
    );
    return unsubscribe;
  }, []);

  const dismissToast = useCallback((toastId: string) => {
    setToasts((current) => current.filter((toast) => toast.id !== toastId));
  }, []);

  const pushToast = useCallback(
    (message: string, tone: ToastTone = 'info', action?: ToastAction, durationMs = 3200) => {
      const id =
        typeof crypto !== 'undefined' && 'randomUUID' in crypto
          ? crypto.randomUUID()
          : `${Date.now()}-${Math.random()}`;

      const wrappedAction = action
        ? {
            ...action,
            onAction: () => {
              action.onAction();
              dismissToast(id);
            }
          }
        : undefined;

      setToasts((current) => [...current, { id, message, tone, action: wrappedAction }]);
      window.setTimeout(() => dismissToast(id), durationMs);
    },
    [dismissToast]
  );

  // Initial load
  useEffect(() => {
    if (initRan.current) return;
    initRan.current = true;

    let isMounted = true;

    const loadNotes = async () => {
      if (!window.heracles?.notes) return;

      const stored = await window.heracles.notes.list();
      if (!isMounted) return;

      let initial = stored;
      // Seed a welcome page (inside the default notebook) when there is no
      // openable document yet. The notebook itself is created by the DB migration.
      const hasDocument = initial.some(
        (note) => note.kind === 'page' || note.kind === 'todolist'
      );
      if (!hasDocument) {
        const firstNotebook = initial.find((note) => note.kind === 'notebook');
        const seed = await window.heracles.notes.create({
          title: 'Welcome to Heracles',
          content:
            '<h1>Welcome to Heracles</h1><p>Use / to open the slash menu.</p><p>Use ⌘K to search.</p>',
          parentId: firstNotebook ? firstNotebook.id : null
        });
        initial = [...initial, seed];
      }

      const sorted = sortNotes(initial);

      // Allow main process to seed an initial note via location hash (#note=ID).
      // Only open documents — notebooks and sections aren't editable.
      const hashMatch = window.location.hash.match(/note=([^&]+)/);
      const requestedNoteId = hashMatch ? decodeURIComponent(hashMatch[1]) : null;
      const firstOpenable = sorted.find(
        (note) => note.kind === 'page' || note.kind === 'todolist'
      );
      const firstId =
        (requestedNoteId && sorted.find((note) => note.id === requestedNoteId)?.id) ||
        firstOpenable?.id ||
        null;
      const newLeaf = createLeaf(firstId ? [firstId] : [], firstId);
      setNotes(sorted);
      setPaneTree(newLeaf);
      setActiveLeafId(newLeaf.id);
    };

    loadNotes();

    return () => {
      isMounted = false;
    };
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      const isQuickSwitch = (event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k';
      if (isQuickSwitch) {
        event.preventDefault();
        setIsSwitcherOpen(true);
        setSwitcherQuery('');
        setSwitcherIndex(0);
        return;
      }

      if (event.key === 'Escape' && isSwitcherOpen) {
        setIsSwitcherOpen(false);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isSwitcherOpen]);

  useEffect(() => {
    if (!isSwitcherOpen) return;
    const id = window.setTimeout(() => switcherInputRef.current?.focus(), 50);
    return () => window.clearTimeout(id);
  }, [isSwitcherOpen]);

  useEffect(() => {
    setSwitcherIndex(0);
  }, [switcherQuery, isSwitcherOpen]);

  // Pane tree operations
  const ensureActiveLeafExists = useCallback(
    (tree: PaneNode, requestedLeafId?: string): string => {
      if (requestedLeafId && getLeaf(tree, requestedLeafId)) {
        return requestedLeafId;
      }
      return firstLeaf(tree).id;
    },
    []
  );

  const openNoteInLeaf = useCallback(
    (noteId: string, targetLeafId?: string) => {
      setPaneTree((current) => {
        const leafId = targetLeafId && getLeaf(current, targetLeafId) ? targetLeafId : activeLeafId;
        const leafExists = getLeaf(current, leafId);
        const finalLeafId = leafExists ? leafId : firstLeaf(current).id;
        const next = openTabInLeaf(current, finalLeafId, noteId);
        setActiveLeafId(finalLeafId);
        return next;
      });
    },
    [activeLeafId]
  );

  const handleSelectTab = useCallback((leafId: string, noteId: string) => {
    setPaneTree((current) => setActiveTabInTree(current, leafId, noteId));
    setActiveLeafId(leafId);
  }, []);

  const handleCloseTab = useCallback((leafId: string, noteId: string) => {
    setPaneTree((current) => closeTabInLeaf(current, leafId, noteId));
  }, []);

  const handleFocusLeaf = useCallback((leafId: string) => {
    setActiveLeafId(leafId);
  }, []);

  const handleSplitLeaf = useCallback(
    (leafId: string, target: SplitTarget, sourceNoteId?: string) => {
      setPaneTree((current) => {
        const initialTabs = sourceNoteId ? [sourceNoteId] : [];
        const { tree, newLeafId } = splitLeaf(current, leafId, target, initialTabs);
        setActiveLeafId(newLeafId);
        return tree;
      });
    },
    []
  );

  const handleCloseLeaf = useCallback(
    (leafId: string) => {
      setPaneTree((current) => {
        const next = closeLeaf(current, leafId);
        const safe = ensureLeaf(next);
        setActiveLeafId(ensureActiveLeafExists(safe));
        return safe;
      });
    },
    [ensureActiveLeafExists]
  );

  const handleSetSplitRatios = useCallback((splitId: string, ratios: number[]) => {
    setPaneTree((current) => setSplitRatios(current, splitId, ratios));
  }, []);

  const handleMoveTabToSplit = useCallback(
    (
      sourceLeafId: string,
      noteId: string,
      destLeafId: string,
      target: SplitTarget
    ) => {
      setPaneTree((current) => {
        const result = moveTabToSplit(current, sourceLeafId, noteId, destLeafId, target);
        setActiveLeafId(result.newLeafId);
        return result.tree;
      });
    },
    []
  );

  const handleMoveTab = useCallback(
    (sourceLeafId: string, noteId: string, destLeafId: string, destIndex: number) => {
      setPaneTree((current) => moveTab(current, sourceLeafId, noteId, destLeafId, destIndex));
      setActiveLeafId(destLeafId);
    },
    []
  );

  const handleUpdateNoteIcon = useCallback(
    async (noteId: string, icon: string | null) => {
      const updatedAt = new Date().toISOString();
      setNotes((current) =>
        sortNotes(
          current.map((note) =>
            note.id === noteId ? { ...note, icon, updatedAt } : note
          )
        )
      );

      if (!window.heracles?.notes) return;
      const result = await window.heracles.notes.update({ id: noteId, icon });
      if (result) {
        const merged = mergePendingEdits(result);
        setNotes((current) =>
          sortNotes(current.map((note) => (note.id === noteId ? merged : note)))
        );
      }
    },
    [mergePendingEdits]
  );

  // Note CRUD
  const replaceNote = useCallback(
    (updated: Note) => {
      const merged = mergePendingEdits(updated);
      setNotes((current) =>
        sortNotes(current.map((note) => (note.id === merged.id ? merged : note)))
      );
    },
    [mergePendingEdits]
  );

  // Close all tabs for the given notes in every pane, collapsing emptied panes.
  // Does not modify the notes list.
  const closeNoteTabsEverywhere = useCallback(
    (noteIds: string | string[]) => {
      const ids = Array.isArray(noteIds) ? noteIds : [noteIds];
      setPaneTree((current) => {
        let next: PaneNode | null = current;
        for (const id of ids) {
          next = removeNoteFromTree(ensureLeaf(next), id);
        }
        const safe = ensureLeaf(next);
        setActiveLeafId(ensureActiveLeafExists(safe));
        return safe;
      });
    },
    [ensureActiveLeafExists]
  );

  // All descendant ids of a note in the current state (children, grandchildren, ...).
  const collectDescendantIds = useCallback(
    (noteId: string): string[] => {
      const found = new Set([noteId]);
      let added = true;
      while (added) {
        added = false;
        for (const note of notes) {
          if (note.parentId && found.has(note.parentId) && !found.has(note.id)) {
            found.add(note.id);
            added = true;
          }
        }
      }
      found.delete(noteId);
      return Array.from(found);
    },
    [notes]
  );

  // Walk up from a note to the notebook that contains it.
  const findNotebookOf = useCallback(
    (noteId: string | null): string | null => {
      if (!noteId) return null;
      const byId = new Map(notes.map((note) => [note.id, note]));
      let current = byId.get(noteId);
      while (current) {
        if (current.kind === 'notebook') return current.id;
        current = current.parentId ? byId.get(current.parentId) : undefined;
      }
      return null;
    },
    [notes]
  );

  // The notebook new top-level content should land in: the active note's
  // notebook, falling back to the first notebook.
  const resolveNotebookId = useCallback((): string | null => {
    const fromActive = findNotebookOf(activeNoteId);
    if (fromActive) return fromActive;
    const firstNotebook = notes.find(
      (note) => note.kind === 'notebook' && !note.archived && !note.trashed
    );
    return firstNotebook ? firstNotebook.id : null;
  }, [activeNoteId, findNotebookOf, notes]);

  const handleCreateNotebook = useCallback(async () => {
    if (!window.heracles?.notes) return;
    const created = await window.heracles.notes.create({
      title: 'New notebook',
      kind: 'notebook'
    });
    const refreshed = await window.heracles.notes.list();
    setNotes(withPendingEdits(refreshed));
    setAutoRenameId(created.id);
    pushToast('Notebook created', 'success');
  }, [pushToast, withPendingEdits]);

  const handleNewNote = useCallback(async () => {
    if (!window.heracles?.notes) return;

    const created = await window.heracles.notes.create({
      title: 'Untitled',
      content: '<p></p>',
      parentId: activeNoteId ?? resolveNotebookId()
    });
    setNotes((current) => sortNotes([created, ...current]));
    openNoteInLeaf(created.id);
    setAutoRenameId(created.id);
    pushToast('New page created', 'success');
  }, [activeNoteId, resolveNotebookId, openNoteInLeaf, pushToast]);

  const handleImportMarkdown = useCallback(async () => {
    if (!window.heracles?.notes?.importMarkdown) return;
    const result = await window.heracles.notes.importMarkdown();
    if (!result || result.canceled || result.files.length === 0) return;

    const created: Note[] = [];
    for (const file of result.files) {
      const content = markdownToEditorHtml(file.markdown);
      const note = await window.heracles.notes.create({
        title: file.name,
        content,
        parentId: activeNoteId ?? resolveNotebookId()
      });
      created.push(note);
    }
    if (created.length === 0) return;

    setNotes((current) => sortNotes([...created, ...current]));
    openNoteInLeaf(created[0].id);
    pushToast(
      created.length === 1
        ? `Imported “${created[0].title}”`
        : `Imported ${created.length} pages`,
      'success'
    );
  }, [activeNoteId, resolveNotebookId, openNoteInLeaf, pushToast]);

  const handleImportHtml = useCallback(async () => {
    if (!window.heracles?.notes?.importHtml) return;
    const result = await window.heracles.notes.importHtml();
    if (!result || result.canceled || result.files.length === 0) return;

    const created: Note[] = [];
    for (const file of result.files) {
      const content = htmlToEditorHtml(file.html);
      const note = await window.heracles.notes.create({
        title: file.name,
        content,
        parentId: activeNoteId ?? resolveNotebookId()
      });
      created.push(note);
    }
    if (created.length === 0) return;

    setNotes((current) => sortNotes([...created, ...current]));
    openNoteInLeaf(created[0].id);
    pushToast(
      created.length === 1
        ? `Imported "${created[0].title}"`
        : `Imported ${created.length} pages`,
      'success'
    );
  }, [activeNoteId, resolveNotebookId, openNoteInLeaf, pushToast]);

  // Copy a todo list's board (columns, tasks, subtasks, fields) onto a fresh
  // duplicate. Comments are not copied — they read as a dated conversation.
  const duplicateBoard = useCallback(async (sourceListId: string, destListId: string) => {
    const tasksApi = window.heracles?.tasks;
    if (!tasksApi) return;

    const [sourceColumns, sourceTasks, seededColumns] = await Promise.all([
      tasksApi.listColumns({ todoListId: sourceListId }),
      tasksApi.list({ todoListId: sourceListId }),
      tasksApi.listColumns({ todoListId: destListId })
    ]);

    const columnIdMap = new Map<string, string>();
    for (const column of sourceColumns) {
      const created = await tasksApi.createColumn({
        todoListId: destListId,
        name: column.name
      });
      columnIdMap.set(column.id, created.id);
    }
    // Drop the default columns seeded on creation, now that the real ones exist.
    for (const column of seededColumns) {
      await tasksApi.deleteColumn({ id: column.id });
    }

    // Create tasks parents-first so subtasks can reference their new parent id.
    const taskIdMap = new Map<string, string>();
    const byParent = new Map<string | null, typeof sourceTasks>();
    for (const task of sourceTasks) {
      const key = task.parentTaskId ?? null;
      const list = byParent.get(key) ?? [];
      list.push(task);
      byParent.set(key, list);
    }
    const copyLevel = async (parentKey: string | null) => {
      const level = [...(byParent.get(parentKey) ?? [])].sort(
        (a, b) => a.position - b.position
      );
      for (const task of level) {
        const created = await tasksApi.create({
          todoListId: destListId,
          columnId: task.columnId ? columnIdMap.get(task.columnId) ?? null : null,
          parentTaskId: task.parentTaskId ? taskIdMap.get(task.parentTaskId) ?? null : null,
          title: task.title,
          description: task.description,
          priority: task.priority
        });
        taskIdMap.set(task.id, created.id);
        const extras: Parameters<typeof tasksApi.update>[0] = { id: created.id };
        if (task.completed) extras.completed = true;
        if (task.dueDate) extras.dueDate = task.dueDate;
        if (task.deadline) extras.deadline = task.deadline;
        if (task.labels.length > 0) extras.labels = task.labels;
        if (task.reminders.length > 0) extras.reminders = task.reminders;
        if (task.location) extras.location = task.location;
        if (Object.keys(extras).length > 1) {
          await tasksApi.update(extras);
        }
        await copyLevel(task.id);
      }
    };
    await copyLevel(null);
  }, []);

  const handleDuplicateNote = useCallback(
    async (note: Note, target?: 'current' | 'right' | 'down') => {
      if (!window.heracles?.notes) return;

      const created = await window.heracles.notes.create({
        title: note.title ? `${note.title} (copy)` : 'Untitled',
        content: note.content,
        icon: note.icon,
        kind: note.kind,
        parentId: note.parentId ?? resolveNotebookId()
      });
      if (note.kind === 'todolist') {
        await duplicateBoard(note.id, created.id);
      }
      setNotes((current) => sortNotes([created, ...current]));

      if (target === 'right') {
        handleSplitLeaf(activeLeafId, 'right', created.id);
      } else if (target === 'down') {
        handleSplitLeaf(activeLeafId, 'down', created.id);
      } else {
        openNoteInLeaf(created.id);
      }
      pushToast('Duplicated', 'success');
    },
    [activeLeafId, duplicateBoard, handleSplitLeaf, openNoteInLeaf, pushToast, resolveNotebookId]
  );

  const handleCopyNoteLink = useCallback(
    async (note: Note) => {
      try {
        await navigator.clipboard.writeText(`heracles://note/${note.id}`);
        pushToast('Link copied to clipboard', 'success');
      } catch {
        pushToast('Could not copy link', 'danger');
      }
    },
    [pushToast]
  );

  const handleArchiveNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) return;
      const updated = await window.heracles.notes.archive({
        id: note.id,
        archived: !note.archived
      });
      if (updated) {
        replaceNote(updated);
        pushToast(updated.archived ? 'Archived' : 'Unarchived', 'info');
      }
    },
    [pushToast, replaceNote]
  );

  const handleTrashNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) return;
      const updated = await window.heracles.notes.trash({ id: note.id });
      if (updated) {
        // Trash cascades to the subtree in the DB — refresh, then close every
        // affected tab and collapse any pane that becomes empty.
        const refreshed = await window.heracles.notes.list();
        setNotes(withPendingEdits(refreshed));
        closeNoteTabsEverywhere([note.id, ...collectDescendantIds(note.id)]);
        pushToast(
          'Moved to trash',
          'info',
          {
            label: 'Undo',
            onAction: () => {
              window.heracles.notes.restore({ id: note.id }).then(async (restored) => {
                if (restored) {
                  const list = await window.heracles.notes.list();
                  setNotes(withPendingEdits(list));
                }
              });
            }
          },
          10000
        );
      }
    },
    [closeNoteTabsEverywhere, collectDescendantIds, pushToast, withPendingEdits]
  );

  const handleRestoreNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) return;
      const updated = await window.heracles.notes.restore({ id: note.id });
      if (updated) {
        // Restore cascades to the subtree (and may reparent) — refresh.
        const refreshed = await window.heracles.notes.list();
        setNotes(withPendingEdits(refreshed));
        pushToast('Restored', 'success');
      }
    },
    [pushToast, withPendingEdits]
  );

  const handleDeleteNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) return;
      const result = await window.heracles.notes.remove({ id: note.id });
      if (result && result.success === false) {
        pushToast("You can't delete your only notebook", 'danger');
        return;
      }
      if (note.kind === 'notebook') {
        // The notebook's contents were reassigned to another notebook in the DB —
        // refresh so the moved items show under their new home.
        const refreshed = await window.heracles.notes.list();
        setNotes(withPendingEdits(refreshed));
        pushToast('Notebook deleted', 'danger');
        return;
      }
      // Delete cascades to the subtree in the DB — mirror it in the UI.
      const deletedIds = result?.deletedIds ?? [note.id];
      setNotes((current) => current.filter((item) => !deletedIds.includes(item.id)));
      closeNoteTabsEverywhere(deletedIds);
      pushToast('Deleted permanently', 'danger');
    },
    [closeNoteTabsEverywhere, pushToast]
  );

  const handleEmptyTrash = useCallback(async () => {
    if (!window.heracles?.notes) return;
    const result = await window.heracles.notes.emptyTrash();
    const deletedIds = result?.deletedIds ?? [];
    setNotes((current) =>
      current.filter((note) => !note.trashed && !deletedIds.includes(note.id))
    );
    if (deletedIds.length > 0) closeNoteTabsEverywhere(deletedIds);
    pushToast('Trash emptied', 'danger');
  }, [closeNoteTabsEverywhere, pushToast]);

  const handleContentChange = useCallback(
    (noteId: string, content: string, title: string) => {
      const updatedAt = new Date().toISOString();
      setNotes((current) =>
        sortNotes(
          current.map((note) =>
            note.id === noteId ? { ...note, content, title, updatedAt } : note
          )
        )
      );
      setSaveState({ status: 'saving', noteId, lastSavedAt: updatedAt });

      if (!window.heracles?.notes) return;

      const existing = saveTimers.current.get(noteId);
      if (existing) window.clearTimeout(existing);

      const payload = { content, title };
      pendingSaves.current.set(noteId, payload);
      const timeout = window.setTimeout(async () => {
        // Read at fire time: a rename may have rewritten the pending title.
        const latest = pendingSaves.current.get(noteId) ?? payload;
        const result = await window.heracles.notes.update({ id: noteId, ...latest });
        // Clear the pending marker only after the save lands, and only if a
        // newer edit hasn't replaced it mid-flight — until then, list refreshes
        // must keep overlaying this content so the DB copy can't clobber it.
        if (pendingSaves.current.get(noteId) === latest) {
          pendingSaves.current.delete(noteId);
        }
        if (result) {
          setSaveState({ status: 'saved', noteId, lastSavedAt: result.updatedAt });
        }
        saveTimers.current.delete(noteId);
      }, 600);

      saveTimers.current.set(noteId, timeout);
    },
    []
  );

  // Flush debounced saves when the window closes so the last edit isn't lost.
  useEffect(() => {
    const flush = () => {
      if (!window.heracles?.notes) return;
      for (const [noteId, payload] of pendingSaves.current) {
        window.heracles.notes.update({ id: noteId, ...payload });
      }
      pendingSaves.current.clear();
    };
    window.addEventListener('beforeunload', flush);
    return () => window.removeEventListener('beforeunload', flush);
  }, []);

  const handleSidebarSelect = useCallback(
    (noteId: string, openInNewPane?: boolean) => {
      if (openInNewPane) {
        handleSplitLeaf(activeLeafId, 'right', noteId);
      } else {
        openNoteInLeaf(noteId);
      }
    },
    [activeLeafId, handleSplitLeaf, openNoteInLeaf]
  );

  const handleRenameNote = useCallback(
    async (noteId: string, nextTitle: string) => {
      const trimmed = nextTitle.trim() || 'Untitled';
      const target = notes.find((existing) => existing.id === noteId);
      if (!target || trimmed === target.title) return;
      const updatedAt = new Date().toISOString();
      setNotes((current) =>
        sortNotes(
          current.map((existing) =>
            existing.id === noteId ? { ...existing, title: trimmed, updatedAt } : existing
          )
        )
      );
      if (!window.heracles?.notes) return;
      // If a debounced content save is still pending, rewrite its title so the
      // upcoming save carries the rename instead of clobbering it.
      const pending = pendingSaves.current.get(noteId);
      if (pending) {
        pendingSaves.current.set(noteId, { ...pending, title: trimmed });
      }
      const result = await window.heracles.notes.update({
        id: noteId,
        title: trimmed,
        content: target.content
      });
      if (result) {
        replaceNote(result);
      }
    },
    [notes, replaceNote]
  );

  const handleSplitOpen = useCallback(
    (note: Note, target: 'right' | 'down') => {
      handleSplitLeaf(activeLeafId, target, note.id);
    },
    [activeLeafId, handleSplitLeaf]
  );

  const handleOpenInNewTab = useCallback(
    (note: Note) => {
      openNoteInLeaf(note.id);
    },
    [openNoteInLeaf]
  );

  const handleOpenInNewWindow = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes?.openWindow) {
        pushToast('New windows not supported', 'danger');
        return;
      }
      await window.heracles.notes.openWindow({ id: note.id });
    },
    [pushToast]
  );

  const handleToggleFavorite = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes?.setFavorited) return;
      const next = !note.favorited;
      const updated = await window.heracles.notes.setFavorited({
        id: note.id,
        favorited: next
      });
      if (updated) {
        replaceNote(updated);
        pushToast(next ? 'Added to Favorites' : 'Removed from Favorites', 'success');
      }
    },
    [pushToast, replaceNote]
  );

  const handleAddChildPage = useCallback(
    async (parent: Note) => {
      if (!window.heracles?.notes) return;
      const created = await window.heracles.notes.create({
        title: 'Untitled',
        content: '<p></p>',
        parentId: parent.id
      });
      setNotes((current) => sortNotes([created, ...current]));
      openNoteInLeaf(created.id);
      setAutoRenameId(created.id);
      pushToast(`Added page inside ${parent.title || 'Untitled'}`, 'success');
    },
    [openNoteInLeaf, pushToast]
  );

  const handleRequestMoveTo = useCallback((note: Note) => {
    setMoveTarget(note);
  }, []);

  const handleConfirmMoveTo = useCallback(
    async (parentId: string | null) => {
      if (!moveTarget || !window.heracles?.notes?.setParent) {
        setMoveTarget(null);
        return;
      }
      const updated = await window.heracles.notes.setParent({
        id: moveTarget.id,
        parentId
      });
      if (updated) {
        replaceNote(updated);
        pushToast('Moved', 'success');
      }
      setMoveTarget(null);
    },
    [moveTarget, pushToast, replaceNote]
  );

  const handleMovePage = useCallback(
    async (noteId: string, parentId: string | null, beforeId: string | null) => {
      if (!window.heracles?.notes?.reorder) return;
      // Auto-flip section sort to manual so the user actually sees their order.
      setSectionSort((current) => {
        if (current.private === 'manual') return current;
        const next = { ...current, private: 'manual' as SectionSort };
        try {
          localStorage.setItem('heracles-section-sort', JSON.stringify(next));
        } catch {
          // ignore
        }
        return next;
      });

      // Refresh from server: the reorder mutates many notes' positions.
      await window.heracles.notes.reorder({ id: noteId, parentId, beforeId });
      const refreshed = await window.heracles.notes.list();
      setNotes(withPendingEdits(refreshed));
    },
    [withPendingEdits]
  );

  const handleCreateSection = useCallback(
    async (parentId: string | null) => {
      if (!window.heracles?.notes) return;
      const created = await window.heracles.notes.create({
        title: 'New section',
        kind: 'section',
        parentId: parentId ?? resolveNotebookId()
      });
      const refreshed = await window.heracles.notes.list();
      setNotes(withPendingEdits(refreshed));
      setAutoRenameId(created.id);
      pushToast('Section created', 'success');
    },
    [resolveNotebookId, pushToast, withPendingEdits]
  );

  const handleCreateTodoList = useCallback(
    async (parentId: string | null) => {
      if (!window.heracles?.notes) return;
      const created = await window.heracles.notes.create({
        title: 'New todo list',
        kind: 'todolist',
        parentId: parentId ?? resolveNotebookId()
      });
      setNotes((current) => sortNotes([created, ...current]));
      openNoteInLeaf(created.id);
      setAutoRenameId(created.id);
      pushToast('Todo list created', 'success');
    },
    [resolveNotebookId, openNoteInLeaf, pushToast]
  );

  const handleAddChildTodoList = useCallback(
    async (parent: Note) => {
      if (!window.heracles?.notes) return;
      const created = await window.heracles.notes.create({
        title: 'New todo list',
        kind: 'todolist',
        parentId: parent.id
      });
      setNotes((current) => sortNotes([created, ...current]));
      openNoteInLeaf(created.id);
      setAutoRenameId(created.id);
      pushToast(`Todo list added inside ${parent.title || 'Untitled'}`, 'success');
    },
    [openNoteInLeaf, pushToast]
  );

  const handleRenameSectionLabel = useCallback((section: Section, label: string) => {
    setSectionLabels((current) => {
      const next = { ...current, [section]: label };
      try {
        localStorage.setItem('heracles-section-labels', JSON.stringify(next));
      } catch {
        // ignore
      }
      return next;
    });
  }, []);

  const handleChangeSectionSort = useCallback((section: Section, sort: SectionSort) => {
    setSectionSort((current) => {
      const next = { ...current, [section]: sort };
      try {
        localStorage.setItem('heracles-section-sort', JSON.stringify(next));
      } catch {
        // ignore quota errors
      }
      return next;
    });
  }, []);

  const switcherNotes = useMemo(() => {
    const term = switcherQuery.trim().toLowerCase();
    const base = notes.filter(
      (note) => !note.trashed && note.kind !== 'section' && note.kind !== 'notebook'
    );
    if (!term) return sortNotes(base).slice(0, 10);
    return sortNotes(
      base.filter((note) => `${note.title} ${stripHtml(note.content)}`.toLowerCase().includes(term))
    ).slice(0, 10);
  }, [notes, switcherQuery]);

  const rootIsLeaf = isLeaf(paneTree);
  const leafCount = useMemo(() => collectLeaves(paneTree).length, [paneTree]);

  return (
    <div className="app">
      <Sidebar
        notes={notes}
        activeNoteId={activeNoteId}
        onSelectNote={handleSidebarSelect}
        onCreateNote={handleNewNote}
        onImportMarkdown={handleImportMarkdown}
        onImportHtml={handleImportHtml}
        onOpenSwitcher={() => {
          setIsSwitcherOpen(true);
          setSwitcherQuery('');
          setSwitcherIndex(0);
        }}
        onEmptyTrash={handleEmptyTrash}
        onTheme={() => setTheme((current) => (current === 'dark' ? 'light' : 'dark'))}
        theme={theme}
        onRenameNote={handleRenameNote}
        onCopyNoteLink={handleCopyNoteLink}
        onDuplicateNote={handleDuplicateNote}
        onArchiveNote={handleArchiveNote}
        onTrashNote={handleTrashNote}
        onRestoreNote={handleRestoreNote}
        onDeleteNote={handleDeleteNote}
        onSplitOpen={handleSplitOpen}
        onOpenInNewTab={handleOpenInNewTab}
        onOpenInNewWindow={handleOpenInNewWindow}
        onAddChildPage={handleAddChildPage}
        onToggleFavorite={handleToggleFavorite}
        onMoveTo={handleRequestMoveTo}
        onMovePage={handleMovePage}
        onCreateSection={handleCreateSection}
        onCreateTodoList={handleCreateTodoList}
        onCreateNotebook={handleCreateNotebook}
        onAddChildTodoList={handleAddChildTodoList}
        sectionSort={sectionSort}
        onChangeSectionSort={handleChangeSectionSort}
        sectionLabels={sectionLabels}
        onRenameSectionLabel={handleRenameSectionLabel}
        autoRenameId={autoRenameId}
      />

      <main className="workspace">
        <div className="workspace__statusbar">
          <span className="workspace__leaf-count">
            {leafCount} {leafCount === 1 ? 'pane' : 'panes'}
          </span>
          <span className="workspace__autosave">
            <span className={`workspace__autosave-dot workspace__autosave-dot--${saveState.status}`} />
            {saveState.status === 'saving' && 'Saving…'}
            {saveState.status === 'saved' && `Saved ${formatTime(saveState.lastSavedAt)}`}
            {saveState.status === 'idle' && 'Autosave ready'}
          </span>
        </div>
        <div className="workspace__panes">
          <PaneTree
            node={paneTree}
            notes={notes}
            activeLeafId={activeLeafId}
            rootIsLeaf={rootIsLeaf}
            onSelectTab={handleSelectTab}
            onCloseTab={handleCloseTab}
            onContentChange={handleContentChange}
            onFocusLeaf={handleFocusLeaf}
            onSplitLeaf={handleSplitLeaf}
            onCloseLeaf={handleCloseLeaf}
            onMoveTab={handleMoveTab}
            onMoveTabToSplit={handleMoveTabToSplit}
            onOpenNoteInLeaf={(leafId, noteId) => openNoteInLeaf(noteId, leafId)}
            onUpdateNoteIcon={handleUpdateNoteIcon}
            onSetSplitRatios={handleSetSplitRatios}
            onArchiveNote={handleArchiveNote}
            onTrashNote={handleTrashNote}
            onRestoreNote={handleRestoreNote}
            onDeleteNote={handleDeleteNote}
            onDuplicateNote={handleDuplicateNote}
            onCopyNoteLink={handleCopyNoteLink}
            onNotify={pushToast}
          />
        </div>
      </main>

      {moveTarget && (
        <MovePagePicker
          note={moveTarget}
          notes={notes}
          onClose={() => setMoveTarget(null)}
          onSelect={handleConfirmMoveTo}
        />
      )}

      {isSwitcherOpen && (
        <div
          className="switcher"
          role="dialog"
          aria-modal="true"
          onClick={() => setIsSwitcherOpen(false)}
        >
          <div
            className="switcher__panel"
            onClick={(event: ReactMouseEvent<HTMLDivElement>) => event.stopPropagation()}
          >
            <input
              ref={switcherInputRef}
              className="switcher__input"
              placeholder="Search notes…"
              value={switcherQuery}
              onChange={(event: ChangeEvent<HTMLInputElement>) => setSwitcherQuery(event.target.value)}
              onKeyDown={(event: ReactKeyboardEvent<HTMLInputElement>) => {
                if (event.key === 'ArrowDown') {
                  event.preventDefault();
                  setSwitcherIndex((current) =>
                    Math.min(current + 1, Math.max(0, switcherNotes.length - 1))
                  );
                }

                if (event.key === 'ArrowUp') {
                  event.preventDefault();
                  setSwitcherIndex((current) => Math.max(current - 1, 0));
                }

                if (event.key === 'Enter') {
                  event.preventDefault();
                  const target = switcherNotes[switcherIndex];
                  if (target) {
                    openNoteInLeaf(target.id);
                    setIsSwitcherOpen(false);
                  }
                }
              }}
            />
            <div className="switcher__results">
              {switcherNotes.length === 0 && (
                <div className="switcher__empty">No matching notes</div>
              )}
              {switcherNotes.map((note, index) => (
                <button
                  key={note.id}
                  type="button"
                  className={`switcher__item ${index === switcherIndex ? 'is-active' : ''}`}
                  onClick={() => {
                    openNoteInLeaf(note.id);
                    setIsSwitcherOpen(false);
                  }}
                >
                  <div className="switcher__title">
                    {note.title || 'Untitled'}
                    {note.archived && <span className="switcher__badge">Archived</span>}
                  </div>
                  <div className="switcher__meta">Updated {formatDate(note.updatedAt)}</div>
                </button>
              ))}
            </div>
          </div>
        </div>
      )}

      {updateReady && (
        <div className="update-banner" role="status">
          <span className="update-banner__text">
            A new version{updateReady.version ? ` (${updateReady.version})` : ''} is ready.
          </span>
          <button
            type="button"
            className="update-banner__action"
            onClick={() => window.heracles?.updates?.restart()}
          >
            Restart to update
          </button>
          <button
            type="button"
            className="update-banner__dismiss"
            aria-label="Dismiss"
            onClick={() => setUpdateReady(null)}
          >
            ×
          </button>
        </div>
      )}

      <div className="toast-stack">
        {toasts.map((toast) => (
          <div key={toast.id} className={`toast toast--${toast.tone}`}>
            {toast.message}
            {toast.action && (
              <button type="button" className="toast__action" onClick={toast.action.onAction}>
                {toast.action.label}
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
};

export default App;
