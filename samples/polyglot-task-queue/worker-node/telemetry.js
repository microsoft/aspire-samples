/**
 * Domain telemetry for the Node.js worker: one tracer and one meter named `TaskQueue.Worker.Node`.
 * The Python and C# workers publish the same `taskqueue.tasks.*` instruments, so the three are
 * easy to compare in the dashboard. Attributes are bounded: task type, outcome, error type and
 * invoice field name.
 */
import { metrics, trace } from '@opentelemetry/api';

const name = 'TaskQueue.Worker.Node';
const version = '1.0.0';

export const tracer = trace.getTracer(name, version);
const meter = metrics.getMeter(name, version);

export const tasksProcessed = meter.createCounter('taskqueue.tasks.processed', {
  description: 'Tasks this worker finished, by task type and outcome (completed or failed).',
  unit: '{task}',
});

export const taskDuration = meter.createHistogram('taskqueue.task.duration', {
  description: 'Time this worker spent on a task, from pickup to result, by task type and outcome.',
  unit: 's',
  advice: { explicitBucketBoundaries: [0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.75, 1, 2, 5] },
});

export const tasksActive = meter.createUpDownCounter('taskqueue.tasks.active', {
  description: 'Tasks this worker is processing right now.',
  unit: '{task}',
});

export const tasksFailed = meter.createCounter('taskqueue.tasks.failed', {
  description: 'Tasks this worker could not process, by error type.',
  unit: '{task}',
});

export const fieldsExtracted = meter.createCounter('taskqueue.extraction.fields', {
  description: 'Invoice fields found in documents, by field name.',
  unit: '{field}',
});
