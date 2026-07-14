/** Loading placeholder that mirrors the forecast's shape. */
export function ForecastSkeleton() {
  return (
    <div className="wx-skeleton" aria-hidden="true">
      <div className="sk-hero">
        <span className="sk-block sk-icon" />
        <div className="sk-stack">
          <span className="sk-block sk-temp" />
          <span className="sk-block sk-label" />
        </div>
      </div>
      <div className="sk-hourly">
        <span className="sk-block sk-hour" />
        <span className="sk-block sk-hour" />
        <span className="sk-block sk-hour" />
        <span className="sk-block sk-hour" />
        <span className="sk-block sk-hour" />
        <span className="sk-block sk-hour" />
      </div>
      <div className="sk-meta">
        <span className="sk-block sk-cell" />
        <span className="sk-block sk-cell" />
        <span className="sk-block sk-cell" />
      </div>
      <div className="sk-rows">
        <span className="sk-block sk-row" />
        <span className="sk-block sk-row" />
        <span className="sk-block sk-row" />
        <span className="sk-block sk-row" />
        <span className="sk-block sk-row" />
      </div>
    </div>
  );
}
