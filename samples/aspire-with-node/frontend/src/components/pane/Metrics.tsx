/** Humidity / wind / UV index summary row. */
import { uvLabel } from '../../lib/format';
import type { CurrentWeather } from '../../types/weather';

interface MetricsProps {
  current: CurrentWeather;
}

export function Metrics({ current }: MetricsProps) {
  return (
    <dl className="metrics">
      <div>
        <dt>Humidity</dt>
        <dd>{current.humidity ?? '-'}%</dd>
      </div>
      <div>
        <dt>Wind</dt>
        <dd>
          {current.windSpeedKph != null ? Math.round(current.windSpeedKph) : '-'}
          <small> km/h</small>
        </dd>
      </div>
      <div>
        <dt>UV index</dt>
        <dd>{uvLabel(current.uvIndex)}</dd>
      </div>
    </dl>
  );
}
