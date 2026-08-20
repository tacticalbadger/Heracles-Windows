import { Node, mergeAttributes } from '@tiptap/core';
import { ReactNodeViewRenderer } from '@tiptap/react';
import ColumnNodeView from './ColumnNodeView';

/**
 * A single column inside a `columnBlock`. Content is permissive (`block*`) so a
 * column may be momentarily emptied during a drag; the ColumnsNormalizer then
 * removes it. `width` is a flex-grow weight (default 1 = equal share) that the
 * resize handle in ColumnNodeView adjusts and persists via `data-width`.
 */
export const Column = Node.create({
  name: 'column',
  content: 'block*',
  isolating: true,
  defining: true,

  addAttributes() {
    return {
      width: {
        default: 1,
        parseHTML: (element: HTMLElement) => {
          const raw = Number(element.getAttribute('data-width'));
          return Number.isFinite(raw) && raw > 0 ? raw : 1;
        },
        renderHTML: (attributes: { width?: number }) => ({
          'data-width': String(attributes.width ?? 1)
        })
      }
    };
  },

  parseHTML() {
    return [{ tag: 'div[data-type="column"]' }];
  },

  renderHTML({ HTMLAttributes }: { HTMLAttributes: Record<string, string | null> }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'column', class: 'column' }), 0];
  },

  addNodeView() {
    return ReactNodeViewRenderer(ColumnNodeView);
  }
});

export default Column;
