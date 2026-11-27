/**
 * OpenAPI 3.1 description of the Fleet Weather API, served raw at `/openapi.json` and rendered
 * by Scalar at `/scalar`. Keep it in sync with the handlers in index.ts and the types in
 * assessment.ts and risk.ts.
 */
import { FLEET_ROUTES } from './fleet.ts';

const ref = (name: string) => ({ $ref: `#/components/schemas/${name}` });
const json = (schema: object, example?: unknown) => ({
  'application/json': { schema, ...(example !== undefined && { example }) },
});
const problemJson = (example: object) => ({ 'application/problem+json': { schema: ref('ProblemDetails'), example } });

const riskLevels = ['low', 'moderate', 'high', 'severe'];
const recommendations = ['go', 'delay', 'reroute'];
const hazardTypes = ['wind', 'rain', 'snow', 'ice', 'visibility', 'thunderstorm', 'cold'];

const exampleTraceId = '4bf92f3577b34da6a3ce929d0e0e4736';

const exampleAssessment = {
  routeId: 'den-gjt',
  name: 'Denver → Grand Junction',
  corridor: 'I-70',
  source: 'predefined',
  windowHours: 24,
  assessedAt: '2026-11-27T04:12:09.512Z',
  distanceKm: 324,
  recommendation: 'reroute',
  overallRiskScore: 96,
  riskLevel: 'severe',
  summary: 'Reroute: high wind (101 km/h) on Idaho Springs → Eisenhower Tunnel.',
  legs: [
    {
      index: 0,
      from: 'Denver',
      to: 'Idaho Springs',
      distanceKm: 45,
      riskScore: 41,
      riskLevel: 'moderate',
      hazards: [
        { type: 'wind', label: 'High wind', severity: 'moderate', score: 41, peakValue: 57.6, unit: 'km/h', peakAt: '2026-11-27T13:00Z' },
      ],
      conditions: { maxGustKph: 58, minTempC: -6.1, maxTempC: 4.2, precipitationMm: 0, snowfallCm: 0.4, minVisibilityM: 18400 },
      path: [[39.7392, -104.9903], [39.7409, -105.252], [39.7425, -105.5136]],
    },
    {
      index: 1,
      from: 'Idaho Springs',
      to: 'Eisenhower Tunnel',
      distanceKm: 36,
      riskScore: 96,
      riskLevel: 'severe',
      hazards: [
        { type: 'wind', label: 'High wind', severity: 'severe', score: 86, peakValue: 100.8, unit: 'km/h', peakAt: '2026-11-27T15:00Z' },
        { type: 'visibility', label: 'Low visibility', severity: 'high', score: 73, peakValue: 300, unit: 'm', peakAt: '2026-11-27T19:00Z' },
        { type: 'snow', label: 'Snow', severity: 'moderate', score: 47, peakValue: 1.1, unit: 'cm/h', peakAt: '2026-11-27T19:00Z' },
      ],
      conditions: { maxGustKph: 101, minTempC: -12.6, maxTempC: -0.9, precipitationMm: 6.4, snowfallCm: 9.1, minVisibilityM: 300 },
      path: [[39.7425, -105.5136], [39.7111, -105.7213], [39.6797, -105.9289]],
    },
  ],
};

