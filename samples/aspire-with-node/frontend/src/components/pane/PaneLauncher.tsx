/** Collapsed-state pill showing the current temperature + place; taps to reopen the pane. */
import { toDisplayTemp } from '../../lib/format';
import type { CurrentWeather } from '../../types/weather';
import { WeatherIcon } from '../icons/WeatherIcon';
import { ExpandGlyph, PanelGlyph } from '../icons/Glyphs';

interface PaneLauncherProps {
  current: CurrentWeather | undefined;
  useCelsius: boolean;
  primaryLocation: string;
  onExpand: () => void;
}

export function PaneLauncher({ current, useCelsius, primaryLocation, onExpand }: PaneLauncherProps) {
  return (
    <button type="button" className="pane-launcher" onClick={onExpand} aria-label="Show weather panel">
      <span className="launcher-icon">
        {current ? <WeatherIcon code={current.weatherCode} size={26} decorative /> : <PanelGlyph />}
      </span>
      <span className="launcher-readout">
        <span className="launcher-temp">
          {current ? toDisplayTemp(current.temperatureC, useCelsius) : 'Weather'}
        </span>
        <span className="launcher-place">{primaryLocation}</span>
      </span>
      <span className="launcher-expand" aria-hidden="true">
        <ExpandGlyph />
      </span>
    </button>
  );
}
