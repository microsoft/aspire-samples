/**
 * The Leaflet map: basemap, optional radar frames, the fleet's routes with each leg colored by
 * its risk level, and the stops of a custom route being planned.
 */
import { useEffect, useMemo } from 'react';
import { CircleMarker, MapContainer, Marker, Polyline, TileLayer, Tooltip, ZoomControl, useMap, useMapEvents } from 'react-leaflet';
import { divIcon, latLngBounds } from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { CUSTOM_ROUTE_ID } from '../../hooks/useFleet';
import { INITIAL_CENTER, INITIAL_ZOOM } from '../../lib/mapStyles';
import type { FleetRoute, RouteAssessment, Waypoint } from '../../types/fleet';
import type { MapStyle, RadarFrame } from '../../types/map';

interface DrawnRoute {
  id: string;
  name: string;
  waypoints: Waypoint[];
  assessment?: RouteAssessment;
}

interface FleetMapProps {
  activeStyle: MapStyle;
  radarOn: boolean;
  radarFrames: RadarFrame[];
  radarIndex: number;
  routes: FleetRoute[];
  assessments: Record<string, RouteAssessment>;
  selectedId: string | null;
  planning: boolean;
  stops: Waypoint[];
  /** Width (px) of the map covered by the side pane, kept clear when fitting a route. */
  paneInset: number;
  onSelectRoute: (id: string) => void;
  onAddStop: (lat: number, lon: number) => void;
}

function RouteLayer({ route, selected, onSelect }: { route: DrawnRoute; selected: boolean; onSelect: () => void }) {
  const { waypoints, assessment } = route;
  const legs = waypoints.slice(1).map((to, i) => ({
    positions: [
      [waypoints[i].lat, waypoints[i].lon],
      [to.lat, to.lon],
    ] as [number, number][],
    label: `${waypoints[i].name} → ${to.name}`,
    level: assessment?.legs[i]?.riskLevel ?? 'pending',
  }));
  const state = selected ? ' is-selected' : '';
  const events = { click: onSelect };
  // Alternate label sides along the route so neighboring stop names don't collide.
  const lats = waypoints.map((p) => p.lat);
  const lons = waypoints.map((p) => p.lon);
  const eastWest = Math.max(...lons) - Math.min(...lons) > Math.max(...lats) - Math.min(...lats);
  const labelSide = (i: number) =>
    eastWest ? (i % 2 ? 'bottom' : 'top') : i % 2 ? 'left' : 'right';
  const labelOffset = (i: number): [number, number] =>
    eastWest ? [0, i % 2 ? 8 : -8] : [i % 2 ? -8 : 8, 0];

  // Leaflet sets a path's class only when it's created, so the keys include the state.
  return (
    <>
      {legs.map((leg, i) => (
        <Polyline
          key={`casing-${i}${state}`}
          positions={leg.positions}
          pathOptions={{ className: `route-casing${state}`, weight: selected ? 13 : 8 }}
          eventHandlers={events}
        />
      ))}
      {legs.map((leg, i) => (
        <Polyline
          key={`leg-${i}-${leg.level}${state}`}
          positions={leg.positions}
          pathOptions={{ className: `route-leg risk-${leg.level}${state}`, weight: selected ? 7 : 4 }}
          eventHandlers={events}
        >
          <Tooltip sticky className="map-tip">
            <b>{route.name}</b>
            <br />
            {leg.label}
          </Tooltip>
        </Polyline>
      ))}
      {waypoints.map((point, i) =>
        selected || i === 0 || i === waypoints.length - 1 ? (
          <CircleMarker
            key={`stop-${i}${state}`}
            center={[point.lat, point.lon]}
            radius={selected ? 5.5 : 4}
            pathOptions={{ className: `waypoint${state}`, weight: 2 }}
            eventHandlers={events}
          >
            <Tooltip
              permanent={selected}
              direction={labelSide(i)}
              offset={labelOffset(i)}
              className="map-tip waypoint-label"
            >
              {point.name}
            </Tooltip>
          </CircleMarker>
        ) : null,
      )}
    </>
  );
}

