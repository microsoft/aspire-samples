/** The collapsible control pane: brand header, search, and the full forecast. */
import type { GeocodeSearch } from '../../hooks/useGeocodeSearch';
import type { WeatherResponse } from '../../types/weather';
import { Forecast } from './Forecast';
import { PaneHeader } from './PaneHeader';
import { SearchBox } from './SearchBox';

interface ControlPaneProps {
  collapsed: boolean;
  radarActive: boolean;
  isTouch: boolean;
  onHide: () => void;
  search: GeocodeSearch;
  onLocate: () => void;
  controlError: string | null;
  weather: WeatherResponse | null;
  loading: boolean;
  error: string | null;
  useCelsius: boolean;
  onUnitChange: (useCelsius: boolean) => void;
}

export function ControlPane({
  collapsed,
  radarActive,
  isTouch,
  onHide,
  search,
  onLocate,
  controlError,
  weather,
  loading,
  error,
  useCelsius,
  onUnitChange,
}: ControlPaneProps) {
  return (
    <section
      className={`control-pane ${collapsed ? 'collapsed' : ''} ${radarActive ? 'radar-active' : ''}`}
      aria-label="Weather controls"
      aria-hidden={collapsed}
    >
      <PaneHeader isTouch={isTouch} onHide={onHide} />

      <div className="pane-body">
        <SearchBox search={search} onLocate={onLocate} />

        {controlError && (
          <p className="control-error" role="alert">
            {controlError}
          </p>
        )}

        <Forecast
          weather={weather}
          loading={loading}
          error={error}
          useCelsius={useCelsius}
          onUnitChange={onUnitChange}
        />
      </div>
    </section>
  );
}
