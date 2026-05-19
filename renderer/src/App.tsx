import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent,
  type PointerEvent as ReactPointerEvent,
  type ChangeEvent
} from 'react';
import { EditorContent, useEditor } from '@tiptap/react';
import { type Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import TaskList from '@tiptap/extension-task-list';
import TaskItem from '@tiptap/extension-task-item';
import { CodeMirrorBlock } from './editor/CodeMirrorBlock';
import { CalloutBlock } from './editor/CalloutBlock';
import { ToggleBlock } from './editor/ToggleBlock';
import SlashCommand from './editor/SlashCommand';
import { BlockId } from './editor/BlockId';
import type { Note } from './types/notes';

type LayoutMode = 'vertical' | 'horizontal';
type PaneKey = 'left' | 'right';
type FilterMode = 'all' | 'archived' | 'trash';

type ToastTone = 'success' | 'info' | 'danger';

type ToastAction = {
  label: string;
  onAction: () => void;
};

type Toast = {
  id: string;
  message: string;
  tone: ToastTone;
  action?: ToastAction;
};

type SaveState = {
  status: 'idle' | 'saving' | 'saved';
  lastSavedAt?: string;
  noteId?: string;
};

type PaneState = {
  activeNoteId: string | null;
  openNoteIds: string[];
};

type BlockAnchor = {
  top: number;
  left: number;
  height: number;
  visible: boolean;
};

type BlockMenuItem = {
  id: string;
  label: string;
  keywords?: string[];
  section: 'actions' | 'turn';
  onSelect: () => void;
};

type EditorPaneProps = {
  paneKey: PaneKey;
  note: Note | null;
  openNotes: Note[];
  activeNoteId: string | null;
  onSelectNote: (pane: PaneKey, noteId: string) => void;
  onCloseNote: (pane: PaneKey, noteId: string) => void;
  onContentChange: (noteId: string, content: string, title: string) => void;
  onFocus: (pane: PaneKey) => void;
  onArchiveNote: (note: Note) => void;
  onTrashNote: (note: Note) => void;
  onRestoreNote: (note: Note) => void;
  onDeleteNote: (note: Note) => void;
  onNotify?: (message: string, tone?: ToastTone) => void;
};

const clamp = (value: number, min: number, max: number) =>
  Math.min(max, Math.max(min, value));

const deriveTitle = (editor: Editor) => {
  const text = editor.getText().trim();
  const firstLine = text.split('\n')[0]?.trim();
  return firstLine ? firstLine.slice(0, 64) : 'Untitled';
};

const formatDate = (value: string) => {
  const date = new Date(value);
  return date.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric'
  });
};

const formatTime = (value?: string) => {
  if (!value) {
    return 'Just now';
  }

  const date = new Date(value);
  return date.toLocaleTimeString(undefined, {
    hour: 'numeric',
    minute: '2-digit'
  });
};

const sortNotes = (items: Note[]) =>
  [...items].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));

const stripHtml = (value: string) => value.replace(/<[^>]*>/g, ' ');

const BLOCK_SELECTOR =
  'p, h1, h2, h3, h4, blockquote, pre, hr, li, .code-block, .callout-block, .toggle-block';

const getBlockInfo = (editor: Editor) => {
  const { $from } = editor.state.selection;

  for (let depth = $from.depth; depth > 0; depth -= 1) {
    const node = $from.node(depth);

    if (node.type.isBlock) {
      const parentDepth = depth - 1;

      if (node.type.name === 'paragraph' && parentDepth > 0) {
        const parent = $from.node(parentDepth);
        if (
          parent.type.name === 'listItem' ||
          parent.type.name === 'taskItem' ||
          parent.type.name === 'blockquote' ||
          parent.type.name === 'calloutBlock' ||
          parent.type.name === 'toggleBlock'
        ) {
          return {
            node: parent,
            pos: $from.before(parentDepth)
          };
        }
      }

      return {
        node,
        pos: $from.before(depth)
      };
    }
  }

  return null;
};

const getBlockDom = (editor: Editor) => {
  const view = editor.view as (Editor['view'] & { docView?: unknown }) | null;

  if (editor.isDestroyed || !view || typeof view.domAtPos !== 'function' || !view.docView) {
    return null;
  }

  const { from } = editor.state.selection;
  const domAtPos = view.domAtPos(from);
  const node = domAtPos.node instanceof Element ? domAtPos.node : domAtPos.node.parentElement;

  if (!node) {
    return null;
  }

  return node.closest('li') ?? node.closest(BLOCK_SELECTOR);
};

