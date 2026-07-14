/**
 * Captures the Leaflet map instance and wires the desktop interaction model:
 * hold Space to pan (otherwise a click drops a pin); on touch, native drag panning.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import type { Map as LeafletMap } from 'leaflet';

/** True when the primary input is touch (phones/tablets), where the map drags natively. */
function detectTouch(): boolean {
  return (
    typeof window !== 'undefined' &&
    (window.matchMedia('(pointer: coarse)').matches || navigator.maxTouchPoints > 0)
  );
}

function isTextEntry(el: EventTarget | null): boolean {
  const node = el as HTMLElement | null;
  if (!node) return false;
  if (node.isContentEditable) return true;
  if (node.tagName === 'TEXTAREA') return true;
  if (node.tagName === 'INPUT') {
    const type = (node as HTMLInputElement).type.toLowerCase();
    return ['text', 'search', 'email', 'url', 'password', 'tel', 'number'].includes(type);
  }
  return false;
}

export function useMapInteraction() {
  const [map, setMap] = useState<LeafletMap | null>(null);
  const [panMode, setPanMode] = useState(false);
  const [isTouch] = useState(detectTouch);
  const spaceHeldRef = useRef(false);

  // Stable callback ref so the map instance is captured once (StrictMode-safe).
  const handleMapRef = useCallback((instance: LeafletMap | null) => {
    if (instance) setMap(instance);
  }, []);

  useEffect(() => {
    if (!map) return;
    if (isTouch) {
      map.dragging.enable();
      return;
    }
    map.dragging.disable();
    // Space-pan is GLOBAL: holding Space always switches to pan mode (grab cursor) no
    // matter which control has focus. The only exception is active text entry, where a
    // space must type a space (e.g. searching "New York").
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.code !== 'Space' || event.repeat) return;
      if (isTextEntry(document.activeElement)) return;
      event.preventDefault();
      if (spaceHeldRef.current) return;
      spaceHeldRef.current = true;
      setPanMode(true);
      map.dragging.enable();
    };
    const onKeyUp = (event: globalThis.KeyboardEvent) => {
      if (event.code !== 'Space' || !spaceHeldRef.current) return;
      event.preventDefault();
      spaceHeldRef.current = false;
      setPanMode(false);
      map.dragging.disable();
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
    };
  }, [map, isTouch]);

  return { map, handleMapRef, isTouch, panMode, spaceHeldRef };
}
