/** Collapsed-state pill with the fleet's go / delay / reroute counts; taps to reopen the pane. */
import type { Recommendation } from '../../types/fleet';
import { ExpandGlyph, TruckGlyph } from '../icons/Glyphs';

interface PaneLauncherProps {
  totals: Record<Recommendation, number>;
  onExpand: () => void;
}

export function PaneLauncher({ totals, onExpand }: PaneLauncherProps) {
  return (
    <button type="button" className="pane-launcher" onClick={onExpand} aria-label="Show dispatch panel">
      <span className="launcher-icon">
        <TruckGlyph size={24} />
      </span>
      <span className="launcher-readout">
        <span className="launcher-title">Fleet Weather Risk</span>
        <span className="launcher-counts">
          {totals.reroute} reroute · {totals.delay} delay · {totals.go} go
        </span>
      </span>
      <span className="launcher-expand" aria-hidden="true">
        <ExpandGlyph />
      </span>
    </button>
  );
}
