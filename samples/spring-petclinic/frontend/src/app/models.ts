export interface Stats {
  owners: number;
  pets: number;
  vets: number;
  visitsToday: number;
  upcomingVisits: number;
}

export interface Pet {
  id: number;
  name: string;
  type: string;
  birthDate: string;
}

export interface Owner {
  id: number;
  firstName: string;
  lastName: string;
  address: string;
  city: string;
  telephone: string;
  pets: Pet[];
}

export interface Vet {
  id: number;
  firstName: string;
  lastName: string;
  specialty: string;
}

export type VisitReasonCode =
  | 'WELLNESS_EXAM'
  | 'VACCINATION'
  | 'SICK_VISIT'
  | 'DENTAL_CLEANING'
  | 'IMAGING'
  | 'SURGERY_CONSULT';

export type VisitStatus = 'SCHEDULED' | 'COMPLETED' | 'CANCELLED';

export interface VisitReason {
  reason: VisitReasonCode;
  label: string;
  specialty: string;
}

export interface Visit {
  id: number;
  startsAt: string;
  reason: VisitReasonCode;
  reasonLabel: string;
  status: VisitStatus;
  notes: string | null;
  pet: { id: number; name: string; type: string };
  owner: { id: number; firstName: string; lastName: string; telephone: string };
  vet: Vet;
}

export interface NewOwner {
  firstName: string;
  lastName: string;
  address: string;
  city: string;
  telephone: string;
}

export interface BookVisit {
  startsAt: string;
  reason: VisitReasonCode;
  vetId: number | null;
  notes: string | null;
}

/** An RFC 9457 problem details response from the API. */
export interface Problem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string>;
  nextAvailableSlot?: string;
}

const SPECIES_GLYPHS: Record<string, string> = {
  cat: '🐱',
  dog: '🐶',
  bird: '🐦',
  hamster: '🐹',
  lizard: '🦎',
  snake: '🐍',
  rabbit: '🐰',
};

export function speciesGlyph(type: string): string {
  return SPECIES_GLYPHS[type] ?? '🐾';
}

/** Formats a local date as yyyy-MM-dd without converting to UTC. */
export function isoDate(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Parses the API's local yyyy-MM-ddTHH:mm[:ss] timestamps as local time. */
export function parseLocal(value: string): Date {
  const [date, time = '00:00'] = value.split('T');
  const [year, month, day] = date.split('-').map(Number);
  const [hours, minutes] = time.split(':').map(Number);
  return new Date(year, month - 1, day, hours, minutes);
}