const EditorPane = ({
  paneKey,
  note,
  openNotes,
  activeNoteId,
  onSelectNote,
  onCloseNote,
  onContentChange,
  onFocus,
  onArchiveNote,
  onTrashNote,
  onRestoreNote,
  onDeleteNote,
  onNotify
}: EditorPaneProps) => {
  const isTrashed = Boolean(note?.trashed);
  const isArchived = Boolean(note?.archived);
  const paneBodyRef = useRef<HTMLDivElement | null>(null);
  const blockHandleRef = useRef<HTMLButtonElement | null>(null);
  const blockMenuRef = useRef<HTMLDivElement | null>(null);
  const blockMenuSearchRef = useRef<HTMLInputElement | null>(null);
  const [blockAnchor, setBlockAnchor] = useState<BlockAnchor>({
    top: 0,
    left: 0,
    height: 0,
    visible: false
  });
  const [blockMenuOpen, setBlockMenuOpen] = useState(false);
  const [blockMenuQuery, setBlockMenuQuery] = useState('');
  const editor = useEditor(
    {
      extensions: [
        BlockId,
        StarterKit.configure({ codeBlock: false }),
        TaskList,
        TaskItem.configure({ nested: true }),
        CodeMirrorBlock,
        CalloutBlock,
        ToggleBlock,
        SlashCommand
      ],
      content: note?.content ?? '<p></p>',
      editorProps: {
        attributes: {
          class: 'editor-content'
        }
      },
      onUpdate: ({ editor }: { editor: Editor }) => {
        if (!note || note.trashed) {
          return;
        }

        const content = editor.getHTML();
        const title = deriveTitle(editor);
        onContentChange(note.id, content, title);
      }
    },
    [note?.id]
  );

  useEffect(() => {
    if (!editor || !note) {
      return;
    }

    const current = editor.getHTML();
    if (current !== note.content) {
      editor.commands.setContent(note.content || '<p></p>', false);
    }
  }, [editor, note?.id, note?.content]);

  useEffect(() => {
    if (!editor) {
      return;
    }

    editor.setEditable(Boolean(note) && !note?.trashed);
  }, [editor, note?.id, note?.trashed]);

  const updateBlockAnchor = useCallback(() => {
    if (!editor || !paneBodyRef.current || !note || note.trashed) {
      setBlockMenuOpen(false);
      setBlockAnchor((prev) => (prev.visible ? { ...prev, visible: false } : prev));
      return;
    }

    const blockDom = getBlockDom(editor);
    if (!blockDom) {
      setBlockMenuOpen(false);
      setBlockAnchor((prev) => (prev.visible ? { ...prev, visible: false } : prev));
      return;
    }

    const container = paneBodyRef.current;
    const containerRect = container.getBoundingClientRect();
    const blockRect = blockDom.getBoundingClientRect();
    const top = blockRect.top - containerRect.top + container.scrollTop;
    const left = blockRect.left - containerRect.left + container.scrollLeft;

    setBlockAnchor({
      top,
      left,
      height: blockRect.height,
      visible: true
    });
  }, [editor, note?.id, note?.trashed]);

  const handleBlockContextMenu = useCallback(
    (event: ReactMouseEvent<HTMLDivElement>) => {
      if (!editor || !note || note.trashed) {
        return;
      }

      const target = event.target as HTMLElement | null;

      if (!target?.closest('.editor-content')) {
        return;
      }

      if (blockMenuRef.current?.contains(target) || blockHandleRef.current?.contains(target)) {
        return;
      }

      event.preventDefault();
      updateBlockAnchor();
      setBlockMenuOpen(true);
      editor.commands.focus();
    },
    [editor, note?.id, note?.trashed, updateBlockAnchor]
  );

  useEffect(() => {
    if (!editor) {
      return;
    }

    const handleUpdate = () => updateBlockAnchor();
    const handleBlur = () => {
      if (blockMenuOpen) {
        return;
      }
      setBlockMenuOpen(false);
    };
    const container = paneBodyRef.current;

    editor.on('selectionUpdate', handleUpdate);
    editor.on('transaction', handleUpdate);
    editor.on('focus', handleUpdate);
    editor.on('blur', handleBlur);
    container?.addEventListener('scroll', handleUpdate, { passive: true });
    window.addEventListener('resize', handleUpdate);

    handleUpdate();

    return () => {
      editor.off('selectionUpdate', handleUpdate);
      editor.off('transaction', handleUpdate);
      editor.off('focus', handleUpdate);
      editor.off('blur', handleBlur);
      container?.removeEventListener('scroll', handleUpdate);
      window.removeEventListener('resize', handleUpdate);
    };
  }, [editor, updateBlockAnchor, blockMenuOpen]);

  useEffect(() => {
    if (!blockMenuOpen) {
      return;
    }

    const handlePointerDown = (event: MouseEvent) => {
      if (event.button === 2) {
        return;
      }

      if (blockMenuRef.current?.contains(event.target as Node)) {
        return;
      }

      if (blockHandleRef.current?.contains(event.target as Node)) {
        return;
      }

      setBlockMenuOpen(false);
    };

    window.addEventListener('mousedown', handlePointerDown);

    return () => {
      window.removeEventListener('mousedown', handlePointerDown);
    };
  }, [blockMenuOpen]);

  useEffect(() => {
    if (!blockMenuOpen) {
      setBlockMenuQuery('');
      return;
    }

    const id = window.setTimeout(() => {
      blockMenuSearchRef.current?.focus();
    }, 40);

    return () => window.clearTimeout(id);
  }, [blockMenuOpen]);

  const convertCodeBlockToParagraph = useCallback(() => {
    if (!editor) {
      return false;
    }

    const info = getBlockInfo(editor);
    if (!info || info.node.type.name !== 'codeMirrorBlock') {
      return false;
    }

    const text = typeof info.node.attrs.code === 'string' ? info.node.attrs.code : '';
    const blockId = info.node.attrs.blockId as string | undefined;

    editor
      .chain()
      .focus()
      .command(({ tr, state }) => {
        const paragraph = state.schema.nodes.paragraph;
        if (!paragraph) {
          return false;
        }

        const attrs = blockId ? { blockId } : null;
        const replacement = paragraph.create(attrs, text ? state.schema.text(text) : undefined);
        tr.replaceRangeWith(info.pos, info.pos + info.node.nodeSize, replacement);
        return true;
      })
      .run();

    return true;
  }, [editor]);

  const copyToClipboard = useCallback(async (value: string) => {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value);
      return true;
    }

    const textarea = document.createElement('textarea');
    textarea.value = value;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    const success = document.execCommand('copy');
    document.body.removeChild(textarea);
    return success;
  }, []);

  const handleCopyBlockLink = useCallback(async () => {
    if (!editor || !note) {
      return;
    }

    const info = getBlockInfo(editor);
    if (!info) {
      return;
    }

    const blockId = (info.node.attrs.blockId as string | undefined) ?? `${info.pos}`;
    let link = `note:${note.id}#block=${blockId}`;

    try {
      const url = new URL(window.location.href);
      url.hash = `note=${note.id}&block=${blockId}`;
      link = url.toString();
    } catch (error) {
      // Fallback to a stable internal link format.
    }

    try {
      const copied = await copyToClipboard(link);
      if (copied) {
        onNotify?.('Link copied', 'success');
      } else {
        onNotify?.('Unable to copy link', 'danger');
      }
    } catch (error) {
      onNotify?.('Unable to copy link', 'danger');
    }

    setBlockMenuOpen(false);
  }, [copyToClipboard, editor, note, onNotify]);

  const applyBlockId = useCallback(
    (blockId?: string) => {
      if (!editor || !blockId) {
        return;
      }

      const info = getBlockInfo(editor);
      if (!info) {
        return;
      }

      editor
        .chain()
        .focus()
        .command(({ tr }) => {
          tr.setNodeMarkup(info.pos, info.node.type, {
            ...info.node.attrs,
            blockId
          });
          return true;
        })
        .run();
    },
    [editor]
  );

  const handleDuplicateBlock = useCallback(() => {
    if (!editor) {
      return;
    }

    const info = getBlockInfo(editor);
    if (!info) {
      return;
    }

    const json = info.node.toJSON();
    if (json.attrs && 'blockId' in json.attrs) {
      json.attrs.blockId = null;
    }

    editor
      .chain()
      .focus()
      .insertContentAt(info.pos + info.node.nodeSize, json)
      .run();
    setBlockMenuOpen(false);
  }, [editor]);

  const handleDeleteBlock = useCallback(() => {
    if (!editor) {
      return;
    }

    const info = getBlockInfo(editor);
    if (!info) {
      return;
    }

    editor
      .chain()
      .focus()
      .deleteRange({ from: info.pos, to: info.pos + info.node.nodeSize })
      .run();
    setBlockMenuOpen(false);
  }, [editor]);

  const handleTurnIntoHeading = useCallback(
    (level: 1 | 2 | 3) => {
      if (!editor) {
        return;
      }

      const info = getBlockInfo(editor);
      const blockId = info?.node.attrs.blockId as string | undefined;

      setBlockMenuOpen(false);
      convertCodeBlockToParagraph();
      editor
        .chain()
        .focus()
        .setNode('heading', blockId ? { level, blockId } : { level })
        .run();
    },
    [convertCodeBlockToParagraph, editor]
  );

  const handleTurnInto = useCallback(
    (next: 'paragraph' | 'bulletList' | 'taskList' | 'blockquote' | 'codeBlock') => {
      if (!editor) {
        return;
      }

      const info = getBlockInfo(editor);
      const blockId = info?.node.attrs.blockId as string | undefined;

      setBlockMenuOpen(false);

      if (next === 'paragraph') {
        if (!convertCodeBlockToParagraph()) {
          editor.chain().focus().setParagraph().run();
        }
        applyBlockId(blockId);
        return;
      }

      if (next === 'blockquote') {
        convertCodeBlockToParagraph();
        editor.chain().focus().toggleBlockquote().run();
        applyBlockId(blockId);
        return;
      }

      if (next === 'bulletList') {
        convertCodeBlockToParagraph();
        editor.chain().focus().toggleBulletList().run();
        applyBlockId(blockId);
        return;
      }

      if (next === 'taskList') {
        convertCodeBlockToParagraph();
        editor.chain().focus().toggleTaskList().run();
        applyBlockId(blockId);
        return;
      }

      if (next === 'codeBlock') {
        if (!info) {
          return;
        }

        const code = info.node.textContent;

        editor
          .chain()
          .focus()
          .command(({ tr, state }) => {
            const nodeType = state.schema.nodes.codeMirrorBlock;
            if (!nodeType) {
              return false;
            }

            const replacement = nodeType.create({
              language: 'javascript',
              code,
              blockId
            });
            tr.replaceRangeWith(info.pos, info.pos + info.node.nodeSize, replacement);
            return true;
          })
          .run();
      }
    },
    [applyBlockId, editor, convertCodeBlockToParagraph]
  );

  const blockMenuActions = useMemo<BlockMenuItem[]>(
    () => [
      {
        id: 'copy-link',
        label: 'Copy link to block',
        keywords: ['link', 'copy'],
        section: 'actions',
        onSelect: handleCopyBlockLink
      },
      {
        id: 'duplicate',
        label: 'Duplicate',
        keywords: ['copy'],
        section: 'actions',
        onSelect: handleDuplicateBlock
      },
      {
        id: 'delete',
        label: 'Delete',
        keywords: ['remove'],
        section: 'actions',
        onSelect: handleDeleteBlock
      }
    ],
    [handleCopyBlockLink, handleDeleteBlock, handleDuplicateBlock]
  );

  const blockMenuTurns = useMemo<BlockMenuItem[]>(
    () => [
      {
        id: 'text',
        label: 'Text',
        keywords: ['paragraph'],
        section: 'turn',
        onSelect: () => handleTurnInto('paragraph')
      },
      {
        id: 'heading-1',
        label: 'Heading 1',
        keywords: ['title'],
        section: 'turn',
        onSelect: () => handleTurnIntoHeading(1)
      },
      {
        id: 'heading-2',
        label: 'Heading 2',
        keywords: ['subtitle'],
        section: 'turn',
        onSelect: () => handleTurnIntoHeading(2)
      },
      {
        id: 'heading-3',
        label: 'Heading 3',
        keywords: ['subheading'],
        section: 'turn',
        onSelect: () => handleTurnIntoHeading(3)
      },
      {
        id: 'bullet-list',
        label: 'Bullet list',
        keywords: ['list'],
        section: 'turn',
        onSelect: () => handleTurnInto('bulletList')
      },
      {
        id: 'task-list',
        label: 'Task list',
        keywords: ['todo', 'checklist'],
        section: 'turn',
        onSelect: () => handleTurnInto('taskList')
      },
      {
        id: 'blockquote',
        label: 'Quote',
        keywords: ['blockquote'],
        section: 'turn',
        onSelect: () => handleTurnInto('blockquote')
      },
      {
        id: 'code-block',
        label: 'Code block',
        keywords: ['code'],
        section: 'turn',
        onSelect: () => handleTurnInto('codeBlock')
      }
    ],
    [handleTurnInto, handleTurnIntoHeading]
  );

  const normalizedQuery = blockMenuQuery.trim().toLowerCase();
  const filterMenuItems = useCallback(
    (items: BlockMenuItem[]) => {
      if (!normalizedQuery) {
        return items;
      }

      return items.filter((item) => {
        const keywords = item.keywords?.join(' ') ?? '';
        const haystack = `${item.label} ${keywords}`.toLowerCase();
        return haystack.includes(normalizedQuery);
      });
    },
    [normalizedQuery]
  );

  const filteredActions = useMemo(
    () => filterMenuItems(blockMenuActions),
    [blockMenuActions, filterMenuItems]
  );
  const filteredTurns = useMemo(
    () => filterMenuItems(blockMenuTurns),
    [blockMenuTurns, filterMenuItems]
  );
  const hasMenuResults = filteredActions.length > 0 || filteredTurns.length > 0;

  return (
    <section className="pane" onMouseDown={() => onFocus(paneKey)}>
      <div className="pane__tabs">
        {openNotes.length === 0 && (
          <span className="pane__tabs-empty">No tabs open</span>
        )}
        {openNotes.map((tabNote) => (
          <div
            key={tabNote.id}
            className={`pane__tab ${tabNote.id === activeNoteId ? 'is-active' : ''}`}
            onClick={() => onSelectNote(paneKey, tabNote.id)}
            role="button"
            tabIndex={0}
            onKeyDown={(event: ReactKeyboardEvent<HTMLDivElement>) => {
              if (event.key === 'Enter') {
                onSelectNote(paneKey, tabNote.id);
              }
            }}
          >
            <span>{tabNote.title}</span>
            <button
              type="button"
              className="pane__tab-close"
              onClick={(event: ReactMouseEvent<HTMLButtonElement>) => {
                event.stopPropagation();
                onCloseNote(paneKey, tabNote.id);
              }}
              aria-label={`Close ${tabNote.title}`}
            >
              ×
            </button>
          </div>
        ))}
      </div>
      <header className="pane__header">
        <div>
          <div className="pane__title">{note?.title ?? 'Select a note'}</div>
          <div className="pane__subtitle">
            {!note && 'Choose from the sidebar'}
            {note && note.trashed && 'In Trash · Read-only'}
            {note && !note.trashed && note.archived && 'Archived'}
            {note && !note.trashed && !note.archived && `Updated ${formatDate(note.updatedAt)}`}
          </div>
        </div>
        <div className="pane__actions">
          {note && !isTrashed && (
            <button
              className="chip chip--muted"
              type="button"
              onClick={() => onArchiveNote(note)}
            >
              {isArchived ? 'Unarchive' : 'Archive'}
            </button>
          )}
          {note && !isTrashed && (
            <button className="chip chip--danger" type="button" onClick={() => onTrashNote(note)}>
              Move to Trash
            </button>
          )}
          {note && isTrashed && (
            <button className="chip chip--muted" type="button" onClick={() => onRestoreNote(note)}>
              Restore
            </button>
          )}
          {note && isTrashed && (
            <button className="chip chip--danger" type="button" onClick={() => onDeleteNote(note)}>
              Delete
            </button>
          )}
          <button
            className="chip"
            type="button"
            onClick={() =>
              editor
                ?.chain()
                .focus()
                .insertContent({ type: 'codeMirrorBlock' })
                .run()
            }
            disabled={!note || isTrashed}
          >
            + Code block
          </button>
        </div>
      </header>
      <div className="pane__body" ref={paneBodyRef} onContextMenu={handleBlockContextMenu}>
        {!note && (
          <div className="pane__empty">
            Select a note from the left and it will appear here.
          </div>
        )}
        {note && editor && !isTrashed && blockAnchor.visible && (
          <div
            className="block-handle"
            style={{
              top: blockAnchor.top + Math.min(8, blockAnchor.height / 4),
              left: Math.max(8, blockAnchor.left - 46)
            }}
          >
            <button
              ref={blockHandleRef}
              type="button"
              className="block-handle__button"
              aria-label="Block actions"
              onMouseDown={(event: ReactMouseEvent<HTMLButtonElement>) => event.preventDefault()}
              onContextMenu={(event: ReactMouseEvent<HTMLButtonElement>) => {
                event.preventDefault();
                event.stopPropagation();
                updateBlockAnchor();
                setBlockMenuOpen(true);
                editor.commands.focus();
              }}
              onClick={() => {
                updateBlockAnchor();
                setBlockMenuOpen((open) => !open);
                editor.commands.focus();
              }}
            >
              ⋮⋮
            </button>
          </div>
        )}
        {note && editor && !isTrashed && blockAnchor.visible && blockMenuOpen && (
          <div
            ref={blockMenuRef}
            className="block-menu"
            style={{
              top: blockAnchor.top - 4,
              left: Math.max(12, blockAnchor.left - 6)
            }}
          >
            <div className="block-menu__search">
              <input
                ref={blockMenuSearchRef}
                className="block-menu__search-input"
                placeholder="Search actions..."
                value={blockMenuQuery}
                onChange={(event: ChangeEvent<HTMLInputElement>) =>
                  setBlockMenuQuery(event.target.value)
                }
                onKeyDown={(event: ReactKeyboardEvent<HTMLInputElement>) => {
                  if (event.key === 'Escape') {
                    event.preventDefault();
                    setBlockMenuOpen(false);
                  }
                }}
              />
            </div>
            {!hasMenuResults && <div className="block-menu__empty">No actions found.</div>}
            {filteredActions.length > 0 && (
              <>
                <div className="block-menu__title">Actions</div>
                <div className="block-menu__section">
                  {filteredActions.map((item) => (
                    <button
                      key={item.id}
                      type="button"
                      className="block-menu__item"
                      onClick={item.onSelect}
                    >
                      {item.label}
                    </button>
                  ))}
                </div>
              </>
            )}
            {filteredActions.length > 0 && filteredTurns.length > 0 && (
              <div className="block-menu__divider" />
            )}
            {filteredTurns.length > 0 && (
              <>
                <div className="block-menu__title">Turn into</div>
                <div className="block-menu__section">
                  {filteredTurns.map((item) => (
                    <button
                      key={item.id}
                      type="button"
                      className="block-menu__item"
                      onClick={item.onSelect}
                    >
                      {item.label}
                    </button>
                  ))}
                </div>
              </>
            )}
          </div>
        )}
        {note && <EditorContent editor={editor} />}
      </div>
    </section>
  );
};

