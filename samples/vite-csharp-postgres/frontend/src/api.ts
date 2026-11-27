import { context, SpanStatusCode, trace, type Attributes } from '@opentelemetry/api';

export type Category = 'Hardware' | 'Software' | 'Access' | 'Network';
export type Priority = 'Low' | 'Medium' | 'High' | 'Critical';
export type Status = 'New' | 'InProgress' | 'Resolved';

export interface ServiceRequest {
  id: number;
  title: string;
  category: Category;
  priority: Priority;
  status: Status;
  requestedBy: string;
  assignedTo: string | null;
  createdAt: string;
  dueAt: string;
  resolvedAt: string | null;
  isOverdue: boolean;
}

export interface QueueSummary {
  open: number;
  unassigned: number;
  inProgress: number;
  critical: number;
  overdue: number;
  resolvedLast24Hours: number;
}

export interface NewRequest {
  title: string;
  requestedBy: string;
  category: Category;
  priority: Priority;
}

export interface RequestChanges {
  title?: string;
  priority?: Priority;
  status?: Status;
  assignedTo?: string;
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (response.status === 204) return undefined as T;

  const data = await response.json().catch(() => null);
  if (!response.ok) {
    // The API answers errors with RFC 9457 problem details; validation problems list errors per field.
    const problem = (data ?? {}) as ProblemDetails;
    const fieldError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
    throw new Error(fieldError ?? problem.detail ?? problem.title ?? `${method} ${path} failed (${response.status})`);
  }
  return data as T;
}

export const api = {
  list: () => call<ServiceRequest[]>('GET', '/requests?limit=100'),
  summary: () => call<QueueSummary>('GET', '/queue/summary'),
  raise: (request: NewRequest) => call<ServiceRequest>('POST', '/requests', request),
  update: (id: number, changes: RequestChanges) => call<ServiceRequest>('PATCH', `/requests/${id}`, changes),
  remove: (id: number) => call<void>('DELETE', `/requests/${id}`),
};

// No-op until telemetry.ts registers a provider (run mode only).
const tracer = trace.getTracer('servicedesk-frontend');

type InSpan = <R>(fn: () => R) => R;

/**
 * Runs a UI action in its own span. Wrap each fetch in `inSpan` so it is parented to the action span even
 * after an `await` (the web SDK's default context manager doesn't follow async continuations).
 */
export function traced<T>(name: string, attributes: Attributes, action: (inSpan: InSpan) => Promise<T>): Promise<T> {
  return tracer.startActiveSpan(name, { attributes }, async (span) => {
    const active = context.active();
    try {
      return await action((fn) => context.with(active, fn));
    } catch (err) {
      span.setStatus({ code: SpanStatusCode.ERROR, message: err instanceof Error ? err.message : String(err) });
      throw err;
    } finally {
      span.end();
    }
  });
}
