import { useCallback, useRef, type PointerEvent as ReactPointerEvent } from 'react';
import type { Note } from '../types/notes';
import { EditorPane, type ToastTone } from './EditorPane';
import type { PaneNode, SplitPane, SplitTarget } from './paneTreeOps';

type Callbacks = {
  notes: Note[];
  activeLeafId: string;
  rootIsLeaf: boolean;
  onSelectTab: (leafId: string, noteId: string) => void;
  onCloseTab: (leafId: string, noteId: string) => void;
  onContentChange: (noteId: string, content: string, title: string) => void;
  onFocusLeaf: (leafId: string) => void;
  onSplitLeaf: (leafId: string, target: SplitTarget, sourceNoteId?: string) => void;
  onCloseLeaf: (leafId: string) => void;
  onMoveTab: (
    sourceLeafId: string,
    noteId: string,
    destLeafId: string,
    destIndex: number
  ) => void;
  onMoveTabToSplit: (
    sourceLeafId: string,
    noteId: string,
    destLeafId: string,
    target: SplitTarget
  ) => void;
  onOpenNoteInLeaf: (leafId: string, noteId: string) => void;
  onUpdateNoteIcon: (noteId: string, icon: string | null) => void;
  onSetSplitRatios: (splitId: string, ratios: number[]) => void;
  onArchiveNote: (note: Note) => void;
  onTrashNote: (note: Note) => void;
  onRestoreNote: (note: Note) => void;
  onDeleteNote: (note: Note) => void;
  onDuplicateNote: (note: Note, target?: 'current' | 'right' | 'down') => void;
  onCopyNoteLink: (note: Note) => void;
  onNotify?: (message: string, tone?: ToastTone) => void;
};

type SplitViewProps = Callbacks & { node: SplitPane };

const SplitView = ({ node, ...callbacks }: SplitViewProps) => {
  const containerRef = useRef<HTMLDivElement | null>(null);

  const handleResize = useCallback(
    (event: ReactPointerEvent<HTMLDivElement>, dividerIndex: number) => {
      event.preventDefault();
      const container = containerRef.current;
      if (!container) return;

      const startEvent = event;
      const startRatios = [...node.ratios];
      const rect = container.getBoundingClientRect();
      const totalSize = node.direction === 'horizontal' ? rect.width : rect.height;
      const start = node.direction === 'horizontal' ? startEvent.clientX : startEvent.clientY;
      const minRatio = 0.12;

      const handleMove = (moveEvent: PointerEvent) => {
        const current =
          node.direction === 'horizontal' ? moveEvent.clientX : moveEvent.clientY;
        const delta = (current - start) / totalSize;

        const next = [...startRatios];
        const before = next[dividerIndex] + delta;
        const after = next[dividerIndex + 1] - delta;
        if (before < minRatio || after < minRatio) return;

        next[dividerIndex] = before;
        next[dividerIndex + 1] = after;
        callbacks.onSetSplitRatios(node.id, next);
      };

      const handleUp = () => {
        window.removeEventListener('pointermove', handleMove);
        window.removeEventListener('pointerup', handleUp);
      };

      window.addEventListener('pointermove', handleMove);
      window.addEventListener('pointerup', handleUp);
    },
    [node, callbacks]
  );

  return (
    <div
      ref={containerRef}
      className={`pane-split pane-split--${node.direction}`}
    >
      {node.children.map((child, index) => {
        const ratio = node.ratios[index] ?? 1 / node.children.length;
        const isLast = index === node.children.length - 1;
        const flexBasis = `${ratio * 100}%`;
        return (
          <div
            key={child.id}
            className="pane-split__cell"
            style={node.direction === 'horizontal' ? { width: flexBasis } : { height: flexBasis }}
          >
            <PaneTree node={child} {...callbacks} />
            {!isLast && (
              <div
                className={`pane-split__divider pane-split__divider--${node.direction}`}
                onPointerDown={(event: ReactPointerEvent<HTMLDivElement>) =>
                  handleResize(event, index)
                }
              />
            )}
          </div>
        );
      })}
    </div>
  );
};

type PaneTreeProps = Callbacks & { node: PaneNode };

export const PaneTree = ({ node, ...callbacks }: PaneTreeProps) => {
  if (node.type === 'leaf') {
    return (
      <EditorPane
        leafId={node.id}
        isActiveLeaf={callbacks.activeLeafId === node.id}
        canCloseLeaf={!callbacks.rootIsLeaf}
        notes={callbacks.notes}
        tabs={node.tabs}
        activeTabId={node.activeTabId}
        onSelectTab={callbacks.onSelectTab}
        onCloseTab={callbacks.onCloseTab}
        onContentChange={callbacks.onContentChange}
        onFocusLeaf={callbacks.onFocusLeaf}
        onSplitLeaf={callbacks.onSplitLeaf}
        onCloseLeaf={callbacks.onCloseLeaf}
        onMoveTab={callbacks.onMoveTab}
        onMoveTabToSplit={callbacks.onMoveTabToSplit}
        onOpenNoteInLeaf={callbacks.onOpenNoteInLeaf}
        onUpdateNoteIcon={callbacks.onUpdateNoteIcon}
        onArchiveNote={callbacks.onArchiveNote}
        onTrashNote={callbacks.onTrashNote}
        onRestoreNote={callbacks.onRestoreNote}
        onDeleteNote={callbacks.onDeleteNote}
        onDuplicateNote={callbacks.onDuplicateNote}
        onCopyNoteLink={callbacks.onCopyNoteLink}
        onNotify={callbacks.onNotify}
      />
    );
  }

  return <SplitView node={node} {...callbacks} />;
};

export default PaneTree;
