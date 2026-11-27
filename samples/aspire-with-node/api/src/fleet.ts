/** The fleet's predefined delivery routes and the geometry helpers used to split them into legs. */

export interface Waypoint {
  name: string;
  lat: number;
  lon: number;
}

export interface FleetRoute {
  id: string;
  name: string;
  region: string;
  corridor: string;
  waypoints: Waypoint[];
}

/** Regional linehaul routes, each with waypoints at towns, junctions and mountain passes. */
export const FLEET_ROUTES: FleetRoute[] = [
  {
    id: 'sea-pdx',
    name: 'Seattle → Portland',
    region: 'Pacific Northwest',
    corridor: 'I-5',
    waypoints: [
      { name: 'Seattle', lat: 47.6062, lon: -122.3321 },
      { name: 'Tacoma', lat: 47.2529, lon: -122.4443 },
      { name: 'Olympia', lat: 47.0379, lon: -122.9007 },
      { name: 'Centralia', lat: 46.7162, lon: -122.9543 },
      { name: 'Kelso', lat: 46.1468, lon: -122.9084 },
      { name: 'Portland', lat: 45.5152, lon: -122.6784 },
    ],
  },
  {
    id: 'den-gjt',
    name: 'Denver → Grand Junction',
    region: 'Colorado Rockies',
    corridor: 'I-70',
    waypoints: [
      { name: 'Denver', lat: 39.7392, lon: -104.9903 },
      { name: 'Idaho Springs', lat: 39.7425, lon: -105.5136 },
      { name: 'Eisenhower Tunnel', lat: 39.6797, lon: -105.9289 },
      { name: 'Vail Pass', lat: 39.5308, lon: -106.2169 },
      { name: 'Glenwood Springs', lat: 39.5505, lon: -107.3248 },
      { name: 'Grand Junction', lat: 39.0639, lon: -108.5506 },
    ],
  },
  {
    id: 'den-slc',
    name: 'Denver → Salt Lake City',
    region: 'Mountain West',
    corridor: 'I-25 / I-80',
    waypoints: [
      { name: 'Denver', lat: 39.7392, lon: -104.9903 },
      { name: 'Fort Collins', lat: 40.5853, lon: -105.0844 },
      { name: 'Cheyenne', lat: 41.14, lon: -104.8202 },
      { name: 'Laramie', lat: 41.3114, lon: -105.5911 },
      { name: 'Rawlins', lat: 41.7911, lon: -107.2387 },
      { name: 'Rock Springs', lat: 41.5875, lon: -109.2029 },
      { name: 'Evanston', lat: 41.2683, lon: -110.9632 },
      { name: 'Salt Lake City', lat: 40.7608, lon: -111.891 },
    ],
  },
  {
    id: 'chi-msp',
    name: 'Chicago → Minneapolis',
    region: 'Upper Midwest',
    corridor: 'I-94',
    waypoints: [
      { name: 'Chicago', lat: 41.8781, lon: -87.6298 },
      { name: 'Milwaukee', lat: 43.0389, lon: -87.9065 },
      { name: 'Madison', lat: 43.0731, lon: -89.4012 },
      { name: 'Wisconsin Dells', lat: 43.6275, lon: -89.771 },
      { name: 'Eau Claire', lat: 44.8113, lon: -91.4985 },
      { name: 'Minneapolis', lat: 44.9778, lon: -93.265 },
    ],
  },
  {
    id: 'dfw-hou',
    name: 'Dallas → Houston',
    region: 'Texas Triangle',
    corridor: 'I-45',
    waypoints: [
      { name: 'Dallas', lat: 32.7767, lon: -96.797 },
      { name: 'Corsicana', lat: 32.0954, lon: -96.4689 },
      { name: 'Buffalo', lat: 31.4635, lon: -96.058 },
      { name: 'Huntsville', lat: 30.7235, lon: -95.5508 },
      { name: 'Houston', lat: 29.7604, lon: -95.3698 },
    ],
  },
];

export function findRoute(id: string): FleetRoute | undefined {
  return FLEET_ROUTES.find((route) => route.id === id);
}

/** Great-circle distance in kilometers (haversine). */
export function distanceKm(a: Waypoint, b: Waypoint): number {
  const rad = Math.PI / 180;
  const dLat = (b.lat - a.lat) * rad;
  const dLon = (b.lon - a.lon) * rad;
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(a.lat * rad) * Math.cos(b.lat * rad) * Math.sin(dLon / 2) ** 2;
  return 2 * 6371 * Math.asin(Math.sqrt(h));
}

export function routeDistanceKm(waypoints: Waypoint[]): number {
  let total = 0;
  for (let i = 1; i < waypoints.length; i++) total += distanceKm(waypoints[i - 1], waypoints[i]);
  return Math.round(total);
}

/** A leg's forecast sample points: start, midpoint and end. */
export function legSamplePoints(from: Waypoint, to: Waypoint): [number, number][] {
  return [
    [from.lat, from.lon],
    [(from.lat + to.lat) / 2, (from.lon + to.lon) / 2],
    [to.lat, to.lon],
  ];
}
