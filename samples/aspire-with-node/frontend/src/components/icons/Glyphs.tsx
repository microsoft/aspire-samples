/** Small inline SVG glyphs used across the controls and forecast. */

/** Small droplet shown next to precipitation probabilities. */
export function DropletIcon() {
  return (
    <svg className="drop-icon" width="10" height="12" viewBox="0 0 12 16" fill="currentColor" aria-hidden="true">
      <path d="M6 1C6 1 1.5 7 1.5 10.5a4.5 4.5 0 0 0 9 0C10.5 7 6 1 6 1z" />
    </svg>
  );
}

/** Magnifier icon for the search control. */
export function SearchGlyph() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
      <circle cx="11" cy="11" r="7" />
      <line x1="16.5" y1="16.5" x2="21" y2="21" />
    </svg>
  );
}

/** GPS-style locate icon that echoes the map reticle. */
export function LocateGlyph() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
      <circle cx="12" cy="12" r="6.5" />
      <circle cx="12" cy="12" r="2" fill="currentColor" stroke="none" />
      <line x1="12" y1="1.5" x2="12" y2="4" />
      <line x1="12" y1="20" x2="12" y2="22.5" />
      <line x1="1.5" y1="12" x2="4" y2="12" />
      <line x1="20" y1="12" x2="22.5" y2="12" />
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

/** Sun rising over a horizon, for the sunrise metric. */
export function SunriseGlyph() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M17 18a5 5 0 0 0-10 0" />
      <line x1="12" y1="9" x2="12" y2="2" />
      <polyline points="9 5 12 2 15 5" />
      <line x1="2" y1="18" x2="22" y2="18" />
      <line x1="4.5" y1="13" x2="5.5" y2="13.5" />
      <line x1="19.5" y1="13" x2="18.5" y2="13.5" />
    </svg>
  );
}

/** Sun dipping below a horizon, for the sunset metric. */
export function SunsetGlyph() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M17 18a5 5 0 0 0-10 0" />
      <line x1="12" y1="2" x2="12" y2="9" />
      <polyline points="9 6 12 9 15 6" />
      <line x1="2" y1="18" x2="22" y2="18" />
      <line x1="4.5" y1="13" x2="5.5" y2="13.5" />
      <line x1="19.5" y1="13" x2="18.5" y2="13.5" />
    </svg>
  );
}

/** Small up / down caret marking today's high and low temperatures. */
export function TrendGlyph({ up }: { up?: boolean }) {
  return (
    <svg width="10" height="10" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {up ? <path d="M6 9.5V2.5M3 5.5 6 2.5l3 3" /> : <path d="M6 2.5v7M3 6.5 6 9.5l3-3" />}
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

/** Layered-panel glyph for the collapsed launcher fallback. */
export function PanelGlyph() {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <line x1="9" y1="4" x2="9" y2="20" />
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
