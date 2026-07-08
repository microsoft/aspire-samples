/** The Leaflet map: base tiles, optional overlays + radar frames, reticle, and click-to-pin. */
import { useMemo } from 'react';
import type { RefObject } from 'react';
import { MapContainer, Marker, TileLayer, ZoomControl } from 'react-leaflet';
import { divIcon, type Map as LeafletMap } from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { INITIAL_CENTER, INITIAL_ZOOM } from '../../lib/mapStyles';
import type { LatLng, MapStyle, RadarFrame } from '../../types/map';
import { MapClickHandler } from './MapClickHandler';

const RETICLE_HTML =
  '<span class="reticle-ping"></span><span class="reticle-inner"><svg width="46" height="46" viewBox="0 0 46 46" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">' +
  '<circle cx="23" cy="23" r="11" />' +
  '<line x1="23" y1="2" x2="23" y2="12" />' +
  '<line x1="23" y1="34" x2="23" y2="44" />' +
  '<line x1="2" y1="23" x2="12" y2="23" />' +
  '<line x1="34" y1="23" x2="44" y2="23" />' +
  '<circle cx="23" cy="23" r="2.5" fill="currentColor" stroke="none" />' +
  '</svg></span>';

interface WeatherMapProps {
  activeStyle: MapStyle;
  radarOn: boolean;
  radarFrames: RadarFrame[];
  radarIndex: number;
  selected: LatLng;
  onSelect: (lat: number, lon: number) => void;
  spaceHeldRef: RefObject<boolean>;
  onMapReady: (map: LeafletMap | null) => void;
}

export function WeatherMap({
  activeStyle,
  radarOn,
  radarFrames,
  radarIndex,
  selected,
  onSelect,
  spaceHeldRef,
  onMapReady,
}: WeatherMapProps) {
  // Custom divIcon avoids missing default-marker assets under bundlers.
  const reticleIcon = useMemo(
    () => divIcon({ className: 'map-reticle', iconSize: [46, 46], iconAnchor: [23, 23], html: RETICLE_HTML }),
    [],
  );

  return (
    <MapContainer
      className="map"
      center={INITIAL_CENTER}
      zoom={INITIAL_ZOOM}
      zoomControl={false}
      worldCopyJump
      minZoom={2}
      ref={onMapReady}
    >
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
            attribution={
              i === 0 ? 'Radar &copy; <a href="https://www.rainviewer.com/">RainViewer</a>' : undefined
            }
          />
        ))}
      <Marker
        key={`${selected[0]},${selected[1]}`}
        position={selected}
        icon={reticleIcon}
        interactive={false}
        keyboard={false}
      />
      <ZoomControl position="bottomright" />
      <MapClickHandler onSelect={onSelect} spaceHeldRef={spaceHeldRef} />
    </MapContainer>
  );
}
