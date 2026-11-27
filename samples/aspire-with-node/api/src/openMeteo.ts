/** Open-Meteo forecast client: one multi-coordinate request returns hourly forecasts for every sample point. */
import { request as httpRequest } from 'node:http';
import { request as httpsRequest } from 'node:https';
import { setTimeout as sleep } from 'node:timers/promises';
import { trace } from '@opentelemetry/api';
import { forecastDuration } from './telemetry.ts';
import type { HourlySample } from './risk.ts';

// Injected by the AppHost, which models Open-Meteo as an external service.
const OPEN_METEO_URL = (process.env.OPEN_METEO_URL || 'https://api.open-meteo.com').replace(/\/+$/, '');

const HOURLY_VARIABLES = [
  'temperature_2m',
  'rain',
  'showers',
  'snowfall',
  'wind_gusts_10m',
  'visibility',
  'weather_code',
] as const;

type HourlyVariable = (typeof HOURLY_VARIABLES)[number];

interface OpenMeteoLocation {
  hourly?: { time?: string[] } & Partial<Record<HourlyVariable, (number | null)[]>>;
}

/** Open-Meteo was unreachable, timed out or answered with an error. */
export class UpstreamError extends Error {
  override name = 'UpstreamError';
}

interface HttpResponse {
  status: number;
  statusText: string;
  body: string;
}

/**
 * GET over node:http(s) rather than fetch(). Node's fetch (undici) defers creating requests
 * that queue behind a new connection, so with several concurrent calls on a cold connection
 * pool their client spans land under the first caller's span. The HTTP instrumentation starts
 * each span synchronously in the caller's context, so every `assess leg` gets its own `GET`.
 */
function httpGet(url: URL): Promise<HttpResponse> {
  const send = url.protocol === 'http:' ? httpRequest : httpsRequest;
  return new Promise((resolve, reject) => {
    const request = send(url, { signal: AbortSignal.timeout(8000) }, (response) => {
      const chunks: Buffer[] = [];
      response.on('data', (chunk: Buffer) => chunks.push(chunk));
      response.on('error', reject);
      response.on('end', () =>
        resolve({
          status: response.statusCode ?? 0,
          statusText: response.statusMessage ?? '',
          body: Buffer.concat(chunks).toString('utf8'),
        }),
      );
    });
    request.on('error', reject);
    request.end();
  });
}

const MAX_ATTEMPTS = 3;

/**
 * Retries network failures, 429 (the free tier allows 600 location-calls a minute) and 5xx
 * with a short exponential backoff. Each attempt is its own client span, and each retry is
 * recorded as a `forecast.retry` event on the active `assess leg` span.
 */
async function getWithRetry(url: URL): Promise<HttpResponse> {
  for (let attempt = 1; ; attempt++) {
    let response: HttpResponse | undefined;
    let failure: unknown;
    try {
      response = await httpGet(url);
    } catch (err) {
      failure = err;
    }
    const retryable = !response || response.status === 429 || response.status >= 500;
    if (!retryable || attempt === MAX_ATTEMPTS) {
      if (!response) throw new UpstreamError(`Open-Meteo request failed: ${(failure as Error).message}`, { cause: failure });
      return response;
    }
    trace.getActiveSpan()?.addEvent('forecast.retry', {
      'forecast.attempt': attempt,
      'forecast.retry_reason': response ? `HTTP ${response.status}` : (failure as Error).message,
    });
    await sleep(250 * 2 ** attempt);
  }
}

// Open-Meteo refreshes its models hourly, so a short cache keeps the board and the dashboard
// command responsive and well inside the free tier (every sample point counts as one call).
const CACHE_TTL_MS = 10 * 60_000;
const CACHE_MAX_ENTRIES = 500;
const cache = new Map<string, { expires: number; forecasts: HourlySample[][] }>();

export interface ForecastResult {
  forecasts: HourlySample[][];
  /** True when served from the in-memory cache instead of Open-Meteo. */
  cached: boolean;
}

/**
 * Hourly forecasts for each point over the window. Set `fresh` to bypass the cache (clients
 * ask for this with `Cache-Control: no-cache`); the result still refreshes the cache.
 */
export async function fetchHourlyForecasts(
  points: [number, number][],
  windowHours: number,
  fresh = false,
): Promise<ForecastResult> {
  const url = new URL('/v1/forecast', OPEN_METEO_URL);
  url.searchParams.set('latitude', points.map(([lat]) => lat.toFixed(4)).join(','));
  url.searchParams.set('longitude', points.map(([, lon]) => lon.toFixed(4)).join(','));
  url.searchParams.set('hourly', HOURLY_VARIABLES.join(','));
  url.searchParams.set('forecast_hours', String(windowHours));
  url.searchParams.set('wind_speed_unit', 'kmh');
  url.searchParams.set('timezone', 'UTC');

  const key = url.search;
  const hit = cache.get(key);
  if (!fresh && hit && hit.expires > Date.now()) return { forecasts: hit.forecasts, cached: true };

  const started = performance.now();
  let outcome = 'error';
  try {
    const response = await getWithRetry(url);
    if (response.status !== 200) throw new UpstreamError(`Open-Meteo responded ${response.status} ${response.statusText}`);

    const body = JSON.parse(response.body) as OpenMeteoLocation | OpenMeteoLocation[];
    // A single coordinate returns an object; several return an array in request order.
    const locations = Array.isArray(body) ? body : [body];
    if (locations.length !== points.length) {
      throw new UpstreamError(`Open-Meteo returned ${locations.length} locations for ${points.length} points`);
    }
    outcome = 'success';
    const forecasts = locations.map(({ hourly = {} }) =>
      (hourly.time ?? []).map((time, i) => ({
        time: `${time}Z`,
        temperatureC: hourly.temperature_2m?.[i] ?? null,
        rainMm: hourly.rain?.[i] ?? null,
        showersMm: hourly.showers?.[i] ?? null,
        snowfallCm: hourly.snowfall?.[i] ?? null,
        gustKph: hourly.wind_gusts_10m?.[i] ?? null,
        visibilityM: hourly.visibility?.[i] ?? null,
        weatherCode: hourly.weather_code?.[i] ?? null,
      })),
    );
    cache.delete(key);
    if (cache.size >= CACHE_MAX_ENTRIES) cache.delete(cache.keys().next().value!);
    cache.set(key, { expires: Date.now() + CACHE_TTL_MS, forecasts });
    return { forecasts, cached: false };
  } finally {
    forecastDuration.record((performance.now() - started) / 1000, { outcome });
  }
}
