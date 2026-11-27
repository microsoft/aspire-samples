/** Keyless base-map styles + the map's initial view. */
import type { LatLng, MapStyle } from '../types/map';

// The continental US, where the fleet's routes run. The map refits once routes load.
export const INITIAL_CENTER: LatLng = [39.5, -98.35];
export const INITIAL_ZOOM = 4;

/** Dark canvas for dark system themes, light canvas otherwise: muted bases that let route colors stand out. */
export function defaultStyleKey(): string {
  return typeof window !== 'undefined' && window.matchMedia('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light';
}

const ESRI = 'https://server.arcgisonline.com/ArcGIS/rest/services';
const ESRI_CANVAS_ATTRIBUTION =
  'Tiles &copy; <a href="https://www.esri.com/">Esri</a> &mdash; Esri, HERE, Garmin, &copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

// Keyless base-map styles the user can switch between (no API key required).
export const MAP_STYLES: MapStyle[] = [
  {
    key: 'light',
    label: 'Light',
    url: `${ESRI}/Canvas/World_Light_Gray_Base/MapServer/tile/{z}/{y}/{x}`,
    attribution: ESRI_CANVAS_ATTRIBUTION,
    overlay: [`${ESRI}/Canvas/World_Light_Gray_Reference/MapServer/tile/{z}/{y}/{x}`],
  },
  {
    key: 'dark',
    label: 'Dark',
    url: `${ESRI}/Canvas/World_Dark_Gray_Base/MapServer/tile/{z}/{y}/{x}`,
    attribution: ESRI_CANVAS_ATTRIBUTION,
    overlay: [`${ESRI}/Canvas/World_Dark_Gray_Reference/MapServer/tile/{z}/{y}/{x}`],
  },
  {
    key: 'streets',
    label: 'Streets',
    url: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  },
  {
    key: 'topo',
    label: 'Terrain',
    url: `${ESRI}/World_Topo_Map/MapServer/tile/{z}/{y}/{x}`,
    attribution: 'Tiles &copy; <a href="https://www.esri.com/">Esri</a> &mdash; Esri, HERE, Garmin, USGS, NGA',
  },
  {
    key: 'satellite',
    label: 'Satellite',
    url: `${ESRI}/World_Imagery/MapServer/tile/{z}/{y}/{x}`,
    attribution:
      'Tiles &copy; <a href="https://www.esri.com/">Esri</a>, Maxar, Earthstar Geographics',
    // Roads + place labels layered on top so the imagery is still navigable.
    overlay: [
      `${ESRI}/Reference/World_Transportation/MapServer/tile/{z}/{y}/{x}`,
      `${ESRI}/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}`,
    ],
  },
];

/** Builds a small preview thumbnail (a fixed New York-area tile) for a map style. */
export function stylePreview(style: MapStyle): string {
  return style.url
    .replace('{s}', (style.subdomains ?? 'abc')[0])
    .replace('{z}', '5')
    .replace('{x}', '9')
    .replace('{y}', '12')
    .replace('{r}', '');
}
