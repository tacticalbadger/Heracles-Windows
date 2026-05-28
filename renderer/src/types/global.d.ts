import type { Note } from './notes';

declare global {
  interface Window {
    heracles: {
      platform: string;
      notes: {
        list: () => Promise<Note[]>;
        get: (id: string) => Promise<Note | null>;
        create: (payload: {
          title?: string;
          content?: string;
          icon?: string | null;
          parentId?: string | null;
          kind?: 'page' | 'section';
        }) => Promise<Note>;
        update: (payload: {
          id: string;
          title?: string;
          content?: string;
          icon?: string | null;
        }) => Promise<Note | null>;
        archive: (payload: { id: string; archived: boolean }) => Promise<Note | null>;
        trash: (payload: { id: string }) => Promise<Note | null>;
        restore: (payload: { id: string }) => Promise<Note | null>;
        remove: (payload: { id: string }) => Promise<{ success: boolean }>;
        emptyTrash: () => Promise<{ success: boolean }>;
        setParent: (payload: { id: string; parentId: string | null }) => Promise<Note | null>;
        setFavorited: (payload: { id: string; favorited: boolean }) => Promise<Note | null>;
        reorder: (payload: {
          id: string;
          parentId: string | null;
          beforeId: string | null;
        }) => Promise<Note | null>;
        openWindow: (payload: { id: string }) => Promise<{ success: boolean }>;
      };
    };
  }
}

export {};
