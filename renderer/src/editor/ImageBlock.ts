import { Node, mergeAttributes } from '@tiptap/core';

export type ImageAttrs = {
  src: string;
  alt?: string | null;
  title?: string | null;
};

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    image: {
      /** Insert a block image (e.g. from a dropped or pasted file). */
      setImage: (attrs: ImageAttrs) => ReturnType;
    };
  }
}

/**
 * A block-level image node. Images live on their own line (they are atoms, so
 * they can be selected/deleted as a unit and dragged to reorder). The `src` is
 * stored inline in the note HTML — drag/drop and paste embed images as base64
 * data URLs, keeping the app fully local-first with no asset files to manage.
 */
export const ImageBlock = Node.create({
  name: 'image',
  group: 'block',
  inline: false,
  atom: true,
  draggable: true,
  selectable: true,

  addAttributes() {
    return {
      src: { default: null },
      alt: { default: null },
      title: { default: null }
    };
  },

  parseHTML() {
    return [{ tag: 'img[src]' }];
  },

  renderHTML({ HTMLAttributes }: { HTMLAttributes: Record<string, string | null> }) {
    return ['img', mergeAttributes(HTMLAttributes, { class: 'editor-image' })];
  },

  addCommands() {
    return {
      setImage:
        (attrs: ImageAttrs) =>
        ({ commands }) =>
          commands.insertContent({ type: this.name, attrs })
    };
  }
});

export default ImageBlock;
