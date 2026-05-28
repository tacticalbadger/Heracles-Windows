import { useEffect, useMemo, useRef, useState, type ChangeEvent } from 'react';

const EMOJI_GROUPS: Array<{ label: string; emojis: string[] }> = [
  {
    label: 'Symbols',
    emojis: [
      '📄',
      '📝',
      '📓',
      '📕',
      '📗',
      '📘',
      '📙',
      '📚',
      '📂',
      '🗂',
      '🗒',
      '📋',
      '📌',
      '📎',
      '🔖',
      '🏷'
    ]
  },
  {
    label: 'Work',
    emojis: [
      '💼',
      '🗓',
      '📅',
      '📆',
      '⏰',
      '⌛',
      '🧠',
      '💡',
      '🎯',
      '🚀',
      '🛠',
      '⚙️',
      '🧰',
      '📊',
      '📈',
      '💰'
    ]
  },
  {
    label: 'Tech',
    emojis: [
      '💻',
      '🖥',
      '⌨️',
      '🖱',
      '🧮',
      '🔌',
      '🔋',
      '📱',
      '☁️',
      '🌐',
      '🛰',
      '📡',
      '🧪',
      '🧬',
      '🔐',
      '🗝'
    ]
  },
  {
    label: 'People',
    emojis: [
      '👤',
      '👥',
      '🧑',
      '🧑‍💻',
      '🧑‍🎨',
      '🧑‍🔬',
      '🧑‍🚀',
      '🧑‍🏫',
      '🤝',
      '👋',
      '🫶',
      '🧘',
      '🚶',
      '🏃',
      '🛌',
      '🎉'
    ]
  },
  {
    label: 'Nature',
    emojis: [
      '🌱',
      '🌲',
      '🌳',
      '🌵',
      '🌷',
      '🌸',
      '🌹',
      '🍀',
      '🍂',
      '🍃',
      '☀️',
      '🌙',
      '⭐',
      '🌈',
      '🔥',
      '💧'
    ]
  },
  {
    label: 'Food',
    emojis: [
      '☕',
      '🍵',
      '🍷',
      '🍺',
      '🥐',
      '🍞',
      '🥗',
      '🍱',
      '🍣',
      '🍕',
      '🍔',
      '🌮',
      '🍩',
      '🍰',
      '🍫',
      '🍎'
    ]
  },
  {
    label: 'Travel',
    emojis: [
      '✈️',
      '🚆',
      '🚗',
      '🚲',
      '🛴',
      '⛵',
      '🚢',
      '🏔',
      '🏝',
      '🗺',
      '🧭',
      '🏛',
      '🏠',
      '🏢',
      '🌍',
      '🌎'
    ]
  },
  {
    label: 'Activities',
    emojis: [
      '🎮',
      '🎲',
      '🎬',
      '🎵',
      '🎸',
      '🎨',
      '📸',
      '🏆',
      '⚽',
      '🏀',
      '🎾',
      '🏋️',
      '🚴',
      '⛷',
      '🧗',
      '♟'
    ]
  }
];

type IconPickerProps = {
  currentIcon: string | null;
  onPick: (icon: string) => void;
  onRemove: () => void;
  onClose: () => void;
};

export const IconPicker = ({ currentIcon, onPick, onRemove, onClose }: IconPickerProps) => {
  const ref = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [query, setQuery] = useState('');

  useEffect(() => {
    const handle = (event: MouseEvent) => {
      if (ref.current?.contains(event.target as Node)) return;
      onClose();
    };
    window.addEventListener('mousedown', handle);
    return () => window.removeEventListener('mousedown', handle);
  }, [onClose]);

  useEffect(() => {
    const id = window.setTimeout(() => inputRef.current?.focus(), 20);
    return () => window.clearTimeout(id);
  }, []);

  const filteredGroups = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return EMOJI_GROUPS;
    return EMOJI_GROUPS.map((group) => ({
      ...group,
      emojis: group.emojis.filter(() => group.label.toLowerCase().includes(term))
    })).filter((group) => group.emojis.length > 0);
  }, [query]);

  const handleRandom = () => {
    const all = EMOJI_GROUPS.flatMap((group) => group.emojis);
    const random = all[Math.floor(Math.random() * all.length)];
    onPick(random);
  };

  return (
    <div className="icon-picker" ref={ref}>
      <div className="icon-picker__header">
        <input
          ref={inputRef}
          className="icon-picker__search"
          placeholder="Filter category…"
          value={query}
          onChange={(event: ChangeEvent<HTMLInputElement>) => setQuery(event.target.value)}
        />
        <button type="button" className="icon-picker__action" onClick={handleRandom}>
          Random
        </button>
        {currentIcon && (
          <button
            type="button"
            className="icon-picker__action icon-picker__action--danger"
            onClick={onRemove}
          >
            Remove
          </button>
        )}
      </div>
      <div className="icon-picker__body">
        {filteredGroups.map((group) => (
          <div key={group.label} className="icon-picker__group">
            <div className="icon-picker__group-label">{group.label}</div>
            <div className="icon-picker__grid">
              {group.emojis.map((emoji) => (
                <button
                  key={emoji}
                  type="button"
                  className={`icon-picker__item ${emoji === currentIcon ? 'is-active' : ''}`}
                  onClick={() => onPick(emoji)}
                >
                  {emoji}
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

export default IconPicker;
