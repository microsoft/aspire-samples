import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { api, traced, type Category, type Priority, type QueueSummary, type ServiceRequest, type Status } from './api';
import { Icon, type IconName } from './Icon';

/** The signed-in service desk agent: "Pick up" assigns requests to them. */
const AGENT = 'Ava Thompson';

const CATEGORIES: { id: Category; icon: IconName }[] = [
  { id: 'Hardware', icon: 'laptop' },
  { id: 'Software', icon: 'app' },
  { id: 'Access', icon: 'key' },
  { id: 'Network', icon: 'wifi' },
];
const CATEGORY_ICON = Object.fromEntries(CATEGORIES.map((c) => [c.id, c.icon])) as Record<Category, IconName>;

const PRIORITIES: { id: Priority; sla: string; short: string }[] = [
  { id: 'Critical', sla: '4 hours', short: '4 h' },
  { id: 'High', sla: '8 hours', short: '8 h' },
  { id: 'Medium', sla: '24 hours', short: '24 h' },
  { id: 'Low', sla: '3 days', short: '3 d' },
];
const PRIORITY_RANK: Record<Priority, number> = { Low: 0, Medium: 1, High: 2, Critical: 3 };

type Tab = 'open' | 'unassigned' | 'inProgress' | 'resolved';
const TABS: { id: Tab; label: string; empty: string }[] = [
  { id: 'open', label: 'Open', empty: 'No open requests. The queue is clear.' },
  { id: 'unassigned', label: 'Unassigned', empty: 'Every open request has an agent.' },
  { id: 'inProgress', label: 'In progress', empty: 'Nobody is working on a request right now.' },
  { id: 'resolved', label: 'Resolved', empty: 'No resolved requests yet.' },
];

const HOUR = 3_600_000;

function inTab(request: ServiceRequest, tab: Tab) {
  switch (tab) {
    case 'open':
      return request.status !== 'Resolved';
    case 'unassigned':
      return request.status === 'New';
    case 'inProgress':
      return request.status === 'InProgress';
    case 'resolved':
      return request.status === 'Resolved';
  }
}

/** Same order as the API: open work by priority then SLA due time, then the most recently resolved. */
function sortQueue(list: ServiceRequest[]) {
  return [...list].sort((a, b) => {
    const aDone = a.status === 'Resolved';
    const bDone = b.status === 'Resolved';
    if (aDone !== bDone) return aDone ? 1 : -1;
    if (aDone) return Date.parse(b.resolvedAt ?? '') - Date.parse(a.resolvedAt ?? '');
    return PRIORITY_RANK[b.priority] - PRIORITY_RANK[a.priority] || Date.parse(a.dueAt) - Date.parse(b.dueAt);
  });
}

function duration(ms: number) {
  const minutes = Math.round(ms / 60_000);
  if (minutes < 60) return `${Math.max(1, minutes)} min`;
  const hours = ms / HOUR;
  if (hours < 10) return `${hours.toFixed(1).replace(/\.0$/, '')} h`;
  if (hours < 48) return `${Math.round(hours)} h`;
  return `${Math.round(hours / 24)} d`;
}

function slaStatus(request: ServiceRequest, now: number): { text: string; tone: 'danger' | 'warn' | 'muted' | 'ok' } {
  const due = Date.parse(request.dueAt);
  if (request.status === 'Resolved') {
    return Date.parse(request.resolvedAt ?? '') <= due
      ? { text: 'SLA met', tone: 'ok' }
      : { text: 'SLA missed', tone: 'danger' };
  }
  const left = due - now;
  if (left < 0) return { text: `Overdue ${duration(-left)}`, tone: 'danger' };
  return { text: `Due in ${duration(left)}`, tone: left < 2 * HOUR ? 'warn' : 'muted' };
}

const initials = (name: string) =>
  name
    .split(/\s+/)
    .map((part) => part[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();

const ticket = (id: number) => `SR-${id}`;
const errorMessage = (err: unknown) => (err instanceof Error ? err.message : String(err));

function useNow(intervalMs = 30_000) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(timer);
  }, [intervalMs]);
  return now;
}

