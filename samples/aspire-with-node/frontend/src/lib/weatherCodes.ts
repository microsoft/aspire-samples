/**
 * WMO weather interpretation code mapping (shared by the icon component and the UI).
 * Kept in its own module so components can Fast Refresh cleanly.
 * @see https://open-meteo.com/en/docs
 */

export type WeatherKind = 'clear' | 'partly' | 'cloud' | 'fog' | 'rain' | 'sleet' | 'snow' | 'thunder';

const CODE_MAP: Record<number, { label: string; kind: WeatherKind }> = {
  0: { label: 'Clear sky', kind: 'clear' },
  1: { label: 'Mainly clear', kind: 'partly' },
  2: { label: 'Partly cloudy', kind: 'partly' },
  3: { label: 'Overcast', kind: 'cloud' },
  45: { label: 'Fog', kind: 'fog' },
  48: { label: 'Rime fog', kind: 'fog' },
  51: { label: 'Light drizzle', kind: 'rain' },
  53: { label: 'Drizzle', kind: 'rain' },
  55: { label: 'Dense drizzle', kind: 'rain' },
  56: { label: 'Freezing drizzle', kind: 'sleet' },
  57: { label: 'Freezing drizzle', kind: 'sleet' },
  61: { label: 'Light rain', kind: 'rain' },
  63: { label: 'Rain', kind: 'rain' },
  65: { label: 'Heavy rain', kind: 'rain' },
  66: { label: 'Freezing rain', kind: 'sleet' },
  67: { label: 'Freezing rain', kind: 'sleet' },
  71: { label: 'Light snow', kind: 'snow' },
  73: { label: 'Snow', kind: 'snow' },
  75: { label: 'Heavy snow', kind: 'snow' },
  77: { label: 'Snow grains', kind: 'snow' },
  80: { label: 'Rain showers', kind: 'rain' },
  81: { label: 'Rain showers', kind: 'rain' },
  82: { label: 'Violent showers', kind: 'rain' },
  85: { label: 'Snow showers', kind: 'snow' },
  86: { label: 'Heavy snow showers', kind: 'snow' },
  95: { label: 'Thunderstorm', kind: 'thunder' },
  96: { label: 'Thunderstorm, hail', kind: 'thunder' },
  99: { label: 'Thunderstorm, hail', kind: 'thunder' },
};

export function describeWeather(code: number): { label: string; kind: WeatherKind } {
  return CODE_MAP[code] ?? { label: 'Unknown', kind: 'cloud' };
}
