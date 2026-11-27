/** Formatting helpers for radar frames, hazards and leg conditions. */
import type { Hazard, LegAssessment, Recommendation } from '../types/fleet';
import type { RadarFrame } from '../types/map';

/** Clock time (e.g. "2:40 PM") for a radar frame's unix-second timestamp. */
export function formatFrameTime(frame: RadarFrame): string {
  return new Date(frame.time * 1000).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

/** Local weekday and hour of a forecast peak, e.g. "Thu 9 AM". */
export function formatPeakTime(iso: string): string {
  return new Date(iso).toLocaleString([], { weekday: 'short', hour: 'numeric' });
}

/** Typographic minus sign for negative temperatures. */
const signed = (value: number) => String(value).replace('-', '−');

export function formatVisibility(meters: number): string {
  return meters >= 1000 ? `${(meters / 1000).toFixed(meters >= 10_000 ? 0 : 1)} km` : `${Math.round(meters)} m`;
}

/** The peak reading that triggered a hazard, e.g. "101 km/h" or "300 m". */
export function formatHazardPeak(hazard: Hazard): string {
  switch (hazard.type) {
    case 'wind':
      return `${Math.round(hazard.peakValue)} km/h`;
    case 'visibility':
      return formatVisibility(hazard.peakValue);
    case 'thunderstorm':
      return hazard.peakValue === 95 ? 'thunder' : 'hail';
    case 'cold':
      return `${signed(hazard.peakValue)} °C`;
    default:
      return `${hazard.peakValue} ${hazard.unit}`;
  }
}

/** One-line conditions summary for a leg, e.g. "Gusts 101 km/h · −13 to −1 °C · Snow 9.1 cm". */
export function formatConditions({ conditions: c }: LegAssessment): string {
  const parts = [
    `Gusts ${c.maxGustKph} km/h`,
    `${signed(Math.round(c.minTempC))} to ${signed(Math.round(c.maxTempC))} °C`,
  ];
  if (c.snowfallCm >= 0.1) parts.push(`Snow ${c.snowfallCm} cm`);
  else if (c.precipitationMm >= 0.1) parts.push(`Rain ${c.precipitationMm} mm`);
  if (c.minVisibilityM !== null && c.minVisibilityM < 5000) parts.push(`Vis ${formatVisibility(c.minVisibilityM)}`);
  return parts.join(' · ');
}

export const RECOMMENDATION_LABELS: Record<Recommendation, string> = {
  go: 'Go',
  delay: 'Delay',
  reroute: 'Reroute',
};
