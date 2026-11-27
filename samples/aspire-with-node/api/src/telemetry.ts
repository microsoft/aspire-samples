/**
 * Domain telemetry for the Fleet Weather API: one tracer and one meter named `FleetWeather.Api`.
 * The global providers are registered by instrumentation.ts; without it these are no-ops.
 *
 * Metric attributes are kept to small, fixed value sets (recommendation, risk level, hazard
 * type, route source, outcome). Route and leg details go on spans and logs instead.
 */
import { metrics, trace } from '@opentelemetry/api';

const name = 'FleetWeather.Api';
const version = '1.0.0';

export const tracer = trace.getTracer(name, version);
const meter = metrics.getMeter(name, version);

export const routeAssessments = meter.createCounter('fleetweather.route.assessments', {
  description: 'Route assessments completed, by dispatch recommendation and route source.',
  unit: '{assessment}',
});

export const activeAssessments = meter.createUpDownCounter('fleetweather.route.assessments.active', {
  description: 'Route assessments currently in flight.',
  unit: '{assessment}',
});

export const legRiskScore = meter.createHistogram('fleetweather.leg.risk_score', {
  description: 'Weather risk score (0-100) of each assessed route leg, by risk level.',
  unit: '{score}',
  advice: { explicitBucketBoundaries: [10, 20, 30, 40, 50, 60, 70, 80, 90, 100] },
});

export const hazardsDetected = meter.createCounter('fleetweather.hazards.detected', {
  description: 'Weather hazards detected on route legs, by hazard type and severity.',
  unit: '{hazard}',
});

export const forecastDuration = meter.createHistogram('fleetweather.open_meteo.request.duration', {
  description: 'Duration of Open-Meteo forecast requests, including retries, by outcome.',
  unit: 's',
  advice: { explicitBucketBoundaries: [0.05, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 10] },
});
