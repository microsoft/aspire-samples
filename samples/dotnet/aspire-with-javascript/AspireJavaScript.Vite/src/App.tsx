import { useCallback, useEffect, useState } from "react";
import {
  IconSnowflake,
  IconWind,
  IconCloudSnow,
  IconCloud,
  IconSunLow,
  IconSun,
  IconSunHigh,
  IconTemperature,
  IconFlame,
  IconRefresh,
  IconBolt,
} from "@tabler/icons-react";
import type { Icon } from "@tabler/icons-react";
import { Forecast } from "./models/Forecast";

const SUMMARY_ICONS: Record<string, Icon> = {
  Freezing: IconSnowflake,
  Bracing: IconWind,
  Chilly: IconCloudSnow,
  Cool: IconCloud,
  Mild: IconSunLow,
  Warm: IconSun,
  Balmy: IconSunHigh,
  Hot: IconTemperature,
  Sweltering: IconFlame,
  Scorching: IconFlame,
};

function tempClass(c: number): string {
  if (c < -5) return "cold";
  if (c < 6) return "cool";
  if (c < 19) return "mild";
  if (c < 31) return "warm";
  return "hot";
}

type LoadState = "loading" | "ready" | "error";

function App() {
  const [forecasts, setForecasts] = useState<Array<Forecast>>([]);
  const [state, setState] = useState<LoadState>("loading");

  const requestWeather = useCallback(async () => {
    setState("loading");
    try {
      const response = await fetch("api/weatherforecast");
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const data = (await response.json()) as Array<Forecast>;
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
      ? "Tuning in the latest forecast…"
      : state === "error"
        ? "Couldn't reach the weather service. Hit refresh to retry."
        : `Broadcasting the next ${forecasts.length} days.`;

  return (
    <>
      <div className="synth-backdrop" aria-hidden="true">
        <div className="synth-sun" />
        <div className="synth-horizon" />
        <div className="synth-grid" />
      </div>

      <a
        href="#forecast"
        className="synth-btn absolute left-[-9999px] top-2 z-50 rounded-md px-4 py-2 font-semibold focus:left-4"
      >
        Skip to forecast
      </a>

      <div className="relative z-10 mx-auto flex min-h-screen max-w-3xl flex-col gap-7 px-5 py-10">
        <header className="synth-masthead flex items-center gap-4">
          <span className="synth-btn grid h-14 w-14 place-items-center rounded-2xl">
            <IconBolt size={28} aria-hidden="true" />
          </span>
          <div>
            <p className="kicker font-mono text-xs font-semibold uppercase tracking-[0.22em]">
              Aspire · React + Vite
            </p>
            <h1 className="neon font-display text-3xl font-bold uppercase tracking-tight sm:text-4xl">
              Vite Weather
            </h1>
          </div>
        </header>

        <main id="forecast">
          <section
            className="synth-panel overflow-hidden rounded-2xl"
            aria-labelledby="forecast-title"
          >
            <div className="synth-head flex flex-wrap items-center gap-3 px-6 py-4">
              <h2
                id="forecast-title"
                className="neon-2 font-display text-lg font-bold uppercase tracking-wide"
              >
                Five-Day Forecast
              </h2>
              <button
                type="button"
                onClick={requestWeather}
                disabled={state === "loading"}
                className="synth-btn ml-auto inline-flex items-center gap-2 rounded-lg px-4 py-2 font-mono text-sm font-semibold uppercase tracking-wide disabled:cursor-progress disabled:opacity-75"
              >
                <IconRefresh
                  size={18}
                  aria-hidden="true"
                  className={state === "loading" ? "spin" : undefined}
                />
                {state === "loading" ? "Syncing" : "Refresh"}
              </button>
            </div>

            <p
              role="status"
              aria-live="polite"
              className="border-b border-[color:var(--edge)] px-6 py-3 font-mono text-sm font-medium text-[color:var(--muted)]"
            >
              {status}
            </p>

            <div className="overflow-x-auto">
              <table className="w-full border-collapse text-left">
                <caption className="px-6 pt-3 text-left text-sm text-[color:var(--muted)]">
                  Randomly generated weather forecast data.
                </caption>
                <thead>
                  <tr>
                    {["Date", "Temp. (\u00b0C)", "Temp. (\u00b0F)", "Summary"].map(
                      (h) => (
                        <th
                          key={h}
                          scope="col"
                          className="px-6 py-3 font-mono text-xs font-bold uppercase tracking-[0.12em] text-[color:var(--ink)]"
                        >
                          {h}
                        </th>
                      )
                    )}
                  </tr>
                </thead>
                <tbody>
                  {forecasts.length === 0 ? (
                    <tr className="row-rule">
                      <td className="px-6 py-3">—</td>
                      <td className="px-6 py-3">—</td>
                      <td className="px-6 py-3">—</td>
                      <td className="px-6 py-3">
                        {state === "loading" ? "Loading…" : "No forecasts"}
                      </td>
                    </tr>
                  ) : (
                    forecasts.map((w) => {
                      const Icon = SUMMARY_ICONS[w.summary] ?? IconCloud;
                      const cls = tempClass(w.temperatureC);
                      return (
                        <tr key={w.date} className="row-rule">
                          <td className="px-6 py-3 font-mono font-semibold tabular-nums">
                            {w.date}
                          </td>
                          <td className="px-6 py-3 font-mono font-semibold tabular-nums">
                            {w.temperatureC}°
                          </td>
                          <td className="px-6 py-3 font-mono font-semibold tabular-nums">
                            {w.temperatureF}°
                          </td>
                          <td className="px-6 py-3">
                            <span className="inline-flex items-center gap-2.5 font-medium">
                              <span
                                className={`chip ${cls} grid h-8 w-8 place-items-center rounded-lg`}
                              >
                                <Icon size={18} aria-hidden="true" />
                              </span>
                              {w.summary}
                            </span>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </section>

          <p className="mt-6 flex items-center gap-2 font-mono text-sm text-[color:var(--muted)]">
            <IconBolt size={16} aria-hidden="true" />
            Powered by{" "}
            <strong className="text-[color:var(--ink)]">Aspire</strong> —
            forecast served by the orchestrated minimal API.
          </p>
        </main>
      </div>
    </>
  );
}

export default App;
