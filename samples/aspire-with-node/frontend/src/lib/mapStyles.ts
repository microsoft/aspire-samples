/** Keyless base-map styles + the map's initial view. */
import type { LatLng, MapStyle } from '../types/map';

// Seattle - a recognizable default so the map has data on first paint.
export const INITIAL_CENTER: LatLng = [47.6062, -122.3321];
export const INITIAL_ZOOM = 9;

// Keyless base-map styles the user can switch between (no API key required).
export const MAP_STYLES: MapStyle[] = [
  {
    key: 'streets',
    label: 'Streets',
    url: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  },
  {
    key: 'voyager',
    label: 'Voyager',
    url: 'https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png',
    attribution:
      '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> &copy; <a href="https://carto.com/attributions">CARTO</a>',
    subdomains: 'abcd',
  },
  {
    key: 'light',
    label: 'Light',
    url: 'https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png',
    attribution:
      '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> &copy; <a href="https://carto.com/attributions">CARTO</a>',
    subdomains: 'abcd',
  },
  {
    key: 'dark',
    label: 'Dark',
    url: 'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',
    attribution:
      '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> &copy; <a href="https://carto.com/attributions">CARTO</a>',
    subdomains: 'abcd',
  },
  {
    key: 'satellite',
    label: 'Satellite',
    url: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
    attribution:
      'Tiles &copy; <a href="https://www.esri.com/">Esri</a>, Maxar, Earthstar Geographics',
    // Roads + place labels layered on top so the imagery is still navigable.
    overlay: [
      'https://server.arcgisonline.com/ArcGIS/rest/services/Reference/World_Transportation/MapServer/tile/{z}/{y}/{x}',
      'https://server.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}',
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
