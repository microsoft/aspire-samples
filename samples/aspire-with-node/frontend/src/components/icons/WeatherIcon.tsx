/**
 * Crisp, self-contained SVG weather icons keyed off WMO interpretation codes.
 * Using inline SVG (instead of emoji) keeps the look identical across every OS and browser.
 */
import { describeWeather, type WeatherKind } from '../../lib/weatherCodes';

const SUN = '#fbbf24';
const CLOUD = '#cbd5e1';
const CLOUD_EDGE = '#94a3b8';
const RAIN = '#60a5fa';
const SNOW = '#e2f2ff';
const BOLT = '#facc15';

// A cloud that sits high in the box, leaving room below for precipitation.
const cloudTop = (
  <g fill={CLOUD}>
    <circle cx="9" cy="9" r="3.4" />
    <circle cx="14.6" cy="8" r="4" />
    <rect x="6" y="9" width="12" height="4.6" rx="2.3" />
  </g>
);

// A fuller, lower cloud for plain overcast conditions.
const cloudFull = (
  <g fill={CLOUD}>
    <circle cx="8.5" cy="13" r="4.2" />
    <circle cx="15" cy="11.8" r="4.8" />
    <rect x="5" y="13" width="14" height="5.6" rx="2.8" />
  </g>
);

function shapesFor(kind: WeatherKind) {
  switch (kind) {
    case 'clear':
      return (
        <>
          <circle cx="12" cy="12" r="5" fill={SUN} />
          <g stroke={SUN} strokeWidth="2" strokeLinecap="round">
            <line x1="12" y1="1.5" x2="12" y2="4" />
            <line x1="12" y1="20" x2="12" y2="22.5" />
            <line x1="1.5" y1="12" x2="4" y2="12" />
            <line x1="20" y1="12" x2="22.5" y2="12" />
            <line x1="4.4" y1="4.4" x2="6.2" y2="6.2" />
            <line x1="17.8" y1="17.8" x2="19.6" y2="19.6" />
            <line x1="19.6" y1="4.4" x2="17.8" y2="6.2" />
            <line x1="6.2" y1="17.8" x2="4.4" y2="19.6" />
          </g>
        </>
      );
    case 'partly':
      return (
        <>
          <circle cx="8" cy="8" r="3.4" fill={SUN} />
          <g stroke={SUN} strokeWidth="1.6" strokeLinecap="round">
            <line x1="8" y1="1.6" x2="8" y2="3.1" />
            <line x1="1.6" y1="8" x2="3.1" y2="8" />
            <line x1="3.4" y1="3.4" x2="4.5" y2="4.5" />
            <line x1="12.6" y1="3.4" x2="11.5" y2="4.5" />
          </g>
          <g fill={CLOUD}>
            <circle cx="12" cy="14.5" r="3.6" />
            <circle cx="16.4" cy="13.4" r="4.1" />
            <rect x="9.5" y="14.5" width="9.5" height="4.9" rx="2.45" />
          </g>
        </>
      );
    case 'cloud':
      return cloudFull;
    case 'fog':
      return (
        <>
          {cloudTop}
          <g stroke={CLOUD_EDGE} strokeWidth="2" strokeLinecap="round">
            <line x1="5" y1="16.5" x2="19" y2="16.5" />
            <line x1="6.5" y1="19.5" x2="17.5" y2="19.5" />
            <line x1="8" y1="22.5" x2="16" y2="22.5" />
          </g>
        </>
      );
    case 'rain':
      return (
        <>
          {cloudTop}
          <g stroke={RAIN} strokeWidth="2" strokeLinecap="round">
            <line x1="8.5" y1="16" x2="7.2" y2="19" />
            <line x1="12" y1="16" x2="10.7" y2="19" />
            <line x1="15.5" y1="16" x2="14.2" y2="19" />
          </g>
        </>
      );
    case 'sleet':
      return (
        <>
          {cloudTop}
          <line x1="9" y1="16" x2="7.8" y2="18.8" stroke={RAIN} strokeWidth="2" strokeLinecap="round" />
          <line x1="15" y1="16" x2="13.8" y2="18.8" stroke={RAIN} strokeWidth="2" strokeLinecap="round" />
          <circle cx="12" cy="19.5" r="1.15" fill={SNOW} />
        </>
      );
    case 'snow':
      return (
        <>
          {cloudTop}
          <g fill={SNOW}>
            <circle cx="8.5" cy="17.5" r="1.15" />
            <circle cx="12" cy="19.5" r="1.15" />
            <circle cx="15.5" cy="17.5" r="1.15" />
          </g>
        </>
      );
    case 'thunder':
      return (
        <>
          {cloudTop}
          <polygon points="13,14.5 9.5,19.5 12,19.5 11,23 15.5,17.5 12.8,17.5 14.2,14.5" fill={BOLT} />
        </>
      );
    default:
      return cloudFull;
  }
}

interface WeatherIconProps {
  code: number;
  size?: number;
  className?: string;
  /** When true, the icon is purely decorative (a text label is shown alongside it). */
  decorative?: boolean;
}

export function WeatherIcon({ code, size = 24, className, decorative }: WeatherIconProps) {
  const { label, kind } = describeWeather(code);
  const a11y = decorative
    ? ({ 'aria-hidden': true } as const)
    : ({ role: 'img', 'aria-label': label } as const);
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      {...a11y}
    >
      {!decorative && <title>{label}</title>}
      {shapesFor(kind)}
    </svg>
  );
}
