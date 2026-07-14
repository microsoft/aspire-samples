/**
 * Composition root: wires the data/interaction hooks to the map, control pane, and
 * radar overlays. State that several regions share (selection, pane visibility, unit,
 * map style, control errors) lives here; everything else is owned by its own module.
 */
import { useCallback, useEffect, useState } from 'react';
import type { CSSProperties } from 'react';
import { MapStyleControl } from './components/map/MapStyleControl';
import { RadarLegend } from './components/map/RadarLegend';
import { RadarTimeline } from './components/map/RadarTimeline';
import { RadarToggle } from './components/map/RadarToggle';
import { WeatherMap } from './components/map/WeatherMap';
import { ControlPane } from './components/pane/ControlPane';
import { PaneLauncher } from './components/pane/PaneLauncher';
import { useGeocodeSearch } from './hooks/useGeocodeSearch';
import { useMapInteraction } from './hooks/useMapInteraction';
import { useRadar } from './hooks/useRadar';
import { useWeather } from './hooks/useWeather';
import { primaryLocationLabel } from './lib/format';
import { INITIAL_CENTER, MAP_STYLES } from './lib/mapStyles';
import { describeWeather } from './lib/weatherCodes';
import { skyTint } from './lib/weatherVisuals';
import type { LatLng } from './types/map';
import './App.css';

function App() {
  const { weather, loading, error, fetchWeather } = useWeather();
  const { map, handleMapRef, isTouch, panMode, spaceHeldRef } = useMapInteraction();

  const [useCelsius, setUseCelsius] = useState(false);
  const [paneCollapsed, setPaneCollapsed] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('(max-width: 768px)').matches,
  );
  const [selected, setSelected] = useState<LatLng>(INITIAL_CENTER);
  const [controlError, setControlError] = useState<string | null>(null);
  const [mapStyle, setMapStyle] = useState('streets');

  const radar = useRadar({ onError: setControlError });

  // Initial load for the default center.
  useEffect(() => {
    fetchWeather(INITIAL_CENTER[0], INITIAL_CENTER[1]);
  }, [fetchWeather]);

  // Pick a point on the map: drop the marker and load its weather. On desktop the pane is
  // a side panel that doesn't cover the map, so reveal it; on touch it's a full-screen
  // drawer, so keep it collapsed and let the launcher reflect the pick (the map stays pannable).
  const selectLocation = useCallback(
    (lat: number, lon: number) => {
      setSelected([lat, lon]);
      if (!isTouch) setPaneCollapsed(false);
      fetchWeather(lat, lon);
    },
    [fetchWeather, isTouch],
  );

  // Glide the camera to a coordinate, drop the marker, reveal the pane, and load weather.
  const flyTo = useCallback(
    (lat: number, lon: number) => {
      setSelected([lat, lon]);
      setPaneCollapsed(false);
      map?.flyTo([lat, lon], Math.max(map.getZoom(), 9), { duration: 0.85 });
      fetchWeather(lat, lon);
    },
    [map, fetchWeather],
  );

  const clearControlError = useCallback(() => setControlError(null), []);
  const search = useGeocodeSearch({ onSelect: flyTo, onClearError: clearControlError });

  const handleLocate = useCallback(() => {
    if (!('geolocation' in navigator)) {
      setControlError('Geolocation is not supported here.');
      return;
    }
    setControlError(null);
    navigator.geolocation.getCurrentPosition(
      (position) => flyTo(position.coords.latitude, position.coords.longitude),
      () => setControlError('Could not get your location.'),
      { enableHighAccuracy: true, timeout: 8000 },
    );
  }, [flyTo]);

  const current = weather?.current;
  const condition = current ? describeWeather(current.weatherCode) : null;
  const skyTintValue = current && condition ? skyTint(current.isDay, condition.kind) : 'transparent';
  const appStyle = { '--sky-tint': skyTintValue } as CSSProperties;
  const activeStyle = MAP_STYLES.find((style) => style.key === mapStyle) ?? MAP_STYLES[0];
  const radarBarVisible = radar.radarOn && radar.radarFrames.length > 0;

  return (
    <div
      className={`app ${panMode ? 'pan-mode' : ''} ${radar.radarAnim ? 'radar-anim' : ''}`}
      style={appStyle}
    >
      <WeatherMap
        activeStyle={activeStyle}
        radarOn={radar.radarOn}
        radarFrames={radar.radarFrames}
        radarIndex={radar.radarIndex}
        selected={selected}
        onSelect={selectLocation}
        spaceHeldRef={spaceHeldRef}
        onMapReady={handleMapRef}
      />

      <ControlPane
        collapsed={paneCollapsed}
        radarActive={radar.radarOn}
        isTouch={isTouch}
        onHide={() => setPaneCollapsed(true)}
        search={search}
        onLocate={handleLocate}
        controlError={controlError}
        weather={weather}
        loading={loading}
        error={error}
        useCelsius={useCelsius}
        onUnitChange={setUseCelsius}
      />

      <MapStyleControl mapStyle={mapStyle} onSelectStyle={setMapStyle} />

      <RadarToggle radarOn={radar.radarOn} onToggle={radar.toggleRadar} />

      {paneCollapsed && (
        <PaneLauncher
          current={current}
          useCelsius={useCelsius}
          primaryLocation={primaryLocationLabel(weather)}
          onExpand={() => setPaneCollapsed(false)}
        />
      )}

      {radarBarVisible && (
        <RadarTimeline
          frames={radar.radarFrames}
          index={radar.radarIndex}
          playing={radar.radarPlaying}
          scrubbing={radar.radarScrubbing}
          hover={radar.radarHover}
          last={radar.radarLast}
          progress={radar.radarProgress}
          nowIndex={radar.radarNowIndex}
          setIndex={radar.setRadarIndex}
          setPlaying={radar.setRadarPlaying}
          setScrubbing={radar.setRadarScrubbing}
          setHover={radar.setRadarHover}
        />
      )}

      {radarBarVisible && <RadarLegend />}
    </div>
  );
}

export default App;
