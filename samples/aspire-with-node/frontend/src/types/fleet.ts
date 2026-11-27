/** Fleet Weather API shapes (see api/src/openapi.ts for the full contract). */

export type RiskLevel = 'low' | 'moderate' | 'high' | 'severe';
export type Recommendation = 'go' | 'delay' | 'reroute';

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
  legCount: number;
  distanceKm: number;
  waypoints: Waypoint[];
}

export interface Hazard {
  type: 'wind' | 'rain' | 'snow' | 'ice' | 'visibility' | 'thunderstorm' | 'cold';
  label: string;
  severity: Exclude<RiskLevel, 'low'>;
  score: number;
  peakValue: number;
  unit: string;
  peakAt: string;
}

export interface LegAssessment {
  index: number;
  from: string;
  to: string;
  distanceKm: number;
  riskScore: number;
  riskLevel: RiskLevel;
  hazards: Hazard[];
  conditions: {
    maxGustKph: number;
    minTempC: number;
    maxTempC: number;
    precipitationMm: number;
    snowfallCm: number;
    minVisibilityM: number | null;
  };
  path: [number, number][];
}

export interface RouteAssessment {
  routeId: string | null;
  name: string;
  corridor: string | null;
  source: 'predefined' | 'custom';
  windowHours: number;
  assessedAt: string;
  distanceKm: number;
  recommendation: Recommendation;
  overallRiskScore: number;
  riskLevel: RiskLevel;
  summary: string;
  legs: LegAssessment[];
}
