import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Observable } from 'rxjs';

import { ClinicApi, toProblem } from '../clinic-api';
import { Problem, Visit, isoDate, parseLocal, speciesGlyph } from '../models';

interface DayTab {
  key: string;
  label: string;
  count: number;
}

const WEEKDAY = new Intl.DateTimeFormat('en-US', { weekday: 'short' });
const LONG_DAY = new Intl.DateTimeFormat('en-US', {
  weekday: 'long',
  month: 'long',
  day: 'numeric',
});

@Component({
  selector: 'app-schedule',
  imports: [MatButtonModule, MatButtonToggleModule, MatIconModule, MatTooltipModule],
  templateUrl: './schedule.html',
  styleUrl: './schedule.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SchedulePanel {
  private readonly api = inject(ClinicApi);

  readonly visits = input.required<Visit[]>();
  readonly days = input.required<number>();
  readonly highlightId = input<number | null>(null);
  readonly selectedDay = model.required<string>();
  readonly changed = output<Visit>();

  protected readonly busyId = signal<number | null>(null);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly glyph = speciesGlyph;

  protected readonly dayTabs = computed<DayTab[]>(() => {
    const start = new Date();
    return Array.from({ length: this.days() }, (_, offset) => {
      const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + offset);
      const key = isoDate(date);
      const label =
        offset === 0
          ? 'Today'
          : offset === 1
            ? 'Tomorrow'
            : `${WEEKDAY.format(date)} ${date.getDate()}`;
      const count = this.visits().filter(
        (v) => v.startsAt.startsWith(key) && v.status !== 'CANCELLED',
      ).length;
      return { key, label, count };
    });
  });

  protected readonly dayVisits = computed(() =>
    this.visits().filter((visit) => visit.startsAt.startsWith(this.selectedDay())),
  );

  protected readonly dayTitle = computed(() => LONG_DAY.format(parseLocal(this.selectedDay())));

  protected time(visit: Visit): string {
    return visit.startsAt.slice(11, 16);
  }

  protected complete(visit: Visit): void {
    this.update(visit, this.api.completeVisit(visit.id));
  }

  protected cancel(visit: Visit): void {
    this.update(visit, this.api.cancelVisit(visit.id));
  }

  private update(visit: Visit, request: Observable<Visit>): void {
    this.busyId.set(visit.id);
    this.problem.set(null);
    request.subscribe({
      next: (updated) => {
        this.busyId.set(null);
        this.changed.emit(updated);
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.problem.set(toProblem(error));
      },
    });
  }
}
