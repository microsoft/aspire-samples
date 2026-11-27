import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { BookVisitPanel } from './book-visit/book-visit';
import { ClinicApi, toProblem } from './clinic-api';
import { Owner, Problem, Stats, Vet, Visit, VisitReason, isoDate } from './models';
import { OwnersPanel } from './owners/owners';
import { SchedulePanel } from './schedule/schedule';

const SCHEDULE_DAYS = 5;

@Component({
  selector: 'app-root',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatTooltipModule,
    BookVisitPanel,
    OwnersPanel,
    SchedulePanel,
  ],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App implements OnInit {
  private readonly api = inject(ClinicApi);

  protected readonly scheduleDays = SCHEDULE_DAYS;
  protected readonly today = new Date();
  protected readonly loading = signal(true);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly stats = signal<Stats | null>(null);
  protected readonly owners = signal<Owner[]>([]);
  protected readonly vets = signal<Vet[]>([]);
  protected readonly reasons = signal<VisitReason[]>([]);
  protected readonly visits = signal<Visit[]>([]);
  protected readonly lastBookedId = signal<number | null>(null);
  protected readonly selectedDay = signal(isoDate(new Date()));

  protected readonly summary = computed(() => {
    const stats = this.stats();
    return stats
      ? [
          { label: 'owners', value: stats.owners },
          { label: 'pets', value: stats.pets },
          { label: 'vets', value: stats.vets },
          { label: 'visits today', value: stats.visitsToday },
          { label: 'upcoming', value: stats.upcomingVisits },
        ]
      : [];
  });

  constructor() {
    // Render every <mat-icon> with the Material Symbols (outlined) font.
    inject(MatIconRegistry).setDefaultFontSetClass('material-symbols-outlined');
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.problem.set(null);
    this.api.loadFrontDesk(SCHEDULE_DAYS).subscribe({
      next: (desk) => {
        this.stats.set(desk.stats);
        this.owners.set(desk.owners);
        this.vets.set(desk.vets);
        this.reasons.set(desk.reasons);
        this.visits.set(desk.visits);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.problem.set(toProblem(error));
        this.loading.set(false);
      },
    });
  }

  protected onBooked(visit: Visit): void {
    this.lastBookedId.set(visit.id);
    this.selectedDay.set(visit.startsAt.slice(0, 10));
    this.visits.update((visits) =>
      [...visits, visit].sort((a, b) => a.startsAt.localeCompare(b.startsAt) || a.id - b.id),
    );
    this.adjustStats(visit, 1);
  }

  protected onVisitChanged(visit: Visit): void {
    this.visits.update((visits) =>
      visits.map((existing) => (existing.id === visit.id ? visit : existing)),
    );
    this.adjustStats(visit, -1);
  }

  protected onOwnerRegistered(owner: Owner): void {
    this.owners.update((owners) =>
      [...owners, owner].sort(
        (a, b) => a.lastName.localeCompare(b.lastName) || a.firstName.localeCompare(b.firstName),
      ),
    );
    this.stats.update(
      (stats) =>
        stats && { ...stats, owners: stats.owners + 1, pets: stats.pets + owner.pets.length },
    );
  }

  private adjustStats(visit: Visit, upcoming: number): void {
    const isToday = visit.startsAt.startsWith(isoDate(new Date()));
    this.stats.update(
      (stats) =>
        stats && {
          ...stats,
          upcomingVisits: stats.upcomingVisits + upcoming,
          visitsToday:
            stats.visitsToday +
            (isToday && (upcoming > 0 || visit.status === 'CANCELLED') ? upcoming : 0),
        },
    );
  }
}
