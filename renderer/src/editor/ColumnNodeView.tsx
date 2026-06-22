import { type MouseEvent as ReactMouseEvent } from 'react';
import { NodeViewWrapper, NodeViewContent } from '@tiptap/react';
import type { Editor } from '@tiptap/core';
import type { Node as PMNode } from 'prosemirror-model';

type ColumnNodeViewProps = {
  node: PMNode;
  editor: Editor;
  getPos: () => number;
};

const MIN_COLUMN_PX = 80;

const ColumnNodeView = ({ node, editor, getPos }: ColumnNodeViewProps) => {
  const width = (node.attrs.width as number) ?? 1;

  // Drag the right-edge handle to redistribute width between this column and the
  // next one. The boundary moves live (direct flex-grow on the two DOM nodes);
  // the final weights are committed to the node attrs on mouse-up so they persist.
  const startResize = (event: ReactMouseEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.stopPropagation();

    const wrapper = event.currentTarget.closest('.column') as HTMLElement | null;
    const next = wrapper?.nextElementSibling as HTMLElement | null;
    if (!wrapper || !next) return;

    const pos = getPos();
    const selfNode = editor.state.doc.nodeAt(pos);
    const nextNode = editor.state.doc.nodeAt(pos + (selfNode?.nodeSize ?? 0));
    if (!selfNode || !nextNode || nextNode.type.name !== 'column') return;

    const pairWeight = ((selfNode.attrs.width as number) ?? 1) + ((nextNode.attrs.width as number) ?? 1);
    const startX = event.clientX;
    const selfPx0 = wrapper.getBoundingClientRect().width;
    const nextPx0 = next.getBoundingClientRect().width;
    const pxSum = selfPx0 + nextPx0;

    let selfWeight = (selfNode.attrs.width as number) ?? 1;
    let nextWeight = (nextNode.attrs.width as number) ?? 1;

    const onMove = (move: MouseEvent) => {
      const dx = move.clientX - startX;
      const newSelfPx = Math.max(MIN_COLUMN_PX, Math.min(pxSum - MIN_COLUMN_PX, selfPx0 + dx));
      const frac = newSelfPx / pxSum;
      selfWeight = pairWeight * frac;
      nextWeight = pairWeight - selfWeight;
      wrapper.style.flexGrow = String(selfWeight);
      next.style.flexGrow = String(nextWeight);
    };

    const onUp = () => {
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      document.body.classList.remove('is-col-resizing');

      const curPos = getPos();
      const cur = editor.state.doc.nodeAt(curPos);
      if (!cur) return;
      const nextPos = curPos + cur.nodeSize;
      const nextCur = editor.state.doc.nodeAt(nextPos);
      const tr = editor.state.tr.setNodeMarkup(curPos, undefined, { ...cur.attrs, width: selfWeight });
      if (nextCur && nextCur.type.name === 'column') {
        tr.setNodeMarkup(nextPos, undefined, { ...nextCur.attrs, width: nextWeight });
      }
      editor.view.dispatch(tr);
    };

    document.body.classList.add('is-col-resizing');
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  };

  return (
    <NodeViewWrapper
      className="column"
      style={{ flexGrow: width, flexShrink: 1, flexBasis: 0, minWidth: 0 }}
    >
      <NodeViewContent className="column__content" />
      <div
        className="column__resize"
        contentEditable={false}
        onMouseDown={startResize}
        aria-hidden
      />
    </NodeViewWrapper>
  );
};

export default ColumnNodeView;
