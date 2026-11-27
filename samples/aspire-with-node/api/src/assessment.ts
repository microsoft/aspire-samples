/**
 * Route assessment: forecast every leg, score it, and decide go / delay / reroute.
 *
 * Trace shape (inside the incoming HTTP request span):
 *   assess route                        fleetweather.route.*, event dispatch.decided
 *   └─ assess leg (one per leg)         fleetweather.leg.*, events forecast.received, hazard.detected
 *      └─ GET (Open-Meteo, from the undici instrumentation; absent when the forecast is cached)
 */
import { SpanStatusCode, type Span } from '@opentelemetry/api';
import { distanceKm, legSamplePoints, routeDistanceKm, type Waypoint } from './fleet.ts';
import { logger } from './logger.ts';
import { fetchHourlyForecasts } from './openMeteo.ts';
import { isRoadClosing, recommend, riskLevel, scoreLeg, type Hazard, type LegRisk, type Recommendation, type RiskLevel } from './risk.ts';
import { activeAssessments, hazardsDetected, legRiskScore, routeAssessments, tracer } from './telemetry.ts';

export type RouteSource = 'predefined' | 'custom';

export interface RouteInput {
  id: string | null;
  name: string;
  corridor: string | null;
  waypoints: Waypoint[];
}

export interface LegAssessment extends LegRisk {
  index: number;
  from: string;
  to: string;
  distanceKm: number;
  path: [number, number][];
}

export interface RouteAssessment {
  routeId: string | null;
  name: string;
  corridor: string | null;
  source: RouteSource;
  windowHours: number;
  assessedAt: string;
  distanceKm: number;
  recommendation: Recommendation;
  overallRiskScore: number;
  riskLevel: RiskLevel;
  summary: string;
  legs: LegAssessment[];
}

async function inSpan<T>(name: string, attributes: Record<string, string | number>, fn: (span: Span) => Promise<T>) {
  return tracer.startActiveSpan(name, { attributes }, async (span) => {
    try {
      return await fn(span);
    } catch (err) {
      span.recordException(err as Error);
      span.setStatus({ code: SpanStatusCode.ERROR, message: (err as Error).message });
      throw err;
    } finally {
      span.end();
    }
  });
}

interface LegInput {
  index: number;
  from: Waypoint;
  to: Waypoint;
  windowHours: number;
  routeKey: string;
  fresh: boolean;
}

function assessLeg({ index, from, to, windowHours, routeKey, fresh }: LegInput) {
  return inSpan(
    'assess leg',
    {
      'fleetweather.leg.index': index,
      'fleetweather.leg.from': from.name,
      'fleetweather.leg.to': to.name,
      'fleetweather.leg.distance_km': Math.round(distanceKm(from, to)),
    },
    async (span): Promise<LegAssessment> => {
      const path = legSamplePoints(from, to);
      const { forecasts, cached } = await fetchHourlyForecasts(path, windowHours, fresh);
      span.setAttribute('fleetweather.forecast.cached', cached);
      span.addEvent('forecast.received', { 'forecast.source': cached ? 'cache' : 'open-meteo' });

      const risk = scoreLeg(forecasts);
      span.setAttributes({
        'fleetweather.leg.risk_score': risk.riskScore,
        'fleetweather.leg.risk_level': risk.riskLevel,
        'fleetweather.leg.max_gust_kph': risk.conditions.maxGustKph,
        'fleetweather.leg.min_temp_c': risk.conditions.minTempC,
      });
      legRiskScore.record(risk.riskScore, { 'fleetweather.risk_level': risk.riskLevel });

      for (const hazard of risk.hazards) {
        span.addEvent('hazard.detected', {
          'hazard.type': hazard.type,
          'hazard.severity': hazard.severity,
          'hazard.score': hazard.score,
          'hazard.peak_value': hazard.peakValue,
          'hazard.unit': hazard.unit,
          'hazard.peak_at': hazard.peakAt,
        });
        hazardsDetected.add(1, { 'fleetweather.hazard.type': hazard.type, 'fleetweather.hazard.severity': hazard.severity });
      }

      if (risk.riskLevel === 'high' || risk.riskLevel === 'severe') {
        logger.warn(
          {
            routeId: routeKey,
            legIndex: index,
            leg: `${from.name} → ${to.name}`,
            riskScore: risk.riskScore,
            riskLevel: risk.riskLevel,
            hazards: risk.hazards.map((h) => h.type),
            maxGustKph: risk.conditions.maxGustKph,
          },
          `${risk.riskLevel === 'severe' ? 'Severe' : 'High'} weather risk on ${routeKey} leg ${from.name} → ${to.name}: ${risk.hazards.map((h) => h.label.toLowerCase()).join(', ')}`,
        );
      }

      return { index, from: from.name, to: to.name, distanceKm: Math.round(distanceKm(from, to)), path, ...risk };
    },
  );
}

