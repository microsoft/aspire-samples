/** Current-conditions hero: place, unit toggle, big temperature, feels-like, and today's H/L. */
import type { CSSProperties } from 'react';
import { describeWeather } from '../../lib/weatherCodes';
import { primaryLocationLabel, secondaryLocationLabel, toDisplayTemp } from '../../lib/format';
import { heroGlow, tempColor } from '../../lib/weatherVisuals';
import type { WeatherResponse } from '../../types/weather';
import { WeatherIcon } from '../icons/WeatherIcon';
import { TrendGlyph } from '../icons/Glyphs';

interface NowCardProps {
  weather: WeatherResponse;
  useCelsius: boolean;
  loading: boolean;
  onUnitChange: (useCelsius: boolean) => void;
}

export function NowCard({ weather, useCelsius, loading, onUnitChange }: NowCardProps) {
  const current = weather.current;
  const condition = describeWeather(current.weatherCode);
  const today = weather.daily[0];
  const nowStyle = { '--now-glow': heroGlow(current.isDay, condition.kind) } as CSSProperties;

  return (
    <section className="now-card" style={nowStyle}>
      <div className="now-head">
        <div className="now-place">
          <h2 className="location-name">
            {primaryLocationLabel(weather)}
            {loading && <span className="loc-spinner" aria-hidden="true" />}
          </h2>
          {secondaryLocationLabel(weather) && (
            <span className="location-detail">{secondaryLocationLabel(weather)}</span>
          )}
          {weather.location.timezone && (
            <span className="location-tz">{weather.location.timezone.replace(/_/g, ' ')}</span>
          )}
        </div>
        <fieldset className="unit-toggle">
          <legend className="visually-hidden">Temperature unit</legend>
          <button
            type="button"
            className="unit-option"
            aria-pressed={!useCelsius}
            onClick={() => onUnitChange(false)}
          >
            °F
          </button>
          <button
            type="button"
            className="unit-option"
            aria-pressed={useCelsius}
            onClick={() => onUnitChange(true)}
          >
            °C
          </button>
        </fieldset>
      </div>

      <div className="now-main">
        <div className="now-readout">
          <span className="hero-temp">{toDisplayTemp(current.temperatureC, useCelsius)}</span>
          <span className="hero-condition">{condition.label}</span>
        </div>
        <WeatherIcon code={current.weatherCode} size={92} className="now-icon" decorative />
      </div>

      <div className="now-meta">
        <span className="now-feels">
          Feels like <b>{toDisplayTemp(current.apparentC, useCelsius)}</b>
        </span>
        {today && (today.maxC != null || today.minC != null) && (
          <span className="now-range">
            <span className="now-hl">
              <span className="now-hl-arrow" style={{ color: tempColor(today.maxC) }}>
                <TrendGlyph up />
              </span>
              <span className="now-hl-val">{toDisplayTemp(today.maxC, useCelsius)}</span>
            </span>
            <span className="now-hl">
              <span className="now-hl-arrow" style={{ color: tempColor(today.minC) }}>
                <TrendGlyph />
              </span>
              <span className="now-hl-val">{toDisplayTemp(today.minC, useCelsius)}</span>
            </span>
          </span>
        )}
      </div>
    </section>
  );
}
