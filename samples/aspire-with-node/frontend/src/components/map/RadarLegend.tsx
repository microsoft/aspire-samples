/** Precipitation intensity key (matches RainViewer color scheme 4), shown with the radar. */
export function RadarLegend() {
  return (
    <div
      className="radar-legend"
      role="img"
      aria-label="Radar scale: colors run from light to heavy precipitation"
    >
      <span className="radar-legend-title">Precipitation</span>
      <div className="radar-legend-row" aria-hidden="true">
        <span>Light</span>
        <span className="radar-legend-scale" />
        <span>Heavy</span>
      </div>
    </div>
  );
}
