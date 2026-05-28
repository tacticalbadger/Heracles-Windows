import { Node, mergeAttributes, type CommandProps } from '@tiptap/core';
import { ReactNodeViewRenderer } from '@tiptap/react';
import CodeMirrorNodeView from './CodeMirrorNodeView';

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    codeMirrorBlock: {
      insertCodeMirrorBlock: () => ReturnType;
    };
  }
}

export const CodeMirrorBlock = Node.create({
  name: 'codeMirrorBlock',
  group: 'block',
  atom: true,
  selectable: true,
  draggable: true,

  addAttributes() {
    return {
      language: {
        default: 'javascript'
      },
      code: {
        default: ''
      }
    };
  },

  parseHTML() {
    return [
      {
        tag: 'pre[data-code-mirror]'
      }
    ];
  },

  renderHTML({ HTMLAttributes }: { HTMLAttributes: Record<string, string> }) {
    return [
      'pre',
      mergeAttributes(HTMLAttributes, {
        'data-code-mirror': 'true',
        'data-language': HTMLAttributes.language,
        'data-code': HTMLAttributes.code
      })
    ];
  },

  addCommands() {
    return {
      insertCodeMirrorBlock:
        () =>
        ({ commands }: CommandProps) => {
          return commands.insertContent({
            type: this.name,
            attrs: {
              language: 'javascript',
              code: ''
            }
          });
        }
    };
  },

  addNodeView() {
    return ReactNodeViewRenderer(CodeMirrorNodeView);
  }
});
