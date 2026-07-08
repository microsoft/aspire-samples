/** Multi-day forecast rows with temperature-range bars scaled across the week. */
import { formatDay, toDisplayTemp } from '../../lib/format';
import { tempColor } from '../../lib/weatherVisuals';
import type { DailyForecast as DailyForecastRow } from '../../types/weather';
import { WeatherIcon } from '../icons/WeatherIcon';
import { DropletIcon } from '../icons/Glyphs';

interface DailyForecastProps {
  days: DailyForecastRow[];
  useCelsius: boolean;
}

export function DailyForecast({ days, useCelsius }: DailyForecastProps) {
  const lows = days.map((day) => day.minC).filter((value): value is number => value != null);
  const highs = days.map((day) => day.maxC).filter((value): value is number => value != null);
  const weekMin = lows.length ? Math.min(...lows) : 0;
  const weekMax = highs.length ? Math.max(...highs) : 0;
  const weekRange = Math.max(1, weekMax - weekMin);

  return (
    <div className="forecast">
      {days.map((day, index) => {
        const lo = day.minC;
        const hi = day.maxC;
        const left = lo != null ? ((lo - weekMin) / weekRange) * 100 : 0;
        const width = lo != null && hi != null ? Math.max(6, ((hi - lo) / weekRange) * 100) : 0;
        return (
          <div className="fc-row" key={day.date} style={{ animationDelay: `${index * 55}ms` }}>
            <div className="fc-daycol">
              <span className="fc-day">{index === 0 ? 'Today' : formatDay(day.date)}</span>
              {day.precipitationProb != null && day.precipitationProb > 0 && (
                <span className="fc-precip">
                  <DropletIcon /> {day.precipitationProb}%
                </span>
              )}
            </div>
            <WeatherIcon code={day.weatherCode} size={22} className="fc-icon" />
            <span className="fc-lo">{toDisplayTemp(lo, useCelsius)}</span>
            <div className="fc-bar">
              <span
                className="fc-fill"
                style={{
                  left: `${left}%`,
                  width: `${width}%`,
                  background: `linear-gradient(90deg, ${tempColor(lo)}, ${tempColor(hi)})`,
                }}
              />
            </div>
            <span className="fc-hi">{toDisplayTemp(hi, useCelsius)}</span>
          </div>
        );
      })}
    </div>
  );
}
