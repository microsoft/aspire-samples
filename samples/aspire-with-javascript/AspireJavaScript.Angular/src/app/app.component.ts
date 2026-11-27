import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { SpanStatusCode, trace } from '@opentelemetry/api';
import { StatusChipComponent } from './status-chip/status-chip.component';
import { ShipmentDetailComponent } from './shipment-detail/shipment-detail.component';
import {
  STATUS_LABELS,
  ShipmentBoard,
  ShipmentDetail,
  ShipmentStatus,
  StatusCount,
} from '../types/shipment';

type LoadState = 'loading' | 'ready' | 'error';

// A no-op tracer unless src/telemetry.ts registered a provider (run mode only).
const tracer = trace.getTracer('angular-tracking-board');

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatProgressBarModule,
    StatusChipComponent,
    ShipmentDetailComponent,
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent implements OnInit {
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly statuses = Object.keys(STATUS_LABELS) as ShipmentStatus[];
  protected readonly board = signal<ShipmentBoard | null>(null);
  protected readonly counts = signal<StatusCount[]>([]);
  protected readonly filter = signal<ShipmentStatus | null>(null);
  protected readonly state = signal<LoadState>('loading');

  protected readonly selectedId = signal<string | null>(null);
  protected readonly detail = signal<ShipmentDetail | null>(null);
  protected readonly detailState = signal<LoadState | 'idle'>('idle');

  private readonly http = inject(HttpClient);

  constructor(iconRegistry: MatIconRegistry) {
    // Render every <mat-icon> with the Material Symbols (outlined) font.
    iconRegistry.setDefaultFontSetClass('material-symbols-outlined');
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const status = this.filter();
    this.state.set('loading');
    // The UI span parents the fetch span, so the dashboard shows one browser-to-API trace per board load.
    tracer.startActiveSpan('load shipment board', { attributes: { 'contoso.shipment.status_filter': status ?? 'all' } }, (span) => {
      this.http.get<ShipmentBoard>('api/shipments', { params: status ? { status } : {} }).subscribe({
        next: (result) => {
          this.board.set(result);
          this.counts.set(result.statusCounts.length ? result.statusCounts : this.counts());
          this.state.set('ready');
          const first = result.shipments[0]?.id ?? null;
          if (!result.shipments.some((s) => s.id === this.selectedId())) this.select(first);
          span.end();
        },
        error: (err) => {
          console.error('Failed to load shipments', err);
          this.board.set(null);
          this.state.set('error');
          span.setStatus({ code: SpanStatusCode.ERROR, message: String(err?.message ?? err) });
          span.end();
        },
      });
    });
  }

  protected setFilter(status: ShipmentStatus | null): void {
    if (status === this.filter()) return;
    this.filter.set(status);
    this.load();
  }

  protected select(id: string | null, scrollIntoView = false): void {
    this.selectedId.set(id);
    if (!id) {
      this.detail.set(null);
      this.detailState.set('idle');
      return;
    }
    this.detailState.set('loading');
    // "track shipment" -> GET -> GET /api/shipments/{id} -> shipments.track -> carrier.track, as one trace.
    tracer.startActiveSpan('track shipment', { attributes: { 'contoso.shipment.id': id } }, (span) => {
      this.http.get<ShipmentDetail>(`api/shipments/${encodeURIComponent(id)}`).subscribe({
        next: (result) => {
          if (this.selectedId() === id) {
            this.detail.set(result);
            this.detailState.set('ready');
            if (scrollIntoView && matchMedia('(max-width: 900px)').matches) {
              queueMicrotask(() => document.getElementById('detail')?.scrollIntoView({ behavior: 'smooth' }));
            }
          }
          span.end();
        },
        error: (err) => {
          console.error(`Failed to load shipment ${id}`, err);
          if (this.selectedId() === id) this.detailState.set('error');
          span.setStatus({ code: SpanStatusCode.ERROR, message: String(err?.message ?? err) });
          span.end();
        },
      });
    });
  }

  protected countFor(status: ShipmentStatus): number {
    return this.counts().find((c) => c.status === status)?.count ?? 0;
  }

  protected get total(): number {
    return this.counts().reduce((sum, c) => sum + c.count, 0);
  }

  protected get statusText(): string {
    switch (this.state()) {
      case 'loading':
        return 'Loading shipments…';
      case 'error':
        return 'Could not reach the shipments API. Select refresh to try again.';
      default: {
        const shown = this.board()?.shipments.length ?? 0;
        const filter = this.filter();
        return `Showing ${shown} ${shown === 1 ? 'shipment' : 'shipments'}${filter ? ` · ${STATUS_LABELS[filter]}` : ''}.`;
      }
    }
  }
}