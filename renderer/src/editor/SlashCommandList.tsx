import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useState,
  type ForwardedRef
} from 'react';
import type { SuggestionKeyDownProps } from '@tiptap/suggestion';
import type { Editor } from '@tiptap/core';

export type CommandItem = {
  title: string;
  description: string;
  command: ({ editor, range }: { editor: Editor; range: { from: number; to: number } }) => void;
};

type SlashCommandListProps = {
  items: CommandItem[];
  command: (item: CommandItem) => void;
};

export type SlashCommandListHandle = {
  onKeyDown: (props: SuggestionKeyDownProps) => boolean;
};

const SlashCommandList = forwardRef<SlashCommandListHandle, SlashCommandListProps>(
  (
    { items, command }: SlashCommandListProps,
    ref: ForwardedRef<SlashCommandListHandle>
  ) => {
    const [selectedIndex, setSelectedIndex] = useState(0);

    useEffect(() => {
      setSelectedIndex(0);
    }, [items]);

    useImperativeHandle(ref, () => ({
      onKeyDown: ({ event }: SuggestionKeyDownProps) => {
        if (items.length === 0) {
          return false;
        }

        if (event.key === 'ArrowUp') {
          setSelectedIndex(
            (current: number) => (current - 1 + items.length) % items.length
          );
          return true;
        }

        if (event.key === 'ArrowDown') {
          setSelectedIndex((current: number) => (current + 1) % items.length);
          return true;
        }

        if (event.key === 'Enter') {
          const item = items[selectedIndex];
          if (item) {
            command(item);
          }
          return true;
        }

        return false;
      }
    }));

    return (
      <div className="slash-menu">
        {items.length === 0 && (
          <div className="slash-menu__empty">No results</div>
        )}
        {items.map((item: CommandItem, index: number) => (
          <button
            key={item.title}
            type="button"
            className={`slash-menu__item ${index === selectedIndex ? 'is-active' : ''}`}
            onClick={() => command(item)}
          >
            <div className="slash-menu__title">{item.title}</div>
            <div className="slash-menu__description">{item.description}</div>
          </button>
        ))}
      </div>
    );
  }
);

export default SlashCommandList;
