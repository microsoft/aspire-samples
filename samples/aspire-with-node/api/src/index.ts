/**
 * Fleet Weather API: scores delivery routes for weather risk and recommends go / delay / reroute.
 *
 * Express 5 runs on tsx; instrumentation.ts is preloaded through NODE_OPTIONS (see apphost.mts).
 */
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { trace } from '@opentelemetry/api';
import { apiReference } from '@scalar/express-api-reference';
import express, { type ErrorRequestHandler, type Request, type Response } from 'express';
import { assessRoute, type RouteInput } from './assessment.ts';
import { FLEET_ROUTES, findRoute, routeDistanceKm, type Waypoint } from './fleet.ts';
import { logger } from './logger.ts';
import { UpstreamError } from './openMeteo.ts';
import { openApiDocument } from './openapi.ts';

const app = express();
const port = Number(process.env.PORT ?? 5000);

const DEFAULT_WINDOW_HOURS = 24;
const MAX_WINDOW_HOURS = 48;
const MAX_WAYPOINTS = 10;

app.disable('x-powered-by');
app.use(express.json({ limit: '16kb' }));

type ValidationErrors = Record<string, string[]>;

const PROBLEM_TYPES: Record<number, string> = {
  400: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  404: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
  500: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
  502: 'https://tools.ietf.org/html/rfc9110#section-15.6.3',
};

/** Writes an RFC 9457 problem details response that carries the trace id for the dashboard. */
function problem(req: Request, res: Response, status: number, title: string, detail: string, errors?: ValidationErrors) {
  res
    .status(status)
    .type('application/problem+json')
    .json({
      type: PROBLEM_TYPES[status] ?? 'about:blank',
      title,
      status,
      detail,
      instance: req.originalUrl,
      traceId: trace.getActiveSpan()?.spanContext().traceId,
      ...(errors && { errors }),
    });
}

function parseWindowHours(value: unknown, errors: ValidationErrors): number {
  if (value === undefined) return DEFAULT_WINDOW_HOURS;
  const hours = Number(value);
  if (!Number.isInteger(hours) || hours < 1 || hours > MAX_WINDOW_HOURS) {
    errors.windowHours = [`windowHours must be a whole number of hours from 1 to ${MAX_WINDOW_HOURS}.`];
  }
  return hours;
}

/** `Cache-Control: no-cache` asks for a fresh forecast instead of the 10-minute cache. */
const wantsFresh = (req: Request) => /\bno-cache\b/i.test(req.get('cache-control') ?? '');

function badRequest(req: Request, res: Response, errors: ValidationErrors) {
  problem(req, res, 400, 'One or more validation errors occurred.', 'See errors for the invalid fields.', errors);
}

/** Validates a custom route body: an optional name, 2-10 waypoints and an optional window. */
function parseCustomRoute(body: unknown, errors: ValidationErrors): RouteInput {
  const input = (typeof body === 'object' && body !== null ? body : {}) as Record<string, unknown>;
  const name = typeof input.name === 'string' && input.name.trim() ? input.name.trim() : 'Custom route';
  if (name.length > 80) errors.name = ['name must be 80 characters or fewer.'];

  const raw = input.waypoints;
  const waypoints: Waypoint[] = [];
  if (!Array.isArray(raw) || raw.length < 2 || raw.length > MAX_WAYPOINTS) {
    errors.waypoints = [`Provide between 2 and ${MAX_WAYPOINTS} waypoints.`];
  } else {
    raw.forEach((item: unknown, i) => {
      const point = (typeof item === 'object' && item !== null ? item : {}) as Record<string, unknown>;
      const { lat, lon } = point;
      if (typeof lat !== 'number' || lat < -90 || lat > 90) errors[`waypoints[${i}].lat`] = ['lat must be a number from -90 to 90.'];
      if (typeof lon !== 'number' || lon < -180 || lon > 180) errors[`waypoints[${i}].lon`] = ['lon must be a number from -180 to 180.'];
      const label = typeof point.name === 'string' && point.name.trim() ? point.name.trim().slice(0, 60) : `Stop ${i + 1}`;
      waypoints.push({ name: label, lat: Number(lat), lon: Number(lon) });
    });
  }
  return { id: null, name, corridor: null, waypoints };
}

