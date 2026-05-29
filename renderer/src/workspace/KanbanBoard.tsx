import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type DragEvent as ReactDragEvent,
  type KeyboardEvent as ReactKeyboardEvent
} from 'react';
import type { Priority, Task, TaskColumn } from '../types/tasks';
import { TaskCard } from './TaskCard';
import { TaskDetailModal } from './TaskDetailModal';

const TASK_DRAG_MIME = 'application/x-heracles-task';

type DropPos = 'before' | 'after' | 'inside-column';
type DragOver = { columnId: string; pos: DropPos; targetTaskId?: string } | null;

type KanbanBoardProps = {
  noteId: string;
  todoListTitle: string;
  readOnly?: boolean;
  onNotify?: (message: string, tone?: 'success' | 'info' | 'danger') => void;
};

export const KanbanBoard = ({
  noteId,
  todoListTitle,
  readOnly = false,
  onNotify
}: KanbanBoardProps) => {
  const [columns, setColumns] = useState<TaskColumn[]>([]);
  const [tasks, setTasks] = useState<Task[]>([]);
  const [loading, setLoading] = useState(true);
  const [openTaskId, setOpenTaskId] = useState<string | null>(null);

  // Inline column rename
  const [renamingColumnId, setRenamingColumnId] = useState<string | null>(null);
  const [columnDraft, setColumnDraft] = useState('');
  const renameRef = useRef<HTMLInputElement | null>(null);

  // Inline column-add task UI
  const [composerColumnId, setComposerColumnId] = useState<string | null>(null);
  const [composerDraft, setComposerDraft] = useState('');
  const composerRef = useRef<HTMLTextAreaElement | null>(null);

  // Drag state
  const [dragOver, setDragOver] = useState<DragOver>(null);

  useEffect(() => {
    if (renamingColumnId && renameRef.current) {
      renameRef.current.focus();
      renameRef.current.select();
    }
  }, [renamingColumnId]);

  useEffect(() => {
    if (composerColumnId && composerRef.current) {
      composerRef.current.focus();
    }
  }, [composerColumnId]);

  // Initial load + reload on noteId change
  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!window.heracles?.tasks) {
        setLoading(false);
        return;
      }
      setLoading(true);
      const [cols, tks] = await Promise.all([
        window.heracles.tasks.listColumns({ todoListId: noteId }),
        window.heracles.tasks.list({ todoListId: noteId })
      ]);
      if (cancelled) return;
      setColumns(cols);
      setTasks(tks);
      setLoading(false);
    })();
    return () => {
      cancelled = true;
    };
  }, [noteId]);

  // Group root tasks (parentTaskId == null) by columnId
  const tasksByColumn = useMemo(() => {
    const map = new Map<string, Task[]>();
    for (const col of columns) map.set(col.id, []);
    for (const task of tasks) {
      if (task.parentTaskId == null && task.columnId && map.has(task.columnId)) {
        map.get(task.columnId)!.push(task);
      }
    }
    for (const list of map.values()) {
      list.sort((a, b) => a.position - b.position);
    }
    return map;
  }, [columns, tasks]);

  // Map of subtasks by parent id for nested counts and modal nav
  const subtasksByParent = useMemo(() => {
    const map = new Map<string, Task[]>();
    for (const task of tasks) {
      if (task.parentTaskId) {
        const list = map.get(task.parentTaskId) ?? [];
        list.push(task);
        map.set(task.parentTaskId, list);
      }
    }
    for (const list of map.values()) {
      list.sort((a, b) => a.position - b.position);
    }
    return map;
  }, [tasks]);

  // ── Helpers ─────────────────────────────────────────────────────────────
  const upsertTask = useCallback((task: Task) => {
    setTasks((prev) => {
      const idx = prev.findIndex((t) => t.id === task.id);
      if (idx >= 0) {
        const copy = [...prev];
        copy[idx] = task;
        return copy;
      }
      return [...prev, task];
    });
  }, []);

  const removeTaskIds = useCallback((ids: string[]) => {
    setTasks((prev) => prev.filter((t) => !ids.includes(t.id)));
  }, []);

  const refreshTasks = useCallback(async () => {
    if (!window.heracles?.tasks) return;
    const next = await window.heracles.tasks.list({ todoListId: noteId });
    setTasks(next);
  }, [noteId]);

  // ── Column actions ──────────────────────────────────────────────────────
  const handleAddColumn = useCallback(async () => {
    if (!window.heracles?.tasks) return;
    const created = await window.heracles.tasks.createColumn({
      todoListId: noteId,
      name: 'New column'
    });
    setColumns((prev) => [...prev, created]);
    setRenamingColumnId(created.id);
    setColumnDraft(created.name);
  }, [noteId]);

  const beginRenameColumn = useCallback((column: TaskColumn) => {
    setRenamingColumnId(column.id);
    setColumnDraft(column.name);
  }, []);

  const commitRenameColumn = useCallback(async () => {
    if (!renamingColumnId || !window.heracles?.tasks) {
      setRenamingColumnId(null);
      return;
    }
    const next = columnDraft.trim();
    const id = renamingColumnId;
    setRenamingColumnId(null);
    if (!next) return;
    const updated = await window.heracles.tasks.updateColumn({ id, name: next });
    if (updated) {
      setColumns((prev) => prev.map((c) => (c.id === id ? updated : c)));
    }
  }, [columnDraft, renamingColumnId]);

  const cancelRenameColumn = useCallback(() => setRenamingColumnId(null), []);

  const handleDeleteColumn = useCallback(
    async (column: TaskColumn) => {
      if (!window.heracles?.tasks) return;
      const ok = window.confirm(
        `Delete the "${column.name}" column? Tasks inside will be moved to another column.`
      );
      if (!ok) return;
      const result = await window.heracles.tasks.deleteColumn({ id: column.id });
      setColumns((prev) => prev.filter((c) => c.id !== column.id));
      // Refresh tasks since some were moved to a fallback column.
      await refreshTasks();
      // Refresh columns list because deleteColumn might have created a fallback.
      const cols = await window.heracles.tasks.listColumns({ todoListId: noteId });
      setColumns(cols);
      onNotify?.('Column deleted', 'info');
      void result;
    },
    [noteId, onNotify, refreshTasks]
  );

  // ── Task actions ────────────────────────────────────────────────────────
  const beginCompose = useCallback((columnId: string) => {
    setComposerColumnId(columnId);
    setComposerDraft('');
  }, []);

  const commitCompose = useCallback(async () => {
    const columnId = composerColumnId;
    const title = composerDraft.trim();
    if (!columnId || !title || !window.heracles?.tasks) {
      setComposerColumnId(null);
      setComposerDraft('');
      return;
    }
    const created = await window.heracles.tasks.create({
      todoListId: noteId,
      columnId,
      title
    });
    setTasks((prev) => [...prev, created]);
    // Stay in compose mode for fast batch entry.
    setComposerDraft('');
  }, [composerColumnId, composerDraft, noteId]);

  const cancelCompose = useCallback(() => {
    setComposerColumnId(null);
    setComposerDraft('');
  }, []);

  const handleToggleTask = useCallback(
    async (taskId: string, completed: boolean) => {
      if (!window.heracles?.tasks) return;
      const updated = await window.heracles.tasks.toggleComplete({
        id: taskId,
        completed
      });
      if (updated) upsertTask(updated);
    },
    [upsertTask]
  );

  const handleUpdateTask = useCallback(
    async (
      taskId: string,
      patch: Partial<{
        title: string;
        description: string;
        completed: boolean;
        dueDate: string | null;
        deadline: string | null;
        priority: Priority;
        labels: string[];
        reminders: string[];
        location: string | null;
      }>
    ) => {
      if (!window.heracles?.tasks) return;
      const updated = await window.heracles.tasks.update({ id: taskId, ...patch });
      if (updated) upsertTask(updated);
    },
    [upsertTask]
  );

  const handleDeleteTask = useCallback(
    async (taskId: string) => {
      if (!window.heracles?.tasks) return;
      const result = await window.heracles.tasks.remove({ id: taskId });
      removeTaskIds(result.deletedIds);
      if (openTaskId && result.deletedIds.includes(openTaskId)) {
        setOpenTaskId(null);
      }
      onNotify?.('Task deleted', 'info');
    },
    [onNotify, openTaskId, removeTaskIds]
  );

  const handleAddSubtask = useCallback(
    async (parentTaskId: string, title: string) => {
      if (!window.heracles?.tasks) return null;
      const trimmed = title.trim();
      if (!trimmed) return null;
      const created = await window.heracles.tasks.create({
        todoListId: noteId,
        parentTaskId,
        title: trimmed
      });
      setTasks((prev) => [...prev, created]);
      return created;
    },
    [noteId]
  );

  // ── Drag and drop ───────────────────────────────────────────────────────
  const handleTaskDragStart = useCallback(
    (task: Task, event: ReactDragEvent<HTMLDivElement>) => {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData(TASK_DRAG_MIME, task.id);
      event.dataTransfer.setData('text/plain', task.title || 'Untitled');
    },
    []
  );

  const computeCardDropPos = (event: ReactDragEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    return event.clientY - rect.top < rect.height / 2 ? 'before' : 'after';
  };

  const handleCardDragOver = useCallback(
    (column: TaskColumn, target: Task, event: ReactDragEvent<HTMLDivElement>) => {
      if (!event.dataTransfer.types.includes(TASK_DRAG_MIME)) return;
      event.preventDefault();
      event.stopPropagation();
      event.dataTransfer.dropEffect = 'move';
      const pos = computeCardDropPos(event) as DropPos;
      setDragOver((prev) => {
        if (
          prev &&
          prev.columnId === column.id &&
          prev.targetTaskId === target.id &&
          prev.pos === pos
        ) {
          return prev;
        }
        return { columnId: column.id, targetTaskId: target.id, pos };
      });
    },
    []
  );

  const handleColumnDragOver = useCallback(
    (column: TaskColumn, event: ReactDragEvent<HTMLElement>) => {
      if (!event.dataTransfer.types.includes(TASK_DRAG_MIME)) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = 'move';
      setDragOver((prev) => {
        if (prev && prev.columnId === column.id && prev.pos === 'inside-column' && !prev.targetTaskId) {
          return prev;
        }
        return { columnId: column.id, pos: 'inside-column' };
      });
    },
    []
  );

  const handleColumnDragLeave = useCallback(
    (event: ReactDragEvent<HTMLElement>) => {
      if (event.currentTarget.contains(event.relatedTarget as Node)) return;
      setDragOver(null);
    },
    []
  );

  const handleColumnDrop = useCallback(
    async (column: TaskColumn, event: ReactDragEvent<HTMLElement>) => {
      if (!event.dataTransfer.types.includes(TASK_DRAG_MIME)) return;
      event.preventDefault();
      event.stopPropagation();
      const sourceId = event.dataTransfer.getData(TASK_DRAG_MIME);
      const drop = dragOver;
      setDragOver(null);
      if (!sourceId) return;
      if (!window.heracles?.tasks) return;
      let beforeId: string | null = null;
      if (drop && drop.columnId === column.id && drop.targetTaskId) {
        const colTasks = tasksByColumn.get(column.id) ?? [];
        const idx = colTasks.findIndex((t) => t.id === drop.targetTaskId);
        if (idx >= 0) {
          beforeId =
            drop.pos === 'before'
              ? colTasks[idx].id
              : colTasks[idx + 1]
                ? colTasks[idx + 1].id
                : null;
        }
      }
      const updated = await window.heracles.tasks.move({
        id: sourceId,
        columnId: column.id,
        parentTaskId: null,
        beforeId
      });
      if (updated) {
        // Re-fetch to get accurate positions for all sibling tasks.
        await refreshTasks();
      }
    },
    [dragOver, refreshTasks, tasksByColumn]
  );

  // ── Render ──────────────────────────────────────────────────────────────
  if (loading) {
    return <div className="kanban kanban--loading">Loading…</div>;
  }

  const openTask = openTaskId ? tasks.find((t) => t.id === openTaskId) ?? null : null;

  return (
    <div className="kanban">
      <div className="kanban__board">
        {columns.map((column) => {
          const colTasks = tasksByColumn.get(column.id) ?? [];
          const isRenaming = renamingColumnId === column.id;
          const isComposing = composerColumnId === column.id;
          const dropTarget = dragOver && dragOver.columnId === column.id ? dragOver : null;
          return (
            <section
              key={column.id}
              className={`kanban__column ${dropTarget ? 'is-drop' : ''}`}
              onDragOver={(event) => handleColumnDragOver(column, event)}
              onDragLeave={handleColumnDragLeave}
              onDrop={(event) => handleColumnDrop(column, event)}
            >
              <header className="kanban__column-header">
                {isRenaming ? (
                  <input
                    ref={renameRef}
                    className="kanban__column-rename"
                    value={columnDraft}
                    onChange={(event: ChangeEvent<HTMLInputElement>) =>
                      setColumnDraft(event.target.value)
                    }
                    onBlur={commitRenameColumn}
                    onKeyDown={(event: ReactKeyboardEvent<HTMLInputElement>) => {
                      if (event.key === 'Enter') {
                        event.preventDefault();
                        commitRenameColumn();
                      } else if (event.key === 'Escape') {
                        event.preventDefault();
                        cancelRenameColumn();
                      }
                    }}
                  />
                ) : (
                  <button
                    type="button"
                    className="kanban__column-title"
                    onClick={() => !readOnly && beginRenameColumn(column)}
                    disabled={readOnly}
                    title={readOnly ? undefined : 'Click to rename'}
                  >
                    {column.name}
                    <span className="kanban__column-count">{colTasks.length}</span>
                  </button>
                )}
                {!readOnly && (
                  <div className="kanban__column-actions">
                    <button
                      type="button"
                      className="kanban__column-action"
                      title="Add task"
                      aria-label={`Add task to ${column.name}`}
                      onClick={() => beginCompose(column.id)}
                    >
                      ＋
                    </button>
                    <button
                      type="button"
                      className="kanban__column-action"
                      title="Delete column"
                      aria-label={`Delete column ${column.name}`}
                      onClick={() => handleDeleteColumn(column)}
                    >
                      ⋯
                    </button>
                  </div>
                )}
              </header>

              <div className="kanban__column-body">
                {colTasks.length === 0 && !isComposing && (
                  <div className="kanban__column-empty">No tasks yet</div>
                )}
                {colTasks.map((task) => {
                  const subtasks = subtasksByParent.get(task.id) ?? [];
                  const completedSubtasks = subtasks.filter((t) => t.completed).length;
                  const isDropTargetCard =
                    dropTarget && dropTarget.targetTaskId === task.id ? dropTarget.pos : null;
                  return (
                    <div
                      key={task.id}
                      className={`kanban__card-wrap ${
                        isDropTargetCard === 'before' ? 'is-drop-before' : ''
                      } ${isDropTargetCard === 'after' ? 'is-drop-after' : ''}`}
                      draggable={!readOnly}
                      onDragStart={(event) => handleTaskDragStart(task, event)}
                      onDragOver={(event) => handleCardDragOver(column, task, event)}
                    >
                      <TaskCard
                        task={task}
                        subtaskCount={subtasks.length}
                        completedSubtaskCount={completedSubtasks}
                        readOnly={readOnly}
                        onOpen={() => setOpenTaskId(task.id)}
                        onToggleComplete={(completed: boolean) =>
                          handleToggleTask(task.id, completed)
                        }
                      />
                    </div>
                  );
                })}

                {isComposing ? (
                  <div className="kanban__composer">
                    <textarea
                      ref={composerRef}
                      className="kanban__composer-input"
                      placeholder="Task name"
                      rows={2}
                      value={composerDraft}
                      onChange={(event: ChangeEvent<HTMLTextAreaElement>) =>
                        setComposerDraft(event.target.value)
                      }
                      onKeyDown={(event: ReactKeyboardEvent<HTMLTextAreaElement>) => {
                        if (event.key === 'Enter' && !event.shiftKey) {
                          event.preventDefault();
                          commitCompose();
                        } else if (event.key === 'Escape') {
                          event.preventDefault();
                          cancelCompose();
                        }
                      }}
                    />
                    <div className="kanban__composer-actions">
                      <button
                        type="button"
                        className="kanban__composer-cancel"
                        onClick={cancelCompose}
                      >
                        Cancel
                      </button>
                      <button
                        type="button"
                        className="kanban__composer-save"
                        onClick={commitCompose}
                        disabled={!composerDraft.trim()}
                      >
                        Add task
                      </button>
                    </div>
                  </div>
                ) : (
                  !readOnly && (
                    <button
                      type="button"
                      className="kanban__column-add"
                      onClick={() => beginCompose(column.id)}
                    >
                      <span aria-hidden>＋</span> Add task
                    </button>
                  )
                )}
              </div>
            </section>
          );
        })}

        {!readOnly && (
          <div className="kanban__add-column-wrap">
            <button
              type="button"
              className="kanban__add-column"
              onClick={handleAddColumn}
            >
              <span aria-hidden>＋</span> Add column
            </button>
          </div>
        )}
      </div>

      {openTask && (
        <TaskDetailModal
          rootTaskId={openTask.id}
          tasks={tasks}
          columns={columns}
          subtasksByParent={subtasksByParent}
          todoListTitle={todoListTitle}
          readOnly={readOnly}
          onClose={() => setOpenTaskId(null)}
          onUpdateTask={handleUpdateTask}
          onToggleComplete={handleToggleTask}
          onAddSubtask={handleAddSubtask}
          onDeleteTask={handleDeleteTask}
        />
      )}
    </div>
  );
};

export default KanbanBoard;
