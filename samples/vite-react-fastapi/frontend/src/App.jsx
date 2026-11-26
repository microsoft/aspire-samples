import { useCallback, useEffect, useMemo, useState } from 'react';
import { SpanStatusCode, trace } from '@opentelemetry/api';
import {
  ArrowPathIcon,
  ArrowRightIcon,
  ArrowUturnLeftIcon,
  CalendarDaysIcon,
  CheckIcon,
  ExclamationTriangleIcon,
  HandRaisedIcon,
  PlayIcon,
  PlusIcon,
  Squares2X2Icon,
  TrashIcon,
  XMarkIcon,
} from '@heroicons/react/20/solid';

const TEAM = ['Priya Patel', 'Marcus Chen', 'Sofia Alvarez', 'Jamal Wright', 'Elena Novak'];
const PRIORITIES = ['urgent', 'high', 'medium', 'low'];
const COLUMNS = [
  { status: 'todo', label: 'To do' },
  { status: 'in_progress', label: 'In progress' },
  { status: 'blocked', label: 'Blocked' },
  { status: 'done', label: 'Done' },
];
// Next moves offered on each card, per column.
const MOVES = {
  todo: [{ to: 'in_progress', label: 'Start', icon: PlayIcon }],
  in_progress: [
    { to: 'blocked', label: 'Block', icon: HandRaisedIcon },
    { to: 'done', label: 'Done', icon: CheckIcon },
  ],
  blocked: [{ to: 'in_progress', label: 'Unblock', icon: ArrowRightIcon }],
  done: [{ to: 'in_progress', label: 'Reopen', icon: ArrowUturnLeftIcon }],
};
const PRIORITY_RANK = { urgent: 0, high: 1, medium: 2, low: 3 };

// No-op until telemetry.js registers a provider (run mode only).
const tracer = trace.getTracer('taskboard-frontend');

/**
 * Runs a UI action inside its own span. `action` must start its fetch synchronously so the
 * fetch span (and the `traceparent` sent to the API) is parented to this span.
 */
function traced(name, attributes, action) {
  return tracer.startActiveSpan(name, { attributes }, async (span) => {
    try {
      return await action();
    } catch (err) {
      span.setStatus({ code: SpanStatusCode.ERROR, message: err.message });
      throw err;
    } finally {
      span.end();
    }
  });
}

