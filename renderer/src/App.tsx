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

const stripHtml = (value: string) => value.replace(/<[^>]*>/g, ' ');

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
  const [paneTree, setPaneTree] = useState<PaneNode>(() => createLeaf());
  const [activeLeafId, setActiveLeafId] = useState<string>(() => firstLeaf(createLeaf()).id);
  const [isSwitcherOpen, setIsSwitcherOpen] = useState(false);
  const [switcherQuery, setSwitcherQuery] = useState('');
  const [switcherIndex, setSwitcherIndex] = useState(0);
  const [saveState, setSaveState] = useState<SaveState>({ status: 'idle' });
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [moveTarget, setMoveTarget] = useState<Note | null>(null);
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
  const switcherInputRef = useRef<HTMLInputElement | null>(null);
  const saveTimers = useRef(new Map<string, number>());
  const initRan = useRef(false);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem('heracles-theme', theme);
  }, [theme]);

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
      if (initial.length === 0) {
        const seed = await window.heracles.notes.create({
          title: 'Welcome to Heracles',
          content:
            '<h1>Welcome to Heracles</h1><p>Use / to open the slash menu.</p><p>Use ⌘K to search.</p>'
        });
        initial = [seed];
      }

      const sorted = sortNotes(initial);

      // Allow main process to seed an initial note via location hash (#note=ID).
      const hashMatch = window.location.hash.match(/note=([^&]+)/);
      const requestedNoteId = hashMatch ? decodeURIComponent(hashMatch[1]) : null;
      const firstId =
        (requestedNoteId && sorted.find((note) => note.id === requestedNoteId)?.id) ||
        sorted[0]?.id ||
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
        setNotes((current) =>
          sortNotes(current.map((note) => (note.id === noteId ? result : note)))
        );
      }
    },
    []
  );

  // Note CRUD
  const replaceNote = useCallback((updated: Note) => {
    setNotes((current) =>
      sortNotes(current.map((note) => (note.id === updated.id ? updated : note)))
    );
  }, []);

  const removeNoteEverywhere = useCallback(
    (noteId: string) => {
      setNotes((current) => current.filter((note) => note.id !== noteId));
      setPaneTree((current) => {
        const next = removeNoteFromTree(current, noteId);
        const safe = ensureLeaf(next);
        setActiveLeafId(ensureActiveLeafExists(safe));
        return safe;
      });
    },
    [ensureActiveLeafExists]
  );

  const handleNewNote = useCallback(async () => {
    if (!window.heracles?.notes) return;

    const created = await window.heracles.notes.create({ title: 'Untitled', content: '<p></p>' });
    setNotes((current) => sortNotes([created, ...current]));
    openNoteInLeaf(created.id);
    pushToast('New page created', 'success');
  }, [openNoteInLeaf, pushToast]);

  const handleDuplicateNote = useCallback(
    async (note: Note, target?: 'current' | 'right' | 'down') => {
      if (!window.heracles?.notes) return;

      const created = await window.heracles.notes.create({
        title: note.title ? `${note.title} (copy)` : 'Untitled',
        content: note.content,
        icon: note.icon
      });
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
    [activeLeafId, handleSplitLeaf, openNoteInLeaf, pushToast]
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
        replaceNote(updated);
        pushToast(
          'Moved to trash',
          'info',
          {
            label: 'Undo',
            onAction: () => {
              window.heracles.notes.restore({ id: note.id }).then((restored) => {
                if (restored) replaceNote(restored);
              });
            }
          },
          10000
        );
      }
    },
    [pushToast, replaceNote]
  );

  const handleRestoreNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) return;
      const updated = await window.heracles.notes.restore({ id: note.id });
      if (updated) {
        replaceNote(updated);
        pushToast('Restored', 'success');
      }
    },
    [pushToast, replaceNote]
  );

  const handleDeleteNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) return;
      await window.heracles.notes.remove({ id: note.id });
      removeNoteEverywhere(note.id);
      pushToast('Deleted permanently', 'danger');
    },
    [pushToast, removeNoteEverywhere]
  );

  const handleEmptyTrash = useCallback(async () => {
    if (!window.heracles?.notes) return;
    await window.heracles.notes.emptyTrash();
    setNotes((current) => current.filter((note) => !note.trashed));
    pushToast('Trash emptied', 'danger');
  }, [pushToast]);

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

      const timeout = window.setTimeout(async () => {
        const result = await window.heracles.notes.update({ id: noteId, content, title });
        if (result) {
          setSaveState({ status: 'saved', noteId, lastSavedAt: result.updatedAt });
        }
        saveTimers.current.delete(noteId);
      }, 600);

      saveTimers.current.set(noteId, timeout);
    },
    []
  );

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
      setNotes(refreshed);
    },
    []
  );

  const handleCreateSection = useCallback(
    async (parentId: string | null) => {
      if (!window.heracles?.notes) return;
      const created = await window.heracles.notes.create({
        title: 'New section',
        kind: 'section',
        parentId
      });
      const refreshed = await window.heracles.notes.list();
      setNotes(refreshed);
      pushToast('Section created', 'success');
      // No auto-open since sections aren't opened as tabs
      void created;
    },
    [pushToast]
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
    const base = notes.filter((note) => !note.trashed && note.kind !== 'section');
    if (!term) return sortNotes(base).slice(0, 10);
    return sortNotes(
      base.filter((note) => `${note.title} ${stripHtml(note.content)}`.toLowerCase().includes(term))
    ).slice(0, 10);
  }, [notes, switcherQuery]);

  const activeNoteId = useMemo(() => {
    const leaf = getLeaf(paneTree, activeLeafId);
    return leaf?.activeTabId ?? null;
  }, [paneTree, activeLeafId]);

  const rootIsLeaf = isLeaf(paneTree);
  const leafCount = useMemo(() => collectLeaves(paneTree).length, [paneTree]);

  return (
    <div className="app">
      <Sidebar
        notes={notes}
        activeNoteId={activeNoteId}
        onSelectNote={handleSidebarSelect}
        onCreateNote={handleNewNote}
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
        sectionSort={sectionSort}
        onChangeSectionSort={handleChangeSectionSort}
        sectionLabels={sectionLabels}
        onRenameSectionLabel={handleRenameSectionLabel}
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
