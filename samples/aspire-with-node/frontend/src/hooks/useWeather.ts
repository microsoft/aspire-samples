/** Loads current weather + forecast, ignoring stale responses so a newer pick wins. */
import { useCallback, useRef, useState } from 'react';
import { fetchWithRetry } from '../lib/http';
import type { WeatherResponse } from '../types/weather';

export function useWeather() {
  const [weather, setWeather] = useState<WeatherResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestRef = useRef(0);

  const fetchWeather = useCallback(async (lat: number, lon: number) => {
    // Ignore-stale guard: a newer request supersedes this one without aborting it,
    // so cancelled fetches never surface as `net::ERR_ABORTED` in the console.
    const requestId = ++requestRef.current;
    setLoading(true);
    setError(null);
    try {
      const res = await fetchWithRetry(`/api/weather?lat=${lat.toFixed(4)}&lon=${lon.toFixed(4)}`);
      if (requestRef.current !== requestId) return;
      if (!res.ok) {
        const body = (await res.json().catch(() => null)) as { error?: string } | null;
        throw new Error(body?.error ?? `HTTP ${res.status}`);
      }
      const data: WeatherResponse = await res.json();
      if (requestRef.current !== requestId) return;
      setWeather(data);
    } catch (err) {
      if (requestRef.current !== requestId) return;
      setError('Could not load weather for this area.');
      console.error('[weather] request failed:', err);
    } finally {
      if (requestRef.current === requestId) setLoading(false);
    }
  }, []);

  return { weather, loading, error, fetchWeather };
}