async function api(method, path, body) {
  const response = await fetch(`/api${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (response.status === 204) return null;
  const data = await response.json().catch(() => null);
  if (!response.ok) {
    // FastAPI returns RFC 9457 problem details.
    throw new Error(data?.detail ?? `${method} ${path} failed (${response.status})`);
  }
  return data;
}

function parseDate(value) {
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y, m - 1, d);
}

function dueLabel(task) {
  if (!task.due_date) return null;
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const due = parseDate(task.due_date);
  const days = Math.round((due - today) / 86_400_000);
  const pretty = due.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  if (task.status === 'done') return { text: pretty, tone: 'muted' };
  if (days < 0) return { text: `Overdue · ${pretty}`, tone: 'danger' };
  if (days === 0) return { text: 'Due today', tone: 'warn' };
  if (days === 1) return { text: 'Due tomorrow', tone: 'warn' };
  return { text: `Due ${pretty}`, tone: 'muted' };
}

const initials = (name) =>
  name
    .split(/\s+/)
    .map((part) => part[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();

const label = (value) => value.replace('_', ' ');

function App() {
  const [tasks, setTasks] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');
  const [busy, setBusy] = useState(new Set());

  const load = useCallback(async () => {
    try {
      setTasks(await api('GET', '/tasks'));
      setError('');
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const withBusy = async (id, work) => {
    if (busy.has(id)) return;
    setBusy((current) => new Set(current).add(id));
    try {
      await work();
      setError('');
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy((current) => {
        const next = new Set(current);
        next.delete(id);
        return next;
      });
    }
  };

  const createTask = (draft) =>
    traced('create task', { 'taskboard.task.priority': draft.priority }, () => api('POST', '/tasks', draft)).then(
      (task) => {
        setTasks((current) => [...current, task]);
        setStatus(`Added "${task.title}".`);
        setError('');
      },
      (err) => {
        setError(err.message);
        throw err;
      }
    );

  const moveTask = (task, to) =>
    withBusy(task.id, async () => {
      const updated = await traced(
        'move task',
        { 'taskboard.task.id': task.id, 'taskboard.transition.from': task.status, 'taskboard.transition.to': to },
        () => api('PATCH', `/tasks/${task.id}`, { status: to })
      );
      setTasks((current) => current.map((t) => (t.id === task.id ? updated : t)));
      setStatus(`Moved "${updated.title}" to ${label(to)}.`);
    });

  const deleteTask = (task) =>
    withBusy(task.id, async () => {
      await traced('delete task', { 'taskboard.task.id': task.id }, () => api('DELETE', `/tasks/${task.id}`));
      setTasks((current) => current.filter((t) => t.id !== task.id));
      setStatus(`Deleted "${task.title}".`);
    });

  const columns = useMemo(
    () =>
      COLUMNS.map((column) => ({
        ...column,
        tasks: tasks
          .filter((t) => t.status === column.status)
          .sort(
            (a, b) =>
              PRIORITY_RANK[a.priority] - PRIORITY_RANK[b.priority] ||
              (a.due_date ?? '9999').localeCompare(b.due_date ?? '9999')
          ),
      })),
    [tasks]
  );

  const open = tasks.filter((t) => t.status !== 'done');
  const stats = [
    { label: 'Open', value: open.length },
    { label: 'Overdue', value: open.filter((t) => t.overdue).length, tone: 'danger' },
    { label: 'Urgent', value: open.filter((t) => t.priority === 'urgent').length, tone: 'warn' },
    { label: 'Done', value: tasks.length - open.length, tone: 'ok' },
  ];

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            <Squares2X2Icon />
          </span>
          <div>
            <h1>Contoso Task Board</h1>
            <p className="subtitle">Platform team · Vite + React · FastAPI on Aspire</p>
          </div>
        </div>
        <dl className="stats">
          {stats.map((stat) => (
            <div key={stat.label} className={`stat ${stat.tone ?? ''}`}>
              <dt>{stat.label}</dt>
              <dd>{stat.value}</dd>
            </div>
          ))}
        </dl>
      </header>

      <Composer onCreate={createTask} onRefresh={load} />

      {error && (
        <div className="alert" role="alert">
          <ExclamationTriangleIcon aria-hidden="true" />
          <span>{error}</span>
          <button type="button" className="icon-btn" onClick={() => setError('')} aria-label="Dismiss">
            <XMarkIcon aria-hidden="true" />
          </button>
        </div>
      )}

      <main className="board" aria-busy={loading}>
        {columns.map((column) => (
          <section key={column.status} className={`column col-${column.status}`} aria-labelledby={`h-${column.status}`}>
            <h2 id={`h-${column.status}`} className="column-head">
              <span className="dot" aria-hidden="true" />
              {column.label}
              <span className="count">{column.tasks.length}</span>
            </h2>
            {column.tasks.length === 0 ? (
              <p className="empty">{loading ? 'Loading…' : 'Nothing here.'}</p>
            ) : (
              <ul className="cards">
                {column.tasks.map((task) => (
                  <TaskCard
                    key={task.id}
                    task={task}
                    busy={busy.has(task.id)}
                    onMove={(to) => moveTask(task, to)}
                    onDelete={() => deleteTask(task)}
                  />
                ))}
              </ul>
            )}
          </section>
        ))}
      </main>

      <p role="status" aria-live="polite" className="visually-hidden">
        {status}
      </p>
    </div>
  );
}

function Composer({ onCreate, onRefresh }) {
  const [draft, setDraft] = useState({ title: '', assignee: '', priority: 'medium', due_date: '' });
  const [saving, setSaving] = useState(false);
  const set = (key) => (e) => setDraft((d) => ({ ...d, [key]: e.target.value }));

  const submit = async (e) => {
    e.preventDefault();
    const title = draft.title.trim();
    if (!title || saving) return;
    setSaving(true);
    try {
      await onCreate({
        title,
        priority: draft.priority,
        assignee: draft.assignee || null,
        due_date: draft.due_date || null,
      });
      setDraft((d) => ({ ...d, title: '' }));
    } catch {
      // Error already surfaced by the parent.
    } finally {
      setSaving(false);
    }
  };

  return (
    <form className="composer" onSubmit={submit}>
      <label className="field grow">
        <span>New task</span>
        <input
          value={draft.title}
          onChange={set('title')}
          placeholder="e.g. Rotate the staging database password"
          maxLength={200}
          autoComplete="off"
          required
        />
      </label>
      <label className="field">
        <span>Assignee</span>
        <select value={draft.assignee} onChange={set('assignee')}>
          <option value="">Unassigned</option>
          {TEAM.map((name) => (
            <option key={name}>{name}</option>
          ))}
        </select>
      </label>
      <label className="field">
        <span>Priority</span>
        <select value={draft.priority} onChange={set('priority')}>
          {PRIORITIES.map((p) => (
            <option key={p} value={p}>
              {p[0].toUpperCase() + p.slice(1)}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        <span>Due</span>
        <input type="date" value={draft.due_date} onChange={set('due_date')} />
      </label>
      <div className="composer-actions">
        <button type="submit" className="btn primary" disabled={saving}>
          <PlusIcon aria-hidden="true" />
          Add task
        </button>
        <button type="button" className="btn ghost" onClick={onRefresh} aria-label="Refresh board" title="Refresh board">
          <ArrowPathIcon aria-hidden="true" />
        </button>
      </div>
    </form>
  );
}

function TaskCard({ task, busy, onMove, onDelete }) {
  const due = dueLabel(task);
  return (
    <li className={`card prio-${task.priority} ${task.status === 'done' ? 'is-done' : ''}`} aria-busy={busy}>
      <div className="card-top">
        <span className={`badge prio-${task.priority}`}>{task.priority}</span>
        <span className="task-id">#{task.id}</span>
      </div>
      <p className="card-title">{task.title}</p>
      <div className="card-meta">
        {task.assignee ? (
          <span className="assignee">
            <span className="avatar" aria-hidden="true">
              {initials(task.assignee)}
            </span>
            {task.assignee}
          </span>
        ) : (
          <span className="assignee unassigned">Unassigned</span>
        )}
        {due && (
          <span className={`due ${due.tone}`}>
            <CalendarDaysIcon aria-hidden="true" />
            {due.text}
          </span>
        )}
      </div>
      <div className="card-actions">
        {MOVES[task.status].map(({ to, label: text, icon: Icon }) => (
          <button key={to} type="button" className="btn small" disabled={busy} onClick={() => onMove(to)}>
            <Icon aria-hidden="true" />
            {text}
          </button>
        ))}
        <button
          type="button"
          className="icon-btn danger"
          disabled={busy}
          onClick={onDelete}
          aria-label={`Delete "${task.title}"`}
          title="Delete"
        >
          <TrashIcon aria-hidden="true" />
        </button>
      </div>
    </li>
  );
}

export default App;
