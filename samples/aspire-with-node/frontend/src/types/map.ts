/** Map + radar domain types. */

/** A `[latitude, longitude]` pair, as Leaflet expects. */
export type LatLng = [number, number];

export interface MapStyle {
  key: string;
  label: string;
  url: string;
  attribution: string;
  subdomains?: string;
  /** Transparent reference tiles drawn over the base (e.g. streets + labels on satellite). */
  overlay?: string[];
}

export interface RadarFrame {
  time: number;
  url: string;
  forecast: boolean;
}
