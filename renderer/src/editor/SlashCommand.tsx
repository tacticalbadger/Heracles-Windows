import { Extension } from '@tiptap/core';
import Suggestion from '@tiptap/suggestion';
import { ReactRenderer } from '@tiptap/react';
import tippy from 'tippy.js';
import SlashCommandList, { type CommandItem, type SlashCommandListHandle } from './SlashCommandList';

const commandItems: CommandItem[] = [
  {
    title: 'Text',
    description: 'Plain paragraph text',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).setParagraph().run();
    }
  },
  {
    title: 'Heading 1',
    description: 'Big section heading',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).setNode('heading', { level: 1 }).run();
    }
  },
  {
    title: 'Heading 2',
    description: 'Medium section heading',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).setNode('heading', { level: 2 }).run();
    }
  },
  {
    title: 'Heading 3',
    description: 'Smaller section heading',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).setNode('heading', { level: 3 }).run();
    }
  },
  {
    title: 'Heading 4',
    description: 'Small heading',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).setNode('heading', { level: 4 }).run();
    }
  },
  {
    title: 'Bullet list',
    description: 'Start a bulleted list',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).toggleBulletList().run();
    }
  },
  {
    title: 'Numbered list',
    description: 'Start a numbered list',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).toggleOrderedList().run();
    }
  },
  {
    title: 'Task list',
    description: 'Checklist style tasks',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).toggleTaskList().run();
    }
  },
  {
    title: 'Quote',
    description: 'Call out an important note',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).toggleBlockquote().run();
    }
  },
  {
    title: 'Callout',
    description: 'Highlight a block with an icon',
    command: ({ editor, range }) => {
      editor
        .chain()
        .focus()
        .deleteRange(range)
        .insertContent({
          type: 'calloutBlock',
          attrs: {
            emoji: '💡'
          },
          content: [
            {
              type: 'paragraph',
              content: [{ type: 'text', text: 'Callout' }]
            }
          ]
        })
        .run();
    }
  },
  {
    title: 'Toggle list',
    description: 'Collapsible list of content',
    command: ({ editor, range }) => {
      editor
        .chain()
        .focus()
        .deleteRange(range)
        .insertContent({
          type: 'toggleBlock',
          attrs: {
            open: true
          },
          content: [
            {
              type: 'paragraph',
              content: [{ type: 'text', text: 'Toggle' }]
            },
            {
              type: 'paragraph',
              content: [{ type: 'text', text: '' }]
            }
          ]
        })
        .run();
    }
  },
  {
    title: 'Divider',
    description: 'Insert a horizontal divider',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).setHorizontalRule().run();
    }
  },
  {
    title: 'Code block',
    description: 'Insert a CodeMirror block',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).insertContent({ type: 'codeMirrorBlock' }).run();
    }
  }
];

const SlashCommand = Extension.create({
  name: 'slashCommand',

  addOptions() {
    return {
      suggestion: {
        char: '/',
        startOfLine: false,
        command: ({ editor, range, props }: { editor: any; range: any; props: CommandItem }) => {
          props.command({ editor, range });
        },
        items: ({ query }: { query: string }) => {
          return commandItems.filter((item) =>
            item.title.toLowerCase().includes(query.toLowerCase())
          );
        },
        render: () => {
          let component: ReactRenderer | null = null;
          let popup: ReturnType<typeof tippy> | null = null;

          return {
            onStart: (props: any) => {
              const renderer = new ReactRenderer(SlashCommandList, {
                props,
                editor: props.editor
              });

              component = renderer;

              if (!props.clientRect) {
                return;
              }

              popup = tippy('body', {
                getReferenceClientRect: props.clientRect,
                appendTo: () => document.body,
                content: renderer.element,
                showOnCreate: true,
                interactive: true,
                trigger: 'manual',
                placement: 'bottom-start'
              });
            },
            onUpdate: (props: any) => {
              component?.updateProps(props);

              if (!props.clientRect) {
                return;
              }

              popup?.[0]?.setProps({
                getReferenceClientRect: props.clientRect
              });
            },
            onKeyDown: (props: any) => {
              const handler = (component as (ReactRenderer & { ref?: SlashCommandListHandle | null }) | null)
                ?.ref?.onKeyDown;
              return handler ? handler(props) : false;
            },
            onExit: () => {
              popup?.[0]?.destroy();
              component?.destroy();
            }
          };
        }
      }
    };
  },

  addProseMirrorPlugins() {
    return [Suggestion({ editor: this.editor, ...this.options.suggestion })];
  }
});

export default SlashCommand;
