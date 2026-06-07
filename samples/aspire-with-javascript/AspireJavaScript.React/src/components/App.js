import { useCallback, useEffect, useState } from "react";
import {
  CloudSun,
  Snowflake,
  Wind,
  CloudSnow,
  CloudFog,
  Sun,
  SunHorizon,
  ThermometerHot,
  Fire,
  ArrowsClockwise,
  Lightning,
} from "@phosphor-icons/react";
import styles from "./App.module.css";

const SUMMARY_ICONS = {
  Freezing: Snowflake,
  Bracing: Wind,
  Chilly: CloudSnow,
  Cool: CloudFog,
  Mild: CloudSun,
  Warm: Sun,
  Balmy: SunHorizon,
  Hot: ThermometerHot,
  Sweltering: Fire,
  Scorching: Fire,
};

function summaryIcon(summary) {
  const Icon = SUMMARY_ICONS[summary] ?? CloudSun;
  return <Icon size={18} weight="bold" aria-hidden="true" />;
}

function tempColor(c) {
  if (typeof c !== "number") return "var(--nb-muted)";
  if (c < -5) return "var(--nb-cold)";
  if (c < 6) return "var(--nb-cool)";
  if (c < 19) return "var(--nb-mild)";
  if (c < 31) return "var(--nb-warm)";
  return "var(--nb-hot)";
}

function App() {
  const [forecasts, setForecasts] = useState([]);
  const [state, setState] = useState("loading");

  const requestWeather = useCallback(async () => {
    setState("loading");
    try {
      const response = await fetch("api/weatherforecast");
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const data = await response.json();
      setForecasts(Array.isArray(data) ? data : []);
      setState("ready");
    } catch (err) {
      console.error("Failed to load weather forecast", err);
      setState("error");
    }
  }, []);

  useEffect(() => {
    requestWeather();
  }, [requestWeather]);

  const status =
    state === "loading"
      ? "Loading the latest forecast…"
      : state === "error"
        ? "Couldn't reach the weather service. Try again."
        : `Showing the next ${forecasts.length} days.`;

  return (
    <>
      <a className={styles.skip} href="#forecast">
        Skip to forecast
      </a>
      <div className={styles.page}>
        <header className={styles.banner}>
          <span className={styles.logo} aria-hidden="true">
            <CloudSun size={34} weight="fill" />
          </span>
          <span className={styles.brandText}>
            <span className={styles.kicker}>Aspire · React + Webpack</span>
            <h1 className={styles.title}>
              React <em>Weather</em>
            </h1>
          </span>
          <span className={styles.stampWrap}>
            <span className={styles.stamp}>
              <Lightning size={14} weight="fill" aria-hidden="true" />
              Live API
            </span>
          </span>
        </header>

        <main id="forecast" className={styles.main}>
          <section className={styles.card} aria-labelledby="forecast-title">
            <div className={styles.cardHead}>
              <h2 id="forecast-title" className={styles.cardTitle}>
                5-Day Forecast
              </h2>
              <button
                type="button"
                className={styles.refresh}
                onClick={requestWeather}
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
                <caption>Randomly generated weather forecast data.</caption>
                <thead>
                  <tr>
                    <th scope="col">Date</th>
                    <th scope="col">Temp. (°C)</th>
                    <th scope="col">Temp. (°F)</th>
                    <th scope="col">Summary</th>
                  </tr>
                </thead>
                <tbody>
                  {forecasts.length === 0 ? (
                    <tr>
                      <td className={styles.dateCell}>—</td>
                      <td>—</td>
                      <td>—</td>
                      <td>
                        {state === "loading" ? "Loading…" : "No forecasts"}
                      </td>
                    </tr>
                  ) : (
                    forecasts.map((w) => (
                      <tr key={w.date}>
                        <td className={styles.dateCell}>{w.date}</td>
                        <td>
                          <span className={styles.temp}>
                            <span
                              className={styles.dot}
                              style={{ background: tempColor(w.temperatureC) }}
                              aria-hidden="true"
                            />
                            {w.temperatureC}°
                          </span>
                        </td>
                        <td>
                          <span className={styles.temp}>{w.temperatureF}°</span>
                        </td>
                        <td>
                          <span className={styles.summaryCell}>
                            <span className={styles.chipIcon}>
                              {summaryIcon(w.summary)}
                            </span>
                            <span className={styles.summaryLabel}>
                              {w.summary}
                            </span>
                          </span>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </section>

          <p className={styles.foot}>
            <Lightning size={16} weight="fill" aria-hidden="true" />
            Powered by <strong>Aspire</strong> — weather forecast from the
            orchestrated minimal API.
          </p>
        </main>
      </div>
    </>
  );
}

export default App;
