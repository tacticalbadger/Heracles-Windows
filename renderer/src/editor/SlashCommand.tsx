import { Extension } from '@tiptap/core';
import Suggestion from '@tiptap/suggestion';
import { ReactRenderer } from '@tiptap/react';
import tippy from 'tippy.js';
import SlashCommandList, { type CommandItem } from './SlashCommandList';

const commandItems: CommandItem[] = [
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
    title: 'Bullet list',
    description: 'Start a bulleted list',
    command: ({ editor, range }) => {
      editor.chain().focus().deleteRange(range).toggleBulletList().run();
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
              component = new ReactRenderer(SlashCommandList, {
                props,
                editor: props.editor
              });

              if (!props.clientRect) {
                return;
              }

              popup = tippy('body', {
                getReferenceClientRect: props.clientRect,
                appendTo: () => document.body,
                content: component.element,
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
              return component?.ref?.onKeyDown(props) ?? false;
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
    return [Suggestion(this.options.suggestion)];
  }
});

export default SlashCommand;
