import { CheckCircle, Tag, Truck, Path, Warning } from "@phosphor-icons/react";
import { STATUS_LABELS } from "../lib/shipments";
import styles from "./StatusChip.module.css";

const ICONS = {
  "label-created": Tag,
  "in-transit": Path,
  "out-for-delivery": Truck,
  delivered: CheckCircle,
  exception: Warning,
};

export default function StatusChip({ status }) {
  const Icon = ICONS[status] ?? Tag;
  return (
    <span className={styles.chip} data-status={status}>
      <Icon size={14} weight="bold" aria-hidden="true" />
      {STATUS_LABELS[status]}
    </span>
  );
}