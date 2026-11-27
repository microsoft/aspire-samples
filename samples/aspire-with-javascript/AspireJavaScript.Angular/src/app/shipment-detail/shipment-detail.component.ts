import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { StatusChipComponent } from '../status-chip/status-chip.component';
import { ShipmentDetail } from '../../types/shipment';

const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto', style: 'short' });

@Component({
  selector: 'app-shipment-detail',
  standalone: true,
  imports: [DatePipe, DecimalPipe, MatIconModule, StatusChipComponent],
  templateUrl: './shipment-detail.component.html',
  styleUrl: './shipment-detail.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShipmentDetailComponent {
  readonly detail = input.required<ShipmentDetail>();

  protected readonly shipment = computed(() => this.detail().shipment);

  /** e.g. "in 9 hr" or "3 hr ago". */
  protected readonly etaRelative = computed(() => {
    const minutes = Math.round((new Date(this.shipment().eta).getTime() - Date.now()) / 60_000);
    if (Math.abs(minutes) < 60) return relative.format(minutes, 'minute');
    const hours = Math.round(minutes / 60);
    if (Math.abs(hours) < 48) return relative.format(hours, 'hour');
    return relative.format(Math.round(hours / 24), 'day');
  });
}