/** Brand + tagline header with the collapse control. */
import aspireLogo from '/Aspire.png';
import { CollapseGlyph } from '../icons/Glyphs';

interface PaneHeaderProps {
  isTouch: boolean;
  onHide: () => void;
}

export function PaneHeader({ isTouch, onHide }: PaneHeaderProps) {
  return (
    <div className="pane-head">
      <img src={aspireLogo} className="pane-logo" alt="Aspire" />
      <div className="pane-title-wrap">
        <h1 className="pane-title">Aspire Weather Explorer</h1>
        <p className="pane-subtitle">
          {isTouch ? 'Tap the map for a live forecast' : 'Click to pin, hold Space to pan'}
        </p>
      </div>
      <button
        type="button"
        className="control-btn pane-hide"
        onClick={onHide}
        aria-label="Hide panel"
        title="Hide panel"
      >
        <CollapseGlyph />
      </button>
    </div>
  );
}
