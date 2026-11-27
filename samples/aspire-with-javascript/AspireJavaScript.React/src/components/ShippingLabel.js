import { Barcode, MapPin, Package, Scales, Truck } from "@phosphor-icons/react";
import StatusChip from "./StatusChip";
import { STATUS_LABELS, formatDateTime } from "../lib/shipments";
import styles from "./ShippingLabel.module.css";

// A decorative barcode derived from the tracking number's digits.
function Bars({ id }) {
  const digits = id.replace(/\D/g, "").split("").map(Number);
  const widths = digits.flatMap((d, i) => [1 + (d % 3), 1 + ((d + i) % 2), 2 + ((d * 7 + i) % 3)]);
  return (
    <span className={styles.bars} aria-hidden="true">
      {widths.map((w, i) => (
        <span key={i} style={{ width: `${w * 2}px`, opacity: i % 2 === 0 ? 1 : 0 }} />
      ))}
    </span>
  );
}

export default function ShippingLabel({ detail }) {
  const { shipment, history } = detail;
  const delivered = shipment.status === "delivered";

  return (
    <article className={styles.label} data-status={shipment.status} aria-labelledby="label-id">
      <header className={styles.head}>
        <span className={styles.brand}>
          <Package size={22} weight="fill" aria-hidden="true" />
          Contoso Shipping
        </span>
        <span className={styles.carrier}>
          <Truck size={18} weight="bold" aria-hidden="true" />
          {shipment.carrier}
        </span>
      </header>

      <div className={styles.addresses}>
        <div className={styles.from}>
          <span className={styles.caption}>From</span>
          <strong>{shipment.origin}</strong>
        </div>
        <div className={styles.to}>
          <span className={styles.caption}>Ship to</span>
          <strong>{shipment.destination}</strong>
        </div>
      </div>

      <div className={styles.barcode}>
        <Bars id={shipment.id} />
        <span id="label-id" className={styles.id}>
          <Barcode size={18} weight="bold" aria-hidden="true" />
          {shipment.id}
        </span>
      </div>

      <dl className={styles.facts}>
        <div>
          <dt>{delivered ? "Delivered" : "ETA"}</dt>
          <dd>{formatDateTime(shipment.eta)}</dd>
        </div>
        <div>
          <dt>Weight</dt>
          <dd>
            <Scales size={16} weight="bold" aria-hidden="true" /> {shipment.weightKg.toFixed(1)} kg
          </dd>
        </div>
        <div>
          <dt>Route</dt>
          <dd>{shipment.progress}%</dd>
        </div>
      </dl>

      <span className={styles.stamp} data-status={shipment.status} aria-hidden="true">
        {STATUS_LABELS[shipment.status]}
      </span>

      <section className={styles.log} aria-labelledby="log-title">
        <h3 id="log-title" className={styles.logTitle}>
          Tracking log <StatusChip status={shipment.status} />
        </h3>
        <ol className={styles.scans}>
          {history.map((scan, i) => (
            <li key={scan.at} className={i === 0 ? styles.latest : undefined}>
              <span className={styles.when}>{formatDateTime(scan.at)}</span>
              <span className={styles.what}>{scan.description}</span>
              <span className={styles.where}>
                <MapPin size={14} weight="bold" aria-hidden="true" />
                {scan.location}
              </span>
            </li>
          ))}
        </ol>
      </section>
    </article>
  );
}