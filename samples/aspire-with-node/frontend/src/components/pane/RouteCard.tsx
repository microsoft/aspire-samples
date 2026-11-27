/** One route in the dispatch list: recommendation, risk score, a leg-by-leg risk strip and, when selected, the legs. */
import { formatConditions, formatHazardPeak, formatPeakTime, RECOMMENDATION_LABELS } from '../../lib/format';
import type { LegAssessment, RouteAssessment } from '../../types/fleet';

interface RouteCardProps {
  title: string;
  meta: string;
  /** Leg distances, used for the strip before the first assessment arrives. */
  legDistances: number[];
  assessment?: RouteAssessment;
  selected: boolean;
  pending: boolean;
  /** The last attempt to assess this route failed and there's no earlier result to show. */
  failed?: boolean;
  onSelect: () => void;
}

function LegRow({ leg }: { leg: LegAssessment }) {
  return (
    <li className="leg-row">
      <span className={`leg-score risk-${leg.riskLevel}`} title={`Risk ${leg.riskScore} (${leg.riskLevel})`}>
        {leg.riskScore}
      </span>
      <div className="leg-body">
        <div className="leg-head">
          <span className="leg-name">
            {leg.from} <span aria-hidden="true">→</span>
            <span className="visually-hidden">to</span> {leg.to}
          </span>
          <span className="leg-distance">{leg.distanceKm} km</span>
        </div>
        {leg.hazards.length > 0 && (
          <ul className="hazard-list" aria-label="Hazards">
            {leg.hazards.map((hazard) => (
              <li key={hazard.type} className={`hazard-chip sev-${hazard.severity}`} title={`Peaks ${formatPeakTime(hazard.peakAt)}`}>
                {hazard.label} <b>{formatHazardPeak(hazard)}</b>
              </li>
            ))}
          </ul>
        )}
        <p className="leg-conditions">{formatConditions(leg)}</p>
      </div>
    </li>
  );
}

export function RouteCard({ title, meta, legDistances, assessment, selected, pending, failed, onSelect }: RouteCardProps) {
  const recommendation = assessment?.recommendation;
  const strip = assessment?.legs.map((leg) => ({ km: leg.distanceKm, level: leg.riskLevel })) ??
    legDistances.map((km) => ({ km, level: 'pending' }));

  return (
    <article className={`route-card${selected ? ' is-selected' : ''}${pending ? ' is-pending' : ''}`}>
      <button type="button" className="route-head" onClick={onSelect} aria-expanded={selected}>
        <span className={`rec-badge rec-${recommendation ?? 'pending'}`}>
          {recommendation ? RECOMMENDATION_LABELS[recommendation] : failed && !pending ? 'No data' : 'Checking'}
        </span>
        <span className="route-title">
          <span className="route-name">{title}</span>
          <span className="route-meta">{meta}</span>
        </span>
        <span className={`risk-score risk-${assessment?.riskLevel ?? 'pending'}`} aria-label={`Risk score ${assessment?.overallRiskScore ?? 'pending'}`}>
          {pending ? <span className="spinner" /> : (assessment?.overallRiskScore ?? '–')}
        </span>
      </button>
      <div className="leg-strip" aria-hidden="true">
        {strip.map((segment, i) => (
          <span key={i} className={`risk-${segment.level}`} style={{ flexGrow: segment.km }} />
        ))}
      </div>
      {selected && assessment && (
        <div className="route-detail">
          <p className="route-summary">{assessment.summary}</p>
          <ol className="leg-list">
            {assessment.legs.map((leg) => (
              <LegRow key={leg.index} leg={leg} />
            ))}
          </ol>
        </div>
      )}
    </article>
  );
}
