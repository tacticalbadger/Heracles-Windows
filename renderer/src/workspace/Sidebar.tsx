import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type DragEvent as ReactDragEvent,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent
} from 'react';
import type { Note } from '../types/notes';
import { SidebarItemMenu } from './SidebarItemMenu';
import { SectionMenu, type SectionSort } from './SectionMenu';

export type Section = 'favorites' | 'private' | 'archive' | 'trash';

export const PAGE_DRAG_MIME = 'application/x-heracles-page';

type DropPosition = 'before' | 'inside' | 'after';

type SidebarProps = {
  notes: Note[];
  activeNoteId: string | null;
  onSelectNote: (noteId: string, openInNewPane?: boolean) => void;
  onCreateNote: () => void;
  onImportMarkdown: () => void;
  onImportHtml: () => void;
  onCreateSection: (parentId: string | null) => void;
  onCreateTodoList: (parentId: string | null) => void;
  onAddChildTodoList: (parentNote: Note) => void;
  onOpenSwitcher: () => void;
  onEmptyTrash: () => void;
  onTheme: () => void;
  theme: 'light' | 'dark';
  onRenameNote: (noteId: string, title: string) => void;
  onCopyNoteLink: (note: Note) => void;
  onDuplicateNote: (note: Note) => void;
  onArchiveNote: (note: Note) => void;
  onTrashNote: (note: Note) => void;
  onRestoreNote: (note: Note) => void;
  onDeleteNote: (note: Note) => void;
  onSplitOpen: (note: Note, target: 'right' | 'down') => void;
  onOpenInNewTab: (note: Note) => void;
  onOpenInNewWindow: (note: Note) => void;
  onAddChildPage: (parentNote: Note) => void;
  onToggleFavorite: (note: Note) => void;
  onMoveTo: (note: Note) => void;
  onMovePage: (
    noteId: string,
    parentId: string | null,
    beforeId: string | null
  ) => void;
  sectionSort: Record<Section, SectionSort>;
  onChangeSectionSort: (section: Section, sort: SectionSort) => void;
  sectionLabels: Partial<Record<Section, string>>;
  onRenameSectionLabel: (section: Section, label: string) => void;
};

type ContextMenuState = { note: Note; x: number; y: number } | null;
type SectionMenuState = { section: Section; x: number; y: number } | null;
type DragOverState = { noteId: string; position: DropPosition } | null;

const sortNotesBy = (list: Note[], sort: SectionSort): Note[] => {
  const copy = [...list];
  switch (sort) {
    case 'last-edited':
      copy.sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
      break;
    case 'created':
      copy.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
      break;
    case 'title':
      copy.sort((a, b) => (a.title || 'Untitled').localeCompare(b.title || 'Untitled'));
      break;
    case 'manual':
    default:
      copy.sort((a, b) => a.position - b.position || b.updatedAt.localeCompare(a.updatedAt));
      break;
  }
  return copy;
};

const DEFAULT_LABELS: Record<Section, string> = {
  favorites: 'Favorites',
  private: 'Private',
  archive: 'Archived',
  trash: 'Trash'
};