export const openApiDocument = {
  openapi: '3.1.0',
  info: {
    title: 'Fleet Weather API',
    version: '1.0.0',
    summary: 'Weather risk scoring for delivery routes.',
    description: [
      'Scores each leg of a delivery route for weather risk over the next 1-48 hours and recommends whether to **go**, **delay** or **reroute**.',
      '',
      'Each leg is sampled at its start, midpoint and end with one multi-coordinate [Open-Meteo](https://open-meteo.com) forecast request (hourly gusts, rain, showers, snowfall, visibility, temperature and WMO weather code).',
      '',
      '| Hazard | Moderate | High | Severe |',
      '| --- | --- | --- | --- |',
      '| Wind gusts | 50 km/h | 70 km/h | 90 km/h |',
      '| Heavy rain | 4 mm/h | 8 mm/h | 16 mm/h |',
      '| Snowfall | 0.5 cm/h | 1.5 cm/h | 3 cm/h |',
      '| Icing | rain at or below 0.5 °C | freezing drizzle | freezing rain |',
      '| Low visibility | 2,000 m | 500 m | 200 m |',
      '| Thunderstorm | | thunder | thunder with hail |',
      '| Extreme cold | -20 °C | -30 °C | -40 °C |',
      '',
      'A leg scores 0-100: its worst hazard plus 5 for each additional hazard. Routes are **rerouted** when any leg has a severe wind, snow or icing hazard, **delayed** when the worst leg scores 60 or more, and otherwise cleared to **go**.',
      '',
      'Forecasts are cached for 10 minutes per leg and window, which keeps a full fleet assessment well inside the Open-Meteo free tier. Send `Cache-Control: no-cache` to re-check with fresh forecasts.',
      '',
      'Errors use [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details with a `traceId` you can paste into the Aspire dashboard.',
    ].join('\n'),
    license: { name: 'MIT', identifier: 'MIT' },
  },
  servers: [{ url: '/', description: 'This API instance' }],
  tags: [
    { name: 'Routes', description: "The fleet's predefined linehaul routes." },
    { name: 'Assessments', description: 'Weather risk assessments with a go / delay / reroute recommendation.' },
    { name: 'System', description: 'Operational endpoints.' },
  ],
  paths: {
    '/api/routes': {
      get: {
        tags: ['Routes'],
        operationId: 'listRoutes',
        summary: 'List fleet routes',
        description: 'Returns the predefined routes with their waypoints, leg count and great-circle distance.',
        responses: {
          '200': {
            description: 'The fleet routes.',
            content: json({ type: 'array', items: ref('FleetRoute') }, [
              {
                ...FLEET_ROUTES[0],
                legCount: FLEET_ROUTES[0].waypoints.length - 1,
                distanceKm: 254,
              },
            ]),
          },
        },
      },
    },
    '/api/routes/{routeId}/assessment': {
      get: {
        tags: ['Assessments'],
        operationId: 'assessRoute',
        summary: 'Assess a predefined route',
        description:
          'Forecasts every leg of the route over the requested window, scores each leg and returns the dispatch recommendation. Each leg lists its hazards (peak value and time) and summary conditions.',
        parameters: [
          {
            name: 'routeId',
            in: 'path',
            required: true,
            description: 'Route id from `GET /api/routes`.',
            schema: { type: 'string', enum: FLEET_ROUTES.map((route) => route.id) },
            example: 'den-gjt',
          },
          { $ref: '#/components/parameters/windowHours' },
          { $ref: '#/components/parameters/CacheControl' },
        ],
        responses: {
          '200': { description: 'The route assessment.', content: json(ref('RouteAssessment'), exampleAssessment) },
          '400': {
            description: '`windowHours` is not a whole number from 1 to 48.',
            content: problemJson({
              type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
              title: 'One or more validation errors occurred.',
              status: 400,
              detail: 'See errors for the invalid fields.',
              instance: '/api/routes/den-gjt/assessment?windowHours=72',
              traceId: exampleTraceId,
              errors: { windowHours: ['windowHours must be a whole number of hours from 1 to 48.'] },
            }),
          },
          '404': {
            description: 'No predefined route has this id.',
            content: problemJson({
              type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
              title: 'Route not found',
              status: 404,
              detail: "No predefined route has the id 'den-lax'. GET /api/routes lists them.",
              instance: '/api/routes/den-lax/assessment',
              traceId: exampleTraceId,
            }),
          },
          '502': { $ref: '#/components/responses/UpstreamUnavailable' },
        },
      },
    },
    '/api/routes/assessments': {
      post: {
        tags: ['Assessments'],
        operationId: 'assessAllRoutes',
        summary: 'Assess all routes',
        description:
          'Assesses every predefined route in parallel and returns one summary line per route plus totals. The Aspire dashboard runs this as the **Assess all routes** command on the `api` resource.',
        parameters: [{ $ref: '#/components/parameters/windowHours' }, { $ref: '#/components/parameters/CacheControl' }],
        responses: {
          '200': {
            description: 'A fleet-wide summary.',
            content: json(ref('FleetAssessment'), {
              windowHours: 24,
              assessedAt: '2026-11-27T04:12:11.087Z',
              totals: { go: 1, delay: 2, reroute: 2 },
              routes: [
                {
                  routeId: 'den-gjt',
                  name: 'Denver → Grand Junction',
                  recommendation: 'reroute',
                  overallRiskScore: 96,
                  riskLevel: 'severe',
                  summary: 'Reroute: high wind (101 km/h) on Idaho Springs → Eisenhower Tunnel.',
                },
                {
                  routeId: 'dfw-hou',
                  name: 'Dallas → Houston',
                  recommendation: 'go',
                  overallRiskScore: 33,
                  riskLevel: 'moderate',
                  summary: 'Clear to dispatch: worst leg Huntsville → Houston has high wind (52.9 km/h).',
                },
              ],
            }),
          },
          '400': { $ref: '#/components/responses/ValidationProblem' },
          '502': { $ref: '#/components/responses/UpstreamUnavailable' },
        },
      },
    },
    '/api/assessments': {
      post: {
        tags: ['Assessments'],
        operationId: 'assessCustomRoute',
        summary: 'Assess a custom route',
        description:
          'Assesses an ad hoc route through 2-10 waypoints, in order. Waypoints without a name are labeled `Stop 1`, `Stop 2` and so on.',
        parameters: [{ $ref: '#/components/parameters/CacheControl' }],
        requestBody: {
          required: true,
          content: json(ref('CustomRouteRequest'), {
            name: 'Cheyenne → Rock Springs',
            windowHours: 12,
            waypoints: [
              { name: 'Cheyenne', lat: 41.14, lon: -104.8202 },
              { name: 'Laramie', lat: 41.3114, lon: -105.5911 },
              { name: 'Rawlins', lat: 41.7911, lon: -107.2387 },
              { name: 'Rock Springs', lat: 41.5875, lon: -109.2029 },
            ],
          }),
        },
        responses: {
          '200': {
            description: 'The custom route assessment (`routeId` is null and `source` is `custom`).',
            content: json(ref('RouteAssessment')),
          },
          '400': {
            description: 'The body is not valid JSON, or a field is missing or out of range.',
            content: problemJson({
              type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
              title: 'One or more validation errors occurred.',
              status: 400,
              detail: 'See errors for the invalid fields.',
              instance: '/api/assessments',
              traceId: exampleTraceId,
              errors: { waypoints: ['Provide between 2 and 10 waypoints.'] },
            }),
          },
          '502': { $ref: '#/components/responses/UpstreamUnavailable' },
        },
      },
    },
    '/health': {
      get: {
        tags: ['System'],
        operationId: 'health',
        summary: 'Liveness probe',
        description: 'Returns `Healthy` when the service is up. The AppHost uses it as the resource health check.',
        responses: {
          '200': { description: 'The service is healthy.', content: { 'text/plain': { schema: { type: 'string' }, example: 'Healthy' } } },
        },
      },
    },
  },
  components: {
    parameters: {
      CacheControl: {
        name: 'Cache-Control',
        in: 'header',
        required: false,
        description: 'Send `no-cache` to fetch fresh forecasts from Open-Meteo instead of the 10-minute forecast cache.',
        schema: { type: 'string', enum: ['no-cache'] },
      },
      windowHours: {
        name: 'windowHours',
        in: 'query',
        required: false,
        description: 'Forecast window in hours, starting at the current hour.',
        schema: { type: 'integer', minimum: 1, maximum: 48, default: 24 },
        example: 24,
      },
    },
    responses: {
      ValidationProblem: {
        description: '`windowHours` is not a whole number from 1 to 48.',
        content: problemJson({
          type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
          title: 'One or more validation errors occurred.',
          status: 400,
          detail: 'See errors for the invalid fields.',
          instance: '/api/routes/assessments?windowHours=0',
          traceId: exampleTraceId,
          errors: { windowHours: ['windowHours must be a whole number of hours from 1 to 48.'] },
        }),
      },
      UpstreamUnavailable: {
        description: 'Open-Meteo could not be reached, timed out or returned an error.',
        content: problemJson({
          type: 'https://tools.ietf.org/html/rfc9110#section-15.6.3',
          title: 'Forecast provider unavailable',
          status: 502,
          detail: 'Open-Meteo could not be reached or returned an error. Try again shortly.',
          instance: '/api/routes/den-slc/assessment',
          traceId: exampleTraceId,
        }),
      },
    },
    schemas: {
      Waypoint: {
        type: 'object',
        description: 'A named stop, junction or mountain pass on a route.',
        properties: {
          name: { type: 'string', maxLength: 60, examples: ['Eisenhower Tunnel'] },
          lat: { type: 'number', minimum: -90, maximum: 90, description: 'Latitude in decimal degrees.', examples: [39.6797] },
          lon: { type: 'number', minimum: -180, maximum: 180, description: 'Longitude in decimal degrees.', examples: [-105.9289] },
        },
        required: ['lat', 'lon'],
      },
      FleetRoute: {
        type: 'object',
        properties: {
          id: { type: 'string', examples: ['den-gjt'] },
          name: { type: 'string', examples: ['Denver → Grand Junction'] },
          region: { type: 'string', examples: ['Colorado Rockies'] },
          corridor: { type: 'string', description: 'Highways the route follows.', examples: ['I-70'] },
          legCount: { type: 'integer', minimum: 1 },
          distanceKm: { type: 'integer', description: 'Great-circle distance through the waypoints, in kilometers.' },
          waypoints: { type: 'array', items: ref('Waypoint'), minItems: 2 },
        },
        required: ['id', 'name', 'region', 'corridor', 'legCount', 'distanceKm', 'waypoints'],
      },
      Hazard: {
        type: 'object',
        description: 'The worst hour of one hazard type on a leg.',
        properties: {
          type: { type: 'string', enum: hazardTypes },
          label: { type: 'string', examples: ['High wind'] },
          severity: { type: 'string', enum: riskLevels.slice(1) },
          score: { type: 'integer', minimum: 30, maximum: 100 },
          peakValue: { type: 'number', description: 'Worst observed value, in `unit`.', examples: [100.8] },
          unit: { type: 'string', examples: ['km/h'] },
          peakAt: { type: 'string', format: 'date-time', description: 'Forecast hour (UTC) of the peak.' },
        },
        required: ['type', 'label', 'severity', 'score', 'peakValue', 'unit', 'peakAt'],
      },
      LegConditions: {
        type: 'object',
        description: 'Summary of the forecast across the leg and window.',
        properties: {
          maxGustKph: { type: 'integer' },
          minTempC: { type: 'number' },
          maxTempC: { type: 'number' },
          precipitationMm: { type: 'number', description: 'Rain and showers at the wettest sample point.' },
          snowfallCm: { type: 'number', description: 'Snowfall at the snowiest sample point.' },
          minVisibilityM: { type: ['integer', 'null'] },
        },
        required: ['maxGustKph', 'minTempC', 'maxTempC', 'precipitationMm', 'snowfallCm', 'minVisibilityM'],
      },
      LegAssessment: {
        type: 'object',
        properties: {
          index: { type: 'integer', minimum: 0 },
          from: { type: 'string' },
          to: { type: 'string' },
          distanceKm: { type: 'integer' },
          riskScore: { type: 'integer', minimum: 0, maximum: 100 },
          riskLevel: { type: 'string', enum: riskLevels },
          hazards: { type: 'array', items: ref('Hazard'), description: 'Hazards of at least moderate severity, worst first.' },
          conditions: ref('LegConditions'),
          path: {
            type: 'array',
            description: 'Forecast sample points as `[lat, lon]`: start, midpoint and end.',
            items: { type: 'array', items: { type: 'number' }, minItems: 2, maxItems: 2 },
          },
        },
        required: ['index', 'from', 'to', 'distanceKm', 'riskScore', 'riskLevel', 'hazards', 'conditions', 'path'],
      },
      RouteAssessment: {
        type: 'object',
        properties: {
          routeId: { type: ['string', 'null'], description: 'Null for custom routes.' },
          name: { type: 'string' },
          corridor: { type: ['string', 'null'] },
          source: { type: 'string', enum: ['predefined', 'custom'] },
          windowHours: { type: 'integer', minimum: 1, maximum: 48 },
          assessedAt: { type: 'string', format: 'date-time' },
          distanceKm: { type: 'integer' },
          recommendation: { type: 'string', enum: recommendations },
          overallRiskScore: { type: 'integer', minimum: 0, maximum: 100, description: 'The worst leg score.' },
          riskLevel: { type: 'string', enum: riskLevels },
          summary: { type: 'string', description: 'One-line explanation of the recommendation.' },
          legs: { type: 'array', items: ref('LegAssessment') },
        },
        required: [
          'routeId', 'name', 'corridor', 'source', 'windowHours', 'assessedAt', 'distanceKm',
          'recommendation', 'overallRiskScore', 'riskLevel', 'summary', 'legs',
        ],
      },
      RouteSummary: {
        type: 'object',
        properties: {
          routeId: { type: 'string' },
          name: { type: 'string' },
          recommendation: { type: 'string', enum: recommendations },
          overallRiskScore: { type: 'integer', minimum: 0, maximum: 100 },
          riskLevel: { type: 'string', enum: riskLevels },
          summary: { type: 'string' },
        },
        required: ['routeId', 'name', 'recommendation', 'overallRiskScore', 'riskLevel', 'summary'],
      },
      FleetAssessment: {
        type: 'object',
        properties: {
          windowHours: { type: 'integer' },
          assessedAt: { type: 'string', format: 'date-time' },
          totals: {
            type: 'object',
            description: 'Number of routes per recommendation.',
            properties: { go: { type: 'integer' }, delay: { type: 'integer' }, reroute: { type: 'integer' } },
            required: ['go', 'delay', 'reroute'],
          },
          routes: { type: 'array', items: ref('RouteSummary') },
        },
        required: ['windowHours', 'assessedAt', 'totals', 'routes'],
      },
      CustomRouteRequest: {
        type: 'object',
        properties: {
          name: { type: 'string', maxLength: 80, default: 'Custom route' },
          windowHours: { type: 'integer', minimum: 1, maximum: 48, default: 24 },
          waypoints: { type: 'array', items: ref('Waypoint'), minItems: 2, maxItems: 10 },
        },
        required: ['waypoints'],
      },
      ProblemDetails: {
        type: 'object',
        description: 'RFC 9457 problem details.',
        properties: {
          type: { type: 'string', format: 'uri' },
          title: { type: 'string' },
          status: { type: 'integer' },
          detail: { type: 'string' },
          instance: { type: 'string' },
          traceId: { type: 'string', description: 'W3C trace id of the failed request; search for it in the Aspire dashboard.' },
          errors: {
            type: 'object',
            description: 'Validation messages keyed by field.',
            additionalProperties: { type: 'array', items: { type: 'string' } },
          },
        },
        required: ['type', 'title', 'status'],
      },
    },
  },
};
