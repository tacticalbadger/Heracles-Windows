export type NoteKind = 'page' | 'section' | 'todolist';

export type Note = {
  id: string;
  title: string;
  content: string;
  icon: string | null;
  kind: NoteKind;
  parentId: string | null;
  position: number;
  favorited: boolean;
  createdAt: string;
  updatedAt: string;
  archived: boolean;
  trashed: boolean;
};
