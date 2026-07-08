/** Floating layers button + upward popup for switching the base map style. */
import { useEffect, useRef, useState } from 'react';
import { MAP_STYLES, stylePreview } from '../../lib/mapStyles';
import { LayersGlyph } from '../icons/Glyphs';

interface MapStyleControlProps {
  mapStyle: string;
  onSelectStyle: (key: string) => void;
}

export function MapStyleControl({ mapStyle, onSelectStyle }: MapStyleControlProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  // Close the menu on an outside click.
  useEffect(() => {
    if (!open) return;
    const handlePointerDown = (event: PointerEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [open]);

  return (
    <div className="map-style-control" ref={ref}>
      {open && (
        <div className="map-style-menu" role="menu" aria-label="Map style">
          {MAP_STYLES.map((style) => (
            <button
              key={style.key}
              type="button"
              role="menuitemradio"
              aria-checked={style.key === mapStyle}
              className={`map-style-option ${style.key === mapStyle ? 'is-active' : ''}`}
              onClick={() => {
                onSelectStyle(style.key);
                setOpen(false);
              }}
            >
              <img className="map-style-thumb" src={stylePreview(style)} alt="" loading="lazy" />
              <span>{style.label}</span>
            </button>
          ))}
        </div>
      )}
      <button
        type="button"
        className="control-btn map-style"
        aria-label="Map style"
        aria-haspopup="menu"
        aria-expanded={open}
        title="Map style"
        onClick={() => setOpen((value) => !value)}
      >
        <LayersGlyph />
      </button>
    </div>
  );
}
