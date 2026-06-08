import { useState, useEffect, useRef } from 'react';
import type { ComponentType } from 'react';
import {
  Send,
  Trash2,
  Cpu,
  BarChart3,
  FileText,
  Activity,
  Clock,
  CheckCircle2,
  SkipForward,
  AlertTriangle,
  Terminal,
} from 'lucide-react';
import './style.css';

interface Task {
  id: string;
  type: string;
  data: string;
  status: string;
  createdAt: string;
  completedAt?: string;
  result?: any;
  worker?: string;
  error?: string;
}

type IconType = ComponentType<{ size?: number; strokeWidth?: number; 'aria-hidden'?: boolean | 'true' | 'false' }>;

const statusDisplayOrder = ['queued', 'processing', 'completed', 'skipped', 'error'] as const;

const statusMeta: Record<string, { label: string; chip: string; Icon: IconType }> = {
  queued: { label: 'Queued', chip: 'chip chip-queued', Icon: Clock },
  processing: { label: 'Processing', chip: 'chip chip-processing', Icon: Activity },
  completed: { label: 'Completed', chip: 'chip chip-completed', Icon: CheckCircle2 },
  skipped: { label: 'Skipped', chip: 'chip chip-skipped', Icon: SkipForward },
  error: { label: 'Error', chip: 'chip chip-error', Icon: AlertTriangle },
  failed: { label: 'Failed', chip: 'chip chip-error', Icon: AlertTriangle },
};

const typeMeta: Record<string, { label: string; worker: string; Icon: IconType }> = {
  analyze: { label: 'Data analysis', worker: 'Python', Icon: BarChart3 },
  report: { label: 'Report generation', worker: 'C#', Icon: FileText },
};

// Keep task cards compact: show the first few lines of the submitted payload
// and summarise the rest rather than printing every row.
function truncateData(data: string, maxLines = 6): string {
  const lines = data.split(/\r?\n/);
  if (lines.length <= maxLines) return data;
  const remaining = lines.length - maxLines;
  return `${lines.slice(0, maxLines).join('\n')}\n… ${remaining} more line${remaining === 1 ? '' : 's'}`;
}

