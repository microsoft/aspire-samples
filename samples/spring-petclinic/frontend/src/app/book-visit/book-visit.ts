import { KeyValuePipe, LowerCasePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { ClinicApi, toProblem } from '../clinic-api';
import {
  Owner,
  Problem,
  Vet,
  Visit,
  VisitReason,
  VisitReasonCode,
  isoDate,
  parseLocal,
  speciesGlyph,
} from '../models';

interface Booking {
  ownerId: number | null;
  petId: number | null;
  reason: VisitReasonCode | null;
  date: string;
  time: string | null;
  vetId: number | null;
  notes: string;
}

const SLOTS = Array.from({ length: 20 }, (_, i) => {
  const minutes = 8 * 60 + i * 30;
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
});

const WHEN = new Intl.DateTimeFormat('en-US', { weekday: 'short', month: 'short', day: 'numeric' });

@Component({
  selector: 'app-book-visit',
  imports: [
    FormsModule,
    KeyValuePipe,
    LowerCasePipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './book-visit.html',
  styleUrl: './book-visit.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookVisitPanel {
  private readonly api = inject(ClinicApi);

  readonly owners = input.required<Owner[]>();
  readonly vets = input.required<Vet[]>();
  readonly reasons = input.required<VisitReason[]>();
  readonly booked = output<Visit>();

  protected readonly slots = SLOTS;
  protected readonly today = isoDate(new Date());
  protected readonly glyph = speciesGlyph;
  protected readonly saving = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  protected readonly confirmation = signal<Visit | null>(null);
  protected readonly ownerId = signal<number | null>(null);
  protected readonly reason = signal<VisitReasonCode | null>(null);

  protected booking: Booking = this.empty();

  protected readonly pets = computed(
    () => this.owners().find((o) => o.id === this.ownerId())?.pets ?? [],
  );
  protected readonly specialty = computed(
    () => this.reasons().find((r) => r.reason === this.reason())?.specialty,
  );

  protected ownerChanged(ownerId: number | null): void {
    this.ownerId.set(ownerId);
    const pets = this.pets();
    this.booking.petId = pets.length === 1 ? pets[0].id : null;
  }

  protected reasonChanged(reason: VisitReasonCode | null): void {
    this.reason.set(reason);
  }

  protected submit(form: NgForm): void {
    const { ownerId, petId, reason, date, time, vetId, notes } = this.booking;
    if (
      form.invalid ||
      this.saving() ||
      ownerId === null ||
      petId === null ||
      reason === null ||
      time === null
    ) {
      return;
    }

    this.saving.set(true);
    this.problem.set(null);
    this.confirmation.set(null);
    this.api
      .bookVisit(ownerId, petId, {
        startsAt: `${date}T${time}:00`,
        reason,
        vetId,
        notes: notes.trim() || null,
      })
      .subscribe({
        next: (visit) => {
          this.saving.set(false);
          this.confirmation.set(visit);
          this.booked.emit(visit);
          this.booking = { ...this.empty(), ownerId, petId, date };
          this.reason.set(null);
          form.resetForm(this.booking);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.problem.set(toProblem(error));
        },
      });
  }

  /** Moves the booking to the slot the API suggested in its 409 problem details, and books it. */
  protected takeSuggestedSlot(form: NgForm, slot: string): void {
    this.booking.date = slot.slice(0, 10);
    this.booking.time = slot.slice(11, 16);
    this.submit(form);
  }

  protected when(value: string): string {
    return `${WHEN.format(parseLocal(value))} at ${value.slice(11, 16)}`;
  }

  private empty(): Booking {
    return {
      ownerId: null,
      petId: null,
      reason: null,
      date: this.tomorrow(),
      time: null,
      vetId: null,
      notes: '',
    };
  }

  private tomorrow(): string {
    const now = new Date();
    return isoDate(new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1));
  }
}
