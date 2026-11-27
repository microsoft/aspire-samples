/** Small inline SVG glyphs used across the controls. */

/** Box truck, for fleet actions and the collapsed launcher. */
export function TruckGlyph({ size = 18 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M2.5 6.5h11v9.5h-11z" />
      <path d="M13.5 9.5h4l3 3.2v3.3h-7" />
      <circle cx="6.5" cy="17.5" r="1.8" fill="var(--panel-solid)" />
      <circle cx="17" cy="17.5" r="1.8" fill="var(--panel-solid)" />
    </svg>
  );
}

/** Dotted path between two pins, for planning a custom route. */
export function RouteGlyph() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="6" cy="18" r="2.2" />
      <circle cx="18" cy="6" r="2.2" />
      <path d="M8 17.5c4-.5 2-5.5 5.5-6.5S17 9.5 17 8" strokeDasharray="2 2.6" />
    </svg>
  );
}

/** Concentric waves for the rain-radar toggle. */
export function RadarGlyph() {
  return (
    <svg
      className="radar-glyph"
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <circle cx="12" cy="12" r="9" />
      <circle cx="12" cy="12" r="4.6" opacity="0.55" />
      <g className="radar-sweep">
        <path d="M12 12 L12 3 A9 9 0 0 0 5.64 5.64 Z" fill="currentColor" stroke="none" opacity="0.22" />
        <line x1="12" y1="12" x2="12" y2="3" />
      </g>
      <circle cx="12" cy="12" r="1.3" fill="currentColor" stroke="none" />
      <circle cx="16" cy="8.4" r="1.05" fill="currentColor" stroke="none" />
    </svg>
  );
}

/** Double chevron used to push the control pane away. */
export function CollapseGlyph() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M13 6l-6 6 6 6" />
      <path d="M18 6l-6 6 6 6" opacity="0.5" />
    </svg>
  );
}

/** Double chevron pointing right: the launcher's "expand the panel" cue. */
export function ExpandGlyph() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M11 6l6 6-6 6" />
      <path d="M6 6l6 6-6 6" opacity="0.5" />
    </svg>
  );
}

/** Stacked-sheets glyph for the map-style switcher. */
export function LayersGlyph() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M12 2 2 7l10 5 10-5-10-5Z" />
      <path d="M2 12l10 5 10-5" />
      <path d="M2 17l10 5 10-5" />
    </svg>
  );
}
