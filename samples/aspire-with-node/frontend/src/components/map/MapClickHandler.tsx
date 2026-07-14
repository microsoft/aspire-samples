/** Drops a pin on click, ignoring the ghost click that can follow a touch/mouse pan. */
import { useRef } from 'react';
import type { RefObject } from 'react';
import { useMapEvents } from 'react-leaflet';

interface MapClickHandlerProps {
  onSelect: (lat: number, lon: number) => void;
  spaceHeldRef: RefObject<boolean>;
}

export function MapClickHandler({ onSelect, spaceHeldRef }: MapClickHandlerProps) {
  // Timestamp of the last drag end. A touch/mouse pan can emit a trailing "ghost"
  // click; ignoring clicks that land right after a drag keeps panning from
  // accidentally dropping a pin where the pan happened to finish.
  const lastDragEnd = useRef(0);
  useMapEvents({
    dragend: () => {
      lastDragEnd.current = Date.now();
    },
    click: (event) => {
      if (spaceHeldRef.current) return;
      if (Date.now() - lastDragEnd.current < 250) return;
      onSelect(event.latlng.lat, event.latlng.lng);
    },
  });
  return null;
}
