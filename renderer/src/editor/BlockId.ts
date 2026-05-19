import { Extension } from '@tiptap/core';
import { Plugin } from 'prosemirror-state';

const createBlockId = () => {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }

  return `block-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
};

type BlockIdOptions = {
  types: string[];
  attributeName: string;
};

export const BlockId = Extension.create<BlockIdOptions>({
  name: 'blockId',

  addOptions() {
    return {
      types: [
        'paragraph',
        'heading',
        'blockquote',
        'listItem',
        'taskItem',
        'horizontalRule',
        'codeMirrorBlock',
        'calloutBlock',
        'toggleBlock'
      ],
      attributeName: 'blockId'
    };
  },

  addGlobalAttributes() {
    return [
      {
        types: this.options.types,
        attributes: {
          [this.options.attributeName]: {
            default: null,
            parseHTML: (element: HTMLElement) => element.getAttribute('data-block-id'),
            renderHTML: (attributes: Record<string, string | null>) => {
              const value = attributes[this.options.attributeName];
              return value ? { 'data-block-id': value } : {};
            }
          }
        }
      }
    ];
  },

  addProseMirrorPlugins() {
    const { types, attributeName } = this.options;

    return [
      new Plugin({
        appendTransaction: (transactions, _oldState, newState) => {
          if (!transactions.some((transaction) => transaction.docChanged)) {
            return;
          }

          const seenIds = new Set<string>();
          let tr = newState.tr;
          let changed = false;

          newState.doc.descendants((node, pos) => {
            if (!types.includes(node.type.name)) {
              return;
            }

            const currentId = node.attrs[attributeName] as string | null | undefined;
            let nextId = currentId;

            if (!nextId || seenIds.has(nextId)) {
              nextId = createBlockId();
              changed = true;
              tr = tr.setNodeMarkup(pos, node.type, { ...node.attrs, [attributeName]: nextId }, node.marks);
            }

            if (nextId) {
              seenIds.add(nextId);
            }
          });

          if (!changed) {
            return;
          }

          return tr;
        }
      })
    ];
  }
});
