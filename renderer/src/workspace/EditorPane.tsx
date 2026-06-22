import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type DragEvent as ReactDragEvent,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent
} from 'react';
import { BubbleMenu, EditorContent, useEditor } from '@tiptap/react';
import { type Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import TaskList from '@tiptap/extension-task-list';
import TaskItem from '@tiptap/extension-task-item';
import Link from '@tiptap/extension-link';
import { CodeMirrorBlock } from '../editor/CodeMirrorBlock';
import { CalloutBlock } from '../editor/CalloutBlock';
import { ToggleBlock } from '../editor/ToggleBlock';
import { ImageBlock } from '../editor/ImageBlock';
import { ColumnBlock } from '../editor/ColumnBlock';
import { Column } from '../editor/Column';
import {
  ColumnsNormalizer,
  BLOCK_DRAG_MIME,
  getDropTarget,
  applyBlockDrop,
  resolveOuterBlock
} from '../editor/columnCommands';
import SlashCommand from '../editor/SlashCommand';
import { BlockId } from '../editor/BlockId';
import type { Note } from '../types/notes';
import type { SplitTarget } from './paneTreeOps';
import { htmlToEditorHtml } from '../lib/htmlImport';
import { IconPicker } from './IconPicker';
import { PageMenu } from './PageMenu';
import { PAGE_DRAG_MIME } from './Sidebar';
import { KanbanBoard } from './KanbanBoard';

export type ToastTone = 'success' | 'info' | 'danger';

// Pull image files out of a drag/clipboard transfer.
const imageFilesFrom = (list: FileList | null | undefined): File[] =>
  Array.from(list ?? []).filter((file) => file.type.startsWith('image/'));

// Read an image File as a base64 data URL so it can be embedded inline in the
// note's HTML (local-first: no asset files on disk to manage).
const readImageAsDataUrl = (file: File): Promise<string> =>
  new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });

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

type TabContextMenuState = {
  noteId: string;
  x: number;
  y: number;
} | null;

type EditorPaneProps = {
  leafId: string;
  isActiveLeaf: boolean;
  canCloseLeaf: boolean;
  notes: Note[];
  tabs: string[];
  activeTabId: string | null;
  onSelectTab: (leafId: string, noteId: string) => void;
  onCloseTab: (leafId: string, noteId: string) => void;
  onContentChange: (noteId: string, content: string, title: string) => void;
  onFocusLeaf: (leafId: string) => void;
  onSplitLeaf: (leafId: string, target: SplitTarget, sourceNoteId?: string) => void;
  onCloseLeaf: (leafId: string) => void;
  onMoveTab: (
    sourceLeafId: string,
    noteId: string,
    destLeafId: string,
    destIndex: number
  ) => void;
  onMoveTabToSplit: (
    sourceLeafId: string,
    noteId: string,
    destLeafId: string,
    target: SplitTarget
  ) => void;
  onOpenNoteInLeaf: (leafId: string, noteId: string) => void;
  onUpdateNoteIcon?: (noteId: string, icon: string | null) => void;
  onArchiveNote: (note: Note) => void;
  onTrashNote: (note: Note) => void;
  onRestoreNote: (note: Note) => void;
  onDeleteNote: (note: Note) => void;
  onDuplicateNote: (note: Note, target?: 'current' | 'right' | 'down') => void;
  onCopyNoteLink: (note: Note) => void;
  onNotify?: (message: string, tone?: ToastTone) => void;
};

export const TAB_DRAG_MIME = 'application/x-heracles-tab';

const deriveTitle = (editor: Editor) => {
  const text = editor.getText().trim();
  const firstLine = text.split('\n')[0]?.trim();
  return firstLine ? firstLine.slice(0, 64) : 'Untitled';
};

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

const formatDate = (value: string) => {
  const date = new Date(value);
  return date.toLocaleString(undefined, {
    month: 'long',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  });
};

