import type { Note } from './notes';
import type { Priority, Task, TaskColumn, TaskComment } from './tasks';

declare global {
  interface Window {
    heracles: {
      platform: string;
      updates: {
        onDownloaded: (callback: (info: { version: string | null }) => void) => () => void;
        restart: () => Promise<{ success: boolean }>;
        check: () => Promise<{ success: boolean }>;
      };
      notes: {
        list: () => Promise<Note[]>;
        get: (id: string) => Promise<Note | null>;
        create: (payload: {
          title?: string;
          content?: string;
          icon?: string | null;
          parentId?: string | null;
          kind?: 'page' | 'section' | 'todolist' | 'notebook';
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
        importMarkdown: () => Promise<{
          canceled: boolean;
          files: { name: string; markdown: string }[];
        }>;
        importHtml: () => Promise<{
          canceled: boolean;
          files: { name: string; html: string }[];
        }>;
      };
      tasks: {
        listColumns: (payload: { todoListId: string }) => Promise<TaskColumn[]>;
        createColumn: (payload: { todoListId: string; name?: string }) => Promise<TaskColumn>;
        updateColumn: (payload: { id: string; name?: string }) => Promise<TaskColumn | null>;
        deleteColumn: (payload: { id: string }) => Promise<{ success: boolean; fallbackId?: string }>;
        reorderColumns: (payload: {
          todoListId: string;
          orderedIds: string[];
        }) => Promise<TaskColumn[] | null>;
        list: (payload: { todoListId: string }) => Promise<Task[]>;
        create: (payload: {
          todoListId: string;
          columnId?: string | null;
          parentTaskId?: string | null;
          title?: string;
          description?: string;
          priority?: Priority;
        }) => Promise<Task>;
        update: (payload: {
          id: string;
          title?: string;
          description?: string;
          completed?: boolean;
          dueDate?: string | null;
          deadline?: string | null;
          priority?: Priority;
          labels?: string[];
          reminders?: string[];
          location?: string | null;
        }) => Promise<Task | null>;
        toggleComplete: (payload: { id: string; completed: boolean }) => Promise<Task | null>;
        move: (payload: {
          id: string;
          columnId?: string | null;
          parentTaskId?: string | null;
          beforeId?: string | null;
        }) => Promise<Task | null>;
        remove: (payload: { id: string }) => Promise<{ success: boolean; deletedIds: string[] }>;
        listComments: (payload: { taskId: string }) => Promise<TaskComment[]>;
        createComment: (payload: { taskId: string; body: string }) => Promise<TaskComment>;
        updateComment: (payload: { id: string; body: string }) => Promise<TaskComment | null>;
        deleteComment: (payload: { id: string }) => Promise<{ success: boolean }>;
      };
    };
  }
}

export {};
