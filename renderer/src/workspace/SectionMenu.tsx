import { useEffect, useLayoutEffect, useRef, useState } from 'react';

export type SectionSort = 'manual' | 'last-edited' | 'created' | 'title';

const SORT_OPTIONS: Array<{ value: SectionSort; label: string }> = [
  { value: 'manual', label: 'Manual' },
  { value: 'last-edited', label: 'Last edited' },
  { value: 'created', label: 'Created' },
  { value: 'title', label: 'Title' }
];

type SectionMenuProps = {
  x: number;
  y: number;
  currentSort: SectionSort;
  onSelectSort: (sort: SectionSort) => void;
  onHide: () => void;
  onClose: () => void;
  allowRename?: boolean;
  onRename?: () => void;
  renameLabel?: string;
  hideLabel?: string;
  onCreateNote?: () => void;
  onCreateTodoList?: () => void;
  onCreateSection?: () => void;
  onDelete?: () => void;
  deleteLabel?: string;
};

export const SectionMenu = ({
  x,
  y,
  currentSort,
  onSelectSort,
  onHide,
  onClose,
  allowRename = false,
  onRename,
  renameLabel = 'Rename section',
  hideLabel = 'Hide section',
  onCreateNote,
  onCreateTodoList,
  onCreateSection,
  onDelete,
  deleteLabel = 'Delete'
}: SectionMenuProps) => {
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

  const run = (fn: () => void) => () => {
    fn();
    onClose();
  };

  return (
    <div
      ref={ref}
      className="page-menu page-menu--narrow"
      role="menu"
      style={{ left: position.left, top: position.top, right: 'auto' }}
      onClick={(event) => event.stopPropagation()}
    >
      {(onCreateNote || onCreateTodoList || onCreateSection) && (
        <>
          <div className="page-menu__title">Add</div>
          {onCreateNote && (
            <button
              type="button"
              className="page-menu__item"
              onClick={run(onCreateNote)}
              role="menuitem"
            >
              <span className="page-menu__icon" aria-hidden>
                ＋
              </span>
              <span className="page-menu__label">New page</span>
            </button>
          )}
          {onCreateTodoList && (
            <button
              type="button"
              className="page-menu__item"
              onClick={run(onCreateTodoList)}
              role="menuitem"
            >
              <span className="page-menu__icon" aria-hidden>
                ☰
              </span>
              <span className="page-menu__label">New todo list</span>
            </button>
          )}
          {onCreateSection && (
            <button
              type="button"
              className="page-menu__item"
              onClick={run(onCreateSection)}
              role="menuitem"
            >
              <span className="page-menu__icon" aria-hidden>
                ◈
              </span>
              <span className="page-menu__label">New section</span>
            </button>
          )}
          <div className="page-menu__divider" />
        </>
      )}
      <div className="page-menu__title">Sort by</div>
      {SORT_OPTIONS.map((option) => (
        <button
          key={option.value}
          type="button"
          className={`page-menu__item ${option.value === currentSort ? 'is-active' : ''}`}
          onClick={run(() => onSelectSort(option.value))}
          role="menuitem"
        >
          <span className="page-menu__icon" aria-hidden>
            {option.value === currentSort ? '✓' : ''}
          </span>
          <span className="page-menu__label">{option.label}</span>
        </button>
      ))}
      <div className="page-menu__divider" />
      {allowRename && onRename && (
        <button
          type="button"
          className="page-menu__item"
          onClick={run(onRename)}
          role="menuitem"
        >
          <span className="page-menu__icon" aria-hidden>
            ✎
          </span>
          <span className="page-menu__label">{renameLabel}</span>
        </button>
      )}
      <button
        type="button"
        className="page-menu__item"
        onClick={run(onHide)}
        role="menuitem"
      >
        <span className="page-menu__icon" aria-hidden>
          ⊘
        </span>
        <span className="page-menu__label">{hideLabel}</span>
      </button>
      {onDelete && (
        <>
          <div className="page-menu__divider" />
          <button
            type="button"
            className="page-menu__item page-menu__item--danger"
            onClick={run(onDelete)}
            role="menuitem"
          >
            <span className="page-menu__icon" aria-hidden>
              ⌫
            </span>
            <span className="page-menu__label">{deleteLabel}</span>
          </button>
        </>
      )}
    </div>
  );
};

export default SectionMenu;
