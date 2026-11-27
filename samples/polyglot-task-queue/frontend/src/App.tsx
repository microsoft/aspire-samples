import { useEffect, useState, type FormEvent } from 'react';
import {
  CircleCheck,
  CircleX,
  Clock,
  FileSpreadsheet,
  FileText,
  LoaderCircle,
  Receipt,
  Send,
  Sparkles,
  Trash2,
  type LucideIcon,
} from 'lucide-react';
import { traced } from './lib/tracing';

type TaskType = 'analyze' | 'report' | 'extract';
type TaskStatus = 'queued' | 'processing' | 'completed' | 'failed';

interface Fact {
  label: string;
  value: string;
}

interface Task {
  id: string;
  type: TaskType;
  name: string;
  data: string;
  status: TaskStatus;
  worker: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  durationMs?: number;
  traceId?: string;
  result?: { headline: string; facts: Fact[]; details: Record<string, unknown> } | null;
  error?: { type: string; message: string } | null;
}

interface WorkerStats extends Record<TaskStatus, number> {
  averageDurationMs: number | null;
}

interface Snapshot {
  tasks: Task[];
  totals: Record<TaskStatus | 'all', number>;
  workers: Partial<Record<TaskType, WorkerStats>>;
}

interface Sample {
  type: TaskType;
  name: string;
  data: string;
}

interface Problem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

const TASK_TYPES: Record<TaskType, { label: string; language: string; worker: string; queue: string; does: string; input: string; Icon: LucideIcon }> = {
  analyze: {
    label: 'Dataset analysis',
    language: 'Python',
    worker: 'worker-python',
    queue: 'tasks.analyze',
    does: 'pandas totals, leaders and trends',
    input: 'CSV with a header row',
    Icon: FileSpreadsheet,
  },
  report: {
    label: 'Report generation',
    language: 'C#',
    worker: 'worker-csharp',
    queue: 'tasks.report',
    does: 'sections, highlights and action items',
    input: 'Markdown meeting notes',
    Icon: FileText,
  },
  extract: {
    label: 'Invoice extraction',
    language: 'Node.js',
    worker: 'worker-node',
    queue: 'tasks.extract',
    does: 'invoice number, PO, total and due date',
    input: 'Emailed invoice text',
    Icon: Receipt,
  },
};
const TYPE_ORDER: TaskType[] = ['analyze', 'report', 'extract'];

const STATUS: Record<TaskStatus, { label: string; Icon: LucideIcon }> = {
  queued: { label: 'Queued', Icon: Clock },
  processing: { label: 'Processing', Icon: LoaderCircle },
  completed: { label: 'Completed', Icon: CircleCheck },
  failed: { label: 'Failed', Icon: CircleX },
};
const STATUS_ORDER: TaskStatus[] = ['queued', 'processing', 'completed', 'failed'];

const EMPTY_SNAPSHOT: Snapshot = {
  tasks: [],
  totals: { queued: 0, processing: 0, completed: 0, failed: 0, all: 0 },
  workers: {},
};

const seconds = (ms: number) => `${(ms / 1000).toFixed(ms < 10_000 ? 2 : 1)} s`;
const clock = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });

/** Follows the API's task board over server-sent events. EventSource reconnects on its own. */
function useTaskStream() {
  const [snapshot, setSnapshot] = useState<Snapshot>(EMPTY_SNAPSHOT);
  const [connected, setConnected] = useState<boolean | null>(null);

  useEffect(() => {
    const source = new EventSource('/tasks/stream');
    source.onopen = () => setConnected(true);
    source.onmessage = (event) => {
      setSnapshot(JSON.parse(event.data) as Snapshot);
      setConnected(true);
    };
    source.onerror = () => setConnected(false);
    return () => source.close();
  }, []);

  return { ...snapshot, connected };
}

async function problemMessage(response: Response) {
  const problem = (await response.json().catch(() => ({}))) as Problem;
  const fieldErrors = Object.values(problem.errors ?? {}).flat();
  return fieldErrors.length ? fieldErrors.join(' ') : (problem.detail ?? `The API answered ${response.status}.`);
}

