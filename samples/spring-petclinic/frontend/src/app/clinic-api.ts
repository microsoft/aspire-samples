import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { context } from '@opentelemetry/api';
import { Observable, forkJoin, map, of, switchMap } from 'rxjs';

import {
  BookVisit,
  NewOwner,
  Owner,
  Pet,
  Problem,
  Stats,
  Vet,
  Visit,
  VisitReason,
  isoDate,
} from './models';
import { resume, traced } from './tracing';

export interface FrontDesk {
  stats: Stats;
  owners: Owner[];
  vets: Vet[];
  reasons: VisitReason[];
  visits: Visit[];
}

/** The Spring Petclinic API. `/api` is proxied same-origin by the Angular dev server (see proxy.conf.cjs). */
@Injectable({ providedIn: 'root' })
export class ClinicApi {
  private readonly http = inject(HttpClient);

  loadFrontDesk(days: number): Observable<FrontDesk> {
    return traced('load front desk', { 'petclinic.schedule.days': days }, () =>
      forkJoin({
        stats: this.http.get<Stats>('/api/stats'),
        owners: this.http.get<Owner[]>('/api/owners'),
        vets: this.http.get<Vet[]>('/api/vets'),
        reasons: this.http.get<VisitReason[]>('/api/visit-reasons'),
        visits: this.http.get<Visit[]>('/api/visits', {
          params: { from: isoDate(new Date()), days },
        }),
      }),
    );
  }

  searchOwners(lastName: string): Observable<Owner[]> {
    return traced('search owners', { 'petclinic.owner.last_name_prefix': lastName }, () =>
      this.http.get<Owner[]>('/api/owners', { params: lastName ? { lastName } : {} }),
    );
  }

  registerOwner(owner: NewOwner, pet: Omit<Pet, 'id'> | null): Observable<Owner> {
    return traced('register owner', { 'petclinic.owner.city': owner.city }, () => {
      const ctx = context.active();
      return this.http
        .post<Owner>('/api/owners', owner)
        .pipe(
          switchMap((created) =>
            pet
              ? resume(ctx, () => this.http.post<Pet>(`/api/owners/${created.id}/pets`, pet)).pipe(
                  map((added) => ({ ...created, pets: [added] })),
                )
              : of(created),
          ),
        );
    });
  }

  bookVisit(ownerId: number, petId: number, visit: BookVisit): Observable<Visit> {
    return traced(
      'book visit',
      {
        'petclinic.owner.id': ownerId,
        'petclinic.pet.id': petId,
        'petclinic.visit.reason': visit.reason,
        'petclinic.visit.starts_at': visit.startsAt,
      },
      () => this.http.post<Visit>(`/api/owners/${ownerId}/pets/${petId}/visits`, visit),
    );
  }

  completeVisit(visitId: number): Observable<Visit> {
    return traced('complete visit', { 'petclinic.visit.id': visitId }, () =>
      this.http.post<Visit>(`/api/visits/${visitId}/complete`, null),
    );
  }

  cancelVisit(visitId: number): Observable<Visit> {
    return traced('cancel visit', { 'petclinic.visit.id': visitId }, () =>
      this.http.post<Visit>(`/api/visits/${visitId}/cancel`, null),
    );
  }
}

/** Reads the API's problem details from a failed request, with a fallback for network failures. */
export function toProblem(error: unknown): Problem {
  if (error instanceof HttpErrorResponse && error.error && typeof error.error === 'object') {
    return error.error as Problem;
  }
  return {
    title: 'The clinic API is unavailable',
    detail: 'Check the api and postgres resources in the Aspire dashboard, then try again.',
  };
}
