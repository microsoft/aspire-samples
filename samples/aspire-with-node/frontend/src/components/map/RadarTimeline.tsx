/** Scrubbable radar playback timeline with per-frame ticks and a hover tooltip. */
import type { CSSProperties, Dispatch, SetStateAction } from 'react';
import { formatFrameTime } from '../../lib/format';
import type { RadarFrame } from '../../types/map';

interface RadarTimelineProps {
  frames: RadarFrame[];
  index: number;
  playing: boolean;
  scrubbing: boolean;
  hover: number | null;
  last: number;
  progress: number;
  nowIndex: number;
  setIndex: Dispatch<SetStateAction<number>>;
  setPlaying: Dispatch<SetStateAction<boolean>>;
  setScrubbing: Dispatch<SetStateAction<boolean>>;
  setHover: Dispatch<SetStateAction<number | null>>;
}

export function RadarTimeline({
  frames,
  index,
  playing,
  scrubbing,
  hover,
  last,
  progress,
  nowIndex,
  setIndex,
  setPlaying,
  setScrubbing,
  setHover,
}: RadarTimelineProps) {
  return (
    <div className="radar-bar" role="group" aria-label="Radar timeline">
      <button
        type="button"
        className="control-btn radar-play"
        onClick={() => setPlaying((value) => !value)}
        aria-label={playing ? 'Pause radar animation' : 'Play radar animation'}
        title={playing ? 'Pause' : 'Play'}
      >
        {playing ? (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <rect x="6" y="5" width="4" height="14" rx="1" />
            <rect x="14" y="5" width="4" height="14" rx="1" />
          </svg>
        ) : (
          <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path d="M8 5v14l11-7z" />
          </svg>
        )}
      </button>
      <div
        className={`radar-track ${scrubbing ? 'is-scrubbing' : ''}`}
        style={{ '--radar-progress': `${progress}%` } as CSSProperties}
        onPointerMove={(event) => {
          if (last <= 0) return;
          const rect = event.currentTarget.getBoundingClientRect();
          const railWidth = rect.width - 20;
          if (railWidth <= 0) return;
          const frac = (event.clientX - rect.left - 10) / railWidth;
          const clamped = Math.max(0, Math.min(1, frac));
          setHover(Math.round(clamped * last));
        }}
        onPointerLeave={() => setHover(null)}
      >
        <div className="radar-rail" aria-hidden="true">
          <span className="radar-rail-line" />
          <span className="radar-rail-fill" />
          <div className="radar-ticks">
            {frames.map((frame, i) => (
              <span
                key={frame.url}
                className={`radar-tick${frame.forecast ? ' is-forecast' : ''}${
                  i === nowIndex ? ' is-now' : ''
                }${i === index ? ' is-active' : ''}${i === hover ? ' is-hover' : ''}`}
              />
            ))}
          </div>
          {hover !== null && frames[hover] && (
            <span
              className="radar-hover-tip"
              style={{ left: `${last > 0 ? (hover / last) * 100 : 0}%` }}
            >
              {formatFrameTime(frames[hover])}
              {frames[hover].forecast && <span className="radar-hover-tag">Forecast</span>}
            </span>
          )}
          <span className="radar-thumb" />
        </div>
        <input
          type="range"
          className="radar-range"
          min={0}
          max={last}
          step={1}
          value={index}
          onPointerDown={() => setScrubbing(true)}
          onPointerUp={() => setScrubbing(false)}
          onPointerCancel={() => setScrubbing(false)}
          onChange={(event) => {
            setPlaying(false);
            setIndex(Number(event.target.value));
          }}
          aria-label="Radar time"
        />
      </div>
      <span className="radar-time">
        {formatFrameTime(frames[index])}
        {frames[index].forecast && <span className="radar-tag">Forecast</span>}
      </span>
    </div>
  );
}
