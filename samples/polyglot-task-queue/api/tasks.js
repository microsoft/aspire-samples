/**
 * Task types, validation and submission. Each task type has its own work queue, consumed by the
 * worker written in the language that suits the job best.
 */
import { randomBytes } from 'node:crypto';
import * as board from './board.js';
import { logger } from './logger.js';
import { publish } from './messaging.js';
import { brokerUnavailable, validationProblem } from './problems.js';
import { tasksSubmitted } from './telemetry.js';

export const TASK_TYPES = {
  analyze: {
    label: 'Dataset analysis',
    queue: 'tasks.analyze',
    worker: 'worker-python',
    description: 'Profiles a CSV dataset with pandas: totals, averages, the leading group and the period-over-period trend.',
  },
  report: {
    label: 'Report generation',
    queue: 'tasks.report',
    worker: 'worker-csharp',
    description: 'Turns Markdown meeting notes into a structured report: sections, highlights, action items and reading time.',
  },
  extract: {
    label: 'Invoice field extraction',
    queue: 'tasks.extract',
    worker: 'worker-node',
    description: 'Pulls invoice number, PO, dates, line items, total and vendor out of an emailed invoice.',
  },
};

export const TYPE_NAMES = /** @type {(keyof typeof TASK_TYPES)[]} */ (Object.keys(TASK_TYPES));
export const TASK_QUEUES = Object.values(TASK_TYPES).map((type) => type.queue);
export const MAX_DATA_LENGTH = 10_000;
export const MAX_NAME_LENGTH = 120;

const DEFAULT_NAMES = { analyze: 'dataset.csv', report: 'notes.md', extract: 'invoice.txt' };

/**
 * @param {unknown} body
 * @returns {{ type: keyof typeof TASK_TYPES, name: string, data: string }}
 */
export function parseSubmission(body) {
  /** @type {Record<string, string[]>} */
  const errors = {};
  if (!body || typeof body !== 'object' || Array.isArray(body)) {
    throw validationProblem({ body: ['The request body must be a JSON object.'] });
  }
  const { type, name, data } = /** @type {Record<string, unknown>} */ (body);

  if (typeof type !== 'string' || !TYPE_NAMES.includes(/** @type {any} */ (type))) {
    errors.type = [`type must be one of: ${TYPE_NAMES.join(', ')}.`];
  }
  if (typeof data !== 'string' || data.trim().length === 0) {
    errors.data = ['data is required: the document text to process.'];
  } else if (data.length > MAX_DATA_LENGTH) {
    errors.data = [`data must be ${MAX_DATA_LENGTH.toLocaleString('en-US')} characters or fewer.`];
  }
  if (name !== undefined && (typeof name !== 'string' || name.trim().length === 0 || name.length > MAX_NAME_LENGTH)) {
    errors.name = [`name must be a non-empty string of up to ${MAX_NAME_LENGTH} characters.`];
  }
  if (Object.keys(errors).length) throw validationProblem(errors);

  const taskType = /** @type {keyof typeof TASK_TYPES} */ (type);
  return { type: taskType, name: typeof name === 'string' ? name.trim() : DEFAULT_NAMES[taskType], data: /** @type {string} */ (data) };
}

const newTaskId = () => `task-${randomBytes(4).toString('hex')}`;

/**
 * Adds the task to the board and publishes it to its type's queue. The publish span (and so the
 * worker's spans) join the active trace: the HTTP request, or a new trace for burst tasks.
 * @param {{ type: keyof typeof TASK_TYPES, name: string, data: string }} submission
 * @param {{ source?: 'api' | 'sample' | 'burst', links?: import('@opentelemetry/api').Link[] }} [options]
 */
export async function submitTask({ type, name, data }, { source = 'api', links } = {}) {
  const { queue, worker } = TASK_TYPES[type];
  /** @type {import('./board.js').Task} */
  const task = {
    id: newTaskId(),
    type,
    name,
    data,
    status: 'queued',
    worker,
    createdAt: new Date().toISOString(),
  };

  // Added before publishing: a fast worker can report back before the broker's confirm arrives.
  board.add(task);
  try {
    task.traceId = await publish(
      queue,
      { taskId: task.id, type, name, data, submittedAt: task.createdAt },
      {
        attributes: { 'taskqueue.task.id': task.id, 'taskqueue.task.type': type, 'taskqueue.document.name': name, 'taskqueue.task.source': source },
        links,
      },
    );
  } catch (err) {
    board.remove(task.id);
    logger.error({ taskId: task.id, taskType: type, error: /** @type {Error} */ (err).message }, `Could not queue ${type} task ${task.id}`);
    throw brokerUnavailable();
  }

  tasksSubmitted.add(1, { 'taskqueue.task.type': type, 'taskqueue.task.source': source });
  logger.info(
    { taskId: task.id, taskType: type, documentName: name, queue, worker, source },
    `Queued ${type} task ${task.id} (${name}) on ${queue} for ${worker}`,
  );
  return task;
}

/**
 * `task_status` messages: a worker picked the task up.
 * @param {{ taskId: string, status: string, worker: string, startedAt: string }} message
 * @param {import('@opentelemetry/api').Span} span
 */
export function onStatus(message, span) {
  span.setAttributes({ 'taskqueue.task.id': message.taskId, 'taskqueue.task.status': message.status, 'taskqueue.worker.name': message.worker });
  if (message.status === 'processing') board.markProcessing(message);
}

/**
 * `results` messages: a worker finished the task, successfully or not.
 * @param {Parameters<typeof board.markFinished>[0]} message
 * @param {import('@opentelemetry/api').Span} span
 */
export function onResult(message, span) {
  span.setAttributes({
    'taskqueue.task.id': message.taskId,
    'taskqueue.task.outcome': message.status,
    'taskqueue.worker.name': message.worker,
  });
  const task = board.markFinished(message);
  if (!task) {
    span.addEvent('task.unknown', { 'taskqueue.task.id': message.taskId });
    return;
  }

  span.setAttribute('taskqueue.task.type', task.type);
  const seconds = ((Date.parse(message.completedAt) - Date.parse(task.createdAt)) / 1000).toFixed(2);
  const fields = { taskId: task.id, taskType: task.type, documentName: task.name, worker: message.worker, durationMs: message.durationMs, turnaroundSeconds: Number(seconds) };
  if (message.status === 'completed') {
    span.addEvent('task.completed', { 'taskqueue.task.turnaround_s': Number(seconds) });
    logger.info({ ...fields, headline: message.result?.headline }, `Task ${task.id} completed by ${message.worker} in ${seconds}s: ${message.result?.headline}`);
  } else {
    span.addEvent('task.failed', { 'error.type': message.error?.type ?? 'unknown' });
    logger.warn({ ...fields, errorType: message.error?.type, error: message.error?.message }, `Task ${task.id} failed in ${message.worker}: ${message.error?.message}`);
  }
}
