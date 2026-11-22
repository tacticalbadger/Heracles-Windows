import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { Note } from '../types/notes';

type SidebarItemMenuProps = {
  note: Note;
  x: number;
  y: number;
  onClose: () => void;
  onCopyLink: (note: Note) => void;
  onDuplicate: (note: Note) => void;
  onRename: (note: Note) => void;
  onArchive: (note: Note) => void;
  onTrash: (note: Note) => void;
  onRestore: (note: Note) => void;
  onDelete: (note: Note) => void;
  onSplitOpen: (note: Note, target: 'right' | 'down') => void;
  onOpenInNewTab: (note: Note) => void;
  onOpenInNewWindow: (note: Note) => void;
  onToggleFavorite: (note: Note) => void;
  onMoveTo: (note: Note) => void;
  onAddChildPage: (parent: Note) => void;
  onAddChildTodoList: (parent: Note) => void;
};

const formatTimestamp = (value: string) => {
  const date = new Date(value);
  const now = new Date();
  const sameDay =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();
  const time = date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
  if (sameDay) return `Today at ${time}`;
  return `${date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} at ${time}`;
};

export const SidebarItemMenu = ({
  note,
  x,
  y,
  onClose,
  onCopyLink,
  onDuplicate,
  onRename,
  onArchive,
  onTrash,
  onRestore,
  onDelete,
  onSplitOpen,
  onOpenInNewTab,
  onOpenInNewWindow,
  onToggleFavorite,
  onMoveTo,
  onAddChildPage,
  onAddChildTodoList
}: SidebarItemMenuProps) => {
  const ref = useRef<HTMLDivElement | null>(null);
  const [position, setPosition] = useState({ left: x, top: y });

  useLayoutEffect(() => {
    if (!ref.current) return;
    const rect = ref.current.getBoundingClientRect();
    const margin = 8;
    let left = x;
    let top = y;
    if (left + rect.width + margin > window.innerWidth) {
      left = window.innerWidth - rect.width - margin;
    }
    if (top + rect.height + margin > window.innerHeight) {
      top = window.innerHeight - rect.height - margin;
    }
    setPosition({ left: Math.max(margin, left), top: Math.max(margin, top) });
  }, [x, y]);

  useEffect(() => {
    const handleDown = (event: MouseEvent) => {
      if (ref.current?.contains(event.target as Node)) return;
      onClose();
    };
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('mousedown', handleDown);
    window.addEventListener('keydown', handleKey);
    return () => {
      window.removeEventListener('mousedown', handleDown);
      window.removeEventListener('keydown', handleKey);
    };
  }, [onClose]);

  const isTrashed = note.trashed;
  const isArchived = note.archived;

  const run = (fn: () => void) => () => {
    fn();
    onClose();
  };

  return (
    <div
      ref={ref}
      className="page-menu"
      role="menu"
      style={{ left: position.left, top: position.top, right: 'auto' }}
      onClick={(event) => event.stopPropagation()}
      onContextMenu={(event) => event.preventDefault()}
    >
      <div className="page-menu__title">Page</div>

      {!isTrashed && (
        <>
          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onToggleFavorite(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              {note.favorited ? '★' : '☆'}
            </span>
            <span className="page-menu__label">
              {note.favorited ? 'Remove from Favorites' : 'Add to Favorites'}
            </span>
          </button>

          <div className="page-menu__divider" />

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onCopyLink(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⌁
            </span>
            <span className="page-menu__label">Copy link</span>
          </button>

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onDuplicate(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⎘
            </span>
            <span className="page-menu__label">Duplicate</span>
            <span className="page-menu__shortcut">⌘D</span>
          </button>

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onRename(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ✎
            </span>
            <span className="page-menu__label">Rename</span>
            <span className="page-menu__shortcut">⌘⇧R</span>
          </button>

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onMoveTo(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ↪
            </span>
            <span className="page-menu__label">Move to</span>
            <span className="page-menu__shortcut">⌘⇧P</span>
          </button>

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onAddChildPage(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ＋
            </span>
            <span className="page-menu__label">Add a page inside</span>
          </button>

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onAddChildTodoList(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ☰
            </span>
            <span className="page-menu__label">Add a todo list inside</span>
          </button>

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onArchive(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              {isArchived ? '↺' : '🗄'}
            </span>
            <span className="page-menu__label">{isArchived ? 'Unarchive' : 'Archive'}</span>
          </button>

          <button
            type="button"
            className="page-menu__item page-menu__item--danger"
            onClick={run(() => onTrash(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⌫
            </span>
            <span className="page-menu__label">Move to Trash</span>
          </button>

          <div className="page-menu__divider" />

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onOpenInNewTab(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ↗
            </span>
            <span className="page-menu__label">Open in new tab</span>
            <span className="page-menu__shortcut">⌘⇧↵</span>
          </button>

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onOpenInNewWindow(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⧉
            </span>
            <span className="page-menu__label">Open in new window</span>
          </button>

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onSplitOpen(note, 'right'))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⊞
            </span>
            <span className="page-menu__label">Open in side peek</span>
            <span className="page-menu__shortcut">⌥Click</span>
          </button>

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onSplitOpen(note, 'down'))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⊟
            </span>
            <span className="page-menu__label">Open below</span>
          </button>
        </>
      )}

      {isTrashed && (
        <>
          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onRestore(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ↺
            </span>
            <span className="page-menu__label">Restore</span>
          </button>

          <button
            type="button"
            className="page-menu__item page-menu__item--danger"
            onClick={run(() => onDelete(note))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⨯
            </span>
            <span className="page-menu__label">Delete permanently</span>
          </button>
        </>
      )}

      <div className="page-menu__footer">Last edited {formatTimestamp(note.updatedAt)}</div>
    </div>
  );
};

export default SidebarItemMenu;
