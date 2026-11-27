/** Brand + tagline header with the collapse control. */
import aspireLogo from '/Aspire.png';
import { CollapseGlyph } from '../icons/Glyphs';

export function PaneHeader({ onHide }: { onHide: () => void }) {
  return (
    <div className="pane-head">
      <img src={aspireLogo} className="pane-logo" alt="Aspire" />
      <div className="pane-title-wrap">
        <h1 className="pane-title">Fleet Weather Risk</h1>
        <p className="pane-subtitle">Contoso Freight linehaul dispatch</p>
      </div>
      <button type="button" className="control-btn pane-hide" onClick={onHide} aria-label="Hide panel" title="Hide panel">
        <CollapseGlyph />
      </button>
    </div>
  );
}
