/**
 * Domain telemetry for the notes API: one tracer and one meter named `Contoso.Notes`.
 * The global providers are registered by instrumentation.ts; without it these are no-ops.
 *
 * Metric attributes are limited to the operation and its outcome. Note IDs go on spans and logs.
 */
import { metrics, trace } from '@opentelemetry/api';

const name = 'Contoso.Notes';
const version = '1.0.0';

export const tracer = trace.getTracer(name, version);
const meter = metrics.getMeter(name, version);

export const noteOperations = meter.createCounter('notes.operations', {
    description: 'Note operations handled by the API, by operation (list, read, write, delete, seed) and outcome.',
    unit: '{operation}',
});

export const noteTextLength = meter.createHistogram('notes.text.length', {
    description: 'Length of the note text written through the API, by whether the note was created or replaced.',
    unit: '{character}',
    advice: { explicitBucketBoundaries: [10, 20, 40, 60, 80, 100, 250, 500, 1000] },
});

// Redis REPL edits bypass the API, so this reports the count the API last saw rather than polling Redis.
let storedNotes: number | undefined;
meter.createObservableGauge('notes.stored', {
    description: 'Notes in the Redis hash as last seen by the API: set by each list, then adjusted by creates and deletes.',
    unit: '{note}',
}).addCallback(result => {
    if (storedNotes !== undefined) result.observe(storedNotes);
});

export function notesListed(count: number) {
    storedNotes = count;
}

export function storedNotesChanged(delta: number) {
    if (storedNotes !== undefined) storedNotes = Math.max(0, storedNotes + delta);
}
