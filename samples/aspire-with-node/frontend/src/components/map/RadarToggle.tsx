/** Floating toggle for the rain-radar overlay. */
import { RadarGlyph } from '../icons/Glyphs';

interface RadarToggleProps {
  radarOn: boolean;
  onToggle: () => void;
}

export function RadarToggle({ radarOn, onToggle }: RadarToggleProps) {
  return (
    <button
      type="button"
      className={`control-btn control-toggle map-radar ${radarOn ? 'is-on' : ''}`}
      aria-pressed={radarOn}
      onClick={onToggle}
      aria-label="Toggle rain radar"
      title="Toggle rain radar"
    >
      <RadarGlyph />
    </button>
  );
}
