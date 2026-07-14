/** Sunrise / sunset times. */
import { formatClock } from '../../lib/format';
import { SunriseGlyph, SunsetGlyph } from '../icons/Glyphs';

interface SunTimesProps {
  sunrise: string | null;
  sunset: string | null;
}

export function SunTimes({ sunrise, sunset }: SunTimesProps) {
  return (
    <div className="sun">
      <div className="sun-item">
        <SunriseGlyph />
        <span className="sun-label">Sunrise</span>
        <time className="sun-time">{formatClock(sunrise)}</time>
      </div>
      <div className="sun-item">
        <SunsetGlyph />
        <span className="sun-label">Sunset</span>
        <time className="sun-time">{formatClock(sunset)}</time>
      </div>
    </div>
  );
}
