import { NodeViewContent, NodeViewWrapper, type NodeViewProps } from '@tiptap/react';

const ToggleBlockNodeView = ({ node, updateAttributes }: NodeViewProps) => {
  const isOpen = node.attrs.open !== false;

  return (
    <NodeViewWrapper
      className={`toggle-block ${isOpen ? 'is-open' : 'is-closed'}`}
      data-open={isOpen ? 'true' : 'false'}
    >
      <button
        type="button"
        className="toggle-block__caret"
        contentEditable={false}
        onMouseDown={(event) => event.preventDefault()}
        onClick={() => updateAttributes({ open: !isOpen })}
        aria-label={isOpen ? 'Collapse toggle' : 'Expand toggle'}
      >
        {isOpen ? '▾' : '▸'}
      </button>
      <div className="toggle-block__content">
        <NodeViewContent />
      </div>
    </NodeViewWrapper>
  );
};

export default ToggleBlockNodeView;
