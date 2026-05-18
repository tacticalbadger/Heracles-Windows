import { useEffect, useMemo, useRef } from 'react';
import { NodeViewWrapper, type NodeViewProps } from '@tiptap/react';
import { EditorState } from '@codemirror/state';
import { EditorView, type ViewUpdate, keymap } from '@codemirror/view';
import { defaultKeymap } from '@codemirror/commands';
import { basicSetup } from '@codemirror/basic-setup';
import { javascript } from '@codemirror/lang-javascript';

const CodeMirrorNodeView = ({ node, updateAttributes }: NodeViewProps) => {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const viewRef = useRef<EditorView | null>(null);

  const themeExtension = useMemo(
    () =>
      EditorView.theme({
        '&': {
          backgroundColor: 'var(--pane-bg)',
          color: 'var(--text-primary)',
          borderRadius: '12px'
        },
        '.cm-content': {
          fontFamily: 'var(--font-mono)',
          fontSize: '13px',
          lineHeight: '1.6'
        },
        '.cm-gutters': {
          backgroundColor: 'var(--pane-bg)',
          color: 'var(--text-muted)',
          borderRight: '1px solid var(--border)'
        }
      }),
    []
  );

  useEffect(() => {
    if (!hostRef.current) {
      return;
    }

    const state = EditorState.create({
      doc: node.attrs.code ?? '',
      extensions: [
        basicSetup,
        keymap.of(defaultKeymap),
        javascript(),
        EditorView.lineWrapping,
        themeExtension,
        EditorView.updateListener.of((update: ViewUpdate) => {
          if (!update.docChanged) {
            return;
          }

          const code = update.state.doc.toString();
          updateAttributes({ code });
        })
      ]
    });

    const view = new EditorView({
      state,
      parent: hostRef.current
    });

    viewRef.current = view;

    return () => {
      view.destroy();
      viewRef.current = null;
    };
  }, [themeExtension, updateAttributes]);

  useEffect(() => {
    const view = viewRef.current;

    if (!view) {
      return;
    }

    const current = view.state.doc.toString();
    const next = node.attrs.code ?? '';

    if (current !== next) {
      view.dispatch({
        changes: {
          from: 0,
          to: current.length,
          insert: next
        }
      });
    }
  }, [node.attrs.code]);

  return (
    <NodeViewWrapper className="code-block">
      <div className="code-block__header">Code block</div>
      <div ref={hostRef} className="code-block__editor" />
    </NodeViewWrapper>
  );
};

export default CodeMirrorNodeView;
