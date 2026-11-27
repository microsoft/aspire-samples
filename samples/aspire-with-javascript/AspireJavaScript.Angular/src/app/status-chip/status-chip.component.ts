import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { STATUS_LABELS, ShipmentStatus } from '../../types/shipment';

const STATUS_ICONS: Record<ShipmentStatus, string> = {
  'label-created': 'new_label',
  'in-transit': 'conversion_path',
  'out-for-delivery': 'local_shipping',
  delivered: 'package_2',
  exception: 'report',
};

@Component({
  selector: 'app-status-chip',
  standalone: true,
  imports: [MatIconModule],
  template: `<mat-icon aria-hidden="true">{{ icon() }}</mat-icon>{{ label() }}`,
  styleUrl: './status-chip.component.css',
  host: { '[attr.data-status]': 'status()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusChipComponent {
  readonly status = input.required<ShipmentStatus>();
  protected readonly icon = computed(() => STATUS_ICONS[this.status()]);
  protected readonly label = computed(() => STATUS_LABELS[this.status()]);
}