function App() {
  const [tasks, setTasks] = useState<Task[]>([]);
  const [taskType, setTaskType] = useState<string>('analyze');
  const [taskData, setTaskData] = useState<string>('');
  const [loading, setLoading] = useState(false);
  const [online, setOnline] = useState<boolean | null>(null);
  const [message, setMessage] = useState('');
  const submittingRef = useRef(false);

  const fetchTasks = async () => {
    try {
      const response = await fetch('/tasks');
      const data = await response.json();
      setTasks(data);
    } catch (error) {
      console.error('Failed to fetch tasks:', error);
    }
  };

  const fetchHealth = async () => {
    try {
      const response = await fetch('/health');
      setOnline(response.ok);
    } catch {
      setOnline(false);
    }
  };

  useEffect(() => {
    fetchTasks();
    fetchHealth();
    const interval = setInterval(() => {
      fetchTasks();
      fetchHealth();
    }, 2000);
    return () => clearInterval(interval);
  }, []);

  const submitTask = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submittingRef.current || !taskData.trim()) return;

    submittingRef.current = true;
    setLoading(true);
    const typeLabel = typeMeta[taskType]?.label ?? taskType;
    try {
      const response = await fetch('/tasks', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ type: taskType, data: taskData }),
      });
      if (!response.ok) throw new Error(`Request failed: ${response.status}`);
      setTaskData('');
      await fetchTasks();
      setMessage(`Task submitted: ${typeLabel}. Queued for processing.`);
    } catch (error) {
      console.error('Failed to submit task:', error);
      setMessage('Could not submit the task. Please try again.');
    } finally {
      submittingRef.current = false;
      setLoading(false);
    }
  };

  const clearTasks = async () => {
    if (loading) return;
    if (!confirm('Clear all tasks?')) return;

    setLoading(true);
    try {
      await fetch('/tasks', { method: 'DELETE' });
      await fetchTasks();
      setMessage('Cleared all tasks from the board.');
    } catch (error) {
      console.error('Failed to clear tasks:', error);
      setMessage('Could not clear the tasks. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const statusCounts = statusDisplayOrder.map((status) => ({
    status,
    label: statusMeta[status].label,
    Icon: statusMeta[status].Icon,
    count: tasks.filter((task) =>
      status === 'error' ? task.status === 'error' || task.status === 'failed' : task.status === status
    ).length,
  }));

  return (
    <>
      <a className="skip-link" href="#main">
        Skip to task board
      </a>

      <div className="shell">
        <header className="flex flex-col gap-3">
          <p className="text-accent inline-flex items-center gap-2 font-mono text-xs font-bold uppercase tracking-[0.22em]">
            <Terminal size={15} aria-hidden="true" />
            Aspire · Polyglot task queue
          </p>
          <h1 className="font-mono text-4xl font-extrabold tracking-tight sm:text-5xl">Task Queue</h1>
          <p className="text-muted max-w-2xl text-base">
            Submit a job and an Aspire-orchestrated worker picks it up from a RabbitMQ queue, runs it
            in the background, and streams the result back here — data analysis in Python, report
            generation in C#.
          </p>

          <div className="hud-panel mt-1 flex flex-wrap items-center gap-x-5 gap-y-3 px-4 py-3">
            <span className="inline-flex items-center gap-2 font-mono text-sm font-semibold">
              <span
                className="live-dot"
                style={online === false ? { background: '#fb7185', boxShadow: '0 0 10px 1px rgba(251,113,133,0.8)' } : undefined}
                aria-hidden="true"
              />
              {online === false ? 'System offline' : 'System online'}
            </span>
            <span className="hud-divider hidden h-5 border-l sm:block" aria-hidden="true" />
            <ul className="flex flex-wrap items-center gap-x-5 gap-y-1" aria-label="Task status totals">
              {statusCounts.map(({ status, label, Icon, count }) => (
                <li key={status} className="inline-flex items-center gap-1.5 font-mono text-sm">
                  <Icon size={15} aria-hidden="true" />
                  <span className="font-bold tabular-nums">{count}</span>
                  <span className="text-muted">{label.toLowerCase()}</span>
                </li>
              ))}
            </ul>
          </div>
        </header>

        <div className="board">
        <section className="dispatch hud-panel flex flex-col gap-4 p-5 sm:p-6" aria-labelledby="submit-heading">
          <h2 id="submit-heading" className="font-mono text-lg font-bold">
            Dispatch task
          </h2>
          <form onSubmit={submitTask} className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="task-type" className="font-mono text-sm font-semibold">
                Task type
              </label>
              <select
                id="task-type"
                className="hud-field"
                value={taskType}
                onChange={(e) => setTaskType(e.target.value)}
                disabled={loading}
              >
                <option value="analyze">Data analysis · Python</option>
                <option value="report">Report generation · C#</option>
              </select>
            </div>

            <div className="flex flex-col gap-1.5">
              <label htmlFor="task-data" className="font-mono text-sm font-semibold">
                Task data <span className="text-muted font-normal">(CSV or JSON)</span>
              </label>
              <textarea
                id="task-data"
                className="hud-field resize-y"
                value={taskData}
                onChange={(e) => setTaskData(e.target.value)}
                placeholder="Enter data to process…"
                rows={4}
                aria-describedby="task-data-help"
                disabled={loading}
              />
              <p id="task-data-help" className="text-muted text-xs">
                The payload is sent to a worker over RabbitMQ and processed off the request thread.
              </p>
            </div>

            <div className="flex flex-wrap gap-3">
              <button type="submit" className="btn btn-primary" disabled={loading || !taskData.trim()}>
                <Send size={17} aria-hidden="true" />
                Submit task
              </button>
              <button
                type="button"
                className="btn btn-ghost"
                onClick={clearTasks}
                disabled={loading || tasks.length === 0}
              >
                <Trash2 size={17} aria-hidden="true" />
                Clear all
              </button>
            </div>
          </form>
        </section>

        <main id="main" className="hud-panel flex flex-col gap-4 p-5 sm:p-6">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="font-mono text-lg font-bold">Task stream</h2>
            <span className="text-muted font-mono text-sm tabular-nums">
              {tasks.length} {tasks.length === 1 ? 'task' : 'tasks'} on the board
            </span>
          </div>

          {tasks.length === 0 ? (
            <p className="text-muted border-border rounded-xl border border-dashed px-4 py-12 text-center font-mono">
              No tasks yet. Submit a job above to see it move through the queue.
            </p>
          ) : (
            <ul className="flex flex-col gap-4">
              {tasks.map((task) => {
                const meta = statusMeta[task.status] ?? {
                  label: task.status,
                  chip: 'chip chip-skipped',
                  Icon: Clock,
                };
                const tmeta = typeMeta[task.type];
                const TypeIcon = tmeta?.Icon ?? FileText;
                const StatusIcon = meta.Icon;
                return (
                  <li key={task.id} className="border-border rounded-xl border bg-[var(--panel-2)] p-4">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-id font-mono text-sm font-semibold break-all">{task.id}</span>
                      <span className={meta.chip}>
                        <StatusIcon size={13} aria-hidden="true" />
                        {meta.label}
                      </span>
                      {task.worker && (
                        <span className="worker-chip">
                          <Cpu size={13} aria-hidden="true" />
                          {task.worker}
                        </span>
                      )}
                      <span className="text-accent ml-auto inline-flex items-center gap-1.5 font-mono text-sm font-semibold">
                        <TypeIcon size={15} aria-hidden="true" />
                        {tmeta?.label ?? task.type}
                      </span>
                    </div>

                    <div className="mt-3 flex flex-col gap-3">
                      <div>
                        <p className="text-muted mb-1 font-mono text-xs font-bold uppercase tracking-wider">Data</p>
                        <pre className="code-block">{truncateData(task.data)}</pre>
                      </div>

                      {task.result && (
                        <div>
                          <p className="text-muted mb-1 font-mono text-xs font-bold uppercase tracking-wider">Result</p>
                          <pre className="code-block code-scroll">{JSON.stringify(task.result, null, 2)}</pre>
                        </div>
                      )}

                      {task.error && (
                        <div>
                          <p className="text-muted mb-1 font-mono text-xs font-bold uppercase tracking-wider">Error</p>
                          <pre className="code-block">{task.error}</pre>
                        </div>
                      )}
                    </div>

                    <div className="text-muted hud-divider mt-3 flex flex-wrap justify-between gap-2 border-t pt-3 font-mono text-xs">
                      <span>Created {new Date(task.createdAt).toLocaleString()}</span>
                      {task.completedAt && <span>Completed {new Date(task.completedAt).toLocaleString()}</span>}
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </main>
        </div>
      </div>

      <p role="status" aria-live="polite" className="visually-hidden">
        {message}
      </p>
    </>
  );
}

export default App;
