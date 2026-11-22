import { Node, mergeAttributes } from '@tiptap/core';
import { ReactNodeViewRenderer } from '@tiptap/react';
import ToggleBlockNodeView from './ToggleBlockNodeView';

export const ToggleBlock = Node.create({
  name: 'toggleBlock',
  group: 'block',
  content: 'block+',
  defining: true,
  draggable: true,

  addAttributes() {
    return {
      open: {
        default: true,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-open') !== 'false',
        renderHTML: (attributes: { open?: boolean }) => ({
          'data-open': attributes.open === false ? 'false' : 'true'
        })
      }
    };
  },

  parseHTML() {
    return [
      {
        tag: 'div[data-toggle]'
      }
    ];
  },

  renderHTML({ HTMLAttributes }: { HTMLAttributes: Record<string, string | null> }) {
    return [
      'div',
      mergeAttributes(HTMLAttributes, {
        'data-toggle': 'true',
        class: 'toggle-block'
      }),
      0
    ];
  },

  addNodeView() {
    return ReactNodeViewRenderer(ToggleBlockNodeView);
  }
});
