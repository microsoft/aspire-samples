import { useCallback, useEffect, useRef, useState } from "react";
import {
  IconAlertTriangle,
  IconArrowRight,
  IconBolt,
  IconCircleCheck,
  IconMapPin,
  IconRefresh,
  IconRoute,
  IconTag,
  IconTruckDelivery,
  IconWeight,
} from "@tabler/icons-react";
import type { Icon } from "@tabler/icons-react";
import {
  STATUSES,
  STATUS_LABELS,
  type ShipmentBoard,
  type ShipmentDetail,
  type ShipmentStatus,
} from "./models/Shipment";
import { traced } from "./lib/tracing";

const STATUS_ICONS: Record<ShipmentStatus, Icon> = {
  "label-created": IconTag,
  "in-transit": IconRoute,
  "out-for-delivery": IconTruckDelivery,
  delivered: IconCircleCheck,
  exception: IconAlertTriangle,
};

const dateTime = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});
const formatDateTime = (iso: string) => dateTime.format(new Date(iso));

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url} returned HTTP ${response.status}`);
  return (await response.json()) as T;
}

function NeonChip({ status }: { status: ShipmentStatus }) {
  const Icon = STATUS_ICONS[status];
  return (
    <span className="neon-chip" data-status={status}>
      <Icon size={14} aria-hidden="true" />
      {STATUS_LABELS[status]}
    </span>
  );
}

type LoadState = "loading" | "ready" | "error";

function App() {
  const [board, setBoard] = useState<ShipmentBoard | null>(null);
  const [filter, setFilter] = useState<ShipmentStatus | null>(null);
  const [state, setState] = useState<LoadState>("loading");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ShipmentDetail | null>(null);
  const selectedRef = useRef<string | null>(null);

  const selectShipment = useCallback((id: string | null) => {
    selectedRef.current = id;
    setSelectedId(id);
    if (!id) {
      setDetail(null);
      return;
    }
    // "track shipment" -> GET -> GET /api/shipments/{id} -> shipments.track -> carrier.track, as one trace.
    traced("track shipment", { "contoso.shipment.id": id }, () =>
      getJson<ShipmentDetail>(`api/shipments/${encodeURIComponent(id)}`)
    )
      .then((result) => {
        if (selectedRef.current === id) setDetail(result);
      })
      .catch((err) => console.error(`Failed to load shipment ${id}`, err));
  }, []);

  const loadBoard = useCallback(
    async (status: ShipmentStatus | null) => {
      setState("loading");
      try {
        const query = status ? `?status=${encodeURIComponent(status)}` : "";
        const result = await traced(
          "load shipment board",
          { "contoso.shipment.status_filter": status ?? "all" },
          () => getJson<ShipmentBoard>(`api/shipments${query}`)
        );
        setBoard(result);
        setState("ready");
        if (!result.shipments.some((s) => s.id === selectedRef.current)) {
          selectShipment(result.shipments[0]?.id ?? null);
        }
      } catch (err) {
        console.error("Failed to load shipments", err);
        setState("error");
      }
    },
    [selectShipment]
  );

  useEffect(() => {
    loadBoard(filter);
  }, [filter, loadBoard]);

  const counts = Object.fromEntries((board?.statusCounts ?? []).map((c) => [c.status, c.count]));
  const shipments = board?.shipments ?? [];

  const status =
    state === "loading"
      ? "Scanning the network…"
      : state === "error"
        ? "Lost the signal to the shipments API. Hit refresh to retry."
        : `Tracking ${shipments.length} ${shipments.length === 1 ? "shipment" : "shipments"}${filter ? ` · ${STATUS_LABELS[filter]}` : ""}.`;

  return (
    <>
      <div className="synth-backdrop" aria-hidden="true">
        <div className="synth-sun" />
        <div className="synth-horizon" />
        <div className="synth-grid" />
      </div>

      <a
        href="#board"
        className="synth-btn absolute left-[-9999px] top-2 z-50 rounded-md px-4 py-2 font-semibold focus:left-4"
      >
        Skip to shipments
      </a>

      <div className="relative z-10 mx-auto flex min-h-screen max-w-6xl flex-col gap-6 px-5 py-9">
        <header className="synth-masthead flex flex-wrap items-center gap-4">
          <span className="synth-btn grid h-14 w-14 place-items-center rounded-2xl">
            <IconBolt size={28} aria-hidden="true" />
          </span>
          <div>
            <p className="kicker font-mono text-xs font-semibold uppercase tracking-[0.22em]">
              Contoso · React + Vite
            </p>
            <h1 className="neon font-display text-3xl font-bold uppercase tracking-tight sm:text-4xl">
              Vite Tracking
            </h1>
          </div>
          <button
            type="button"
            onClick={() => loadBoard(filter)}
            disabled={state === "loading"}
            className="synth-btn ml-auto inline-flex items-center gap-2 rounded-lg px-4 py-2 font-mono text-sm font-semibold uppercase tracking-wide disabled:cursor-progress disabled:opacity-75"
          >
            <IconRefresh size={18} aria-hidden="true" className={state === "loading" ? "spin" : undefined} />
            {state === "loading" ? "Syncing" : "Refresh"}
          </button>
        </header>

        <nav className="flex flex-wrap gap-2.5" aria-label="Filter by status">
          <button type="button" className="filter-chip" aria-pressed={filter === null} onClick={() => setFilter(null)}>
            All <span className="filter-count">{board?.total ?? "–"}</span>
          </button>
          {STATUSES.map((s) => (
            <button
              key={s}
              type="button"
              className="filter-chip"
              data-status={s}
              aria-pressed={filter === s}
              onClick={() => setFilter(s)}
            >
              {STATUS_LABELS[s]} <span className="filter-count">{counts[s] ?? "–"}</span>
            </button>
          ))}
        </nav>

        <main id="board" className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_24rem]">
          <section className="synth-panel overflow-hidden rounded-2xl" aria-labelledby="board-title">
            <div className="synth-head flex flex-wrap items-center gap-3 px-6 py-4">
              <h2
                id="board-title"
                className="neon-2 font-display text-lg font-bold uppercase tracking-wide"
              >
                Shipment board
              </h2>
              <p
                role="status"
                aria-live="polite"
                className="ml-auto font-mono text-sm font-medium text-[color:var(--muted)]"
              >
                {status}
              </p>
            </div>

            <ul className="divide-y divide-[color:var(--rule)]" aria-label="Shipments">
              {shipments.map((s) => (
                <li key={s.id}>
                  <button
                    type="button"
                    className="board-row grid w-full grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-1 px-6 py-3 text-left sm:grid-cols-[9.5rem_minmax(0,1fr)_auto]"
                    aria-pressed={s.id === selectedId}
                    onClick={() => selectShipment(s.id)}
                  >
                    <span>
                      <span className="block font-mono font-bold tabular-nums">{s.id}</span>
                      <span className="block font-mono text-xs text-[color:var(--muted)]">{s.carrier}</span>
                    </span>
                    <span className="order-last col-span-2 min-w-0 sm:order-none sm:col-span-1">
                      <span className="flex flex-wrap items-center gap-1.5 font-semibold">
                        {s.origin}
                        <IconArrowRight size={15} aria-label="to" className="text-[color:var(--accent-2)]" />
                        {s.destination}
                      </span>
                      <span className="block font-mono text-xs text-[color:var(--muted)]">
                        {s.status === "delivered" ? "Delivered" : "ETA"} {formatDateTime(s.eta)}
                      </span>
                    </span>
                    <NeonChip status={s.status} />
                  </button>
                </li>
              ))}
              {state === "ready" && shipments.length === 0 && (
                <li className="px-6 py-6 font-mono text-sm text-[color:var(--muted)]">No shipments with this status.</li>
              )}
            </ul>
          </section>

          <aside className="lg:sticky lg:top-6" aria-live="polite">
            {detail ? (
              <article
                className="synth-panel overflow-hidden rounded-2xl"
                data-status={detail.shipment.status}
                aria-labelledby="detail-title"
              >
                <div className="synth-head px-6 py-5">
                  <p className="font-mono text-xs font-semibold uppercase tracking-[0.2em] text-[color:var(--muted)]">
                    Tracking number
                  </p>
                  <h2 id="detail-title" className="neon font-display text-2xl font-bold tracking-wide">
                    {detail.shipment.id}
                  </h2>
                  <p className="mt-1 flex flex-wrap items-center gap-1.5 font-semibold">
                    {detail.shipment.origin}
                    <IconArrowRight size={16} aria-hidden="true" className="text-[color:var(--accent-2)]" />
                    {detail.shipment.destination}
                  </p>
                  <div className="mt-4 flex items-center justify-between gap-3">
                    <NeonChip status={detail.shipment.status} />
                    <span className="font-mono text-sm tabular-nums text-[color:var(--muted)]">
                      {detail.shipment.progress}% of route
                    </span>
                  </div>
                  <div
                    className="neon-track mt-3"
                    role="progressbar"
                    aria-label="Route progress"
                    aria-valuenow={detail.shipment.progress}
                    aria-valuemin={0}
                    aria-valuemax={100}
                  >
                    <span style={{ width: `${Math.max(detail.shipment.progress, 4)}%` }} />
                  </div>
                </div>

                <dl className="grid grid-cols-2 border-b border-[color:var(--edge)] font-mono text-sm">
                  <div className="border-r border-[color:var(--edge)] px-6 py-3">
                    <dt className="text-xs uppercase tracking-[0.16em] text-[color:var(--muted)]">
                      {detail.shipment.status === "delivered" ? "Delivered" : "ETA"}
                    </dt>
                    <dd className="font-semibold">{formatDateTime(detail.shipment.eta)}</dd>
                  </div>
                  <div className="px-6 py-3">
                    <dt className="text-xs uppercase tracking-[0.16em] text-[color:var(--muted)]">Weight</dt>
                    <dd className="flex items-center gap-1 font-semibold">
                      <IconWeight size={15} aria-hidden="true" />
                      {detail.shipment.weightKg.toFixed(1)} kg
                    </dd>
                  </div>
                </dl>

                <section className="px-6 py-4" aria-labelledby="log-title">
                  <h3
                    id="log-title"
                    className="neon-2 mb-3 font-display text-sm font-bold uppercase tracking-[0.18em]"
                  >
                    Scan log
                  </h3>
                  <ol className="scan-log">
                    {detail.history.map((scan, i) => (
                      <li key={scan.at} className={i === 0 ? "latest" : undefined}>
                        <p className="font-semibold">{scan.description}</p>
                        <p className="flex flex-wrap items-center gap-1 font-mono text-xs text-[color:var(--muted)]">
                          <IconMapPin size={13} aria-hidden="true" />
                          {scan.location} · {formatDateTime(scan.at)}
                        </p>
                      </li>
                    ))}
                  </ol>
                </section>
              </article>
            ) : (
              <p className="synth-panel rounded-2xl px-6 py-8 font-mono text-sm text-[color:var(--muted)]">
                Select a shipment to tune into its scan log.
              </p>
            )}
          </aside>
        </main>

        <p className="flex items-center gap-2 font-mono text-sm text-[color:var(--muted)]">
          <IconBolt size={16} aria-hidden="true" />
          Powered by <strong className="text-[color:var(--ink)]">Aspire</strong> — shipments served by the
          orchestrated minimal API.
        </p>
      </div>
    </>
  );
}

export default App;