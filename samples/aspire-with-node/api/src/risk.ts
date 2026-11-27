/**
 * Weather risk scoring for a route leg. Pure functions, no I/O.
 *
 * Every hourly forecast sample along a leg is scored 0-100 per hazard type by interpolating
 * the thresholds below. A leg's score is its worst hazard score plus 5 for each additional
 * hazard of at least moderate severity (capped at 100). Levels: low < 30 <= moderate < 60
 * <= high < 80 <= severe.
 *
 * | Hazard          | Moderate (30)  | High (60)  | Severe (80+)                 |
 * | --------------- | -------------- | ---------- | ---------------------------- |
 * | Wind gusts      | 50 km/h        | 70 km/h    | 90 km/h                      |
 * | Heavy rain      | 4 mm/h         | 8 mm/h     | 16 mm/h                      |
 * | Snowfall        | 0.5 cm/h       | 1.5 cm/h   | 3 cm/h                       |
 * | Icing           | any rain at or below 0.5 °C, or freezing drizzle (high) / freezing rain (severe) |
 * | Low visibility  | 2,000 m        | 500 m      | 200 m                        |
 * | Thunderstorm    | -              | thunder    | thunder with hail            |
 * | Extreme cold    | -20 °C         | -30 °C     | -40 °C                       |
 */

export type RiskLevel = 'low' | 'moderate' | 'high' | 'severe';
export type Recommendation = 'go' | 'delay' | 'reroute';
export type HazardType = 'wind' | 'rain' | 'snow' | 'ice' | 'visibility' | 'thunderstorm' | 'cold';

/** One hour of forecast at one sample point. Open-Meteo returns null for unavailable values. */
export interface HourlySample {
  time: string;
  temperatureC: number | null;
  rainMm: number | null;
  showersMm: number | null;
  snowfallCm: number | null;
  gustKph: number | null;
  visibilityM: number | null;
  weatherCode: number | null;
}

export interface Hazard {
  type: HazardType;
  label: string;
  severity: Exclude<RiskLevel, 'low'>;
  score: number;
  peakValue: number;
  unit: string;
  peakAt: string;
}

export interface LegConditions {
  maxGustKph: number;
  minTempC: number;
  maxTempC: number;
  precipitationMm: number;
  snowfallCm: number;
  minVisibilityM: number | null;
}

export interface LegRisk {
  riskScore: number;
  riskLevel: RiskLevel;
  hazards: Hazard[];
  conditions: LegConditions;
}

type Curve = [x: number, score: number][];

const HAZARDS: Record<HazardType, { label: string; unit: string; curve: Curve }> = {
  wind: { label: 'High wind', unit: 'km/h', curve: [[25, 0], [50, 30], [70, 60], [90, 80], [110, 100]] },
  rain: { label: 'Heavy rain', unit: 'mm/h', curve: [[0, 0], [4, 30], [8, 60], [16, 80], [30, 100]] },
  snow: { label: 'Snow', unit: 'cm/h', curve: [[0, 0], [0.5, 30], [1.5, 60], [3, 80], [6, 100]] },
  ice: { label: 'Icing', unit: 'mm/h', curve: [[0, 0], [0.1, 45], [1, 70], [3, 85]] },
  visibility: { label: 'Low visibility', unit: 'm', curve: [[50, 100], [200, 80], [500, 60], [2000, 30], [10000, 0]] },
  thunderstorm: { label: 'Thunderstorm', unit: 'WMO code', curve: [] },
  cold: { label: 'Extreme cold', unit: '°C', curve: [[-40, 80], [-30, 60], [-20, 30], [-15, 0]] },
};

/** Hazards that close or restrict mountain and plains corridors, so waiting rarely helps. */
const ROAD_CLOSING: ReadonlySet<HazardType> = new Set(['wind', 'snow', 'ice']);

function interpolate(curve: Curve, x: number): number {
  if (x <= curve[0][0]) return curve[0][1];
  for (let i = 1; i < curve.length; i++) {
    const [x1, y1] = curve[i];
    if (x <= x1) {
      const [x0, y0] = curve[i - 1];
      return y0 + ((x - x0) / (x1 - x0)) * (y1 - y0);
    }
  }
  return curve[curve.length - 1][1];
}

export function riskLevel(score: number): RiskLevel {
  if (score >= 80) return 'severe';
  if (score >= 60) return 'high';
  if (score >= 30) return 'moderate';
  return 'low';
}