function App() {
  const { tasks, totals, workers, connected } = useTaskStream();
  const [samples, setSamples] = useState<Partial<Record<TaskType, Sample>>>({});
  const [type, setType] = useState<TaskType>('extract');
  const [name, setName] = useState('');
  const [data, setData] = useState('');
  const [busy, setBusy] = useState<'submit' | 'samples' | 'clear' | null>(null);
  const [notice, setNotice] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null);

  useEffect(() => {
    fetch('/tasks/samples')
      .then((response) => (response.ok ? (response.json() as Promise<Sample[]>) : []))
      .then((list) => setSamples(Object.fromEntries(list.map((sample) => [sample.type, sample]))))
      .catch(() => setSamples({}));
  }, []);

  const meta = TASK_TYPES[type];

  const loadSample = () => {
    const sample = samples[type];
    if (!sample) return;
    setName(sample.name);
    setData(sample.data);
  };

  const submitTask = async (event: FormEvent) => {
    event.preventDefault();
    if (busy || !data.trim()) return;
    setBusy('submit');
    setNotice(null);
    try {
      await traced('submit task', { 'taskqueue.task.type': type }, async () => {
        const response = await fetch('/tasks', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ type, data, ...(name.trim() && { name: name.trim() }) }),
        });
        if (!response.ok) throw new Error(await problemMessage(response));
        const task = (await response.json()) as Task;
        setNotice({ tone: 'ok', text: `Queued ${task.id} on ${meta.queue} for the ${meta.language} worker.` });
        setName('');
        setData('');
      });
    } catch (err) {
      setNotice({ tone: 'error', text: (err as Error).message });
    } finally {
      setBusy(null);
    }
  };

  const submitSamples = async () => {
    setBusy('samples');
    setNotice(null);
    try {
      await traced('submit sample tasks', {}, async () => {
        const response = await fetch('/tasks/samples', { method: 'POST' });
        if (!response.ok) throw new Error(await problemMessage(response));
        setNotice({ tone: 'ok', text: 'Queued one sample document for each worker: Python, C# and Node.js.' });
      });
    } catch (err) {
      setNotice({ tone: 'error', text: (err as Error).message });
    } finally {
      setBusy(null);
    }
  };

  const clearBoard = async () => {
    setBusy('clear');
    setNotice(null);
    try {
      await traced('clear board', {}, async () => {
        const response = await fetch('/tasks', { method: 'DELETE' });
        if (!response.ok) throw new Error(await problemMessage(response));
        const { cleared } = (await response.json()) as { cleared: number };
        setNotice({ tone: 'ok', text: `Cleared ${cleared} ${cleared === 1 ? 'task' : 'tasks'} from the board.` });
      });
    } catch (err) {
      setNotice({ tone: 'error', text: (err as Error).message });
    } finally {
      setBusy(null);
    }
  };

  return (
    <>
      <a className="skip-link" href="#stream">
        Skip to the task stream
      </a>

      <div className="shell">
        <header className="masthead">
          <div className="masthead-copy">
            <h1>Polyglot Task Queue</h1>
            <p className="lede">
              Back-office documents go onto RabbitMQ and the worker written in the best language for the job picks them
              up: Python analyzes datasets, C# writes reports, Node.js extracts invoices. Results stream back here.
            </p>
          </div>
          <div className="status-bar">
            <span className={`connection ${connected === false ? 'is-offline' : ''}`}>
              <span className="connection-dot" aria-hidden="true" />
              {connected === false ? 'Reconnecting to the task stream…' : 'Live task stream'}
            </span>
            <ul className="totals" aria-label="Tasks on the board by status">
              {STATUS_ORDER.map((status) => (
                <li key={status} className={`total total-${status}`}>
                  <span className="total-count">{totals[status]}</span> {STATUS[status].label.toLowerCase()}
                </li>
              ))}
            </ul>
          </div>
        </header>

        <section className="panel lanes" aria-labelledby="lanes-heading">
          <h2 id="lanes-heading" className="visually-hidden">
            Workers
          </h2>
          {TYPE_ORDER.map((taskType) => {
            const lane = TASK_TYPES[taskType];
            const stats = workers[taskType];
            const active = (stats?.queued ?? 0) + (stats?.processing ?? 0);
            return (
              <article key={taskType} className={`lane lang-${taskType}`}>
                <div className="lane-head">
                  <span className="lang-badge">{lane.language}</span>
                  <h3>{lane.label}</h3>
                </div>
                <p className="lane-route">
                  <code>{lane.queue}</code> → <code>{lane.worker}</code>
                </p>
                <p className="lane-does">{lane.does}</p>
                <dl className="lane-stats">
                  <div>
                    <dt>Done</dt>
                    <dd>{stats?.completed ?? 0}</dd>
                  </div>
                  <div>
                    <dt>Failed</dt>
                    <dd className={stats?.failed ? 'has-failures' : undefined}>{stats?.failed ?? 0}</dd>
                  </div>
                  <div>
                    <dt>In flight</dt>
                    <dd>{active}</dd>
                  </div>
                  <div>
                    <dt>Avg time</dt>
                    <dd>{stats?.averageDurationMs != null ? seconds(stats.averageDurationMs) : '–'}</dd>
                  </div>
                </dl>
              </article>
            );
          })}
        </section>

        <div className="board">
          <section className="panel dispatch" aria-labelledby="dispatch-heading">
            <h2 id="dispatch-heading">New task</h2>
            <form onSubmit={submitTask} className="dispatch-form">
              <fieldset className="type-picker">
                <legend>Task type</legend>
                {TYPE_ORDER.map((taskType) => {
                  const option = TASK_TYPES[taskType];
                  return (
                    <label key={taskType} className={`type-option lang-${taskType}`}>
                      <input
                        type="radio"
                        name="task-type"
                        value={taskType}
                        checked={type === taskType}
                        onChange={() => setType(taskType)}
                      />
                      <option.Icon size={18} aria-hidden="true" />
                      <span className="type-option-label">{option.label}</span>
                      <span className="lang-badge">{option.language}</span>
                    </label>
                  );
                })}
              </fieldset>

              <div className="field">
                <label htmlFor="task-name">Document name</label>
                <input
                  id="task-name"
                  className="input"
                  value={name}
                  maxLength={120}
                  placeholder={samples[type]?.name ?? 'document.txt'}
                  onChange={(event) => setName(event.target.value)}
                />
              </div>

              <div className="field">
                <div className="field-label-row">
                  <label htmlFor="task-data">Document</label>
                  <button type="button" className="link-button" onClick={loadSample} disabled={!samples[type]}>
                    Use sample
                  </button>
                </div>
                <textarea
                  id="task-data"
                  className="input input-code"
                  value={data}
                  rows={7}
                  maxLength={10_000}
                  placeholder={`${meta.input}…`}
                  aria-describedby="task-data-help"
                  onChange={(event) => setData(event.target.value)}
                />
                <p id="task-data-help" className="hint">
                  {meta.input}. Published to <code>{meta.queue}</code> for <code>{meta.worker}</code>.
                </p>
              </div>

              <button type="submit" className="button button-primary" disabled={busy !== null || !data.trim()}>
                <Send size={16} aria-hidden="true" />
                {busy === 'submit' ? 'Queuing…' : 'Submit task'}
              </button>
            </form>

            <div className="dispatch-extras">
              <button type="button" className="button button-secondary" onClick={submitSamples} disabled={busy !== null}>
                <Sparkles size={16} aria-hidden="true" />
                {busy === 'samples' ? 'Queuing…' : 'Submit sample tasks'}
              </button>
              <p className="hint">One sample document per worker, in a single trace across all three languages.</p>
              <button
                type="button"
                className="button button-quiet"
                onClick={clearBoard}
                disabled={busy !== null || totals.all === 0}
              >
                <Trash2 size={16} aria-hidden="true" />
                Clear board
              </button>
            </div>

            {notice && (
              <p className={`notice notice-${notice.tone}`} role={notice.tone === 'error' ? 'alert' : 'status'}>
                {notice.text}
              </p>
            )}
          </section>

          <main id="stream" className="panel stream" aria-labelledby="stream-heading">
            <div className="stream-head">
              <h2 id="stream-heading">Task stream</h2>
              <span className="stream-count">
                {totals.all > tasks.length ? `Latest ${tasks.length} of ${totals.all}` : `${totals.all} ${totals.all === 1 ? 'task' : 'tasks'}`}
              </span>
            </div>

            {tasks.length === 0 ? (
              <div className="empty">
                <p className="empty-title">No tasks on the board yet</p>
                <p>Submit a document, or queue the samples to watch all three workers pick up a task.</p>
              </div>
            ) : (
              <ol className="task-list">
                {tasks.map((task) => (
                  <TaskCard key={task.id} task={task} />
                ))}
              </ol>
            )}
          </main>
        </div>
      </div>
    </>
  );
}

