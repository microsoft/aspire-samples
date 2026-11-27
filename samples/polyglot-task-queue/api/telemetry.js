/**
 * Domain telemetry for the task queue API: one tracer and one meter named `TaskQueue.Api`.
 * The global providers are registered by instrumentation.js; without it these are no-ops.
 *
 * Metric attributes only use small, fixed value sets (task type, source, outcome). Task ids and
 * document names go on spans and logs instead.
 */
import { metrics, trace } from '@opentelemetry/api';

const name = 'TaskQueue.Api';
const version = '1.0.0';

export const tracer = trace.getTracer(name, version);
const meter = metrics.getMeter(name, version);

export const tasksSubmitted = meter.createCounter('taskqueue.tasks.submitted', {
  description: 'Tasks published to a worker queue, by task type and source (api, sample or burst).',
  unit: '{task}',
});

export const tasksPending = meter.createUpDownCounter('taskqueue.tasks.pending', {
  description: 'Tasks submitted that have not reported a result yet (queued or processing), by task type.',
  unit: '{task}',
});

export const taskTurnaround = meter.createHistogram('taskqueue.task.turnaround', {
  description: 'Time from submission to the worker\'s result, including time spent waiting in the queue, by task type and outcome.',
  unit: 's',
  advice: { explicitBucketBoundaries: [0.1, 0.25, 0.5, 0.75, 1, 1.5, 2, 3, 5, 10, 30] },
});