/** Fits the selected route (or the whole fleet) into the part of the map the pane doesn't cover. */
function FitView({ points, inset }: { points: Waypoint[]; inset: number }) {
  const map = useMap();
  const key = points.map((p) => `${p.lat},${p.lon}`).join(';');
  useEffect(() => {
    if (!key) return;
    const bounds = latLngBounds(key.split(';').map((pair) => pair.split(',').map(Number) as [number, number]));
    map.fitBounds(bounds, { paddingTopLeft: [inset + 48, 64], paddingBottomRight: [72, 64], maxZoom: 8 });
  }, [map, key, inset]);
  return null;
}

function PlanningClicks({ planning, onAddStop }: { planning: boolean; onAddStop: (lat: number, lon: number) => void }) {
  useMapEvents({
    click: (event) => {
      if (planning) onAddStop(event.latlng.lat, event.latlng.lng);
    },
  });
  return null;
}

const stopIcon = (n: number) =>
  divIcon({ className: 'stop-pin', html: `<span>${n}</span>`, iconSize: [26, 26], iconAnchor: [13, 13] });

export function FleetMap({
  activeStyle,
  radarOn,
  radarFrames,
  radarIndex,
  routes,
  assessments,
  selectedId,
  planning,
  stops,
  paneInset,
  onSelectRoute,
  onAddStop,
}: FleetMapProps) {
  const drawn = useMemo<DrawnRoute[]>(() => {
    const list: DrawnRoute[] = routes.map((route) => ({ ...route, assessment: assessments[route.id] }));
    const custom = assessments[CUSTOM_ROUTE_ID];
    if (custom && stops.length >= 2) list.push({ id: CUSTOM_ROUTE_ID, name: 'Custom route', waypoints: stops, assessment: custom });
    // Draw the selected route last so it sits on top.
    return list.sort((a, b) => Number(a.id === selectedId) - Number(b.id === selectedId));
  }, [routes, assessments, stops, selectedId]);

  const hasCustom = Boolean(assessments[CUSTOM_ROUTE_ID]);
  const focus = useMemo(() => {
    if (selectedId === CUSTOM_ROUTE_ID) return hasCustom ? stops : [];
    const selected = routes.find((route) => route.id === selectedId);
    return selected ? selected.waypoints : routes.flatMap((route) => route.waypoints);
    // Stops are only refit once the custom route is assessed, not on every click.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [routes, selectedId, hasCustom]);

  return (
    <MapContainer className="map" center={INITIAL_CENTER} zoom={INITIAL_ZOOM} zoomControl={false} minZoom={3}>
      <TileLayer
        key={activeStyle.key}
        attribution={activeStyle.attribution}
        url={activeStyle.url}
        subdomains={activeStyle.subdomains ?? 'abc'}
      />
      {activeStyle.overlay?.map((overlayUrl) => (
        <TileLayer key={overlayUrl} url={overlayUrl} zIndex={210} />
      ))}
      {radarOn &&
        radarFrames.map((frame, i) => (
          <TileLayer
            key={frame.url}
            url={frame.url}
            opacity={i === radarIndex ? 0.65 : 0}
            zIndex={500 + i}
            maxNativeZoom={7}
            className="radar-frame-layer"
            attribution={i === 0 ? 'Radar &copy; <a href="https://www.rainviewer.com/">RainViewer</a>' : undefined}
          />
        ))}
      {drawn.map((route) => (
        <RouteLayer
          key={route.id}
          route={route}
          selected={route.id === selectedId}
          onSelect={() => {
            if (!planning) onSelectRoute(route.id);
          }}
        />
      ))}
      {planning && stops.length > 1 && (
        <Polyline
          positions={stops.map((stop) => [stop.lat, stop.lon] as [number, number])}
          pathOptions={{ className: 'custom-draft', weight: 4, dashArray: '8 8' }}
          interactive={false}
        />
      )}
      {(planning || !hasCustom) &&
        stops.map((stop, i) => (
          <Marker key={`${stop.lat},${stop.lon}`} position={[stop.lat, stop.lon]} icon={stopIcon(i + 1)} interactive={false} />
        ))}
      <ZoomControl position="bottomright" />
      <FitView points={focus} inset={paneInset} />
      <PlanningClicks planning={planning} onAddStop={onAddStop} />
    </MapContainer>
  );
}
