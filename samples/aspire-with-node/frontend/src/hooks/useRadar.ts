/** Owns the rain-radar frame set, playback, scrubbing, and derived timeline values. */
import { useCallback, useEffect, useState } from 'react';
import type { RadarFrame } from '../types/map';

interface RainViewerResponse {
  host: string;
  radar?: {
    past?: { time: number; path: string }[];
    nowcast?: { time: number; path: string }[];
  };
}

interface UseRadarOptions {
  /** Surfaces a user-facing message when the radar feed is unavailable. */
  onError?: (message: string) => void;
}

export function useRadar({ onError }: UseRadarOptions = {}) {
  const [radarOn, setRadarOn] = useState(false);
  const [radarFrames, setRadarFrames] = useState<RadarFrame[]>([]);
  const [radarIndex, setRadarIndex] = useState(0);
  const [radarPlaying, setRadarPlaying] = useState(false);
  const [radarScrubbing, setRadarScrubbing] = useState(false);
  const [radarHover, setRadarHover] = useState<number | null>(null);
  const [radarAnim, setRadarAnim] = useState(false);

  const toggleRadar = useCallback(async () => {
    if (radarOn) {
      setRadarOn(false);
      setRadarPlaying(false);
      setRadarHover(null);
      return;
    }
    try {
      let frames = radarFrames;
      if (frames.length === 0) {
        const res = await fetch('https://api.rainviewer.com/public/weather-maps.json');
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data: RainViewerResponse = await res.json();
        const toFrames = (
          list: { time: number; path: string }[] | undefined,
          forecast: boolean,
        ): RadarFrame[] =>
          (list ?? []).map((frame) => ({
            time: frame.time,
            forecast,
            url: `${data.host}${frame.path}/256/{z}/{x}/{y}/4/1_1.png`,
          }));
        const past = toFrames(data.radar?.past, false);
        frames = [...past, ...toFrames(data.radar?.nowcast, true)];
        if (frames.length === 0) throw new Error('No radar frames available');
        setRadarFrames(frames);
        setRadarIndex(Math.max(0, past.length - 1));
      }
      setRadarOn(true);
      setRadarPlaying(true);
    } catch (err) {
      onError?.('Rain radar is unavailable right now.');
      console.error('[radar] request failed:', err);
    }
  }, [radarOn, radarFrames, onError]);

  // Advance the radar frame while playing, looping back to the start.
  useEffect(() => {
    if (!radarOn || !radarPlaying || radarFrames.length === 0) return;
    const id = window.setInterval(() => {
      setRadarIndex((index) => (index + 1) % radarFrames.length);
    }, 500);
    return () => window.clearInterval(id);
  }, [radarOn, radarPlaying, radarFrames.length]);

  // Enable frame crossfades only after the stacked layers have painted, so turning
  // radar on does not briefly flash every preloaded frame during the first transition.
  useEffect(() => {
    if (!radarOn) {
      setRadarAnim(false);
      return;
    }
    const raf = requestAnimationFrame(() => requestAnimationFrame(() => setRadarAnim(true)));
    return () => cancelAnimationFrame(raf);
  }, [radarOn]);

  const radarLast = radarFrames.length - 1;
  const radarProgress = radarLast > 0 ? (radarIndex / radarLast) * 100 : 0;
  const radarFirstForecast = radarFrames.findIndex((frame) => frame.forecast);
  const radarNowIndex = radarFirstForecast === -1 ? radarLast : radarFirstForecast - 1;

  return {
    radarOn,
    radarFrames,
    radarIndex,
    radarPlaying,
    radarScrubbing,
    radarHover,
    radarAnim,
    setRadarIndex,
    setRadarPlaying,
    setRadarScrubbing,
    setRadarHover,
    toggleRadar,
    radarLast,
    radarProgress,
    radarNowIndex,
  };
}