export const EditorPane = ({
  leafId,
  isActiveLeaf,
  canCloseLeaf,
  notes,
  tabs,
  activeTabId,
  onSelectTab,
  onCloseTab,
  onContentChange,
  onFocusLeaf,
  onSplitLeaf,
  onCloseLeaf,
  onMoveTab,
  onMoveTabToSplit,
  onOpenNoteInLeaf,
  onUpdateNoteIcon,
  onArchiveNote,
  onTrashNote,
  onRestoreNote,
  onDeleteNote,
  onDuplicateNote,
  onCopyNoteLink,
  onNotify
}: EditorPaneProps) => {
  const note = useMemo(
    () => notes.find((item) => item.id === activeTabId) ?? null,
    [notes, activeTabId]
  );
  const openTabs = useMemo(
    () =>
      tabs
        .map((tabId) => notes.find((item) => item.id === tabId))
        .filter((item): item is Note => Boolean(item)),
    [notes, tabs]
  );

  const isTrashed = Boolean(note?.trashed);
  const paneBodyRef = useRef<HTMLDivElement | null>(null);
  const blockHandleRef = useRef<HTMLDivElement | null>(null);
  const blockMenuRef = useRef<HTMLDivElement | null>(null);
  const blockMenuSearchRef = useRef<HTMLInputElement | null>(null);
  const tabContextRef = useRef<HTMLDivElement | null>(null);
  const titleRef = useRef<HTMLHeadingElement | null>(null);
  // The block element currently under the gutter handle (drag source for columns).
  const hoverBlockDomRef = useRef<HTMLElement | null>(null);
  const [blockAnchor, setBlockAnchor] = useState<BlockAnchor>({
    top: 0,
    left: 0,
    height: 0,
    visible: false
  });
  const [blockHandleVisible, setBlockHandleVisible] = useState(false);
  const [blockMenuOpen, setBlockMenuOpen] = useState(false);
  const [blockMenuQuery, setBlockMenuQuery] = useState('');
  const [tabMenu, setTabMenu] = useState<TabContextMenuState>(null);
  const [dragOverIndex, setDragOverIndex] = useState<number | null>(null);
  const [dropZone, setDropZone] = useState<'left' | 'right' | 'up' | 'down' | 'center' | null>(
    null
  );
  const [iconPickerOpen, setIconPickerOpen] = useState(false);
  const [pageMenuRect, setPageMenuRect] = useState<DOMRect | null>(null);
  // Live drop indicator while dragging a block (column edge vs. reorder).
  const [columnDrop, setColumnDrop] = useState<{
    top: number;
    left: number;
    height: number;
    width: number;
    mode: 'column' | 'reorder';
  } | null>(null);
  const toolbarDisabled = !note || isTrashed;

  const editor = useEditor(
    {
      extensions: [
        BlockId,
        StarterKit.configure({ codeBlock: false }),
        TaskList,
        TaskItem.configure({ nested: true }),
        Link.configure({ openOnClick: false, autolink: true }),
        CodeMirrorBlock,
        CalloutBlock,
        ToggleBlock,
        ImageBlock,
        ColumnBlock,
        Column,
        ColumnsNormalizer,
        SlashCommand
      ],
      content: note?.content ?? '<p></p>',
      editorProps: {
        attributes: {
          class: 'editor-content'
        },
        // Drag a block (via the gutter grip) → reorder or make a column.
        handleDrop: (view, event) => {
          const dragEvent = event as DragEvent;
          if (dragEvent.dataTransfer?.types.includes(BLOCK_DRAG_MIME)) {
            event.preventDefault();
            setColumnDrop(null);
            const raw = dragEvent.dataTransfer.getData(BLOCK_DRAG_MIME);
            const sourcePos = Number(raw);
            const target = getDropTarget(view, dragEvent.clientX, dragEvent.clientY);
            if (Number.isFinite(sourcePos) && target) {
              return applyBlockDrop(view, sourcePos, target);
            }
            return true;
          }
          // Drag an image file in from the OS → insert an image block at the drop point.
          const images = imageFilesFrom((event as DragEvent).dataTransfer?.files);
          if (images.length === 0) {
            return false;
          }
          event.preventDefault();
          const coords = view.posAtCoords({
            left: (event as DragEvent).clientX,
            top: (event as DragEvent).clientY
          });
          const dropPos = coords ? coords.pos : view.state.selection.from;
          Promise.all(images.map(readImageAsDataUrl)).then((sources) => {
            let tr = view.state.tr;
            let pos = dropPos;
            sources.forEach((src, index) => {
              const node = view.state.schema.nodes.image.create({
                src,
                alt: images[index].name
              });
              tr = tr.insert(pos, node);
              pos += node.nodeSize;
            });
            view.dispatch(tr.scrollIntoView());
          });
          return true;
        },
        handlePaste: (view, event, slice) => {
          // Pasted image data (e.g. a screenshot) → insert image block(s).
          const images = imageFilesFrom(event.clipboardData?.files);
          if (images.length > 0) {
            event.preventDefault();
            Promise.all(images.map(readImageAsDataUrl)).then((sources) => {
              let tr = view.state.tr;
              if (!tr.selection.empty) {
                tr = tr.deleteSelection();
              }
              let pos = tr.selection.from;
              sources.forEach((src, index) => {
                const node = view.state.schema.nodes.image.create({
                  src,
                  alt: images[index].name
                });
                tr = tr.insert(pos, node);
                pos += node.nodeSize;
              });
              view.dispatch(tr.scrollIntoView());
            });
            return true;
          }
          // Check if there's HTML content in the clipboard
          const html = event.clipboardData?.getData('text/html');
          if (html && html.trim().length > 0) {
            // Convert HTML to editor-compatible format and insert
            const converted = htmlToEditorHtml(html);
            if (converted && converted !== '<p></p>') {
              view.dispatch(
                view.state.tr.replaceSelectionWith(
                  view.state.schema.nodeFromJSON({
                    type: 'paragraph',
                    content: []
                  })
                )
              );
              // Let TipTap handle the converted HTML
              return false;
            }
          }
          // Let default paste handler process it
          return false;
        },
        handleDOMEvents: {
          // Show a live indicator (column edge vs. between-blocks) while dragging a block.
          dragover: (view, event) => {
            if (!event.dataTransfer?.types.includes(BLOCK_DRAG_MIME)) return false;
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
            const container = paneBodyRef.current;
            const target = getDropTarget(view, event.clientX, event.clientY);
            if (!container || !target) {
              setColumnDrop(null);
              return true;
            }
            const cr = container.getBoundingClientRect();
            const { rect } = target;
            if (target.mode === 'column') {
              setColumnDrop({
                top: rect.top - cr.top + container.scrollTop,
                left: rect.right - cr.left + container.scrollLeft,
                height: rect.height,
                width: 3,
                mode: 'column'
              });
            } else {
              const edgeY = target.side === 'before' ? rect.top : rect.bottom;
              setColumnDrop({
                top: edgeY - cr.top + container.scrollTop,
                left: rect.left - cr.left + container.scrollLeft,
                height: 3,
                width: rect.width,
                mode: 'reorder'
              });
            }
            return true;
          },
          dragleave: (_view, event) => {
            if (!event.dataTransfer?.types.includes(BLOCK_DRAG_MIME)) return false;
            const related = event.relatedTarget as Node | null;
            if (related && paneBodyRef.current?.contains(related)) return false;
            setColumnDrop(null);
            return false;
          }
        }
      },
      onUpdate: ({ editor: instance }: { editor: Editor }) => {
        if (!note || note.trashed) {
          return;
        }

        const content = instance.getHTML();
        // Auto-derive title only for brand-new untitled notes; once renamed, the title sticks.
        const shouldDerive = !note.title || note.title === 'Untitled';
        const title = shouldDerive ? deriveTitle(instance) : note.title;
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
    if (!titleRef.current || !note) return;
    const titleNow = titleRef.current.textContent ?? '';
    const target = note.title || 'Untitled';
    if (titleNow !== target && document.activeElement !== titleRef.current) {
      titleRef.current.textContent = target;
    }
  }, [note?.id, note?.title]);

  useEffect(() => {
    if (!editor) {
      return;
    }

    editor.setEditable(Boolean(note) && !note?.trashed);
  }, [editor, note?.id, note?.trashed]);

  const updateBlockAnchorFromDom = useCallback((blockDom: Element | null) => {
    if (!paneBodyRef.current || !blockDom) {
      return false;
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

    return true;
  }, []);

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

    updateBlockAnchorFromDom(blockDom);
  }, [editor, note?.id, note?.trashed, updateBlockAnchorFromDom]);

  const handleAddBlock = useCallback(() => {
    if (!editor || !note || note.trashed) {
      return;
    }

    const blockInfo = getBlockInfo(editor);
    if (!blockInfo) {
      return;
    }

    const insertPos = blockInfo.pos + blockInfo.node.nodeSize;
    editor.commands.insertContentAt(insertPos, { type: 'paragraph' });
    editor.commands.setTextSelection(insertPos + 1);
    editor.commands.focus();
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
    if (!editor || !note || note.trashed) {
      setBlockHandleVisible(false);
      return;
    }

    const container = paneBodyRef.current;
    if (!container) {
      return;
    }

    const handlePointerMove = (event: MouseEvent) => {
      if (blockMenuOpen) {
        setBlockHandleVisible(true);
        return;
      }

      if (!(event.target instanceof Node)) {
        setBlockHandleVisible(false);
        return;
      }

      const target =
        event.target instanceof Element ? event.target : event.target.parentElement;
      if (!target) {
        setBlockHandleVisible(false);
        return;
      }

      if (blockMenuRef.current?.contains(target) || blockHandleRef.current?.contains(target)) {
        setBlockHandleVisible(true);
        return;
      }

      if (!target.closest('.editor-content')) {
        setBlockHandleVisible(false);
        return;
      }

      const blockDom = target.closest('li') ?? target.closest(BLOCK_SELECTOR);
      if (!blockDom) {
        setBlockHandleVisible(false);
        return;
      }

      const blockRect = blockDom.getBoundingClientRect();
      const gutterMin = blockRect.left - 46;
      const gutterMax = blockRect.left + 12;
      if (event.clientX < gutterMin || event.clientX > gutterMax) {
        setBlockHandleVisible(false);
        return;
      }

      hoverBlockDomRef.current = blockDom as HTMLElement;
      updateBlockAnchorFromDom(blockDom);
      setBlockHandleVisible(true);
    };

    const handlePointerLeave = () => {
      if (!blockMenuOpen) {
        setBlockHandleVisible(false);
      }
    };

    container.addEventListener('mousemove', handlePointerMove);
    container.addEventListener('mouseleave', handlePointerLeave);

    return () => {
      container.removeEventListener('mousemove', handlePointerMove);
      container.removeEventListener('mouseleave', handlePointerLeave);
    };
  }, [editor, note?.id, note?.trashed, blockMenuOpen, updateBlockAnchorFromDom]);

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

  useEffect(() => {
    if (!tabMenu) {
      return;
    }

    const handleClick = (event: MouseEvent) => {
      if (tabContextRef.current?.contains(event.target as Node)) {
        return;
      }
      setTabMenu(null);
    };

    window.addEventListener('mousedown', handleClick);
    return () => window.removeEventListener('mousedown', handleClick);
  }, [tabMenu]);

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
    } catch {
      // ignore
    }

    try {
      const copied = await copyToClipboard(link);
      onNotify?.(copied ? 'Link copied' : 'Unable to copy link', copied ? 'success' : 'danger');
    } catch {
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
    if (!editor) return;

    const info = getBlockInfo(editor);
    if (!info) return;

    const json = info.node.toJSON();
    if (json.attrs && 'blockId' in json.attrs) {
      json.attrs.blockId = null;
    }

    editor.chain().focus().insertContentAt(info.pos + info.node.nodeSize, json).run();
    setBlockMenuOpen(false);
  }, [editor]);

  const handleDeleteBlock = useCallback(() => {
    if (!editor) return;

    const info = getBlockInfo(editor);
    if (!info) return;

    editor
      .chain()
      .focus()
      .deleteRange({ from: info.pos, to: info.pos + info.node.nodeSize })
      .run();
    setBlockMenuOpen(false);
  }, [editor]);

  const handleTurnIntoHeading = useCallback(
    (level: 1 | 2 | 3) => {
      if (!editor) return;

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
      if (!editor) return;

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
        if (!info) return;

        const code = info.node.textContent;
        editor
          .chain()
          .focus()
          .command(({ tr, state }) => {
            const nodeType = state.schema.nodes.codeMirrorBlock;
            if (!nodeType) return false;

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
      { id: 'text', label: 'Text', keywords: ['paragraph'], section: 'turn', onSelect: () => handleTurnInto('paragraph') },
      { id: 'heading-1', label: 'Heading 1', keywords: ['title'], section: 'turn', onSelect: () => handleTurnIntoHeading(1) },
      { id: 'heading-2', label: 'Heading 2', keywords: ['subtitle'], section: 'turn', onSelect: () => handleTurnIntoHeading(2) },
      { id: 'heading-3', label: 'Heading 3', keywords: ['subheading'], section: 'turn', onSelect: () => handleTurnIntoHeading(3) },
      { id: 'bullet-list', label: 'Bullet list', keywords: ['list'], section: 'turn', onSelect: () => handleTurnInto('bulletList') },
      { id: 'task-list', label: 'Task list', keywords: ['todo', 'checklist'], section: 'turn', onSelect: () => handleTurnInto('taskList') },
      { id: 'blockquote', label: 'Quote', keywords: ['blockquote'], section: 'turn', onSelect: () => handleTurnInto('blockquote') },
      { id: 'code-block', label: 'Code block', keywords: ['code'], section: 'turn', onSelect: () => handleTurnInto('codeBlock') }
    ],
    [handleTurnInto, handleTurnIntoHeading]
  );

  const normalizedQuery = blockMenuQuery.trim().toLowerCase();
  const filterMenuItems = useCallback(
    (items: BlockMenuItem[]) => {
      if (!normalizedQuery) return items;
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
    <section
      className={`leaf ${isActiveLeaf ? 'is-active' : ''}`}
      onMouseDown={() => onFocusLeaf(leafId)}
    >
      <div className="leaf__tabs">
        <div
          className="leaf__tabs-strip"
          onDragOver={(event) => {
            if (!event.dataTransfer.types.includes(TAB_DRAG_MIME)) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
            if (dragOverIndex === null) setDragOverIndex(openTabs.length);
          }}
          onDragLeave={(event) => {
            if (event.currentTarget.contains(event.relatedTarget as Node)) return;
            setDragOverIndex(null);
          }}
          onDrop={(event) => {
            const payload = event.dataTransfer.getData(TAB_DRAG_MIME);
            if (!payload) return;
            event.preventDefault();
            const [sourceLeafId, noteId] = payload.split('|');
            const insertAt = dragOverIndex ?? openTabs.length;
            setDragOverIndex(null);
            if (sourceLeafId && noteId) onMoveTab(sourceLeafId, noteId, leafId, insertAt);
          }}
        >
          {openTabs.length === 0 && <span className="leaf__tabs-empty">No tabs open</span>}
          {openTabs.map((tabNote, index) => (
            <div
              key={tabNote.id}
              className={`leaf__tab ${tabNote.id === activeTabId ? 'is-active' : ''} ${
                dragOverIndex === index ? 'is-drop-before' : ''
              } ${dragOverIndex === index + 1 && index === openTabs.length - 1 ? 'is-drop-after' : ''}`}
              draggable
              onDragStart={(event) => {
                event.dataTransfer.effectAllowed = 'move';
                event.dataTransfer.setData(TAB_DRAG_MIME, `${leafId}|${tabNote.id}`);
                event.dataTransfer.setData('text/plain', tabNote.title || 'Untitled');
              }}
              onDragOver={(event) => {
                if (!event.dataTransfer.types.includes(TAB_DRAG_MIME)) return;
                event.preventDefault();
                event.dataTransfer.dropEffect = 'move';
                const rect = event.currentTarget.getBoundingClientRect();
                const before = event.clientX < rect.left + rect.width / 2;
                setDragOverIndex(before ? index : index + 1);
              }}
              onDrop={(event) => {
                const payload = event.dataTransfer.getData(TAB_DRAG_MIME);
                if (!payload) return;
                event.preventDefault();
                event.stopPropagation();
                const [sourceLeafId, noteId] = payload.split('|');
                const insertAt = dragOverIndex ?? index;
                setDragOverIndex(null);
                if (sourceLeafId && noteId) onMoveTab(sourceLeafId, noteId, leafId, insertAt);
              }}
              onDragEnd={() => setDragOverIndex(null)}
              onClick={() => onSelectTab(leafId, tabNote.id)}
              onContextMenu={(event: ReactMouseEvent<HTMLDivElement>) => {
                event.preventDefault();
                event.stopPropagation();
                setTabMenu({ noteId: tabNote.id, x: event.clientX, y: event.clientY });
              }}
              role="button"
              tabIndex={0}
              onKeyDown={(event: ReactKeyboardEvent<HTMLDivElement>) => {
                if (event.key === 'Enter') {
                  onSelectTab(leafId, tabNote.id);
                }
              }}
            >
              <span className="leaf__tab-icon">{tabNote.icon || '▦'}</span>
              <span className="leaf__tab-title">{tabNote.title || 'Untitled'}</span>
              <button
                type="button"
                className="leaf__tab-close"
                onClick={(event: ReactMouseEvent<HTMLButtonElement>) => {
                  event.stopPropagation();
                  onCloseTab(leafId, tabNote.id);
                }}
                aria-label={`Close ${tabNote.title}`}
              >
                ×
              </button>
            </div>
          ))}
        </div>
        <div className="leaf__tabs-actions">
          {note && (
            <button
              type="button"
              className="leaf__tab-action"
              title="More page actions"
              aria-label="Open page menu"
              onClick={(event: ReactMouseEvent<HTMLButtonElement>) => {
                const rect = event.currentTarget.getBoundingClientRect();
                setPageMenuRect(pageMenuRect ? null : rect);
              }}
            >
              ⋯
            </button>
          )}
          <button
            type="button"
            className="leaf__tab-action"
            title="Split right"
            onClick={() => onSplitLeaf(leafId, 'right')}
          >
            ⊟
          </button>
          <button
            type="button"
            className="leaf__tab-action"
            title="Split down"
            onClick={() => onSplitLeaf(leafId, 'down')}
          >
            ⊟
          </button>
          {canCloseLeaf && (
            <button
              type="button"
              className="leaf__tab-action"
              title="Close pane"
              onClick={() => onCloseLeaf(leafId)}
            >
              ×
            </button>
          )}
        </div>
      </div>
      <div
        className="leaf__body"
        ref={paneBodyRef}
        onContextMenu={handleBlockContextMenu}
        onDragEnter={(event) => {
          const types = event.dataTransfer.types;
          if (!types.includes(TAB_DRAG_MIME) && !types.includes(PAGE_DRAG_MIME)) return;
          event.preventDefault();
          event.dataTransfer.dropEffect = 'move';
        }}
        onDragOver={(event) => {
          const types = event.dataTransfer.types;
          if (!types.includes(TAB_DRAG_MIME) && !types.includes(PAGE_DRAG_MIME)) return;
          event.preventDefault();
          event.dataTransfer.dropEffect = 'move';
          const rect = event.currentTarget.getBoundingClientRect();
          const x = event.clientX - rect.left;
          const y = event.clientY - rect.top;
          const w = rect.width;
          const h = rect.height;
          // 25% margins on each side; remaining centre means "stack as tab"
          const leftEdge = w * 0.25;
          const rightEdge = w * 0.75;
          const topEdge = h * 0.25;
          const bottomEdge = h * 0.75;
          let zone: typeof dropZone = 'center';
          // Pick the zone whose edge the pointer is closest to
          const distLeft = x;
          const distRight = w - x;
          const distTop = y;
          const distBottom = h - y;
          const min = Math.min(distLeft, distRight, distTop, distBottom);
          if (x < leftEdge && min === distLeft) zone = 'left';
          else if (x > rightEdge && min === distRight) zone = 'right';
          else if (y < topEdge && min === distTop) zone = 'up';
          else if (y > bottomEdge && min === distBottom) zone = 'down';
          else zone = 'center';
          if (zone !== dropZone) setDropZone(zone);
        }}
        onDragLeave={(event) => {
          // Use geometry rather than relatedTarget — relatedTarget is unreliable
          // during HTML5 drags (esp. when crossing child boundaries inside the body).
          const rect = event.currentTarget.getBoundingClientRect();
          const stillInside =
            event.clientX >= rect.left &&
            event.clientX < rect.right &&
            event.clientY >= rect.top &&
            event.clientY < rect.bottom;
          if (stillInside) return;
          setDropZone(null);
        }}
        onDrop={(event) => {
          const types = event.dataTransfer.types;
          const isTabDrag = types.includes(TAB_DRAG_MIME);
          const isPageDrag = types.includes(PAGE_DRAG_MIME);
          if (!isTabDrag && !isPageDrag) return;
          event.preventDefault();
          event.stopPropagation();
          const zone = dropZone ?? 'center';
          setDropZone(null);

          if (isTabDrag) {
            const payload = event.dataTransfer.getData(TAB_DRAG_MIME);
            const [sourceLeafId, noteId] = payload.split('|');
            if (!sourceLeafId || !noteId) return;
            if (zone === 'center') {
              onMoveTab(sourceLeafId, noteId, leafId, Number.MAX_SAFE_INTEGER);
              return;
            }
            onMoveTabToSplit(sourceLeafId, noteId, leafId, zone);
            return;
          }

          // Page drag from the sidebar.
          const noteId = event.dataTransfer.getData(PAGE_DRAG_MIME);
          if (!noteId) return;
          if (zone === 'center') {
            onOpenNoteInLeaf(leafId, noteId);
            return;
          }
          onSplitLeaf(leafId, zone, noteId);
        }}
      >
        {dropZone && (
          <div className={`leaf__drop-overlay leaf__drop-overlay--${dropZone}`} aria-hidden />
        )}
        {/* Stable, dedicated host for the BubbleMenu. @tiptap/extension-bubble-menu
            calls element.remove() on its child <div>, detaching it from React's tree.
            Isolating it inside this empty host prevents that detachment from breaking
            sibling reconciliation in leaf__body. */}
        <div className="bubble-menu-host">
          {editor && (
            <BubbleMenu
              editor={editor}
              className="editor-bubble"
              tippyOptions={{
                duration: 150,
                placement: 'top-start',
                offset: [0, 8]
              }}
              shouldShow={({ editor: bubbleEditor, state }) =>
                Boolean(note) &&
                note?.kind !== 'todolist' &&
                !isTrashed &&
                !toolbarDisabled &&
                bubbleEditor.isFocused &&
                !state.selection.empty
              }
            >
            <div className="editor-toolbar__group">
              <button
                type="button"
                className={`editor-toolbar__button ${
                  editor.isActive('paragraph') ? 'is-active' : ''
                }`}
                aria-pressed={editor.isActive('paragraph')}
                disabled={toolbarDisabled}
                onClick={() => editor.chain().focus().setParagraph().run()}
              >
                Text
              </button>
              {([1, 2, 3] as const).map((level) => (
                <button
                  key={level}
                  type="button"
                  className={`editor-toolbar__button ${
                    editor.isActive('heading', { level }) ? 'is-active' : ''
                  }`}
                  aria-pressed={editor.isActive('heading', { level })}
                  disabled={toolbarDisabled}
                  onClick={() => editor.chain().focus().toggleHeading({ level }).run()}
                >
                  H{level}
                </button>
              ))}
            </div>
            <div className="editor-toolbar__group">
              <button
                type="button"
                className={`editor-toolbar__button editor-toolbar__button--bold ${
                  editor.isActive('bold') ? 'is-active' : ''
                }`}
                aria-pressed={editor.isActive('bold')}
                disabled={toolbarDisabled}
                onClick={() => editor.chain().focus().toggleBold().run()}
              >
                B
              </button>
              <button
                type="button"
                className={`editor-toolbar__button editor-toolbar__button--italic ${
                  editor.isActive('italic') ? 'is-active' : ''
                }`}
                aria-pressed={editor.isActive('italic')}
                disabled={toolbarDisabled}
                onClick={() => editor.chain().focus().toggleItalic().run()}
              >
                I
              </button>
              <button
                type="button"
                className={`editor-toolbar__button editor-toolbar__button--strike ${
                  editor.isActive('strike') ? 'is-active' : ''
                }`}
                aria-pressed={editor.isActive('strike')}
                disabled={toolbarDisabled}
                onClick={() => editor.chain().focus().toggleStrike().run()}
              >
                S
              </button>
            </div>
          </BubbleMenu>
          )}
        </div>
        {!note && (
          <div className="leaf__empty">
            Open a note from the sidebar, or right-click a tab to split this pane.
          </div>
        )}
        {note && (
          <div className="leaf__page">
            <div className="leaf__page-meta">
              <div className="leaf__icon-row">
                {note.icon ? (
                  <button
                    type="button"
                    className="leaf__icon leaf__icon--large"
                    onClick={() => setIconPickerOpen((open) => !open)}
                    disabled={note.trashed}
                    title="Change icon"
                  >
                    {note.icon}
                  </button>
                ) : (
                  !note.trashed && (
                    <button
                      type="button"
                      className="leaf__add-icon"
                      onClick={() => setIconPickerOpen((open) => !open)}
                    >
                      <span aria-hidden>☺</span> Add icon
                    </button>
                  )
                )}
                {iconPickerOpen && onUpdateNoteIcon && (
                  <IconPicker
                    currentIcon={note.icon}
                    onPick={(icon) => {
                      onUpdateNoteIcon(note.id, icon);
                      setIconPickerOpen(false);
                    }}
                    onRemove={() => {
                      onUpdateNoteIcon(note.id, null);
                      setIconPickerOpen(false);
                    }}
                    onClose={() => setIconPickerOpen(false)}
                  />
                )}
              </div>
              <h1
                ref={titleRef}
                className="leaf__title"
                contentEditable={!isTrashed}
                suppressContentEditableWarning
                spellCheck={false}
                data-placeholder="Untitled"
                onFocus={(event) => {
                  if ((event.currentTarget.textContent ?? '') === 'Untitled') {
                    const range = document.createRange();
                    range.selectNodeContents(event.currentTarget);
                    const sel = window.getSelection();
                    sel?.removeAllRanges();
                    sel?.addRange(range);
                  }
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    (event.currentTarget as HTMLElement).blur();
                    editor?.commands.focus('start');
                  }
                  if (event.key === 'Escape') {
                    event.preventDefault();
                    if (titleRef.current) titleRef.current.textContent = note.title || 'Untitled';
                    (event.currentTarget as HTMLElement).blur();
                  }
                }}
                onBlur={(event) => {
                  const next = (event.currentTarget.textContent ?? '').trim();
                  const final = next || 'Untitled';
                  if (final !== note.title) {
                    onContentChange(note.id, note.content, final);
                  }
                  if (event.currentTarget.textContent !== final) {
                    event.currentTarget.textContent = final;
                  }
                }}
              />

              <div className="leaf__subtitle">
                {note.trashed && 'In Trash · Read-only'}
                {!note.trashed && note.archived && 'Archived'}
                {!note.trashed && !note.archived && `Last edited ${formatDate(note.updatedAt)}`}
              </div>
            </div>
            {note.kind !== 'todolist' &&
              editor &&
              !isTrashed &&
              blockAnchor.visible &&
              (blockMenuOpen || blockHandleVisible) && (
                <div
                  ref={blockHandleRef}
                  className="block-handle"
                  style={{
                    top: blockAnchor.top + Math.min(8, blockAnchor.height / 4),
                    left: Math.max(8, blockAnchor.left - 42)
                  }}
                >
                  <div
                    className="block-handle__button block-handle__button--drag"
                    role="button"
                    aria-label="Drag to move or make a column"
                    title="Drag to move · drag to a block's right edge to make a column"
                    draggable
                    onDragStart={(event: ReactDragEvent<HTMLDivElement>) => {
                      const dom = hoverBlockDomRef.current;
                      if (!dom || !editor) {
                        event.preventDefault();
                        return;
                      }
                      try {
                        const posInside = editor.view.posAtDOM(dom, 0);
                        const source = resolveOuterBlock(editor.state.doc, posInside);
                        if (!source) {
                          event.preventDefault();
                          return;
                        }
                        event.dataTransfer.effectAllowed = 'move';
                        event.dataTransfer.setData(BLOCK_DRAG_MIME, String(source.pos));
                        event.dataTransfer.setData('text/plain', '');
                        setBlockMenuOpen(false);
                      } catch {
                        event.preventDefault();
                      }
                    }}
                    onDragEnd={() => setColumnDrop(null)}
                  >
                    ⠿
                  </div>
                  <button
                    type="button"
                    className="block-handle__button block-handle__button--add"
                    aria-label="Add block"
                    onMouseDown={(event: ReactMouseEvent<HTMLButtonElement>) =>
                      event.preventDefault()
                    }
                    onClick={handleAddBlock}
                  >
                    +
                  </button>
                  <button
                    type="button"
                    className="block-handle__button block-handle__button--menu"
                    aria-label="Block actions"
                    onMouseDown={(event: ReactMouseEvent<HTMLButtonElement>) =>
                      event.preventDefault()
                    }
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
            {columnDrop && (
              <div
                className={`block-drop-indicator block-drop-indicator--${columnDrop.mode}`}
                style={{
                  top: columnDrop.top,
                  left: columnDrop.left,
                  width: columnDrop.width,
                  height: columnDrop.height
                }}
              />
            )}
            {note.kind !== 'todolist' && editor && !isTrashed && blockAnchor.visible && blockMenuOpen && (
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
            {note.kind === 'todolist' ? (
              <KanbanBoard
                noteId={note.id}
                todoListTitle={note.title}
                readOnly={isTrashed}
                onNotify={onNotify}
              />
            ) : (
              <EditorContent editor={editor} />
            )}
          </div>
        )}
      </div>
      {tabMenu && (
        <div
          ref={tabContextRef}
          className="context-menu"
          style={{ top: tabMenu.y, left: tabMenu.x }}
        >
          <button
            type="button"
            className="context-menu__item"
            onClick={() => {
              onSplitLeaf(leafId, 'right', tabMenu.noteId);
              setTabMenu(null);
            }}
          >
            Split right
          </button>
          <button
            type="button"
            className="context-menu__item"
            onClick={() => {
              onSplitLeaf(leafId, 'down', tabMenu.noteId);
              setTabMenu(null);
            }}
          >
            Split down
          </button>
          <div className="context-menu__divider" />
          <button
            type="button"
            className="context-menu__item context-menu__item--danger"
            onClick={() => {
              onCloseTab(leafId, tabMenu.noteId);
              setTabMenu(null);
            }}
          >
            Close tab
          </button>
        </div>
      )}
      {pageMenuRect && note && (
        <PageMenu
          note={note}
          anchorRect={pageMenuRect}
          onClose={() => setPageMenuRect(null)}
          onCopyLink={onCopyNoteLink}
          onDuplicate={onDuplicateNote}
          onArchive={onArchiveNote}
          onTrash={onTrashNote}
          onRestore={onRestoreNote}
          onDelete={onDeleteNote}
          onSplitOpen={(targetNote, target) => onSplitLeaf(leafId, target, targetNote.id)}
        />
      )}
    </section>
  );
};

export default EditorPane;
