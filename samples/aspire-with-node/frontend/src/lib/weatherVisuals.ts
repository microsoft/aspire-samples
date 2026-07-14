/** Weather-driven colors used for the range bars and ambient tints. */

/** Maps a temperature (°C) to a cool-to-warm color for the forecast range bars. */
export function tempColor(celsius: number | null): string {
  if (celsius == null) return 'rgb(148, 163, 184)';
  const stops: Array<[number, [number, number, number]]> = [
    [-10, [96, 165, 250]],
    [4, [56, 189, 248]],
    [14, [45, 212, 191]],
    [22, [250, 204, 21]],
    [30, [248, 113, 113]],
  ];
  if (celsius <= stops[0][0]) return `rgb(${stops[0][1].join(', ')})`;
  const last = stops[stops.length - 1];
  if (celsius >= last[0]) return `rgb(${last[1].join(', ')})`;
  for (let i = 0; i < stops.length - 1; i++) {
    const [t0, c0] = stops[i];
    const [t1, c1] = stops[i + 1];
    if (celsius >= t0 && celsius <= t1) {
      const f = (celsius - t0) / (t1 - t0);
      const c = c0.map((v, j) => Math.round(v + (c1[j] - v) * f));
      return `rgb(${c.join(', ')})`;
    }
  }
  return 'rgb(148, 163, 184)';
}

/** Weather-driven tint for the top-of-map ambiance scrim. */
export function skyTint(isDay: boolean, kind: string): string {
  if (!isDay) return 'rgba(30, 27, 75, 0.5)';
  switch (kind) {
    case 'clear':
    case 'partly':
      return 'rgba(59, 130, 246, 0.28)';
    case 'rain':
    case 'sleet':
    case 'thunder':
      return 'rgba(51, 65, 85, 0.42)';
    case 'snow':
      return 'rgba(148, 163, 184, 0.32)';
    default:
      return 'rgba(71, 85, 105, 0.34)';
  }
}

/** Condition-driven glow tint for the current-conditions card's corner. */
export function heroGlow(isDay: boolean, kind: string): string {
  if (!isDay) return 'rgba(139, 92, 240, 0.30)';
  switch (kind) {
    case 'clear':
    case 'partly':
      return 'rgba(245, 181, 69, 0.34)';
    case 'rain':
    case 'sleet':
    case 'thunder':
      return 'rgba(125, 211, 252, 0.30)';
    case 'snow':
      return 'rgba(191, 219, 254, 0.30)';
    default:
      return 'rgba(148, 163, 184, 0.26)';
  }
}
