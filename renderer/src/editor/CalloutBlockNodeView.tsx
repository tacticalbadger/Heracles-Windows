import { NodeViewContent, NodeViewWrapper, type NodeViewProps } from '@tiptap/react';

const CalloutBlockNodeView = ({ node }: NodeViewProps) => {
  const emoji = (node.attrs.emoji as string | undefined) ?? '💡';

  return (
    <NodeViewWrapper className="callout-block" data-emoji={emoji}>
      <div className="callout-block__icon" contentEditable={false}>
        {emoji}
      </div>
      <div className="callout-block__content">
        <NodeViewContent />
      </div>
    </NodeViewWrapper>
  );
};

export default CalloutBlockNodeView;
