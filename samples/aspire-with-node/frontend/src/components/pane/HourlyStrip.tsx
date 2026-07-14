/** Next-eight-hours strip that shares the pane width evenly. */
import { formatHour, toDisplayTemp } from '../../lib/format';
import type { HourlyPoint } from '../../types/weather';
import { WeatherIcon } from '../icons/WeatherIcon';

interface HourlyStripProps {
  hourly: HourlyPoint[];
  useCelsius: boolean;
}

export function HourlyStrip({ hourly, useCelsius }: HourlyStripProps) {
  return (
    <div className="hourly">
      {hourly.slice(0, 8).map((hour, index) => (
        <div className="hour" key={hour.time} style={{ animationDelay: `${index * 35 + 60}ms` }}>
          <span className="hour-label">{formatHour(hour.time, index)}</span>
          <WeatherIcon code={hour.weatherCode} size={22} className="hour-icon" decorative />
          <span className="hour-temp">{toDisplayTemp(hour.temperatureC, useCelsius)}</span>
        </div>
      ))}
    </div>
  );
}