function TaskCard({ task }: { task: Task }) {
  const meta = TASK_TYPES[task.type];
  const status = STATUS[task.status] ?? STATUS.queued;
  const pending = task.status === 'queued' || task.status === 'processing';

  return (
    <li className={`task task-${task.status}`}>
      <div className="task-head">
        <span className={`chip chip-${task.status}`}>
          <status.Icon size={13} aria-hidden="true" className={task.status === 'processing' ? 'spin' : undefined} />
          {status.label}
        </span>
        <span className="task-name">{task.name}</span>
        <span className={`lang-badge lang-${task.type}`}>{meta?.language ?? task.worker}</span>
      </div>

      <p className={`task-headline ${task.error ? 'is-error' : ''}`}>
        {task.result?.headline ??
          (task.error
            ? `${task.error.type}: ${task.error.message}`
            : task.status === 'processing'
              ? `${task.worker} is working on it…`
              : `Waiting in ${meta?.queue ?? 'the queue'}…`)}
      </p>

      {task.result?.facts?.length ? (
        <dl className="facts">
          {task.result.facts.map((fact) => (
            <div key={fact.label} className="fact">
              <dt>{fact.label}</dt>
              <dd>{fact.value}</dd>
            </div>
          ))}
        </dl>
      ) : null}

      <div className="task-meta">
        <span className="mono">{task.id}</span>
        {task.traceId && (
          <span className="mono" title={`Trace ${task.traceId}: search for it in the Aspire dashboard`}>
            trace {task.traceId.slice(0, 8)}
          </span>
        )}
        <span>{meta?.label ?? task.type}</span>
        <span>queued {clock(task.createdAt)}</span>
        {!pending && task.durationMs != null && (
          <span>
            {seconds(task.durationMs)} in {task.worker}
          </span>
        )}
      </div>

      <details className="task-details">
        <summary>Document{task.result ? ' and result' : ''}</summary>
        <pre className="code-block">{task.data}</pre>
        {task.result && <pre className="code-block">{JSON.stringify(task.result.details, null, 2)}</pre>}
      </details>
    </li>
  );
}

export default App;
