import { Node, mergeAttributes } from '@tiptap/core';
import { ReactNodeViewRenderer } from '@tiptap/react';
import CalloutBlockNodeView from './CalloutBlockNodeView';

export const CalloutBlock = Node.create({
  name: 'calloutBlock',
  group: 'block',
  content: 'block+',
  defining: true,
  draggable: true,

  addAttributes() {
    return {
      emoji: {
        default: '💡',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-emoji') ?? '💡',
        renderHTML: (attributes: { emoji?: string }) => ({
          'data-emoji': attributes.emoji ?? '💡'
        })
      }
    };
  },

  parseHTML() {
    return [
      {
        tag: 'div[data-callout]'
      }
    ];
  },

  renderHTML({ HTMLAttributes }: { HTMLAttributes: Record<string, string | null> }) {
    return [
      'div',
      mergeAttributes(HTMLAttributes, {
        'data-callout': 'true',
        class: 'callout-block'
      }),
      0
    ];
  },

  addNodeView() {
    return ReactNodeViewRenderer(CalloutBlockNodeView);
  }
});
