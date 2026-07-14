/** Locale-aware formatting helpers for weather values, times, and labels. */
import type { RadarFrame } from '../types/map';
import type { WeatherResponse } from '../types/weather';

/** Clock time (e.g. "2:40 PM") for a radar frame's unix-second timestamp. */
export function formatFrameTime(frame: RadarFrame): string {
  return new Date(frame.time * 1000).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

/** Rounds a Celsius value to whole degrees in the requested unit, e.g. "72°". */
export function toDisplayTemp(celsius: number | null, useCelsius: boolean): string {
  if (celsius === null || celsius === undefined) return '-';
  const value = useCelsius ? celsius : (celsius * 9) / 5 + 32;
  return `${Math.round(value)}°`;
}

/**
 * Short weekday for a daily forecast row. `date` is a bare calendar date
 * ("YYYY-MM-DD"); `new Date("2026-07-06")` parses as UTC midnight, so in a timezone
 * behind UTC the weekday renders one day early ("Sun" for a Monday). Parse it as
 * LOCAL midnight so the weekday matches the date.
 */
export function formatDay(dateString: string): string {
  return new Date(`${dateString}T00:00:00`).toLocaleDateString(undefined, { weekday: 'short' });
}

/** Drops the ISO "(the)" suffix some geocoders append (e.g. "United States of America (the)"). */
export function cleanCountryName(country: string | null): string | null {
  return country ? country.replace(/\s*\(the\)$/i, '') : null;
}

/** "Now" for the current hour, otherwise a short local hour like "2 PM". */
export function formatHour(iso: string, index: number): string {
  if (index === 0) return 'Now';
  return new Date(iso).toLocaleTimeString([], { hour: 'numeric' });
}

/** Local clock time like "5:17 AM" for sunrise / sunset. */
export function formatClock(iso: string | null): string {
  if (!iso) return '-';
  return new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

/** UV index number plus its WHO exposure band. */
export function uvLabel(uv: number | null): string {
  if (uv == null) return '-';
  const value = Math.round(uv);
  if (uv < 3) return `${value} Low`;
  if (uv < 6) return `${value} Moderate`;
  if (uv < 8) return `${value} High`;
  if (uv < 11) return `${value} Very high`;
  return `${value} Extreme`;
}

/** Primary place label: the geocoded name, or a rounded lat/lon fallback. */
export function primaryLocationLabel(weather: WeatherResponse | null): string {
  if (!weather) return 'Locating…';
  return (
    weather.location.name ??
    `${weather.location.latitude.toFixed(3)}, ${weather.location.longitude.toFixed(3)}`
  );
}

/** Region + country, de-duplicated (e.g. "Washington, United States of America"). */
export function secondaryLocationLabel(weather: WeatherResponse | null): string {
  if (!weather) return '';
  return Array.from(
    new Set(
      [weather.location.region, cleanCountryName(weather.location.country)].filter(
        (part): part is string => Boolean(part),
      ),
    ),
  ).join(', ');
}
