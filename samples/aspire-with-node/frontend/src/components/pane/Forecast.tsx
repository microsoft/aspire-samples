/** The live-region forecast block: location, error/skeleton states, and all forecast sections. */
import { primaryLocationLabel, secondaryLocationLabel } from '../../lib/format';
import { describeWeather } from '../../lib/weatherCodes';
import type { WeatherResponse } from '../../types/weather';
import { DailyForecast } from './DailyForecast';
import { ForecastSkeleton } from './ForecastSkeleton';
import { HourlyStrip } from './HourlyStrip';
import { Metrics } from './Metrics';
import { NowCard } from './NowCard';
import { SunTimes } from './SunTimes';

interface ForecastProps {
  weather: WeatherResponse | null;
  loading: boolean;
  error: string | null;
  useCelsius: boolean;
  onUnitChange: (useCelsius: boolean) => void;
}

export function Forecast({ weather, loading, error, useCelsius, onUnitChange }: ForecastProps) {
  const current = weather?.current;
  const condition = current ? describeWeather(current.weatherCode) : null;
  const primaryLocation = primaryLocationLabel(weather);
  const secondaryLocation = secondaryLocationLabel(weather);
  const days = weather?.daily ?? [];

  return (
    <div className="weather" aria-live="polite" aria-label="Weather forecast">
      {!weather && (
        <div className="location">
          <h2 className="location-name">
            {primaryLocation}
            {loading && <span className="loc-spinner" aria-hidden="true" />}
          </h2>
          {secondaryLocation && <span className="location-detail">{secondaryLocation}</span>}
        </div>
      )}

      {error && (
        <div className="error" role="alert">
          {error}
        </div>
      )}

      {!weather && !error && <ForecastSkeleton />}

      {/* `key` on each section replays its entry animation when the place changes. */}
      {weather && current && condition && (
        <>
          <NowCard
            key={`now-${primaryLocation}`}
            weather={weather}
            useCelsius={useCelsius}
            loading={loading}
            onUnitChange={onUnitChange}
          />
          {weather.hourly.length > 0 && (
            <HourlyStrip key={`hours-${primaryLocation}`} hourly={weather.hourly} useCelsius={useCelsius} />
          )}
          <Metrics key={`metrics-${primaryLocation}`} current={current} />
          <SunTimes key={`sun-${primaryLocation}`} sunrise={weather.sun.sunrise} sunset={weather.sun.sunset} />
        </>
      )}

      {days.length > 0 && <DailyForecast key={primaryLocation} days={days} useCelsius={useCelsius} />}

      <p className="panel-footer">
        Weather by{' '}
        <a href="https://open-meteo.com" target="_blank" rel="noopener noreferrer">
          Open-Meteo
        </a>{' '}
        · Map ©{' '}
        <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">
          OpenStreetMap
        </a>
      </p>
    </div>
  );
}