const App = () => {
  const [theme, setTheme] = useState<'light' | 'dark'>(() => {
    const stored = localStorage.getItem('heracles-theme');
    return stored === 'dark' ? 'dark' : 'light';
  });
  const [layout, setLayout] = useState<LayoutMode>('vertical');
  const [notes, setNotes] = useState<Note[]>([]);
  const [filter, setFilter] = useState<FilterMode>('all');
  const [activePane, setActivePane] = useState<PaneKey>('left');
  const [panes, setPanes] = useState<Record<PaneKey, PaneState>>({
    left: { activeNoteId: null, openNoteIds: [] },
    right: { activeNoteId: null, openNoteIds: [] }
  });
  const [isSwitcherOpen, setIsSwitcherOpen] = useState(false);
  const [switcherQuery, setSwitcherQuery] = useState('');
  const [switcherIndex, setSwitcherIndex] = useState(0);
  const [saveState, setSaveState] = useState<SaveState>({ status: 'idle' });
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [splitRatio, setSplitRatio] = useState(0.55);
  const splitRef = useRef<HTMLDivElement | null>(null);
  const switcherInputRef = useRef<HTMLInputElement | null>(null);
  const saveTimers = useRef(new Map<string, number>());

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem('heracles-theme', theme);
  }, [theme]);

  const dismissToast = useCallback((toastId: string) => {
    setToasts((current: Toast[]) =>
      current.filter((toast: Toast) => toast.id !== toastId)
    );
  }, []);

  const pushToast = useCallback(
    (
      message: string,
      tone: ToastTone = 'info',
      action?: ToastAction,
      durationMs = 3200
    ) => {
      const id = typeof crypto !== 'undefined' && 'randomUUID' in crypto
        ? crypto.randomUUID()
        : `${Date.now()}-${Math.random()}`;

      const wrappedAction = action
        ? {
            ...action,
            onAction: () => {
              action.onAction();
              dismissToast(id);
            }
          }
        : undefined;

      setToasts((current: Toast[]) => [...current, { id, message, tone, action: wrappedAction }]);

      window.setTimeout(() => {
        dismissToast(id);
      }, durationMs);
    },
    [dismissToast]
  );

  useEffect(() => {
    if (!isSwitcherOpen) {
      return;
    }

    const id = window.setTimeout(() => {
      switcherInputRef.current?.focus();
    }, 50);

    return () => window.clearTimeout(id);
  }, [isSwitcherOpen]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      const isQuickSwitch = (event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k';
      if (isQuickSwitch) {
        event.preventDefault();
        setIsSwitcherOpen(true);
        setSwitcherQuery('');
        setSwitcherIndex(0);
        return;
      }

      if (event.key === 'Escape' && isSwitcherOpen) {
        setIsSwitcherOpen(false);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isSwitcherOpen]);

  useEffect(() => {
    setSwitcherIndex(0);
  }, [switcherQuery, isSwitcherOpen]);

  useEffect(() => {
    let isMounted = true;

    const loadNotes = async () => {
      if (!window.heracles?.notes) {
        return;
      }

      const stored = await window.heracles.notes.list();
      if (!isMounted) {
        return;
      }

      if (stored.length === 0) {
        const seed = await window.heracles.notes.create({
          title: 'Welcome to Heracles',
          content:
            '<h1>Welcome to Heracles</h1><p>Use / to open the command palette.</p><p>Create notes from the sidebar.</p>'
        });
        setNotes(sortNotes([seed]));
        setPanes({
          left: { activeNoteId: seed.id, openNoteIds: [seed.id] },
          right: { activeNoteId: null, openNoteIds: [] }
        });
      } else {
        setNotes(sortNotes(stored));
        setPanes({
          left: {
            activeNoteId: stored[0]?.id ?? null,
            openNoteIds: stored[0] ? [stored[0].id] : []
          },
          right: {
            activeNoteId: stored[1]?.id ?? null,
            openNoteIds: stored[1] ? [stored[1].id] : []
          }
        });
      }
    };

    loadNotes();

    return () => {
      isMounted = false;
    };
  }, []);

  const openNote = useCallback((paneKey: PaneKey, noteId: string) => {
    setPanes((current: Record<PaneKey, PaneState>) => {
      const pane = current[paneKey];
      const openNoteIds = pane.openNoteIds.includes(noteId)
        ? pane.openNoteIds
        : [noteId, ...pane.openNoteIds];

      return {
        ...current,
        [paneKey]: { activeNoteId: noteId, openNoteIds }
      };
    });
  }, []);

  const replaceNote = useCallback((updated: Note) => {
    setNotes((current: Note[]) =>
      sortNotes(current.map((note: Note) => (note.id === updated.id ? updated : note)))
    );
  }, []);

  const removeNoteEverywhere = useCallback((noteId: string) => {
    setNotes((current: Note[]) => current.filter((note: Note) => note.id !== noteId));
    setPanes((current: Record<PaneKey, PaneState>) => {
      const updatePane = (pane: PaneState) => {
        const openNoteIds = pane.openNoteIds.filter((id) => id !== noteId);
        const activeNoteId =
          pane.activeNoteId === noteId ? openNoteIds[0] ?? null : pane.activeNoteId;
        return { activeNoteId, openNoteIds };
      };

      return {
        left: updatePane(current.left),
        right: updatePane(current.right)
      };
    });
  }, []);

  const closeNote = useCallback((paneKey: PaneKey, noteId: string) => {
    setPanes((current: Record<PaneKey, PaneState>) => {
      const pane = current[paneKey];
      const openNoteIds = pane.openNoteIds.filter((id) => id !== noteId);
      const activeNoteId =
        pane.activeNoteId === noteId ? openNoteIds[0] ?? null : pane.activeNoteId;

      return {
        ...current,
        [paneKey]: { activeNoteId, openNoteIds }
      };
    });
  }, []);

  const handleNewNote = useCallback(async () => {
    if (!window.heracles?.notes) {
      return;
    }

    const created = await window.heracles.notes.create({
      title: 'Untitled',
      content: '<p></p>'
    });

    setNotes((current: Note[]) => sortNotes([created, ...current]));
    openNote(activePane, created.id);
    pushToast('New note created', 'success');
  }, [activePane, openNote]);

  const handleArchiveNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) {
        return;
      }

      const updated = await window.heracles.notes.archive({
        id: note.id,
        archived: !note.archived
      });

      if (updated) {
        replaceNote(updated);
        pushToast(updated.archived ? 'Note archived' : 'Note unarchived', 'info');
      }
    },
    [pushToast, replaceNote]
  );

  const handleTrashNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) {
        return;
      }

      const updated = await window.heracles.notes.trash({ id: note.id });
      if (updated) {
        replaceNote(updated);
        pushToast(
          'Moved to trash',
          'info',
          {
            label: 'Undo',
            onAction: () => {
              window.heracles.notes.restore({ id: note.id }).then((restored) => {
                if (restored) {
                  replaceNote(restored);
                }
              });
            }
          },
          10000
        );
      }
    },
    [pushToast, replaceNote]
  );

  const handleRestoreNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) {
        return;
      }

      const updated = await window.heracles.notes.restore({ id: note.id });
      if (updated) {
        replaceNote(updated);
        pushToast('Note restored', 'success');
      }
    },
    [pushToast, replaceNote]
  );

  const handleDeleteNote = useCallback(
    async (note: Note) => {
      if (!window.heracles?.notes) {
        return;
      }

      await window.heracles.notes.remove({ id: note.id });
      removeNoteEverywhere(note.id);
      pushToast('Note deleted', 'danger');
    },
    [pushToast, removeNoteEverywhere]
  );

  const handleEmptyTrash = useCallback(async () => {
    if (!window.heracles?.notes) {
      return;
    }

    await window.heracles.notes.emptyTrash();
    setNotes((current: Note[]) => current.filter((note: Note) => !note.trashed));
    setPanes((current: Record<PaneKey, PaneState>) => {
      const updatePane = (pane: PaneState) => {
        const openNoteIds = pane.openNoteIds.filter((id) => {
          const note = notes.find((item: Note) => item.id === id);
          return note ? !note.trashed : false;
        });
        const activeNoteId =
          pane.activeNoteId && openNoteIds.includes(pane.activeNoteId)
            ? pane.activeNoteId
            : openNoteIds[0] ?? null;
        return { activeNoteId, openNoteIds };
      };

      return {
        left: updatePane(current.left),
        right: updatePane(current.right)
      };
    });

    pushToast('Trash emptied', 'danger');
  }, [notes, pushToast]);

  const handleContentChange = useCallback(
    (noteId: string, content: string, title: string) => {
      const updatedAt = new Date().toISOString();
      setNotes((current: Note[]) =>
        sortNotes(
          current.map((note: Note) =>
            note.id === noteId ? { ...note, content, title, updatedAt } : note
          )
        )
      );
      setSaveState({ status: 'saving', noteId, lastSavedAt: updatedAt });

      if (!window.heracles?.notes) {
        return;
      }

      const existing = saveTimers.current.get(noteId);
      if (existing) {
        window.clearTimeout(existing);
      }

      const timeout = window.setTimeout(async () => {
        const result = await window.heracles.notes.update({ id: noteId, content, title });
        if (result) {
          setSaveState({ status: 'saved', noteId, lastSavedAt: result.updatedAt });
        }
        saveTimers.current.delete(noteId);
      }, 600);

      saveTimers.current.set(noteId, timeout);
    },
    []
  );

  const getOpenNotes = useCallback(
    (paneKey: PaneKey) => {
      return panes[paneKey].openNoteIds
        .map((id: string) => notes.find((note: Note) => note.id === id))
        .filter((note: Note | undefined): note is Note => Boolean(note));
    },
    [notes, panes]
  );

  const filteredNotes = useMemo(() => {
    const base = notes.filter((note: Note) => {
      if (filter === 'archived') {
        return note.archived && !note.trashed;
      }
      if (filter === 'trash') {
        return note.trashed;
      }
      return !note.archived && !note.trashed;
    });

    return sortNotes(base);
  }, [filter, notes]);

  const switcherNotes = useMemo(() => {
    const query = switcherQuery.trim().toLowerCase();
    const base = notes.filter((note: Note) => !note.trashed);
    if (!query) {
      return sortNotes(base).slice(0, 10);
    }

    const result = base.filter((note: Note) => {
      const haystack = `${note.title} ${stripHtml(note.content)}`.toLowerCase();
      return haystack.includes(query);
    });

    return sortNotes(result).slice(0, 10);
  }, [notes, switcherQuery]);

  const leftNote = useMemo(
    () => notes.find((note: Note) => note.id === panes.left.activeNoteId) ?? null,
    [notes, panes.left.activeNoteId]
  );
  const rightNote = useMemo(
    () => notes.find((note: Note) => note.id === panes.right.activeNoteId) ?? null,
    [notes, panes.right.activeNoteId]
  );

  const startResize = useCallback(
    (event: ReactPointerEvent<HTMLDivElement>) => {
      event.preventDefault();
      const container = splitRef.current;
      if (!container) {
        return;
      }

      const handleMove = (moveEvent: PointerEvent) => {
        const rect = container.getBoundingClientRect();
        const ratio =
          layout === 'vertical'
            ? (moveEvent.clientX - rect.left) / rect.width
            : (moveEvent.clientY - rect.top) / rect.height;

        setSplitRatio(clamp(ratio, 0.2, 0.8));
      };

      const handleUp = () => {
        window.removeEventListener('pointermove', handleMove);
        window.removeEventListener('pointerup', handleUp);
      };

      window.addEventListener('pointermove', handleMove);
      window.addEventListener('pointerup', handleUp);
    },
    [layout]
  );

  return (
    <div className="app">
      <header className="app__header">
        <div>
          <div className="app__brand">Heracles Notes</div>
          <div className="app__tagline">Local-first, block-based workspace</div>
        </div>
        <div className="app__controls">
          <button
            className="chip chip--ghost"
            type="button"
            onClick={() => {
              setSwitcherQuery('');
              setSwitcherIndex(0);
              setIsSwitcherOpen(true);
            }}
          >
            Quick switch ⌘K
          </button>
          <div className="autosave">
            <span className={`autosave__dot autosave__dot--${saveState.status}`} />
            {saveState.status === 'saving' && 'Saving…'}
            {saveState.status === 'saved' && `Saved ${formatTime(saveState.lastSavedAt)}`}
            {saveState.status === 'idle' && 'Autosave ready'}
          </div>
          <button
            className="chip"
            type="button"
            onClick={() =>
              setLayout((current: LayoutMode) =>
                current === 'vertical' ? 'horizontal' : 'vertical'
              )
            }
          >
            Split: {layout === 'vertical' ? 'Side-by-side' : 'Stacked'}
          </button>
          <button
            className="chip chip--primary"
            type="button"
            onClick={() => setTheme(theme === 'light' ? 'dark' : 'light')}
          >
            Theme: {theme === 'light' ? 'Light' : 'Dark'}
          </button>
        </div>
      </header>

      <main className="workspace">
        <aside className="sidebar">
          <div className="sidebar__header">
            <div className="sidebar__title">Notes</div>
            <button className="sidebar__button" type="button" onClick={handleNewNote}>
              + New note
            </button>
          </div>
          <div className="sidebar__filters">
            <button
              type="button"
              className={`sidebar__filter ${filter === 'all' ? 'is-active' : ''}`}
              onClick={() => setFilter('all')}
            >
              All
            </button>
            <button
              type="button"
              className={`sidebar__filter ${filter === 'archived' ? 'is-active' : ''}`}
              onClick={() => setFilter('archived')}
            >
              Archived
            </button>
            <button
              type="button"
              className={`sidebar__filter ${filter === 'trash' ? 'is-active' : ''}`}
              onClick={() => setFilter('trash')}
            >
              Trash
            </button>
            {filter === 'trash' && (
              <button
                type="button"
                className="sidebar__filter sidebar__filter--danger"
                onClick={handleEmptyTrash}
              >
                Empty trash
              </button>
            )}
          </div>
          <div className="sidebar__pane-toggle">
            Target pane:
            <button
              type="button"
              className={`sidebar__pane-button ${activePane === 'left' ? 'is-active' : ''}`}
              onClick={() => setActivePane('left')}
            >
              Left
            </button>
            <button
              type="button"
              className={`sidebar__pane-button ${activePane === 'right' ? 'is-active' : ''}`}
              onClick={() => setActivePane('right')}
            >
              Right
            </button>
          </div>
          <ul className="note-list">
            {filteredNotes.map((note: Note) => {
              const isOpen =
                panes.left.activeNoteId === note.id || panes.right.activeNoteId === note.id;

              return (
                <li key={note.id}>
                  <button
                    type="button"
                    className={`note-list__item ${isOpen ? 'is-active' : ''}`}
                    onClick={() => openNote(activePane, note.id)}
                  >
                    <div className="note-list__title">
                      {note.title}
                      {note.archived && !note.trashed && (
                        <span className="note-list__badge">Archived</span>
                      )}
                      {note.trashed && <span className="note-list__badge note-list__badge--danger">Trash</span>}
                    </div>
                    <div className="note-list__meta">Updated {formatDate(note.updatedAt)}</div>
                  </button>
                </li>
              );
            })}
          </ul>
        </aside>

        <div className={`split-view split-view--${layout}`} ref={splitRef}>
          <div className="split-view__pane" style={{ flexBasis: `${splitRatio * 100}%` }}>
            <EditorPane
              paneKey="left"
              note={leftNote}
              openNotes={getOpenNotes('left')}
              activeNoteId={panes.left.activeNoteId}
              onSelectNote={openNote}
              onCloseNote={closeNote}
              onContentChange={handleContentChange}
              onFocus={setActivePane}
              onArchiveNote={handleArchiveNote}
              onTrashNote={handleTrashNote}
              onRestoreNote={handleRestoreNote}
              onDeleteNote={handleDeleteNote}
              onNotify={pushToast}
            />
          </div>
          <div className="split-view__divider" onPointerDown={startResize} />
          <div className="split-view__pane" style={{ flexBasis: `${(1 - splitRatio) * 100}%` }}>
            <EditorPane
              paneKey="right"
              note={rightNote}
              openNotes={getOpenNotes('right')}
              activeNoteId={panes.right.activeNoteId}
              onSelectNote={openNote}
              onCloseNote={closeNote}
              onContentChange={handleContentChange}
              onFocus={setActivePane}
              onArchiveNote={handleArchiveNote}
              onTrashNote={handleTrashNote}
              onRestoreNote={handleRestoreNote}
              onDeleteNote={handleDeleteNote}
              onNotify={pushToast}
            />
          </div>
        </div>
      </main>
      {isSwitcherOpen && (
        <div
          className="switcher"
          role="dialog"
          aria-modal="true"
          onClick={() => setIsSwitcherOpen(false)}
        >
          <div
            className="switcher__panel"
            onClick={(event: ReactMouseEvent<HTMLDivElement>) => event.stopPropagation()}
          >
            <div className="switcher__header">
              <span>Quick switch</span>
              <button
                type="button"
                className="switcher__close"
                onClick={() => setIsSwitcherOpen(false)}
              >
                Esc
              </button>
            </div>
            <input
              ref={switcherInputRef}
              className="switcher__input"
              placeholder="Search notes…"
              value={switcherQuery}
              onChange={(event: ChangeEvent<HTMLInputElement>) => setSwitcherQuery(event.target.value)}
              onKeyDown={(event: ReactKeyboardEvent<HTMLInputElement>) => {
                if (event.key === 'ArrowDown') {
                  event.preventDefault();
                  setSwitcherIndex((current: number) =>
                    Math.min(current + 1, Math.max(0, switcherNotes.length - 1))
                  );
                }

                if (event.key === 'ArrowUp') {
                  event.preventDefault();
                  setSwitcherIndex((current: number) => Math.max(current - 1, 0));
                }

                if (event.key === 'Enter') {
                  event.preventDefault();
                  const target = switcherNotes[switcherIndex];
                  if (target) {
                    openNote(activePane, target.id);
                    setIsSwitcherOpen(false);
                  }
                }
              }}
            />
            <div className="switcher__results">
              {switcherNotes.length === 0 && (
                <div className="switcher__empty">No matching notes</div>
              )}
              {switcherNotes.map((note: Note, index: number) => (
                <button
                  key={note.id}
                  type="button"
                  className={`switcher__item ${index === switcherIndex ? 'is-active' : ''}`}
                  onClick={() => {
                    openNote(activePane, note.id);
                    setIsSwitcherOpen(false);
                  }}
                >
                  <div className="switcher__title">
                    {note.title}
                    {note.archived && <span className="switcher__badge">Archived</span>}
                  </div>
                  <div className="switcher__meta">Updated {formatDate(note.updatedAt)}</div>
                </button>
              ))}
            </div>
          </div>
        </div>
      )}
      <div className="toast-stack">
        {toasts.map((toast: Toast) => (
          <div key={toast.id} className={`toast toast--${toast.tone}`}>
            {toast.message}
            {toast.action && (
              <button type="button" className="toast__action" onClick={toast.action.onAction}>
                {toast.action.label}
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
};

export default App;