export default function App() {
  const [requests, setRequests] = useState<ServiceRequest[]>([]);
  const [summary, setSummary] = useState<QueueSummary | null>(null);
  const [tab, setTab] = useState<Tab>('open');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<ReadonlySet<number>>(new Set());
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [fresh, setFresh] = useState<number | null>(null);
  const now = useNow();

  const load = useCallback(async () => {
    try {
      const [list, counts] = await traced('load queue', {}, (inSpan) =>
        Promise.all([inSpan(() => api.list()), inSpan(() => api.summary())])
      );
      setRequests(sortQueue(list));
      setSummary(counts);
      setError('');
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
    // Pick up changes made by the dashboard's Seed sample data / Simulate traffic commands.
    window.addEventListener('focus', load);
    return () => window.removeEventListener('focus', load);
  }, [load]);

  const withBusy = async (id: number, work: () => Promise<string>) => {
    if (busy.has(id)) return;
    setBusy((current) => new Set(current).add(id));
    try {
      setNotice(await work());
      setError('');
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy((current) => {
        const next = new Set(current);
        next.delete(id);
        return next;
      });
    }
  };

  const move = (request: ServiceRequest, status: Status, action: string) =>
    withBusy(request.id, () =>
      traced(
        `${action} request`,
        {
          'servicedesk.request.id': request.id,
          'servicedesk.request.priority': request.priority,
          'servicedesk.transition.from': request.status,
          'servicedesk.transition.to': status,
        },
        async (inSpan) => {
          const changes = action === 'pick up' ? { status, assignedTo: AGENT } : { status };
          const updated = await inSpan(() => api.update(request.id, changes));
          setRequests((current) => sortQueue(current.map((r) => (r.id === request.id ? updated : r))));
          setSummary(await inSpan(() => api.summary()));
          if (status === 'Resolved') {
            const hours = duration(Date.parse(updated.resolvedAt ?? '') - Date.parse(updated.createdAt));
            return `Resolved ${ticket(request.id)} in ${hours}.`;
          }
          return action === 'pick up' ? `Picked up ${ticket(request.id)}.` : `Reopened ${ticket(request.id)}.`;
        }
      )
    );

  const remove = (request: ServiceRequest) =>
    withBusy(request.id, () =>
      traced('delete request', { 'servicedesk.request.id': request.id }, async (inSpan) => {
        await inSpan(() => api.remove(request.id));
        setRequests((current) => current.filter((r) => r.id !== request.id));
        setSummary(await inSpan(() => api.summary()));
        return `Deleted ${ticket(request.id)}.`;
      })
    );

  const raise = (draft: Parameters<typeof api.raise>[0]) =>
    traced(
      'raise request',
      { 'servicedesk.request.category': draft.category, 'servicedesk.request.priority': draft.priority },
      async (inSpan) => {
        const created = await inSpan(() => api.raise(draft));
        setRequests((current) => sortQueue([...current, created]));
        setSummary(await inSpan(() => api.summary()));
        setTab((current) => (current === 'open' || current === 'unassigned' ? current : 'open'));
        setFresh(created.id);
        setNotice(`Raised ${ticket(created.id)}: ${created.title}`);
        setError('');
      }
    ).catch((err) => {
      setError(errorMessage(err));
      throw err;
    });

  const counts = useMemo(
    () => Object.fromEntries(TABS.map((t) => [t.id, requests.filter((r) => inTab(r, t.id)).length])) as Record<Tab, number>,
    [requests]
  );
  const visible = requests.filter((r) => inTab(r, tab));
  const activeTab = TABS.find((t) => t.id === tab)!;

  const health: { label: string; value: number | undefined; icon: IconName; tone: string }[] = [
    { label: 'critical', value: summary?.critical, icon: 'flame', tone: 'critical' },
    { label: 'past SLA', value: summary?.overdue, icon: 'alert', tone: 'danger' },
    { label: 'resolved in 24 h', value: summary?.resolvedLast24Hours, icon: 'check-circle', tone: 'ok' },
  ];

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            <Icon name="headset" size={20} />
          </span>
          <h1 className="brand-name">Contoso Service Desk</h1>
        </div>
        <ul className="health" role="list" aria-label="Queue health">
          {health.map((h) => (
            <li key={h.label} data-tone={h.tone}>
              <Icon name={h.icon} size={16} />
              <strong>{h.value ?? '–'}</strong> {h.label}
            </li>
          ))}
        </ul>
        <div className="agent">
          <span className="avatar avatar-lg" aria-hidden="true">
            {initials(AGENT)}
          </span>
          <div className="agent-text">
            <strong>{AGENT}</strong>
            <span>Service desk agent</span>
          </div>
        </div>
      </header>
      <div className="layout">
        <section className="card queue" aria-labelledby="queue-title" aria-busy={loading}>
          <div className="queue-head">
            <div>
              <h2 id="queue-title" className="card-title">
                Request queue
              </h2>
              <p className="card-subtitle" role="status" aria-live="polite">
                {notice || 'Most urgent first, then closest to breaching its SLA.'}
              </p>
            </div>
            <div className="queue-tools">
              <div className="seg" role="group" aria-label="Filter requests">
                {TABS.map((t) => (
                  <button key={t.id} type="button" className="seg-btn" aria-pressed={tab === t.id} onClick={() => setTab(t.id)}>
                    {t.label}
                    <span className="seg-count">{counts[t.id]}</span>
                  </button>
                ))}
              </div>
              <button type="button" className="icon-btn" onClick={load} aria-label="Refresh the queue" title="Refresh">
                <Icon name="refresh" />
              </button>
            </div>
          </div>

          {error && (
            <div className="alert" role="alert">
              <Icon name="alert" />
              <span>{error}</span>
              <button type="button" className="icon-btn" onClick={() => setError('')} aria-label="Dismiss">
                <Icon name="x" />
              </button>
            </div>
          )}

          <div className="columns" aria-hidden="true">
            <span>Request</span>
            <span>Priority</span>
            <span>Assignee</span>
            <span>SLA</span>
          </div>

          {loading ? (
            <ul className="rows" role="list" aria-label="Loading the queue">
              {[0, 1, 2, 3].map((i) => (
                <li key={i} className="row row-skeleton" aria-hidden="true">
                  <span className="skeleton skeleton-title" />
                  <span className="skeleton skeleton-meta" />
                </li>
              ))}
            </ul>
          ) : visible.length === 0 ? (
            <div className="empty">
              <Icon name="inbox" size={22} />
              <p>{activeTab.empty}</p>
              <p className="empty-hint">
                Raise a request with the form, or run <strong>Seed sample data</strong> on the <code>api</code> resource in the Aspire dashboard.
              </p>
            </div>
          ) : (
            <ul className="rows" role="list">
              {visible.map((r) => {
                const sla = slaStatus(r, now);
                const isBusy = busy.has(r.id);
                const since = r.status === 'Resolved' ? `resolved ${duration(now - Date.parse(r.resolvedAt ?? ''))} ago` : `${duration(now - Date.parse(r.createdAt))} ago`;
                return (
                  <li
                    key={r.id}
                    className={`row${fresh === r.id ? ' row-fresh' : ''}`}
                    data-priority={r.priority.toLowerCase()}
                    data-status={r.status}
                  >
                    <div className="row-main">
                      <p className="row-title">{r.title}</p>
                      <p className="row-meta">
                        <span className="ticket">{ticket(r.id)}</span>
                        <span aria-hidden="true">·</span>
                        <span className="row-category">
                          <Icon name={CATEGORY_ICON[r.category]} size={14} />
                          {r.category}
                        </span>
                        <span aria-hidden="true">·</span>
                        <span>{r.requestedBy}</span>
                        <span aria-hidden="true">·</span>
                        <span>{since}</span>
                      </p>
                    </div>
                    <div className="row-chips">
                      <span className="pill" data-priority={r.priority.toLowerCase()}>
                        <span className="pill-dot" aria-hidden="true" />
                        {r.priority}
                      </span>
                      <span className={`assignee${r.assignedTo ? '' : ' assignee-none'}`}>
                        {r.assignedTo ? (
                          <>
                            <span className="avatar" aria-hidden="true">
                              {initials(r.assignedTo)}
                            </span>
                            <span className="assignee-name">{r.assignedTo}</span>
                          </>
                        ) : (
                          'Unassigned'
                        )}
                      </span>
                      <span className="sla" data-tone={sla.tone}>
                        <Icon name={sla.tone === 'ok' ? 'check' : sla.tone === 'danger' ? 'alert' : 'clock'} size={14} />
                        {sla.text}
                      </span>
                    </div>
                    <div className="row-actions">
                      {r.status === 'New' && (
                        <button type="button" className="btn btn-outline btn-sm" disabled={isBusy} onClick={() => move(r, 'InProgress', 'pick up')}>
                          <Icon name="user-plus" size={15} />
                          Pick up
                        </button>
                      )}
                      {r.status === 'InProgress' && (
                        <button type="button" className="btn btn-soft-success btn-sm" disabled={isBusy} onClick={() => move(r, 'Resolved', 'resolve')}>
                          <Icon name="check" size={15} />
                          Resolve
                        </button>
                      )}
                      {r.status === 'Resolved' && (
                        <button type="button" className="btn btn-ghost btn-sm" disabled={isBusy} onClick={() => move(r, 'InProgress', 'reopen')}>
                          <Icon name="reopen" size={15} />
                          Reopen
                        </button>
                      )}
                      <button
                        type="button"
                        className="icon-btn icon-btn-danger"
                        disabled={isBusy}
                        onClick={() => remove(r)}
                        aria-label={`Delete ${ticket(r.id)}`}
                        title="Delete"
                      >
                        <Icon name="trash" size={16} />
                      </button>
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </section>

        <aside className="side">
          <Composer onRaise={raise} />
        </aside>
      </div>

      <footer className="footnote">Built with Aspire · ASP.NET Core Minimal API · EF Core · PostgreSQL · React + Vite</footer>
    </div>
  );
}

function Composer({ onRaise }: { onRaise: (draft: Parameters<typeof api.raise>[0]) => Promise<void> }) {
  const [title, setTitle] = useState('');
  const [requestedBy, setRequestedBy] = useState('');
  const [category, setCategory] = useState<Category>('Hardware');
  const [priority, setPriority] = useState<Priority>('Medium');
  const [saving, setSaving] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!title.trim() || !requestedBy.trim()) return;
    setSaving(true);
    try {
      await onRaise({ title: title.trim(), requestedBy: requestedBy.trim(), category, priority });
      setTitle('');
    } catch {
      // The error is shown above the queue.
    } finally {
      setSaving(false);
    }
  };

  const sla = PRIORITIES.find((p) => p.id === priority)!.sla;

  return (
    <form className="card composer" onSubmit={submit} aria-labelledby="composer-title">
      <h2 id="composer-title" className="card-title">
        Raise a request
      </h2>
      <p className="card-subtitle">Log a call or walk-up on behalf of an employee.</p>

      <div className="composer-group">
        <label className="field-label" htmlFor="request-title">
          What's the problem?
        </label>
        <input
          id="request-title"
          className="field"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="e.g. VPN keeps disconnecting"
          maxLength={200}
          autoComplete="off"
          required
        />

        <label className="field-label" htmlFor="request-by">
          Requested by
        </label>
        <input
          id="request-by"
          className="field"
          value={requestedBy}
          onChange={(e) => setRequestedBy(e.target.value)}
          placeholder="Employee name"
          maxLength={80}
          autoComplete="off"
          required
        />
      </div>

      <div className="composer-group">
        <fieldset className="choice">
          <legend className="field-label">Category</legend>
          <div className="choice-grid">
            {CATEGORIES.map((c) => (
              <button key={c.id} type="button" className="choice-btn" aria-pressed={category === c.id} onClick={() => setCategory(c.id)}>
                <Icon name={c.icon} size={16} />
                {c.id}
              </button>
            ))}
          </div>
        </fieldset>

        <fieldset className="choice">
          <legend className="field-label">Priority</legend>
          <div className="choice-grid choice-grid-4">
            {PRIORITIES.map((p) => (
              <button
                key={p.id}
                type="button"
                className="choice-btn"
                data-priority={p.id.toLowerCase()}
                aria-pressed={priority === p.id}
                onClick={() => setPriority(p.id)}
              >
                <span className="pill-dot" aria-hidden="true" />
                {p.id}
                <span className="choice-meta">{p.short}</span>
              </button>
            ))}
          </div>
          <p className="field-hint">
            <Icon name="clock" size={14} /> SLA: resolve within {sla} of raising it.
          </p>
        </fieldset>
      </div>

      <button type="submit" className="btn btn-primary btn-block" disabled={saving || !title.trim() || !requestedBy.trim()}>
        <Icon name="plus" size={16} />
        Raise request
      </button>
    </form>
  );
}