export const Sidebar = ({
  notes,
  activeNoteId,
  onSelectNote,
  onCreateNote,
  onImportMarkdown,
  onImportHtml,
  onCreateSection,
  onCreateTodoList,
  onAddChildTodoList,
  onOpenSwitcher,
  onEmptyTrash,
  onTheme,
  theme,
  onRenameNote,
  onCopyNoteLink,
  onDuplicateNote,
  onArchiveNote,
  onTrashNote,
  onRestoreNote,
  onDeleteNote,
  onSplitOpen,
  onOpenInNewTab,
  onOpenInNewWindow,
  onAddChildPage,
  onToggleFavorite,
  onMoveTo,
  onMovePage,
  sectionSort,
  onChangeSectionSort,
  sectionLabels,
  onRenameSectionLabel
}: SidebarProps) => {
  const [open, setOpen] = useState<Record<Section, boolean>>(() => {
    try {
      const stored = localStorage.getItem('heracles-section-open');
      if (stored) {
        const parsed = JSON.parse(stored) as Partial<Record<Section, boolean>>;
        return {
          favorites: parsed.favorites ?? true,
          private: parsed.private ?? true,
          archive: parsed.archive ?? false,
          trash: parsed.trash ?? false
        };
      }
    } catch {
      /* ignore */
    }
    return { favorites: true, private: true, archive: false, trash: false };
  });

  const [expandedIds, setExpandedIds] = useState<Set<string>>(() => {
    try {
      const stored = localStorage.getItem('heracles-expanded');
      if (stored) return new Set(JSON.parse(stored) as string[]);
    } catch {
      /* ignore */
    }
    return new Set();
  });

  const [query, setQuery] = useState('');
  const [contextMenu, setContextMenu] = useState<ContextMenuState>(null);
  const [sectionMenu, setSectionMenu] = useState<SectionMenuState>(null);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState('');
  const renameInputRef = useRef<HTMLInputElement | null>(null);

  // Built-in section label rename
  const [renamingSection, setRenamingSection] = useState<Section | null>(null);
  const [sectionDraft, setSectionDraft] = useState('');
  const sectionInputRef = useRef<HTMLInputElement | null>(null);

  // Drag state for moving pages within the sidebar
  const [dragOver, setDragOver] = useState<DragOverState>(null);

  useEffect(() => {
    try {
      localStorage.setItem('heracles-section-open', JSON.stringify(open));
    } catch {
      /* ignore */
    }
  }, [open]);

  useEffect(() => {
    try {
      localStorage.setItem('heracles-expanded', JSON.stringify(Array.from(expandedIds)));
    } catch {
      /* ignore */
    }
  }, [expandedIds]);

  useEffect(() => {
    if (renamingId && renameInputRef.current) {
      renameInputRef.current.focus();
      renameInputRef.current.select();
    }
  }, [renamingId]);

  useEffect(() => {
    if (renamingSection && sectionInputRef.current) {
      sectionInputRef.current.focus();
      sectionInputRef.current.select();
    }
  }, [renamingSection]);

  const beginRename = (note: Note) => {
    setRenameDraft(note.title || 'Untitled');
    setRenamingId(note.id);
  };

  const commitRename = () => {
    if (!renamingId) return;
    const trimmed = renameDraft.trim() || 'Untitled';
    onRenameNote(renamingId, trimmed);
    setRenamingId(null);
  };

  const cancelRename = () => setRenamingId(null);

  const beginSectionRename = (section: Section) => {
    if (section !== 'private' && section !== 'favorites') return; // only allow Private + Favorites
    setSectionDraft(sectionLabels[section] ?? DEFAULT_LABELS[section]);
    setRenamingSection(section);
  };

  const commitSectionRename = () => {
    if (!renamingSection) return;
    const trimmed = sectionDraft.trim() || DEFAULT_LABELS[renamingSection];
    onRenameSectionLabel(renamingSection, trimmed);
    setRenamingSection(null);
  };

  const cancelSectionRename = () => setRenamingSection(null);

  const childrenByParent = useMemo(() => {
    const map = new Map<string | null, Note[]>();
    for (const note of notes) {
      if (note.archived || note.trashed) continue;
      const key = note.parentId ?? null;
      const list = map.get(key) ?? [];
      list.push(note);
      map.set(key, list);
    }
    return map;
  }, [notes]);

  // Helper for drop validation: precompute descendant set for each potentially-dragged note
  const isDescendantOf = useMemo(() => {
    const childrenMap = childrenByParent;
    return (candidateId: string, ancestorId: string): boolean => {
      if (candidateId === ancestorId) return true;
      const queue: string[] = [ancestorId];
      while (queue.length) {
        const next = queue.shift();
        if (!next) break;
        const kids = childrenMap.get(next) ?? [];
        for (const k of kids) {
          if (k.id === candidateId) return true;
          queue.push(k.id);
        }
      }
      return false;
    };
  }, [childrenByParent]);

  const matches = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return null;
    return notes.filter(
      (note) =>
        note.title.toLowerCase().includes(term) || note.content.toLowerCase().includes(term)
    );
  }, [notes, query]);

  const favoriteRoots = useMemo(() => {
    const favoritedIds = new Set(
      notes
        .filter((note) => note.favorited && !note.archived && !note.trashed)
        .map((note) => note.id)
    );
    // Only show a favorited note as a top-level entry in Favorites if its
    // parent (if any) is not itself favorited — avoids duplicate rendering.
    return sortNotesBy(
      notes.filter(
        (note) =>
          note.favorited &&
          !note.archived &&
          !note.trashed &&
          (!note.parentId || !favoritedIds.has(note.parentId))
      ),
      sectionSort.favorites
    );
  }, [notes, sectionSort.favorites]);

  const privateRoots = useMemo(() => {
    const tops = (childrenByParent.get(null) ?? []).filter((note) => !note.favorited);
    return sortNotesBy(tops, sectionSort.private);
  }, [childrenByParent, sectionSort.private]);

  const archivedNotes = useMemo(
    () =>
      sortNotesBy(
        notes.filter((note) => note.archived && !note.trashed),
        sectionSort.archive
      ),
    [notes, sectionSort.archive]
  );

  const trashedNotes = useMemo(
    () => sortNotesBy(notes.filter((note) => note.trashed), sectionSort.trash),
    [notes, sectionSort.trash]
  );

  const toggleSection = (section: Section) =>
    setOpen((current) => ({ ...current, [section]: !current[section] }));

  const toggleExpanded = (noteId: string) =>
    setExpandedIds((current) => {
      const next = new Set(current);
      if (next.has(noteId)) next.delete(noteId);
      else next.add(noteId);
      return next;
    });

  const handleItemContextMenu = (note: Note, event: ReactMouseEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.stopPropagation();
    setContextMenu({ note, x: event.clientX, y: event.clientY });
  };

  // ── Drag-and-drop helpers ────────────────────────────────────────────────
  const computeDropPosition = (
    event: ReactDragEvent<HTMLDivElement>,
    target: Note
  ): DropPosition => {
    const rect = event.currentTarget.getBoundingClientRect();
    const y = event.clientY - rect.top;
    const h = rect.height;
    if (target.kind === 'section') {
      // For sections, the entire row (except small top/bottom edges) means inside
      if (y < h * 0.2) return 'before';
      if (y > h * 0.8) return 'after';
      return 'inside';
    }
    if (y < h * 0.3) return 'before';
    if (y > h * 0.7) return 'after';
    return 'inside';
  };

  const handleItemDragStart = (note: Note, event: ReactDragEvent<HTMLDivElement>) => {
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData(PAGE_DRAG_MIME, note.id);
    event.dataTransfer.setData('text/plain', note.title || 'Untitled');
  };

  const handleItemDragOver = (
    note: Note,
    event: ReactDragEvent<HTMLDivElement>
  ) => {
    if (!event.dataTransfer.types.includes(PAGE_DRAG_MIME)) return;
    event.preventDefault();
    event.stopPropagation();
    event.dataTransfer.dropEffect = 'move';
    const position = computeDropPosition(event, note);
    if (
      !dragOver ||
      dragOver.noteId !== note.id ||
      dragOver.position !== position
    ) {
      setDragOver({ noteId: note.id, position });
    }
  };

  const handleItemDragLeave = (event: ReactDragEvent<HTMLDivElement>) => {
    // Ignore non-page drags (e.g. editor tabs being dragged across the window).
    if (!event.dataTransfer.types.includes(PAGE_DRAG_MIME)) return;
    if (event.currentTarget.contains(event.relatedTarget as Node)) return;
    // Capture the leaving note id synchronously — currentTarget is nulled
    // by the time React runs the setState updater.
    const leavingNoteId = (event.currentTarget as HTMLElement).dataset.noteId;
    setDragOver((current) =>
      current && current.noteId === leavingNoteId ? null : current
    );
  };

  const handleItemDrop = (target: Note, event: ReactDragEvent<HTMLDivElement>) => {
    if (!event.dataTransfer.types.includes(PAGE_DRAG_MIME)) return;
    const sourceId = event.dataTransfer.getData(PAGE_DRAG_MIME);
    event.preventDefault();
    event.stopPropagation();
    setDragOver(null);
    if (!sourceId || sourceId === target.id) return;
    if (isDescendantOf(target.id, sourceId)) return; // can't drop into own subtree

    const position = computeDropPosition(event, target);

    if (position === 'inside') {
      onMovePage(sourceId, target.id, null);
      setExpandedIds((current) => new Set(current).add(target.id));
      return;
    }

    // before/after: place as sibling of target
    const newParentId = target.parentId ?? null;
    const siblings = (childrenByParent.get(newParentId) ?? []).filter(
      (n) => n.id !== sourceId
    );
    const sortedSiblings = sortNotesBy(siblings, sectionSort.private);
    const targetIdx = sortedSiblings.findIndex((n) => n.id === target.id);
    if (targetIdx < 0) {
      onMovePage(sourceId, newParentId, null);
      return;
    }
    if (position === 'before') {
      onMovePage(sourceId, newParentId, target.id);
    } else {
      const next = sortedSiblings[targetIdx + 1];
      onMovePage(sourceId, newParentId, next ? next.id : null);
    }
  };

  // Drop on a section body's empty area / end → root of that section
  const handleSectionBodyDragOver = (event: ReactDragEvent<HTMLDivElement>) => {
    if (!event.dataTransfer.types.includes(PAGE_DRAG_MIME)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
  };

  const handleSectionBodyDrop = (event: ReactDragEvent<HTMLDivElement>) => {
    if (!event.dataTransfer.types.includes(PAGE_DRAG_MIME)) return;
    const sourceId = event.dataTransfer.getData(PAGE_DRAG_MIME);
    event.preventDefault();
    setDragOver(null);
    if (!sourceId) return;
    onMovePage(sourceId, null, null);
  };

  const renderTreeItem = (note: Note, depth: number): JSX.Element => {
    const isActive = note.id === activeNoteId;
    const isRenaming = note.id === renamingId;
    const childList = childrenByParent.get(note.id) ?? [];
    const sortedChildren = sortNotesBy(childList, sectionSort.private);
    const hasChildren = sortedChildren.length > 0;
    const isExpanded = expandedIds.has(note.id);
    const isSection = note.kind === 'section';

    const dragHere = dragOver && dragOver.noteId === note.id ? dragOver.position : null;

    return (
      <div key={note.id} className="sidebar__tree-node">
        <div
          data-note-id={note.id}
          className={`sidebar__item ${isActive ? 'is-active' : ''} ${
            isSection ? 'sidebar__item--section' : ''
          } ${dragHere ? `is-drop-${dragHere}` : ''}`}
          style={{ paddingLeft: 6 + depth * 14 }}
          draggable={!isRenaming}
          onDragStart={(event) => handleItemDragStart(note, event)}
          onDragOver={(event) => handleItemDragOver(note, event)}
          onDragLeave={handleItemDragLeave}
          onDrop={(event) => handleItemDrop(note, event)}
          onContextMenu={(event) => handleItemContextMenu(note, event)}
          onClick={(event: ReactMouseEvent<HTMLDivElement>) => {
            if (isRenaming) return;
            if (isSection) {
              // Toggle expand on click for sections
              toggleExpanded(note.id);
              return;
            }
            if (event.altKey) {
              onSplitOpen(note, 'right');
              return;
            }
            onSelectNote(note.id, event.metaKey || event.ctrlKey);
          }}
          onDoubleClick={(event) => {
            event.preventDefault();
            if (!note.trashed) beginRename(note);
          }}
          title={
            isSection
              ? 'Click to expand · Right-click for options · Double-click to rename'
              : 'Click to open · ⌘/Ctrl+Click to split right · ⌥+Click for side peek · Double-click to rename'
          }
          role="button"
          tabIndex={0}
        >
          <button
            type="button"
            className={`sidebar__item-twisty ${
              hasChildren || isSection ? 'has-children' : ''
            } ${isExpanded ? 'is-open' : ''}`}
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
              if (hasChildren || isSection) toggleExpanded(note.id);
            }}
            tabIndex={-1}
            aria-label={hasChildren ? (isExpanded ? 'Collapse' : 'Expand') : undefined}
          >
            {hasChildren || isSection ? '›' : ''}
          </button>
          <span className="sidebar__item-icon" aria-hidden>
            {note.icon ||
              (isSection ? '◈' : note.kind === 'todolist' ? '☰' : '▦')}
          </span>
          {isRenaming ? (
            <input
              ref={renameInputRef}
              className="sidebar__item-rename"
              value={renameDraft}
              onChange={(event: ChangeEvent<HTMLInputElement>) =>
                setRenameDraft(event.target.value)
              }
              onClick={(event) => event.stopPropagation()}
              onBlur={commitRename}
              onKeyDown={(event: ReactKeyboardEvent<HTMLInputElement>) => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  commitRename();
                } else if (event.key === 'Escape') {
                  event.preventDefault();
                  cancelRename();
                }
              }}
            />
          ) : (
            <span
              className={`sidebar__item-title ${
                isSection ? 'sidebar__item-title--section' : ''
              }`}
            >
              {note.title || (isSection ? 'New section' : 'Untitled')}
            </span>
          )}
          {!isRenaming && (
            <>
              <button
                type="button"
                className="sidebar__item-action"
                title="More options"
                aria-label={`Options for ${note.title || 'Untitled'}`}
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  const rect = event.currentTarget.getBoundingClientRect();
                  setContextMenu({ note, x: rect.right, y: rect.bottom + 4 });
                }}
              >
                ⋯
              </button>
              <button
                type="button"
                className="sidebar__item-action"
                title={isSection ? 'Add a page in this section' : 'Add a page inside'}
                aria-label={`Add page inside ${note.title || 'Untitled'}`}
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  setExpandedIds((current) => new Set(current).add(note.id));
                  onAddChildPage(note);
                }}
              >
                ＋
              </button>
            </>
          )}
        </div>
        {(hasChildren || isSection) && isExpanded && (
          <div className="sidebar__tree-children">
            {sortedChildren.map((child) => renderTreeItem(child, depth + 1))}
          </div>
        )}
      </div>
    );
  };

  const renderFlatItem = (note: Note): JSX.Element => {
    const isActive = note.id === activeNoteId;

    return (
      <div
        key={note.id}
        className={`sidebar__item ${isActive ? 'is-active' : ''}`}
        onContextMenu={(event) => handleItemContextMenu(note, event)}
        onClick={(event: ReactMouseEvent<HTMLDivElement>) =>
          onSelectNote(note.id, event.metaKey || event.ctrlKey)
        }
        role="button"
        tabIndex={0}
      >
        <span className="sidebar__item-twisty" />
        <span className="sidebar__item-icon" aria-hidden>
          {note.icon || '▦'}
        </span>
        <span className="sidebar__item-title">{note.title || 'Untitled'}</span>
        <button
          type="button"
          className="sidebar__item-action"
          title="More options"
          onClick={(event) => {
            event.preventDefault();
            event.stopPropagation();
            const rect = event.currentTarget.getBoundingClientRect();
            setContextMenu({ note, x: rect.right, y: rect.bottom + 4 });
          }}
        >
          ⋯
        </button>
      </div>
    );
  };

  const renderSectionHeader = (
    section: Section,
    label: string,
    count: number,
    isOpen: boolean,
    extraAction?: { icon: string; title: string; onClick: () => void }
  ) => {
    const isRenamingThis = renamingSection === section;
    return (
      <div className={`sidebar__section-header-row ${isOpen ? 'is-open' : ''}`}>
        <button
          type="button"
          className="sidebar__section-header"
          onClick={() => toggleSection(section)}
          onDoubleClick={(event) => {
            event.preventDefault();
            event.stopPropagation();
            if (section === 'private' || section === 'favorites') {
              beginSectionRename(section);
            }
          }}
        >
          <span className={`sidebar__chevron ${isOpen ? 'is-open' : ''}`}>›</span>
          {isRenamingThis ? (
            <input
              ref={sectionInputRef}
              className="sidebar__section-rename"
              value={sectionDraft}
              onChange={(event: ChangeEvent<HTMLInputElement>) =>
                setSectionDraft(event.target.value)
              }
              onClick={(event) => event.stopPropagation()}
              onBlur={commitSectionRename}
              onKeyDown={(event: ReactKeyboardEvent<HTMLInputElement>) => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  commitSectionRename();
                } else if (event.key === 'Escape') {
                  event.preventDefault();
                  cancelSectionRename();
                }
              }}
            />
          ) : (
            <span className="sidebar__section-label">{label}</span>
          )}
          <span className="sidebar__section-count">{count}</span>
        </button>
        <button
          type="button"
          className="sidebar__section-action"
          title="Section options"
          aria-label={`${label} options`}
          onClick={(event) => {
            event.stopPropagation();
            const rect = event.currentTarget.getBoundingClientRect();
            setSectionMenu({ section, x: rect.right, y: rect.bottom + 4 });
          }}
        >
          ⋯
        </button>
        {extraAction && (
          <button
            type="button"
            className="sidebar__section-action"
            title={extraAction.title}
            aria-label={extraAction.title}
            onClick={(event) => {
              event.stopPropagation();
              extraAction.onClick();
            }}
          >
            {extraAction.icon}
          </button>
        )}
      </div>
    );
  };

  const labelOf = (section: Section): string =>
    sectionLabels[section] ?? DEFAULT_LABELS[section];

  return (
    <aside className="sidebar">
      <div className="sidebar__top">
        <div className="sidebar__brand">
          <span className="sidebar__brand-mark">H</span>
          <span className="sidebar__brand-name">Heracles</span>
          <button
            type="button"
            className="sidebar__theme"
            onClick={onTheme}
            title="Toggle theme"
            aria-label="Toggle theme"
          >
            {theme === 'dark' ? '☾' : '☀'}
          </button>
        </div>
        <button type="button" className="sidebar__row" onClick={onOpenSwitcher}>
          <span className="sidebar__row-icon">⌕</span>
          <span className="sidebar__row-label">Search</span>
          <span className="sidebar__row-shortcut">⌘K</span>
        </button>
        <button type="button" className="sidebar__row" onClick={onCreateNote}>
          <span className="sidebar__row-icon">＋</span>
          <span className="sidebar__row-label">New page</span>
        </button>
        <button type="button" className="sidebar__row" onClick={onImportMarkdown}>
          <span className="sidebar__row-icon">⤓</span>
          <span className="sidebar__row-label">New page from Markdown</span>
        </button>
        <button type="button" className="sidebar__row" onClick={onImportHtml}>
          <span className="sidebar__row-icon">⤓</span>
          <span className="sidebar__row-label">New page from HTML</span>
        </button>
        <button
          type="button"
          className="sidebar__row"
          onClick={() => onCreateTodoList(null)}
        >
          <span className="sidebar__row-icon">☰</span>
          <span className="sidebar__row-label">New todo list</span>
        </button>
        <button
          type="button"
          className="sidebar__row"
          onClick={() => onCreateSection(null)}
        >
          <span className="sidebar__row-icon">◈</span>
          <span className="sidebar__row-label">New section</span>
        </button>
      </div>

      <div className="sidebar__filter">
        <input
          className="sidebar__filter-input"
          type="text"
          placeholder="Filter pages…"
          value={query}
          onChange={(event: ChangeEvent<HTMLInputElement>) => setQuery(event.target.value)}
        />
      </div>

      <div className="sidebar__scroll">
        {matches ? (
          <div className="sidebar__section">
            <div className="sidebar__section-header sidebar__section-header--static">
              <span className="sidebar__section-label">Results</span>
              <span className="sidebar__section-count">{matches.length}</span>
            </div>
            <div className="sidebar__section-body">
              {matches.length === 0 && <div className="sidebar__empty">No matches</div>}
              {matches.map(renderFlatItem)}
            </div>
          </div>
        ) : (
          <>
            {favoriteRoots.length > 0 && (
              <div className="sidebar__section">
                {renderSectionHeader(
                  'favorites',
                  labelOf('favorites'),
                  favoriteRoots.length,
                  open.favorites
                )}
                {open.favorites && (
                  <div
                    className="sidebar__section-body"
                    onDragOver={handleSectionBodyDragOver}
                  >
                    {favoriteRoots.map((note) => renderTreeItem(note, 0))}
                  </div>
                )}
              </div>
            )}

            <div className="sidebar__section">
              {renderSectionHeader(
                'private',
                labelOf('private'),
                privateRoots.length,
                open.private,
                {
                  icon: '＋',
                  title: 'Add a page',
                  onClick: onCreateNote
                }
              )}
              {open.private && (
                <div
                  className="sidebar__section-body"
                  onDragOver={handleSectionBodyDragOver}
                  onDrop={handleSectionBodyDrop}
                >
                  {privateRoots.length === 0 && (
                    <div className="sidebar__empty">No pages yet</div>
                  )}
                  {privateRoots.map((note) => renderTreeItem(note, 0))}
                </div>
              )}
            </div>

            {archivedNotes.length > 0 && (
              <div className="sidebar__section">
                {renderSectionHeader(
                  'archive',
                  labelOf('archive'),
                  archivedNotes.length,
                  open.archive
                )}
                {open.archive && (
                  <div className="sidebar__section-body">
                    {archivedNotes.map(renderFlatItem)}
                  </div>
                )}
              </div>
            )}

            <div className="sidebar__section sidebar__section--bottom">
              {renderSectionHeader(
                'trash',
                labelOf('trash'),
                trashedNotes.length,
                open.trash
              )}
              {open.trash && (
                <div className="sidebar__section-body">
                  {trashedNotes.length === 0 && <div className="sidebar__empty">Empty</div>}
                  {trashedNotes.map(renderFlatItem)}
                  {trashedNotes.length > 0 && (
                    <button
                      type="button"
                      className="sidebar__row sidebar__row--danger"
                      onClick={onEmptyTrash}
                    >
                      <span className="sidebar__row-icon">⌫</span>
                      <span className="sidebar__row-label">Empty trash</span>
                    </button>
                  )}
                </div>
              )}
            </div>
          </>
        )}
      </div>

      {contextMenu && (
        <SidebarItemMenu
          note={contextMenu.note}
          x={contextMenu.x}
          y={contextMenu.y}
          onClose={() => setContextMenu(null)}
          onCopyLink={onCopyNoteLink}
          onDuplicate={onDuplicateNote}
          onRename={(note) => beginRename(note)}
          onArchive={onArchiveNote}
          onTrash={onTrashNote}
          onRestore={onRestoreNote}
          onDelete={onDeleteNote}
          onSplitOpen={onSplitOpen}
          onOpenInNewTab={onOpenInNewTab}
          onOpenInNewWindow={onOpenInNewWindow}
          onToggleFavorite={onToggleFavorite}
          onMoveTo={onMoveTo}
          onAddChildPage={onAddChildPage}
          onAddChildTodoList={onAddChildTodoList}
        />
      )}

      {sectionMenu && (
        <SectionMenu
          x={sectionMenu.x}
          y={sectionMenu.y}
          currentSort={sectionSort[sectionMenu.section]}
          onSelectSort={(sort) => onChangeSectionSort(sectionMenu.section, sort)}
          onHide={() => {
            const section = sectionMenu.section;
            setOpen((current) => ({ ...current, [section]: false }));
          }}
          onClose={() => setSectionMenu(null)}
          allowRename={
            sectionMenu.section === 'private' || sectionMenu.section === 'favorites'
          }
          onRename={() => {
            const section = sectionMenu.section;
            setSectionMenu(null);
            beginSectionRename(section);
          }}
          onCreateNote={
            sectionMenu.section === 'private' || sectionMenu.section === 'favorites'
              ? onCreateNote
              : undefined
          }
          onCreateTodoList={
            sectionMenu.section === 'private' || sectionMenu.section === 'favorites'
              ? () => onCreateTodoList(null)
              : undefined
          }
        />
      )}
    </aside>
  );
};

export default Sidebar;