app.get('/api/routes', (_req, res) => {
  res.json(
    FLEET_ROUTES.map((route) => ({
      ...route,
      legCount: route.waypoints.length - 1,
      distanceKm: routeDistanceKm(route.waypoints),
    })),
  );
});

app.get('/api/routes/:routeId/assessment', async (req, res) => {
  const route = findRoute(req.params.routeId);
  if (!route) {
    problem(req, res, 404, 'Route not found', `No predefined route has the id '${req.params.routeId}'. GET /api/routes lists them.`);
    return;
  }
  const errors: ValidationErrors = {};
  const windowHours = parseWindowHours(req.query.windowHours, errors);
  if (Object.keys(errors).length) {
    badRequest(req, res, errors);
    return;
  }
  res.json(await assessRoute(route, windowHours, 'predefined', wantsFresh(req)));
});

// Also exposed as the "Assess all routes" dashboard command (see apphost.mts).
app.post('/api/routes/assessments', async (req, res) => {
  const errors: ValidationErrors = {};
  const windowHours = parseWindowHours(req.query.windowHours, errors);
  if (Object.keys(errors).length) {
    badRequest(req, res, errors);
    return;
  }
  const fresh = wantsFresh(req);
  const assessments = await Promise.all(FLEET_ROUTES.map((route) => assessRoute(route, windowHours, 'predefined', fresh)));
  const totals = { go: 0, delay: 0, reroute: 0 };
  for (const assessment of assessments) totals[assessment.recommendation]++;
  logger.info(
    { windowHours, ...totals },
    `Fleet assessed: ${totals.go} go, ${totals.delay} delay, ${totals.reroute} reroute`,
  );
  res.json({
    windowHours,
    assessedAt: new Date().toISOString(),
    totals,
    routes: assessments.map(({ routeId, name, recommendation, overallRiskScore, riskLevel, summary }) => ({
      routeId,
      name,
      recommendation,
      overallRiskScore,
      riskLevel,
      summary,
    })),
  });
});

app.post('/api/assessments', async (req, res) => {
  const errors: ValidationErrors = {};
  const route = parseCustomRoute(req.body, errors);
  const windowHours = parseWindowHours((req.body as Record<string, unknown> | undefined)?.windowHours, errors);
  if (Object.keys(errors).length) {
    badRequest(req, res, errors);
    return;
  }
  res.json(await assessRoute(route, windowHours, 'custom', wantsFresh(req)));
});

app.use('/api', (req, res) => {
  problem(req, res, 404, 'Not found', `${req.method} ${req.originalUrl} is not an API operation. See /scalar.`);
});

app.get('/health', (_req, res) => {
  res.type('text/plain').send('Healthy');
});

app.get('/openapi.json', (_req, res) => {
  res.json(openApiDocument);
});

app.use(
  '/scalar',
  apiReference({
    content: openApiDocument,
    theme: 'purple',
    _integration: 'express',
    metaData: { title: 'Fleet Weather API reference' },
  }),
);
app.get('/reference', (_req, res) => {
  res.redirect(301, '/scalar');
});

// In publish mode the frontend build is copied into ./static (publishWithContainerFiles in apphost.mts).
const staticDir = join(import.meta.dirname, '..', 'static');
if (existsSync(staticDir)) {
  app.use(express.static(staticDir));
} else {
  app.get('/', (_req, res) => {
    res.redirect('/scalar');
  });
}

const errorHandler: ErrorRequestHandler = (err, req, res, _next) => {
  if (err instanceof UpstreamError) {
    logger.error({ error: err.message }, 'Forecast provider unavailable');
    problem(req, res, 502, 'Forecast provider unavailable', 'Open-Meteo could not be reached or returned an error. Try again shortly.');
    return;
  }
  // Body parser errors (malformed JSON, oversized body) carry a 4xx status.
  if (typeof err?.status === 'number' && err.status >= 400 && err.status < 500) {
    badRequest(req, res, { body: [err.type === 'entity.parse.failed' ? 'The request body is not valid JSON.' : String(err.message)] });
    return;
  }
  logger.error({ error: (err as Error)?.message }, 'Unhandled error');
  problem(req, res, 500, 'Unexpected error', 'The server failed to process the request.');
};
app.use(errorHandler);

app.listen(port, () => {
  logger.info({ port }, `Fleet Weather API listening on port ${port}`);
});
