import { useEffect, useMemo, useRef, useState, type ChangeEvent, type KeyboardEvent } from 'react';
import type { Note } from '../types/notes';

type MovePagePickerProps = {
  note: Note;
  notes: Note[];
  onClose: () => void;
  onSelect: (parentId: string | null) => void;
};

const collectDescendantIds = (rootId: string, notes: Note[]): Set<string> => {
  const result = new Set<string>([rootId]);
  let added = true;
  while (added) {
    added = false;
    for (const note of notes) {
      if (note.parentId && result.has(note.parentId) && !result.has(note.id)) {
        result.add(note.id);
        added = true;
      }
    }
  }
  return result;
};

const buildBreadcrumb = (note: Note, byId: Map<string, Note>): string => {
  const trail: string[] = [];
  let current: Note | undefined = note;
  while (current) {
    trail.unshift(current.title || 'Untitled');
    current = current.parentId ? byId.get(current.parentId) : undefined;
  }
  return trail.join(' / ');
};

export const MovePagePicker = ({ note, notes, onClose, onSelect }: MovePagePickerProps) => {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);

  useEffect(() => {
    const id = window.setTimeout(() => inputRef.current?.focus(), 30);
    return () => window.clearTimeout(id);
  }, []);

  useEffect(() => {
    const handleKey = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [onClose]);

  const byId = useMemo(() => new Map(notes.map((n) => [n.id, n])), [notes]);
  const blocked = useMemo(() => collectDescendantIds(note.id, notes), [note.id, notes]);

  type Candidate = { id: string | null; label: string; subtitle?: string; icon?: string };

  const candidates = useMemo<Candidate[]>(() => {
    const eligible = notes.filter(
      (n) => !n.trashed && !n.archived && !blocked.has(n.id)
    );
    // Note: sections are valid parents (they hold pages); pages are valid parents (nesting).
    const term = query.trim().toLowerCase();
    const filtered = !term
      ? eligible
      : eligible.filter((n) => (n.title || 'Untitled').toLowerCase().includes(term));
    const sortedNotes = [...filtered]
      .sort((a, b) => (a.title || 'Untitled').localeCompare(b.title || 'Untitled'))
      .slice(0, 50)
      .map<Candidate>((n) => ({
        id: n.id,
        label: n.title || 'Untitled',
        subtitle: buildBreadcrumb(n, byId),
        icon: n.kind === 'section' ? '◈' : n.kind === 'notebook' ? '▤' : n.icon || '▦'
      }));

    // Everything lives inside a notebook, so there is no "top level" target —
    // a notebook is itself the top-level destination.
    return sortedNotes;
  }, [notes, blocked, query, byId]);

  useEffect(() => {
    if (activeIndex >= candidates.length) setActiveIndex(0);
  }, [candidates, activeIndex]);

  const commitSelection = (target: Candidate | undefined) => {
    if (!target) return;
    onSelect(target.id);
    onClose();
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((current) => Math.min(candidates.length - 1, current + 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((current) => Math.max(0, current - 1));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      commitSelection(candidates[activeIndex]);
    }
  };

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div
        className="move-picker"
        role="dialog"
        aria-modal="true"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="move-picker__header">
          <span className="move-picker__title">
            Move "{note.title || 'Untitled'}" to
          </span>
        </div>
        <input
          ref={inputRef}
          type="text"
          className="move-picker__input"
          placeholder="Search for a page…"
          value={query}
          onChange={(event: ChangeEvent<HTMLInputElement>) => {
            setQuery(event.target.value);
            setActiveIndex(0);
          }}
          onKeyDown={handleKeyDown}
        />
        <div className="move-picker__list">
          {candidates.length === 0 && (
            <div className="move-picker__empty">No matching pages</div>
          )}
          {candidates.map((candidate, index) => (
            <button
              key={candidate.id ?? '__root__'}
              type="button"
              className={`move-picker__item ${index === activeIndex ? 'is-active' : ''}`}
              onMouseEnter={() => setActiveIndex(index)}
              onClick={() => commitSelection(candidate)}
            >
              <span className="move-picker__item-icon" aria-hidden>
                {candidate.id === null ? '⌂' : candidate.icon || '▦'}
              </span>
              <span className="move-picker__item-text">
                <span className="move-picker__item-label">{candidate.label}</span>
                {candidate.subtitle && (
                  <span className="move-picker__item-subtitle">{candidate.subtitle}</span>
                )}
              </span>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
};

export default MovePagePicker;
