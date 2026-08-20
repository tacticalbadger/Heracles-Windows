import { Node, mergeAttributes } from '@tiptap/core';

/**
 * A horizontal row of columns. Holds one-or-more `column` children (the schema
 * stays permissive so transient 1-column states are legal; the ColumnsNormalizer
 * unwraps anything left with fewer than 2 columns). Renders to
 * `<div data-type="columns">` so the layout round-trips through the stored HTML.
 */
export const ColumnBlock = Node.create({
  name: 'columnBlock',
  group: 'block',
  content: 'column+',
  isolating: true,
  defining: true,

  parseHTML() {
    return [{ tag: 'div[data-type="columns"]' }];
  },

  renderHTML({ HTMLAttributes }: { HTMLAttributes: Record<string, string | null> }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'columns', class: 'columns' }), 0];
  }
});

export default ColumnBlock;
