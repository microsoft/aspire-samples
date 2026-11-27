/**
 * Fleet Weather API client. Requests go to the same-origin `/api` path (proxied by Vite in
 * run mode, served by the API itself in publish mode), so the browser's fetch spans add a
 * `traceparent` header without any CORS setup.
 */
import type { FleetRoute, RouteAssessment, Waypoint } from '../types/fleet';

/** Starts the request synchronously, so it joins the caller's active span. */
async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    // Errors are RFC 9457 problem details; surface the human-readable part.
    const problem = (await response.json().catch(() => null)) as { title?: string; detail?: string } | null;
    throw new Error(problem?.detail ?? problem?.title ?? `Request failed (HTTP ${response.status}).`);
  }
  return (await response.json()) as T;
}

export const fetchRoutes = () => request<FleetRoute[]>('/api/routes');

/** `fresh` asks the API to bypass its 10-minute forecast cache. */
export const fetchAssessment = (routeId: string, windowHours: number, fresh = false) =>
  request<RouteAssessment>(
    `/api/routes/${encodeURIComponent(routeId)}/assessment?windowHours=${windowHours}`,
    fresh ? { headers: { 'Cache-Control': 'no-cache' } } : undefined,
  );

export const assessCustomRoute = (waypoints: Waypoint[], windowHours: number) =>
  request<RouteAssessment>('/api/assessments', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name: 'Custom route', windowHours, waypoints }),
  });