function describeHazard(hazard: Hazard): string {
  if (hazard.type === 'thunderstorm') return hazard.peakValue === 95 ? 'thunderstorms' : 'thunderstorms with hail';
  const value = hazard.peakValue >= 10 ? Math.round(hazard.peakValue) : hazard.peakValue;
  return `${hazard.label.toLowerCase()} (${value} ${hazard.unit})`;
}

/** The leg and hazard behind the recommendation: for a reroute, the strongest road-closing hazard; otherwise the worst leg's top hazard. */
function explain(recommendation: Recommendation, legs: LegAssessment[]): { leg: LegAssessment; hazard: Hazard | undefined } {
  if (recommendation === 'reroute') {
    let cause: { leg: LegAssessment; hazard: Hazard } | undefined;
    for (const leg of legs) {
      for (const hazard of leg.hazards) {
        if (isRoadClosing(hazard) && (!cause || hazard.score > cause.hazard.score)) cause = { leg, hazard };
      }
    }
    if (cause) return cause;
  }
  const worst = legs.reduce((a, b) => (b.riskScore > a.riskScore ? b : a));
  return { leg: worst, hazard: worst.hazards[0] };
}

function summarize(recommendation: Recommendation, legs: LegAssessment[]): string {
  const { leg, hazard } = explain(recommendation, legs);
  const where = `${leg.from} → ${leg.to}`;
  const detail = hazard ? describeHazard(hazard) : 'no significant hazards';
  switch (recommendation) {
    case 'reroute':
      return `Reroute: ${detail} on ${where}.`;
    case 'delay':
      return `Delay departure: ${detail} on ${where} should pass.`;
    default:
      return hazard ? `Clear to dispatch: worst leg ${where} has ${detail}.` : 'Clear to dispatch: no significant hazards.';
  }
}

/** Assesses every leg in parallel. `fresh` bypasses the forecast cache (see openMeteo.ts). */
export function assessRoute(
  route: RouteInput,
  windowHours: number,
  source: RouteSource,
  fresh = false,
): Promise<RouteAssessment> {
  const routeKey = route.id ?? 'custom';
  return inSpan(
    'assess route',
    {
      'fleetweather.route.id': routeKey,
      'fleetweather.route.name': route.name,
      'fleetweather.route.source': source,
      'fleetweather.route.leg_count': route.waypoints.length - 1,
      'fleetweather.route.distance_km': routeDistanceKm(route.waypoints),
      'fleetweather.window_hours': windowHours,
    },
    async (span) => {
      activeAssessments.add(1, { 'fleetweather.route.source': source });
      try {
        const legs = await Promise.all(
          route.waypoints
            .slice(1)
            .map((to, i) => assessLeg({ index: i, from: route.waypoints[i], to, windowHours, routeKey, fresh })),
        );

        const overallRiskScore = Math.max(...legs.map((leg) => leg.riskScore));
        const recommendation = recommend(legs);
        const summary = summarize(recommendation, legs);
        span.setAttributes({
          'fleetweather.route.risk_score': overallRiskScore,
          'fleetweather.recommendation': recommendation,
        });
        span.addEvent('dispatch.decided', {
          'dispatch.recommendation': recommendation,
          'dispatch.risk_score': overallRiskScore,
          'dispatch.summary': summary,
        });
        routeAssessments.add(1, { 'fleetweather.recommendation': recommendation, 'fleetweather.route.source': source });
        logger.info(
          {
            routeId: routeKey,
            routeName: route.name,
            recommendation,
            overallRiskScore,
            windowHours,
            hazards: [...new Set(legs.flatMap((leg) => leg.hazards.map((h) => h.type)))],
          },
          `Route ${routeKey} assessed: ${recommendation} (risk ${overallRiskScore})`,
        );

        return {
          routeId: route.id,
          name: route.name,
          corridor: route.corridor,
          source,
          windowHours,
          assessedAt: new Date().toISOString(),
          distanceKm: routeDistanceKm(route.waypoints),
          recommendation,
          overallRiskScore,
          riskLevel: riskLevel(overallRiskScore),
          summary,
          legs,
        };
      } finally {
        activeAssessments.add(-1, { 'fleetweather.route.source': source });
      }
    },
  );
}
