import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEvent,
  type DragEvent as ReactDragEvent,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent
} from 'react';
import type { Priority, Task, TaskColumn, TaskComment } from '../types/tasks';

type TaskPatch = Partial<{
  title: string;
  description: string;
  completed: boolean;
  dueDate: string | null;
  deadline: string | null;
  priority: Priority;
  labels: string[];
  reminders: string[];
  location: string | null;
}>;

const SUBTASK_DRAG_MIME = 'application/x-heracles-subtask';

type MoveDestination =
  | { kind: 'column'; columnId: string }
  | { kind: 'task'; parentTaskId: string };

type TaskDetailModalProps = {
  rootTaskId: string;
  tasks: Task[];
  columns: TaskColumn[];
  subtasksByParent: Map<string, Task[]>;
  todoListTitle: string;
  readOnly?: boolean;
  onClose: () => void;
  onUpdateTask: (taskId: string, patch: TaskPatch) => Promise<void> | void;
  onToggleComplete: (taskId: string, completed: boolean) => Promise<void> | void;
  onAddSubtask: (parentTaskId: string, title: string) => Promise<Task | null>;
  onDeleteTask: (taskId: string) => Promise<void> | void;
  onMoveSubtask?: (taskId: string, parentTaskId: string, beforeId: string | null) => Promise<void> | void;
  onMoveTaskTo?: (taskId: string, destination: MoveDestination) => Promise<void> | void;
};

const PRIORITY_LABEL: Record<Priority, string> = {
  1: 'P1 — Urgent',
  2: 'P2 — High',
  3: 'P3 — Medium',
  4: 'P4 — None'
};

const PRIORITY_SHORT: Record<Priority, string> = {
  1: 'P1',
  2: 'P2',
  3: 'P3',
  4: 'P4'
};

const formatDateChip = (value: string | null): string | null => {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  return date.toLocaleDateString(undefined, {
    weekday: 'short',
    month: 'short',
    day: 'numeric'
  });
};

const toLocalInput = (iso: string | null): string => {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  // Strip timezone for `<input type="datetime-local">`
  const offsetMs = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offsetMs).toISOString().slice(0, 16);
};

const fromLocalInput = (value: string): string | null => {
  if (!value) return null;
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return null;
  return d.toISOString();
};

