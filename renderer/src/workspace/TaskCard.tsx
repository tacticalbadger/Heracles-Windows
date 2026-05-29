import type { MouseEvent as ReactMouseEvent } from 'react';
import type { Priority, Task } from '../types/tasks';

const PRIORITY_CLASS: Record<Priority, string> = {
  1: 'task-card__flag task-card__flag--p1',
  2: 'task-card__flag task-card__flag--p2',
  3: 'task-card__flag task-card__flag--p3',
  4: 'task-card__flag task-card__flag--p4'
};

const formatDueDate = (value: string | null): string | null => {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  const today = new Date();
  const tomorrow = new Date();
  tomorrow.setDate(today.getDate() + 1);
  const same = (a: Date, b: Date) =>
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate();
  if (same(date, today)) return 'Today';
  if (same(date, tomorrow)) return 'Tomorrow';
  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
};

const isOverdue = (dueDate: string | null, completed: boolean): boolean => {
  if (!dueDate || completed) return false;
  const date = new Date(dueDate);
  if (Number.isNaN(date.getTime())) return false;
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const compare = new Date(date);
  compare.setHours(0, 0, 0, 0);
  return compare.getTime() < today.getTime();
};

type TaskCardProps = {
  task: Task;
  subtaskCount: number;
  completedSubtaskCount: number;
  readOnly?: boolean;
  onOpen: () => void;
  onToggleComplete: (completed: boolean) => void;
  onDelete: () => void;
};

export const TaskCard = ({
  task,
  subtaskCount,
  completedSubtaskCount,
  readOnly = false,
  onOpen,
  onToggleComplete,
  onDelete
}: TaskCardProps) => {
  const due = formatDueDate(task.dueDate);
  const overdue = isOverdue(task.dueDate, task.completed);

  const handleCheckboxClick = (event: ReactMouseEvent<HTMLButtonElement>) => {
    event.stopPropagation();
    if (readOnly) return;
    onToggleComplete(!task.completed);
  };

  const handleDeleteClick = (event: ReactMouseEvent<HTMLButtonElement>) => {
    event.stopPropagation();
    if (readOnly) return;
    if (window.confirm('Delete this task and all its subtasks?')) {
      onDelete();
    }
  };

  return (
    <article
      className={`task-card ${task.completed ? 'is-complete' : ''}`}
      onClick={onOpen}
      role="button"
      tabIndex={0}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          onOpen();
        }
      }}
    >
      <button
        type="button"
        className={PRIORITY_CLASS[task.priority]}
        onClick={handleCheckboxClick}
        aria-label={task.completed ? 'Mark as not done' : 'Mark as done'}
        aria-pressed={task.completed}
        disabled={readOnly}
      >
        <span className="task-card__check" aria-hidden>
          {task.completed ? '✓' : ''}
        </span>
      </button>
      {!readOnly && (
        <button
          type="button"
          className="task-card__delete"
          aria-label="Delete task"
          title="Delete task"
          onClick={handleDeleteClick}
        >
          <span aria-hidden>×</span>
        </button>
      )}
      <div className="task-card__body">
        <div className="task-card__title">{task.title || 'Untitled task'}</div>
        {task.description && (
          <div className="task-card__description">{task.description}</div>
        )}
        <div className="task-card__meta">
          {due && (
            <span
              className={`task-card__chip task-card__chip--date ${
                overdue ? 'task-card__chip--overdue' : ''
              }`}
            >
              <span aria-hidden>📅</span> {due}
            </span>
          )}
          {subtaskCount > 0 && (
            <span className="task-card__chip task-card__chip--subtasks">
              <span aria-hidden>↳</span>
              {completedSubtaskCount}/{subtaskCount}
            </span>
          )}
          {task.labels.length > 0 &&
            task.labels.slice(0, 3).map((label) => (
              <span key={label} className="task-card__chip task-card__chip--label">
                {label}
              </span>
            ))}
          {task.labels.length > 3 && (
            <span className="task-card__chip task-card__chip--more">
              +{task.labels.length - 3}
            </span>
          )}
          {task.location && (
            <span className="task-card__chip task-card__chip--location" title={task.location}>
              <span aria-hidden>📍</span>
            </span>
          )}
        </div>
      </div>
    </article>
  );
};

export default TaskCard;
