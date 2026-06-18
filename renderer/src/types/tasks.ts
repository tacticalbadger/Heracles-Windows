export type Priority = 1 | 2 | 3 | 4;

export type TaskColumn = {
  id: string;
  todoListId: string;
  name: string;
  position: number;
  createdAt: string;
  updatedAt: string;
};

export type Task = {
  id: string;
  todoListId: string;
  parentTaskId: string | null;
  columnId: string | null;
  title: string;
  description: string;
  completed: boolean;
  position: number;
  dueDate: string | null;
  deadline: string | null;
  priority: Priority;
  labels: string[];
  reminders: string[];
  location: string | null;
  createdAt: string;
  updatedAt: string;
};

export type TaskComment = {
  id: string;
  taskId: string;
  body: string;
  createdAt: string;
  updatedAt: string;
};
