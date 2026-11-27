/**
 * Composition root: wires the fleet and radar hooks to the map, the dispatch pane and the map
 * controls. State shared across regions (pane visibility, map style, control errors) lives here.
 */
import { useCallback, useState } from 'react';
import { FleetMap } from './components/map/FleetMap';
import { MapStyleControl } from './components/map/MapStyleControl';
import { RadarLegend } from './components/map/RadarLegend';
import { RadarTimeline } from './components/map/RadarTimeline';
import { RadarToggle } from './components/map/RadarToggle';
import { ControlPane } from './components/pane/ControlPane';
import { PaneLauncher } from './components/pane/PaneLauncher';
import { useFleet } from './hooks/useFleet';
import { useRadar } from './hooks/useRadar';
import { defaultStyleKey, MAP_STYLES } from './lib/mapStyles';
import './App.css';

// Pane width plus its left margin (see .control-pane in App.css).
const PANE_INSET = 432;

const isNarrow = () => typeof window !== 'undefined' && window.matchMedia('(max-width: 768px)').matches;

function App() {
  const fleet = useFleet();
  const [controlError, setControlError] = useState<string | null>(null);
  const radar = useRadar({ onError: setControlError });
  const [narrow] = useState(isNarrow);
  const [paneCollapsed, setPaneCollapsed] = useState(narrow);
  const [mapStyle, setMapStyle] = useState(defaultStyleKey);

  const activeStyle = MAP_STYLES.find((style) => style.key === mapStyle) ?? MAP_STYLES[0];
  const radarBarVisible = radar.radarOn && radar.radarFrames.length > 0;
  const { selectRoute } = fleet;

  // Picking a route on the map reveals its details in the pane (on phones the pane covers
  // the map, so it stays collapsed there).
  const selectFromMap = useCallback(
    (id: string) => {
      selectRoute(id);
      if (!narrow) setPaneCollapsed(false);
    },
    [narrow, selectRoute],
  );

  return (
    <div className={`app${fleet.planning ? ' is-planning' : ''}${radar.radarAnim ? ' radar-anim' : ''}`}>
      <FleetMap
        activeStyle={activeStyle}
        radarOn={radar.radarOn}
        radarFrames={radar.radarFrames}
        radarIndex={radar.radarIndex}
        routes={fleet.routes}
        assessments={fleet.assessments}
        selectedId={fleet.selectedId}
        planning={fleet.planning}
        stops={fleet.stops}
        paneInset={paneCollapsed || narrow ? 0 : PANE_INSET}
        onSelectRoute={selectFromMap}
        onAddStop={fleet.addStop}
      />

      <ControlPane
        fleet={fleet}
        collapsed={paneCollapsed}
        radarActive={radar.radarOn}
        controlError={controlError}
        onHide={() => setPaneCollapsed(true)}
      />

      <MapStyleControl mapStyle={mapStyle} onSelectStyle={setMapStyle} />

      <RadarToggle radarOn={radar.radarOn} onToggle={radar.toggleRadar} />

      {paneCollapsed && <PaneLauncher totals={fleet.totals} onExpand={() => setPaneCollapsed(false)} />}

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