export const TaskDetailModal = ({
  rootTaskId,
  tasks,
  columns,
  subtasksByParent,
  todoListTitle,
  readOnly = false,
  onClose,
  onUpdateTask,
  onToggleComplete,
  onAddSubtask,
  onDeleteTask,
  onMoveSubtask,
  onMoveTaskTo
}: TaskDetailModalProps) => {
  // Navigation stack: rootTaskId → drilled-in subtask ids
  const [navStack, setNavStack] = useState<string[]>([rootTaskId]);
  // Reset when rootTaskId changes
  useEffect(() => {
    setNavStack([rootTaskId]);
  }, [rootTaskId]);

  const currentTaskId = navStack[navStack.length - 1] ?? rootTaskId;
  const taskMap = useMemo(() => {
    const m = new Map<string, Task>();
    for (const t of tasks) m.set(t.id, t);
    return m;
  }, [tasks]);

  const currentTask = taskMap.get(currentTaskId) ?? null;

  // Breadcrumb chain (todo list → root task → ... → current)
  const ancestorChain = useMemo(() => {
    if (!currentTask) return [];
    const chain: Task[] = [currentTask];
    let cursor = currentTask;
    while (cursor.parentTaskId) {
      const parent = taskMap.get(cursor.parentTaskId);
      if (!parent) break;
      chain.unshift(parent);
      cursor = parent;
    }
    return chain;
  }, [currentTask, taskMap]);

  const directSubtasks = useMemo(
    () => (currentTask ? subtasksByParent.get(currentTask.id) ?? [] : []),
    [currentTask, subtasksByParent]
  );

  // Locally editable fields (debounced save)
  const [titleDraft, setTitleDraft] = useState(currentTask?.title ?? '');
  const [descDraft, setDescDraft] = useState(currentTask?.description ?? '');
  const [labelsInput, setLabelsInput] = useState('');
  const [reminderDraft, setReminderDraft] = useState('');
  const [locationDraft, setLocationDraft] = useState(currentTask?.location ?? '');
  const [showPriorityPicker, setShowPriorityPicker] = useState(false);

  const [subtaskDraft, setSubtaskDraft] = useState('');
  const [composingSubtask, setComposingSubtask] = useState(false);
  const subtaskComposerRef = useRef<HTMLInputElement | null>(null);

  // Comments
  const [comments, setComments] = useState<TaskComment[]>([]);
  const [commentDraft, setCommentDraft] = useState('');
  const [commentsLoaded, setCommentsLoaded] = useState(false);

  // Drag-and-drop state for subtasks
  type SubtaskDropPos = 'before' | 'after';
  type SubtaskDragOver = { targetTaskId: string; pos: SubtaskDropPos } | null;
  const [subtaskDragOver, setSubtaskDragOver] = useState<SubtaskDragOver>(null);

  // "Move to…" menu — tracks which subtask's menu is open
  const [moveMenuTaskId, setMoveMenuTaskId] = useState<string | null>(null);

  // All tasks in tree order (depth-first) for the "move under task" picker
  const orderedTasks = useMemo(() => {
    const out: { task: Task; depth: number }[] = [];
    const roots = tasks
      .filter((t) => t.parentTaskId == null)
      .sort((a, b) => a.position - b.position);
    const walk = (node: Task, depth: number) => {
      out.push({ task: node, depth });
      for (const child of subtasksByParent.get(node.id) ?? []) {
        walk(child, depth + 1);
      }
    };
    for (const root of roots) walk(root, 0);
    return out;
  }, [tasks, subtasksByParent]);

  const getDescendantIds = useCallback(
    (rootId: string): Set<string> => {
      const result = new Set<string>();
      const queue = [rootId];
      while (queue.length) {
        const id = queue.shift() as string;
        for (const child of subtasksByParent.get(id) ?? []) {
          if (!result.has(child.id)) {
            result.add(child.id);
            queue.push(child.id);
          }
        }
      }
      return result;
    },
    [subtasksByParent]
  );

  // Sync drafts when current task changes
  useEffect(() => {
    setTitleDraft(currentTask?.title ?? '');
    setDescDraft(currentTask?.description ?? '');
    setLocationDraft(currentTask?.location ?? '');
    setLabelsInput('');
    setReminderDraft('');
    setShowPriorityPicker(false);
    setComposingSubtask(false);
    setSubtaskDraft('');
    setCommentDraft('');
    setCommentsLoaded(false);
    setComments([]);
    setMoveMenuTaskId(null);
    setSubtaskDragOver(null);
  }, [currentTaskId, currentTask?.title, currentTask?.description, currentTask?.location]);

  // Lazy-load comments for the focused task
  useEffect(() => {
    if (!currentTask) return;
    let cancelled = false;
    (async () => {
      if (!window.heracles?.tasks) return;
      const items = await window.heracles.tasks.listComments({ taskId: currentTask.id });
      if (cancelled) return;
      setComments(items);
      setCommentsLoaded(true);
    })();
    return () => {
      cancelled = true;
    };
  }, [currentTaskId, currentTask?.id]);

  // Esc closes the move menu first, otherwise the modal
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      if (moveMenuTaskId) {
        setMoveMenuTaskId(null);
        return;
      }
      onClose();
    };
    window.addEventListener('keydown', handle);
    return () => window.removeEventListener('keydown', handle);
  }, [onClose, moveMenuTaskId]);

  // Close the move menu when clicking outside of it
  useEffect(() => {
    if (!moveMenuTaskId) return;
    const handle = (event: MouseEvent) => {
      const target = event.target as HTMLElement | null;
      if (target?.closest('.task-modal__move-menu') || target?.closest('.task-modal__subtask-move')) {
        return;
      }
      setMoveMenuTaskId(null);
    };
    document.addEventListener('mousedown', handle);
    return () => document.removeEventListener('mousedown', handle);
  }, [moveMenuTaskId]);

  // Focus subtask composer when started
  useEffect(() => {
    if (composingSubtask && subtaskComposerRef.current) {
      subtaskComposerRef.current.focus();
    }
  }, [composingSubtask]);

  // ── Field commits ──────────────────────────────────────────────────────
  const commitTitle = useCallback(() => {
    if (!currentTask) return;
    const next = titleDraft.trim() || 'Untitled task';
    if (next !== currentTask.title) {
      onUpdateTask(currentTask.id, { title: next });
    }
  }, [currentTask, onUpdateTask, titleDraft]);

  const commitDescription = useCallback(() => {
    if (!currentTask) return;
    if (descDraft !== currentTask.description) {
      onUpdateTask(currentTask.id, { description: descDraft });
    }
  }, [currentTask, descDraft, onUpdateTask]);

  const commitLocation = useCallback(() => {
    if (!currentTask) return;
    const next = locationDraft.trim();
    if ((next || null) !== (currentTask.location ?? null)) {
      onUpdateTask(currentTask.id, { location: next || null });
    }
  }, [currentTask, locationDraft, onUpdateTask]);

  const setDate = (iso: string | null) => {
    if (!currentTask) return;
    onUpdateTask(currentTask.id, { dueDate: iso });
  };
  const setDeadline = (iso: string | null) => {
    if (!currentTask) return;
    onUpdateTask(currentTask.id, { deadline: iso });
  };
  const setPriority = (priority: Priority) => {
    if (!currentTask) return;
    setShowPriorityPicker(false);
    onUpdateTask(currentTask.id, { priority });
  };
  const addLabel = () => {
    if (!currentTask) return;
    const next = labelsInput.trim();
    if (!next) return;
    if (currentTask.labels.includes(next)) {
      setLabelsInput('');
      return;
    }
    onUpdateTask(currentTask.id, { labels: [...currentTask.labels, next] });
    setLabelsInput('');
  };
  const removeLabel = (label: string) => {
    if (!currentTask) return;
    onUpdateTask(currentTask.id, { labels: currentTask.labels.filter((l) => l !== label) });
  };
  const addReminder = () => {
    if (!currentTask) return;
    const iso = fromLocalInput(reminderDraft);
    if (!iso) return;
    onUpdateTask(currentTask.id, { reminders: [...currentTask.reminders, iso] });
    setReminderDraft('');
  };
  const removeReminder = (iso: string) => {
    if (!currentTask) return;
    onUpdateTask(currentTask.id, { reminders: currentTask.reminders.filter((r) => r !== iso) });
  };

  const commitAddSubtask = async () => {
    if (!currentTask) return;
    const next = subtaskDraft.trim();
    if (!next) return;
    await onAddSubtask(currentTask.id, next);
    setSubtaskDraft('');
  };

  // ── Comments ───────────────────────────────────────────────────────────
  const submitComment = async () => {
    if (!currentTask || !window.heracles?.tasks) return;
    const body = commentDraft.trim();
    if (!body) return;
    const created = await window.heracles.tasks.createComment({
      taskId: currentTask.id,
      body
    });
    setComments((prev) => [...prev, created]);
    setCommentDraft('');
  };

  const handleDeleteComment = async (commentId: string) => {
    if (!window.heracles?.tasks) return;
    await window.heracles.tasks.deleteComment({ id: commentId });
    setComments((prev) => prev.filter((c) => c.id !== commentId));
  };

  // ── Drag and drop for subtasks ───────────────────────────────────────
  const handleSubtaskDragStart = useCallback(
    (subtask: Task, event: ReactDragEvent<HTMLLIElement>) => {
      if (readOnly) {
        event.preventDefault();
        return;
      }
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData(SUBTASK_DRAG_MIME, subtask.id);
      event.dataTransfer.setData('text/plain', subtask.title || 'Untitled');
    },
    [readOnly]
  );

  const computeSubtaskDropPos = (event: ReactDragEvent<HTMLLIElement>): SubtaskDropPos => {
    const rect = event.currentTarget.getBoundingClientRect();
    return event.clientY - rect.top < rect.height / 2 ? 'before' : 'after';
  };

  const handleSubtaskDragOver = useCallback(
    (targetSubtask: Task, event: ReactDragEvent<HTMLLIElement>) => {
      if (!event.dataTransfer.types.includes(SUBTASK_DRAG_MIME)) return;
      event.preventDefault();
      event.stopPropagation();
      event.dataTransfer.dropEffect = 'move';
      const pos = computeSubtaskDropPos(event);
      setSubtaskDragOver((prev) => {
        if (prev && prev.targetTaskId === targetSubtask.id && prev.pos === pos) {
          return prev;
        }
        return { targetTaskId: targetSubtask.id, pos };
      });
    },
    []
  );

  const handleSubtaskDragLeave = useCallback((event: ReactDragEvent<HTMLLIElement>) => {
    if (event.currentTarget.contains(event.relatedTarget as Node)) return;
    setSubtaskDragOver(null);
  }, []);

  const handleSubtaskDrop = useCallback(
    async (targetSubtask: Task, event: ReactDragEvent<HTMLLIElement>) => {
      if (!event.dataTransfer.types.includes(SUBTASK_DRAG_MIME)) return;
      event.preventDefault();
      event.stopPropagation();
      const sourceId = event.dataTransfer.getData(SUBTASK_DRAG_MIME);
      const drop = subtaskDragOver;
      setSubtaskDragOver(null);
      if (!sourceId || !currentTask || !onMoveSubtask) return;
      if (sourceId === targetSubtask.id) return; // dropped on self

      // Determine beforeId based on drop position
      const siblings = directSubtasks;
      let beforeId: string | null = null;
      const idx = siblings.findIndex((s) => s.id === targetSubtask.id);
      if (idx >= 0) {
        if (drop?.pos === 'before') {
          beforeId = targetSubtask.id;
        } else {
          // 'after' - insert before the next sibling
          beforeId = siblings[idx + 1]?.id ?? null;
        }
      }
      await onMoveSubtask(sourceId, currentTask.id, beforeId);
    },
    [subtaskDragOver, currentTask, directSubtasks, onMoveSubtask]
  );

  const handleSubtaskDragEnd = useCallback(() => {
    setSubtaskDragOver(null);
  }, []);

  // ── "Move to…" menu ──────────────────────────────────────────────────
  const handleMoveTo = useCallback(
    async (taskId: string, destination: MoveDestination) => {
      setMoveMenuTaskId(null);
      if (!onMoveTaskTo) return;
      await onMoveTaskTo(taskId, destination);
    },
    [onMoveTaskTo]
  );

  // ── Render ─────────────────────────────────────────────────────────────
  if (!currentTask) {
    return null;
  }

  const rootTask = ancestorChain[0];
  const rootColumn = rootTask ? columns.find((c) => c.id === rootTask.columnId) ?? null : null;

  return (
    <div
      className="task-modal__backdrop"
      role="dialog"
      aria-modal="true"
      onClick={onClose}
    >
      <div
        className="task-modal"
        onClick={(event: ReactMouseEvent<HTMLDivElement>) => event.stopPropagation()}
      >
        <header className="task-modal__header">
          <nav className="task-modal__breadcrumb" aria-label="Task path">
            <span className="task-modal__breadcrumb-item">
              <span aria-hidden>＃</span> {todoListTitle || 'Untitled list'}
            </span>
            {rootColumn && (
              <>
                <span className="task-modal__breadcrumb-sep">/</span>
                <span className="task-modal__breadcrumb-item">{rootColumn.name}</span>
              </>
            )}
            {ancestorChain.map((task, index) => (
              <span key={task.id} style={{ display: 'contents' }}>
                <span className="task-modal__breadcrumb-sep">/</span>
                {index < ancestorChain.length - 1 ? (
                  <button
                    type="button"
                    className="task-modal__breadcrumb-link"
                    onClick={() => setNavStack((stack) => stack.slice(0, index + 1))}
                  >
                    {task.title || 'Untitled task'}
                  </button>
                ) : (
                  <span className="task-modal__breadcrumb-current">
                    {task.title || 'Untitled task'}
                  </span>
                )}
              </span>
            ))}
          </nav>
          <div className="task-modal__header-actions">
            {!readOnly && (
              <button
                type="button"
                className="task-modal__icon-btn task-modal__icon-btn--danger"
                title="Delete task"
                aria-label="Delete task"
                onClick={() => {
                  if (window.confirm('Delete this task and all its subtasks?')) {
                    onDeleteTask(currentTask.id);
                  }
                }}
              >
                ⌫
              </button>
            )}
            <button
              type="button"
              className="task-modal__icon-btn"
              aria-label="Close"
              title="Close"
              onClick={onClose}
            >
              ×
            </button>
          </div>
        </header>

        <div className="task-modal__body">
          <div className="task-modal__main">
            <div className="task-modal__title-row">
              <button
                type="button"
                className={`task-modal__check task-modal__check--p${currentTask.priority}`}
                aria-label={currentTask.completed ? 'Mark as not done' : 'Mark as done'}
                aria-pressed={currentTask.completed}
                disabled={readOnly}
                onClick={() => onToggleComplete(currentTask.id, !currentTask.completed)}
              >
                {currentTask.completed ? '✓' : ''}
              </button>
              <textarea
                className={`task-modal__title-input ${
                  currentTask.completed ? 'is-complete' : ''
                }`}
                value={titleDraft}
                placeholder="Task name"
                rows={1}
                disabled={readOnly}
                onChange={(event: ChangeEvent<HTMLTextAreaElement>) =>
                  setTitleDraft(event.target.value)
                }
                onBlur={commitTitle}
                onKeyDown={(event: ReactKeyboardEvent<HTMLTextAreaElement>) => {
                  if (event.key === 'Enter' && !event.shiftKey) {
                    event.preventDefault();
                    commitTitle();
                    (event.currentTarget as HTMLTextAreaElement).blur();
                  }
                }}
              />
            </div>
            <textarea
              className="task-modal__description"
              placeholder="Description"
              rows={3}
              value={descDraft}
              disabled={readOnly}
              onChange={(event: ChangeEvent<HTMLTextAreaElement>) =>
                setDescDraft(event.target.value)
              }
              onBlur={commitDescription}
            />

            <section className="task-modal__subtasks">
              <header className="task-modal__subtasks-header">
                <span>Sub-tasks</span>
                <span className="task-modal__subtasks-count">
                  {directSubtasks.filter((t) => t.completed).length}/{directSubtasks.length}
                </span>
              </header>
              <ul className="task-modal__subtask-list">
                {directSubtasks.map((sub) => {
                  const isDropTargetBefore =
                    subtaskDragOver?.targetTaskId === sub.id && subtaskDragOver?.pos === 'before';
                  const isDropTargetAfter =
                    subtaskDragOver?.targetTaskId === sub.id && subtaskDragOver?.pos === 'after';
                  const isMoveMenuOpen = moveMenuTaskId === sub.id;
                  const subDescendants = isMoveMenuOpen ? getDescendantIds(sub.id) : null;
                  const candidateTasks = isMoveMenuOpen
                    ? orderedTasks.filter(
                        ({ task }) =>
                          task.id !== sub.id &&
                          task.id !== sub.parentTaskId &&
                          !(subDescendants?.has(task.id) ?? false)
                      )
                    : [];
                  return (
                    <li
                      key={sub.id}
                      className={`task-modal__subtask ${sub.completed ? 'is-complete' : ''} ${isDropTargetBefore ? 'is-drop-before' : ''} ${isDropTargetAfter ? 'is-drop-after' : ''}`}
                      draggable={!readOnly}
                      onDragStart={(event) => handleSubtaskDragStart(sub, event)}
                      onDragOver={(event) => handleSubtaskDragOver(sub, event)}
                      onDragLeave={handleSubtaskDragLeave}
                      onDrop={(event) => handleSubtaskDrop(sub, event)}
                      onDragEnd={handleSubtaskDragEnd}
                    >
                      {!readOnly && (
                        <span className="task-modal__subtask-drag-handle" title="Drag to reorder">
                          ⋮⋮
                        </span>
                      )}
                      <button
                        type="button"
                        className={`task-modal__check task-modal__check--small task-modal__check--p${sub.priority}`}
                        aria-label={sub.completed ? 'Mark as not done' : 'Mark as done'}
                        aria-pressed={sub.completed}
                        disabled={readOnly}
                        onClick={() => onToggleComplete(sub.id, !sub.completed)}
                      >
                        {sub.completed ? '✓' : ''}
                      </button>
                      <button
                        type="button"
                        className="task-modal__subtask-title"
                        onClick={() => setNavStack((stack) => [...stack, sub.id])}
                        title="Open subtask"
                      >
                        {sub.title || 'Untitled task'}
                        {(subtasksByParent.get(sub.id)?.length ?? 0) > 0 && (
                          <span className="task-modal__subtask-count">
                            ↳ {subtasksByParent.get(sub.id)?.length}
                          </span>
                        )}
                      </button>
                      {sub.dueDate && (
                        <span className="task-modal__subtask-date">{formatDateChip(sub.dueDate)}</span>
                      )}
                      {!readOnly && onMoveTaskTo && (
                        <div className="task-modal__subtask-move">
                          <button
                            type="button"
                            className="task-modal__subtask-move-btn"
                            aria-label="Move to…"
                            aria-haspopup="menu"
                            aria-expanded={isMoveMenuOpen}
                            title="Move to another card or column"
                            onClick={(event) => {
                              event.stopPropagation();
                              setMoveMenuTaskId((prev) => (prev === sub.id ? null : sub.id));
                            }}
                          >
                            ⤴
                          </button>
                          {isMoveMenuOpen && (
                            <div className="task-modal__move-menu" role="menu">
                              <div className="task-modal__move-group-label">
                                Make its own card in…
                              </div>
                              {columns.length === 0 && (
                                <div className="task-modal__move-empty">No columns</div>
                              )}
                              {columns.map((col) => (
                                <button
                                  key={col.id}
                                  type="button"
                                  role="menuitem"
                                  className="task-modal__move-item"
                                  onClick={() =>
                                    handleMoveTo(sub.id, { kind: 'column', columnId: col.id })
                                  }
                                >
                                  <span aria-hidden>▦</span> {col.name}
                                </button>
                              ))}
                              <div className="task-modal__move-group-label">
                                Move under another task…
                              </div>
                              {candidateTasks.length === 0 && (
                                <div className="task-modal__move-empty">No other tasks</div>
                              )}
                              {candidateTasks.map(({ task, depth }) => (
                                <button
                                  key={task.id}
                                  type="button"
                                  role="menuitem"
                                  className="task-modal__move-item"
                                  style={{ paddingLeft: 8 + depth * 14 }}
                                  onClick={() =>
                                    handleMoveTo(sub.id, {
                                      kind: 'task',
                                      parentTaskId: task.id
                                    })
                                  }
                                >
                                  <span aria-hidden>↳</span> {task.title || 'Untitled task'}
                                </button>
                              ))}
                            </div>
                          )}
                        </div>
                      )}
                    </li>
                  );
                })}
              </ul>
              {composingSubtask ? (
                <div className="task-modal__subtask-composer">
                  <input
                    ref={subtaskComposerRef}
                    type="text"
                    className="task-modal__subtask-input"
                    placeholder="Subtask name"
                    value={subtaskDraft}
                    onChange={(event: ChangeEvent<HTMLInputElement>) =>
                      setSubtaskDraft(event.target.value)
                    }
                    onKeyDown={(event: ReactKeyboardEvent<HTMLInputElement>) => {
                      if (event.key === 'Enter') {
                        event.preventDefault();
                        commitAddSubtask();
                      } else if (event.key === 'Escape') {
                        event.preventDefault();
                        setComposingSubtask(false);
                        setSubtaskDraft('');
                      }
                    }}
                  />
                  <div className="task-modal__subtask-composer-actions">
                    <button
                      type="button"
                      className="task-modal__btn task-modal__btn--ghost"
                      onClick={() => {
                        setComposingSubtask(false);
                        setSubtaskDraft('');
                      }}
                    >
                      Cancel
                    </button>
                    <button
                      type="button"
                      className="task-modal__btn task-modal__btn--primary"
                      onClick={commitAddSubtask}
                      disabled={!subtaskDraft.trim()}
                    >
                      Add
                    </button>
                  </div>
                </div>
              ) : (
                !readOnly && (
                  <button
                    type="button"
                    className="task-modal__add-subtask"
                    onClick={() => setComposingSubtask(true)}
                  >
                    <span aria-hidden>＋</span> Add sub-task
                  </button>
                )
              )}
            </section>

            <section className="task-modal__comments">
              <header className="task-modal__comments-header">Comments</header>
              {commentsLoaded && comments.length === 0 && (
                <div className="task-modal__comments-empty">No comments yet.</div>
              )}
              <ul className="task-modal__comment-list">
                {comments.map((comment) => (
                  <li key={comment.id} className="task-modal__comment">
                    <div className="task-modal__comment-meta">
                      <span className="task-modal__comment-time">
                        {new Date(comment.createdAt).toLocaleString(undefined, {
                          month: 'short',
                          day: 'numeric',
                          hour: 'numeric',
                          minute: '2-digit'
                        })}
                      </span>
                      {!readOnly && (
                        <button
                          type="button"
                          className="task-modal__comment-delete"
                          onClick={() => handleDeleteComment(comment.id)}
                          title="Delete comment"
                          aria-label="Delete comment"
                        >
                          ×
                        </button>
                      )}
                    </div>
                    <div className="task-modal__comment-body">{comment.body}</div>
                  </li>
                ))}
              </ul>
              {!readOnly && (
                <div className="task-modal__comment-composer">
                  <textarea
                    className="task-modal__comment-input"
                    placeholder="Write a comment…"
                    rows={2}
                    value={commentDraft}
                    onChange={(event: ChangeEvent<HTMLTextAreaElement>) =>
                      setCommentDraft(event.target.value)
                    }
                    onKeyDown={(event: ReactKeyboardEvent<HTMLTextAreaElement>) => {
                      if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
                        event.preventDefault();
                        submitComment();
                      }
                    }}
                  />
                  <div className="task-modal__comment-actions">
                    <span className="task-modal__hint">⌘↵ to send</span>
                    <button
                      type="button"
                      className="task-modal__btn task-modal__btn--primary"
                      onClick={submitComment}
                      disabled={!commentDraft.trim()}
                    >
                      Send
                    </button>
                  </div>
                </div>
              )}
            </section>
          </div>

          <aside className="task-modal__side">
            <div className="task-modal__side-section">
              <div className="task-modal__side-label">Project</div>
              <div className="task-modal__side-value">
                <span aria-hidden>＃</span> {todoListTitle}
              </div>
            </div>

            <div className="task-modal__side-section">
              <div className="task-modal__side-label">Date</div>
              <input
                type="date"
                className="task-modal__side-date"
                value={currentTask.dueDate ? currentTask.dueDate.slice(0, 10) : ''}
                disabled={readOnly}
                onChange={(event: ChangeEvent<HTMLInputElement>) => {
                  const value = event.target.value;
                  if (!value) {
                    setDate(null);
                    return;
                  }
                  const iso = new Date(`${value}T09:00`).toISOString();
                  setDate(iso);
                }}
              />
              {currentTask.dueDate && !readOnly && (
                <button
                  type="button"
                  className="task-modal__side-clear"
                  onClick={() => setDate(null)}
                >
                  Clear
                </button>
              )}
            </div>

            <div className="task-modal__side-section">
              <div className="task-modal__side-label">Deadline</div>
              <input
                type="date"
                className="task-modal__side-date"
                value={currentTask.deadline ? currentTask.deadline.slice(0, 10) : ''}
                disabled={readOnly}
                onChange={(event: ChangeEvent<HTMLInputElement>) => {
                  const value = event.target.value;
                  if (!value) {
                    setDeadline(null);
                    return;
                  }
                  const iso = new Date(`${value}T17:00`).toISOString();
                  setDeadline(iso);
                }}
              />
              {currentTask.deadline && !readOnly && (
                <button
                  type="button"
                  className="task-modal__side-clear"
                  onClick={() => setDeadline(null)}
                >
                  Clear
                </button>
              )}
            </div>

            <div className="task-modal__side-section">
              <div className="task-modal__side-label">Priority</div>
              <button
                type="button"
                className={`task-modal__priority task-modal__priority--p${currentTask.priority}`}
                disabled={readOnly}
                onClick={() => setShowPriorityPicker((open) => !open)}
              >
                <span className={`task-modal__priority-flag task-modal__priority-flag--p${currentTask.priority}`} aria-hidden>
                  ⚑
                </span>
                {PRIORITY_LABEL[currentTask.priority]}
              </button>
              {showPriorityPicker && (
                <div className="task-modal__priority-picker">
                  {([1, 2, 3, 4] as Priority[]).map((p) => (
                    <button
                      key={p}
                      type="button"
                      className={`task-modal__priority-option task-modal__priority-option--p${p}`}
                      onClick={() => setPriority(p)}
                    >
                      <span className={`task-modal__priority-flag task-modal__priority-flag--p${p}`} aria-hidden>
                        ⚑
                      </span>
                      {PRIORITY_SHORT[p]}
                    </button>
                  ))}
                </div>
              )}
            </div>

            <div className="task-modal__side-section">
              <div className="task-modal__side-label">Labels</div>
              <div className="task-modal__chips">
                {currentTask.labels.map((label) => (
                  <span key={label} className="task-modal__chip">
                    {label}
                    {!readOnly && (
                      <button
                        type="button"
                        className="task-modal__chip-remove"
                        onClick={() => removeLabel(label)}
                        aria-label={`Remove label ${label}`}
                      >
                        ×
                      </button>
                    )}
                  </span>
                ))}
              </div>
              {!readOnly && (
                <div className="task-modal__add-row">
                  <input
                    type="text"
                    className="task-modal__add-input"
                    placeholder="Add label"
                    value={labelsInput}
                    onChange={(event: ChangeEvent<HTMLInputElement>) =>
                      setLabelsInput(event.target.value)
                    }
                    onKeyDown={(event: ReactKeyboardEvent<HTMLInputElement>) => {
                      if (event.key === 'Enter' || event.key === ',') {
                        event.preventDefault();
                        addLabel();
                      }
                    }}
                  />
                  <button
                    type="button"
                    className="task-modal__btn task-modal__btn--ghost"
                    onClick={addLabel}
                    disabled={!labelsInput.trim()}
                  >
                    Add
                  </button>
                </div>
              )}
            </div>

            <div className="task-modal__side-section">
              <div className="task-modal__side-label">Reminders</div>
              <ul className="task-modal__reminder-list">
                {currentTask.reminders.length === 0 && (
                  <li className="task-modal__side-empty">None</li>
                )}
                {currentTask.reminders.map((iso) => (
                  <li key={iso} className="task-modal__reminder">
                    <span>
                      {new Date(iso).toLocaleString(undefined, {
                        month: 'short',
                        day: 'numeric',
                        hour: 'numeric',
                        minute: '2-digit'
                      })}
                    </span>
                    {!readOnly && (
                      <button
                        type="button"
                        className="task-modal__reminder-remove"
                        onClick={() => removeReminder(iso)}
                        aria-label="Remove reminder"
                      >
                        ×
                      </button>
                    )}
                  </li>
                ))}
              </ul>
              {!readOnly && (
                <div className="task-modal__add-row">
                  <input
                    type="datetime-local"
                    className="task-modal__add-input"
                    value={reminderDraft}
                    onChange={(event: ChangeEvent<HTMLInputElement>) =>
                      setReminderDraft(event.target.value)
                    }
                  />
                  <button
                    type="button"
                    className="task-modal__btn task-modal__btn--ghost"
                    onClick={addReminder}
                    disabled={!reminderDraft}
                  >
                    Add
                  </button>
                </div>
              )}
            </div>

            <div className="task-modal__side-section">
              <div className="task-modal__side-label">Location</div>
              <input
                type="text"
                className="task-modal__add-input"
                placeholder="Add a location"
                value={locationDraft}
                disabled={readOnly}
                onChange={(event: ChangeEvent<HTMLInputElement>) =>
                  setLocationDraft(event.target.value)
                }
                onBlur={commitLocation}
              />
            </div>
          </aside>
        </div>
      </div>
    </div>
  );
};

export default TaskDetailModal;
