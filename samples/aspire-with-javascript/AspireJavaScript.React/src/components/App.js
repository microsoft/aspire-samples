import { useCallback, useEffect, useRef, useState } from "react";
import { ArrowRight, ArrowsClockwise, Lightning, Package } from "@phosphor-icons/react";
import StatusChip from "./StatusChip";
import ShippingLabel from "./ShippingLabel";
import { STATUSES, STATUS_LABELS, formatDateTime, getJson } from "../lib/shipments";
import { traced } from "../lib/tracing";
import styles from "./App.module.css";

function App() {
  const [board, setBoard] = useState(null);
  const [filter, setFilter] = useState(null);
  const [state, setState] = useState("loading");
  const [selectedId, setSelectedId] = useState(null);
  const [detail, setDetail] = useState(null);
  const selectedRef = useRef(null);

  const selectShipment = useCallback((id) => {
    selectedRef.current = id;
    setSelectedId(id);
    if (!id) {
      setDetail(null);
      return;
    }
    // "track shipment" -> GET -> GET /api/shipments/{id} -> shipments.track -> carrier.track, as one trace.
    traced("track shipment", { "contoso.shipment.id": id }, () => getJson(`api/shipments/${encodeURIComponent(id)}`))
      .then((result) => {
        if (selectedRef.current === id) setDetail(result);
      })
      .catch((err) => console.error(`Failed to load shipment ${id}`, err));
  }, []);

  const loadBoard = useCallback(
    async (status) => {
      setState("loading");
      try {
        const query = status ? `?status=${encodeURIComponent(status)}` : "";
        const result = await traced("load shipment board", { "contoso.shipment.status_filter": status ?? "all" }, () =>
          getJson(`api/shipments${query}`)
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
      ? "Loading the manifest…"
      : state === "error"
        ? "Couldn't reach the shipments API. Try again."
        : `${shipments.length} ${shipments.length === 1 ? "shipment" : "shipments"}${filter ? ` · ${STATUS_LABELS[filter]}` : " on the manifest"}.`;

  return (
    <>
      <a className={styles.skip} href="#manifest">
        Skip to manifest
      </a>
      <div className={styles.page}>
        <header className={styles.banner}>
          <span className={styles.logo} aria-hidden="true">
            <Package size={34} weight="fill" />
          </span>
          <span className={styles.brandText}>
            <span className={styles.kicker}>Contoso · React + Webpack</span>
            <h1 className={styles.title}>
              Parcel <em>Manifest</em>
            </h1>
          </span>
          <span className={styles.stampWrap}>
            <span className={styles.stamp}>
              <Lightning size={14} weight="fill" aria-hidden="true" />
              Live API
            </span>
          </span>
        </header>

        <nav className={styles.filters} aria-label="Filter by status">
          <button
            type="button"
            className={styles.filter}
            aria-pressed={filter === null}
            onClick={() => setFilter(null)}
          >
            All <span className={styles.count}>{board?.total ?? "–"}</span>
          </button>
          {STATUSES.map((s) => (
            <button
              key={s}
              type="button"
              className={styles.filter}
              data-status={s}
              aria-pressed={filter === s}
              onClick={() => setFilter(s)}
            >
              {STATUS_LABELS[s]} <span className={styles.count}>{counts[s] ?? "–"}</span>
            </button>
          ))}
        </nav>

        <main id="manifest" className={styles.layout}>
          <section className={styles.card} aria-labelledby="manifest-title">
            <div className={styles.cardHead}>
              <h2 id="manifest-title" className={styles.cardTitle}>
                Shipment manifest
              </h2>
              <button
                type="button"
                className={styles.refresh}
                onClick={() => loadBoard(filter)}
                disabled={state === "loading"}
              >
                <ArrowsClockwise
                  size={18}
                  weight="bold"
                  aria-hidden="true"
                  className={state === "loading" ? styles.spin : undefined}
                />
                {state === "loading" ? "Loading" : "Refresh"}
              </button>
            </div>

            <p
              className={`${styles.status} ${state === "error" ? styles.statusError : ""}`}
              role="status"
              aria-live="polite"
            >
              {status}
            </p>

            <div className={styles.tableWrap}>
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th scope="col">Tracking #</th>
                    <th scope="col">Route</th>
                    <th scope="col">Status</th>
                    <th scope="col">ETA</th>
                  </tr>
                </thead>
                <tbody>
                  {shipments.length === 0 ? (
                    <tr>
                      <td colSpan={4}>{state === "loading" ? "Loading…" : "No shipments with this status."}</td>
                    </tr>
                  ) : (
                    shipments.map((s) => (
                      <tr key={s.id} className={s.id === selectedId ? styles.selected : undefined}>
                        <th scope="row">
                          <button
                            type="button"
                            className={styles.rowButton}
                            aria-pressed={s.id === selectedId}
                            onClick={() => selectShipment(s.id)}
                          >
                            {s.id}
                          </button>
                          <span className={styles.carrier}>{s.carrier}</span>
                        </th>
                        <td>
                          <span className={styles.route}>
                            {s.origin}
                            <ArrowRight size={14} weight="bold" aria-label="to" />
                            {s.destination}
                          </span>
                        </td>
                        <td>
                          <StatusChip status={s.status} />
                        </td>
                        <td className={styles.eta}>{formatDateTime(s.eta)}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </section>

          <aside className={styles.detail} aria-live="polite">
            {detail ? (
              <ShippingLabel detail={detail} />
            ) : (
              <p className={styles.placeholder}>Select a shipment to print its label.</p>
            )}
          </aside>
        </main>

        <p className={styles.foot}>
          <Lightning size={16} weight="fill" aria-hidden="true" />
          Powered by <strong>Aspire</strong> — shipments served by the orchestrated minimal API.
        </p>
      </div>
    </>
  );
}

export default App;