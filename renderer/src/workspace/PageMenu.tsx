import { useEffect, useRef } from 'react';
import type { Note } from '../types/notes';

type PageMenuProps = {
  note: Note;
  anchorRect: DOMRect | null;
  onClose: () => void;
  onCopyLink: (note: Note) => void;
  onDuplicate: (note: Note, target?: 'current' | 'right' | 'down') => void;
  onArchive: (note: Note) => void;
  onTrash: (note: Note) => void;
  onRestore: (note: Note) => void;
  onDelete: (note: Note) => void;
  onSplitOpen: (note: Note, target: 'right' | 'down') => void;
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

export const PageMenu = ({
  note,
  anchorRect,
  onClose,
  onCopyLink,
  onDuplicate,
  onArchive,
  onTrash,
  onRestore,
  onDelete,
  onSplitOpen
}: PageMenuProps) => {
  const ref = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const handle = (event: MouseEvent) => {
      if (ref.current?.contains(event.target as Node)) return;
      onClose();
    };
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('mousedown', handle);
    window.addEventListener('keydown', handleKey);
    return () => {
      window.removeEventListener('mousedown', handle);
      window.removeEventListener('keydown', handleKey);
    };
  }, [onClose]);

  const top = anchorRect ? anchorRect.bottom + 6 : 80;
  const right = anchorRect ? Math.max(8, window.innerWidth - anchorRect.right) : 8;

  const isTrashed = note.trashed;
  const isArchived = note.archived;

  const run = (fn: () => void) => () => {
    fn();
    onClose();
  };

  return (
    <div
      className="page-menu"
      ref={ref}
      style={{ top, right }}
      role="menu"
      onClick={(event) => event.stopPropagation()}
    >
      <div className="page-menu__title">Page</div>

      {!isTrashed && (
        <>
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
            onClick={run(() => onDuplicate(note, 'current'))}
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
            onClick={run(() => onSplitOpen(note, 'right'))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⊞
            </span>
            <span className="page-menu__label">Open in side peek</span>
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

          <button
            type="button"
            className="page-menu__item"
            onClick={run(() => onDuplicate(note, 'right'))}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⊞
            </span>
            <span className="page-menu__label">Duplicate to side</span>
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

export default PageMenu;