/** Scores one hourly sample, returning [score, observed value] per hazard type. */
function scoreSample(s: HourlySample): Partial<Record<HazardType, [number, number]>> {
  const scores: Partial<Record<HazardType, [number, number]>> = {};
  const liquid = (s.rainMm ?? 0) + (s.showersMm ?? 0);
  const code = s.weatherCode ?? 0;

  if (s.gustKph !== null) scores.wind = [interpolate(HAZARDS.wind.curve, s.gustKph), s.gustKph];
  if (liquid > 0) scores.rain = [interpolate(HAZARDS.rain.curve, liquid), liquid];
  if (s.snowfallCm) scores.snow = [interpolate(HAZARDS.snow.curve, s.snowfallCm), s.snowfallCm];
  if (s.visibilityM !== null) {
    scores.visibility = [interpolate(HAZARDS.visibility.curve, s.visibilityM), s.visibilityM];
  }
  if (s.temperatureC !== null) scores.cold = [interpolate(HAZARDS.cold.curve, s.temperatureC), s.temperatureC];

  // Rain falling at or below freezing, or WMO freezing drizzle (56, 57) / freezing rain (66, 67).
  let ice = s.temperatureC !== null && s.temperatureC <= 0.5 && liquid > 0 ? interpolate(HAZARDS.ice.curve, liquid) : 0;
  if (code === 56 || code === 57) ice = Math.max(ice, 65);
  if (code === 66 || code === 67) ice = Math.max(ice, 85);
  if (ice > 0) scores.ice = [ice, liquid];

  // WMO 95: thunderstorm, 96/99: thunderstorm with hail.
  if (code >= 95) scores.thunderstorm = [code === 95 ? 60 : code === 96 ? 75 : 85, code];

  return scores;
}

/** Scores a leg from the hourly forecasts of its sample points. */
export function scoreLeg(points: HourlySample[][]): LegRisk {
  const peaks = new Map<HazardType, { score: number; value: number; at: string }>();
  const conditions: LegConditions = {
    maxGustKph: 0,
    minTempC: Number.POSITIVE_INFINITY,
    maxTempC: Number.NEGATIVE_INFINITY,
    precipitationMm: 0,
    snowfallCm: 0,
    minVisibilityM: null,
  };

  for (const hours of points) {
    let precipitation = 0;
    let snowfall = 0;
    for (const sample of hours) {
      for (const [type, [score, value]] of Object.entries(scoreSample(sample)) as [HazardType, [number, number]][]) {
        const peak = peaks.get(type);
        if (!peak || score > peak.score) peaks.set(type, { score, value, at: sample.time });
      }
      conditions.maxGustKph = Math.max(conditions.maxGustKph, sample.gustKph ?? 0);
      if (sample.temperatureC !== null) {
        conditions.minTempC = Math.min(conditions.minTempC, sample.temperatureC);
        conditions.maxTempC = Math.max(conditions.maxTempC, sample.temperatureC);
      }
      if (sample.visibilityM !== null) {
        conditions.minVisibilityM = Math.min(conditions.minVisibilityM ?? Number.POSITIVE_INFINITY, sample.visibilityM);
      }
      precipitation += (sample.rainMm ?? 0) + (sample.showersMm ?? 0);
      snowfall += sample.snowfallCm ?? 0;
    }
    // Report the wettest sample point's totals over the window.
    conditions.precipitationMm = Math.max(conditions.precipitationMm, precipitation);
    conditions.snowfallCm = Math.max(conditions.snowfallCm, snowfall);
  }

  const hazards: Hazard[] = [...peaks]
    .filter(([, peak]) => peak.score >= 30)
    .map(([type, peak]) => ({
      type,
      label: HAZARDS[type].label,
      severity: riskLevel(peak.score) as Hazard['severity'],
      score: Math.round(peak.score),
      peakValue: Math.round(peak.value * 10) / 10,
      unit: HAZARDS[type].unit,
      peakAt: peak.at,
    }))
    .sort((a, b) => b.score - a.score);

  const worst = Math.max(0, ...[...peaks.values()].map((peak) => peak.score));
  const riskScore = Math.min(100, Math.round(worst + 5 * Math.max(0, hazards.length - 1)));

  return {
    riskScore,
    riskLevel: riskLevel(riskScore),
    hazards,
    conditions: {
      maxGustKph: Math.round(conditions.maxGustKph),
      minTempC: Number.isFinite(conditions.minTempC) ? Math.round(conditions.minTempC * 10) / 10 : 0,
      maxTempC: Number.isFinite(conditions.maxTempC) ? Math.round(conditions.maxTempC * 10) / 10 : 0,
      precipitationMm: Math.round(conditions.precipitationMm * 10) / 10,
      snowfallCm: Math.round(conditions.snowfallCm * 10) / 10,
      minVisibilityM: conditions.minVisibilityM === null ? null : Math.round(conditions.minVisibilityM),
    },
  };
}

/**
 * Dispatch decision for a route: reroute when any leg has a severe wind, snow or icing hazard
 * (closure risk), delay when the worst leg is high or severe for other reasons (it should pass),
 * otherwise go.
 */
export function recommend(legs: Pick<LegRisk, 'riskScore' | 'hazards'>[]): Recommendation {
  if (legs.some((leg) => leg.hazards.some((h) => h.severity === 'severe' && ROAD_CLOSING.has(h.type)))) {
    return 'reroute';
  }
  return Math.max(0, ...legs.map((leg) => leg.riskScore)) >= 60 ? 'delay' : 'go';
}
