/**
 * Fleet state: the route catalog, the latest assessment per route, the forecast window and
 * the custom route being planned. Every user action that calls the API runs inside a UI span
 * (see lib/tracing.ts), so one click shows up as one browser → API → Open-Meteo trace.
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { assessCustomRoute, fetchAssessment, fetchRoutes } from '../lib/api';
import { traced } from '../lib/tracing';
import type { FleetRoute, Recommendation, RouteAssessment, Waypoint } from '../types/fleet';

export const WINDOW_OPTIONS = [6, 12, 24, 48] as const;
export const CUSTOM_ROUTE_ID = 'custom';
export const MAX_STOPS = 10;

const message = (err: unknown) => (err instanceof Error ? err.message : String(err));

/** State updater that adds (or removes) ids from a set. */
const withIds = (ids: string[], add: boolean) => (current: ReadonlySet<string>) => {
  const next = new Set(current);
  for (const id of ids) {
    if (add) next.add(id);
    else next.delete(id);
  }
  return next;
};

export function useFleet() {
  const [routes, setRoutes] = useState<FleetRoute[]>([]);
  const [assessments, setAssessments] = useState<Record<string, RouteAssessment>>({});
  const [pending, setPending] = useState<ReadonlySet<string>>(new Set());
  const [failed, setFailed] = useState<ReadonlySet<string>>(new Set());
  const [windowHours, setWindowHours] = useState<number>(24);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [planning, setPlanning] = useState(false);
  const [stops, setStops] = useState<Waypoint[]>([]);

  const assessAll = useCallback(
    (list: FleetRoute[], hours: number) => {
      const ids = list.map((route) => route.id);
      setPending(withIds(ids, true));
      setError(null);
      return traced(
        'assess all routes',
        { 'fleetweather.route.count': ids.length, 'fleetweather.window_hours': hours },
        async (span) => {
          // One fetch per route, all started inside the span: a single fan-out trace.
          const results = await Promise.allSettled(ids.map((id) => fetchAssessment(id, hours)));
          const next: Record<string, RouteAssessment> = {};
          const totals: Record<Recommendation, number> = { go: 0, delay: 0, reroute: 0 };
          results.forEach((result, i) => {
            if (result.status === 'fulfilled') {
              next[ids[i]] = result.value;
              totals[result.value.recommendation]++;
            }
          });
          span.setAttributes({
            'fleetweather.routes.go': totals.go,
            'fleetweather.routes.delay': totals.delay,
            'fleetweather.routes.reroute': totals.reroute,
          });
          setAssessments((current) => ({ ...current, ...next }));
          setFailed(withIds(ids, false));
          setFailed(withIds(ids.filter((_, i) => results[i].status === 'rejected'), true));
          const failure = results.find((result) => result.status === 'rejected');
          if (failure) throw failure.reason;
        },
      )
        .catch((err) => setError(message(err)))
        .finally(() => setPending(withIds(ids, false)));
    },
    [],
  );

  // Load the fleet and assess it once on startup (the ref guards StrictMode's double effect).
  const started = useRef(false);
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    traced('load routes', {}, () => fetchRoutes())
      .then((list) => {
        setRoutes(list);
        return assessAll(list, 24);
      })
      .catch((err) => setError(message(err)));
  }, [assessAll]);

  /** Selects a route and re-assesses it with a fresh forecast; selecting it again collapses it. */
  const selectRoute = useCallback(
    (id: string) => {
      setPlanning(false);
      if (id === selectedId) {
        setSelectedId(null);
        return;
      }
      setSelectedId(id);
      if (id === CUSTOM_ROUTE_ID) return;
      setPending(withIds([id], true));
      setError(null);
      traced('select route', { 'fleetweather.route.id': id, 'fleetweather.window_hours': windowHours }, async (span) => {
        const assessment = await fetchAssessment(id, windowHours, true);
        span.setAttributes({
          'fleetweather.recommendation': assessment.recommendation,
          'fleetweather.route.risk_score': assessment.overallRiskScore,
        });
        setAssessments((current) => ({ ...current, [id]: assessment }));
        setFailed(withIds([id], false));
      })
        .catch((err) => {
          setFailed(withIds([id], true));
          setError(message(err));
        })
        .finally(() => setPending(withIds([id], false)));
    },
    [selectedId, windowHours],
  );

  const assessCustom = useCallback(
    (waypoints: Waypoint[], hours: number) => {
      setPending(withIds([CUSTOM_ROUTE_ID], true));
      setError(null);
      return traced(
        'assess custom route',
        { 'fleetweather.route.waypoints': waypoints.length, 'fleetweather.window_hours': hours },
        async (span) => {
          const assessment = await assessCustomRoute(waypoints, hours);
          span.setAttributes({
            'fleetweather.recommendation': assessment.recommendation,
            'fleetweather.route.risk_score': assessment.overallRiskScore,
          });
          setAssessments((current) => ({ ...current, [CUSTOM_ROUTE_ID]: assessment }));
          setSelectedId(CUSTOM_ROUTE_ID);
          setPlanning(false);
        },
      )
        .catch((err) => setError(message(err)))
        .finally(() => setPending(withIds([CUSTOM_ROUTE_ID], false)));
    },
    [],
  );

  const changeWindow = useCallback(
    (hours: number) => {
      setWindowHours(hours);
      void assessAll(routes, hours);
      if (assessments[CUSTOM_ROUTE_ID] && stops.length >= 2) void assessCustom(stops, hours);
    },
    [assessAll, assessCustom, assessments, routes, stops],
  );

  const startPlanning = useCallback(() => {
    setPlanning(true);
    setSelectedId(CUSTOM_ROUTE_ID);
  }, []);

  const addStop = useCallback((lat: number, lon: number) => {
    setStops((current) =>
      current.length >= MAX_STOPS ? current : [...current, { name: `Stop ${current.length + 1}`, lat, lon }],
    );
  }, []);

  const clearCustomRoute = useCallback(() => {
    setStops([]);
    setPlanning(false);
    setSelectedId((current) => (current === CUSTOM_ROUTE_ID ? null : current));
    setAssessments((current) => {
      const next = { ...current };
      delete next[CUSTOM_ROUTE_ID];
      return next;
    });
  }, []);

  const totals = useMemo(() => {
    const counts: Record<Recommendation, number> = { go: 0, delay: 0, reroute: 0 };
    for (const route of routes) {
      const assessment = assessments[route.id];
      if (assessment) counts[assessment.recommendation]++;
    }
    return counts;
  }, [routes, assessments]);

  return {
    routes,
    assessments,
    pending,
    failed,
    windowHours,
    selectedId,
    error,
    planning,
    stops,
    totals,
    assessAll: () => assessAll(routes, windowHours),
    selectRoute,
    changeWindow,
    startPlanning,
    addStop,
    assessCustom: () => assessCustom(stops, windowHours),
    clearCustomRoute,
  };
}

export type Fleet = ReturnType<typeof useFleet>;
