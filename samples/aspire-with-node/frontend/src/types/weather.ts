/** Shapes returned by the `/api/weather` and `/api/geocode` endpoints. */

export interface DailyForecast {
  date: string;
  weatherCode: number;
  maxC: number | null;
  minC: number | null;
  precipitationProb: number | null;
}

export interface HourlyPoint {
  time: string;
  temperatureC: number | null;
  weatherCode: number;
}

export interface CurrentWeather {
  temperatureC: number | null;
  apparentC: number | null;
  humidity: number | null;
  weatherCode: number;
  windSpeedKph: number | null;
  uvIndex: number | null;
  isDay: boolean;
  time: string | null;
}

export interface WeatherLocation {
  name: string | null;
  region: string | null;
  country: string | null;
  latitude: number;
  longitude: number;
  timezone: string | null;
}

export interface WeatherResponse {
  location: WeatherLocation;
  current: CurrentWeather;
  sun: {
    sunrise: string | null;
    sunset: string | null;
  };
  hourly: HourlyPoint[];
  daily: DailyForecast[];
}

export interface GeoResult {
  name: string;
  region: string | null;
  country: string | null;
  latitude: number;
  longitude: number;
}
