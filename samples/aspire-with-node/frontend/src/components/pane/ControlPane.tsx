/** The collapsible dispatch pane: forecast window, fleet totals, the route list and the custom route planner. */
import { CUSTOM_ROUTE_ID, MAX_STOPS, WINDOW_OPTIONS, type Fleet } from '../../hooks/useFleet';
import { RECOMMENDATION_LABELS } from '../../lib/format';
import type { Recommendation } from '../../types/fleet';
import { RouteGlyph, TruckGlyph } from '../icons/Glyphs';
import { PaneHeader } from './PaneHeader';
import { RouteCard } from './RouteCard';

interface ControlPaneProps {
  fleet: Fleet;
  collapsed: boolean;
  radarActive: boolean;
  controlError: string | null;
  onHide: () => void;
}

const ORDER: Recommendation[] = ['reroute', 'delay', 'go'];

function CustomRoute({ fleet }: { fleet: Fleet }) {
  const { planning, stops, assessments, pending, selectedId, windowHours } = fleet;
  const custom = assessments[CUSTOM_ROUTE_ID];

  if (planning) {
    return (
      <div className="custom-planner" role="group" aria-label="Custom route">
        <p className="custom-hint">
          Click the map to add stops in driving order. <b>{stops.length}/{MAX_STOPS}</b>
        </p>
        <div className="custom-actions">
          <button
            type="button"
            className="control-btn primary-btn"
            onClick={fleet.assessCustom}
            disabled={stops.length < 2 || pending.has(CUSTOM_ROUTE_ID)}
          >
            {pending.has(CUSTOM_ROUTE_ID) ? <span className="spinner" /> : <RouteGlyph />}
            Assess {windowHours} h
          </button>
          <button type="button" className="control-btn" onClick={fleet.clearCustomRoute}>
            Cancel
          </button>
        </div>
      </div>
    );
  }

  if (custom) {
    return (
      <div className="custom-result">
        <RouteCard
          title="Custom route"
          meta={`${stops.length} stops · ${custom.distanceKm} km`}
          legDistances={custom.legs.map((leg) => leg.distanceKm)}
          assessment={custom}
          selected={selectedId === CUSTOM_ROUTE_ID}
          pending={pending.has(CUSTOM_ROUTE_ID)}
          onSelect={() => fleet.selectRoute(CUSTOM_ROUTE_ID)}
        />
        <button type="button" className="link-btn" onClick={fleet.clearCustomRoute}>
          Clear custom route
        </button>
      </div>
    );
  }

  return (
    <button type="button" className="control-btn custom-start" onClick={fleet.startPlanning}>
      <RouteGlyph />
      Plan a custom route
    </button>
  );
}

export function ControlPane({ fleet, collapsed, radarActive, controlError, onHide }: ControlPaneProps) {
  const { routes, assessments, pending, selectedId, windowHours, totals } = fleet;
  const assessingFleet = routes.some((route) => pending.has(route.id));
  const error = fleet.error ?? controlError;

  return (
    <section
      className={`control-pane ${collapsed ? 'collapsed' : ''} ${radarActive ? 'radar-active' : ''}`}
      aria-label="Fleet dispatch"
      aria-hidden={collapsed}
      inert={collapsed}
    >
      <PaneHeader onHide={onHide} />

      <div className="pane-body">
        <div className="toolbar">
          <div className="window-picker" role="radiogroup" aria-label="Forecast window">
            {WINDOW_OPTIONS.map((hours) => (
              <button
                key={hours}
                type="button"
                role="radio"
                aria-checked={hours === windowHours}
                className="window-option"
                onClick={() => fleet.changeWindow(hours)}
              >
                {hours} h
              </button>
            ))}
          </div>
          <button
            type="button"
            className="control-btn primary-btn"
            onClick={fleet.assessAll}
            disabled={assessingFleet || routes.length === 0}
          >
            {assessingFleet ? <span className="spinner" /> : <TruckGlyph />}
            Assess all
          </button>
        </div>

        <ul className="fleet-totals" aria-label="Fleet recommendations">
          {ORDER.map((key) => (
            <li key={key} className={`total rec-${key}`}>
              <b>{totals[key]}</b> {RECOMMENDATION_LABELS[key]}
            </li>
          ))}
          <li className="total-window">next {windowHours} h</li>
        </ul>

        {error && (
          <p className="control-error" role="alert">
            {error}
          </p>
        )}

        <ol className="route-list" aria-busy={routes.length === 0}>
          {routes.length === 0 && !error
            ? [0, 1, 2, 3, 4].map((i) => <li key={i} className="route-skeleton" />)
            : routes.map((route) => (
                <li key={route.id}>
                  <RouteCard
                    title={route.name}
                    meta={`${route.corridor} · ${route.distanceKm} km · ${route.region}`}
                    legDistances={route.waypoints.slice(1).map(() => 1)}
                    assessment={assessments[route.id]}
                    selected={selectedId === route.id}
                    pending={pending.has(route.id)}
                    failed={fleet.failed.has(route.id)}
                    onSelect={() => fleet.selectRoute(route.id)}
                  />
                </li>
              ))}
        </ol>

        <CustomRoute fleet={fleet} />

        <p className="panel-footer">
          Forecasts by <a href="https://open-meteo.com">Open-Meteo</a> · Radar by{' '}
          <a href="https://www.rainviewer.com">RainViewer</a>
        </p>
      </div>
    </section>
  );
}
