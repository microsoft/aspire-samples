/**
 * Node.js worker: consumes `tasks.extract` and pulls invoice fields out of emailed invoices.
 *
 * instrumentation.js is preloaded through NODE_OPTIONS (see apphost.mts). Each task is handled in
 * the trace the API started: process tasks.extract -> extract invoice fields -> publish task_status
 * and publish results, which the API consumes.
 */
import { setTimeout as delay } from 'node:timers/promises';
import { randomInt } from 'node:crypto';
import { SpanStatusCode } from '@opentelemetry/api';
import { extractInvoice, toResult } from './extract.js';
import { logger } from './logger.js';
import { connect, consume, publish, RESULTS_QUEUE, STATUS_QUEUE } from './messaging.js';
import { fieldsExtracted, taskDuration, tasksActive, tasksFailed, tasksProcessed, tracer } from './telemetry.js';

const WORKER = 'worker-node';
const TASK_TYPE = 'extract';
const QUEUE = 'tasks.extract';
const PREFETCH = 4;
const MAX_DATA_LENGTH = 10_000;

const FIELD_METRIC_NAMES = {
  invoiceNumber: 'invoice_number',
  poNumber: 'po_number',
  invoiceDate: 'invoice_date',
  dueDate: 'due_date',
  total: 'total',
  vendor: 'vendor',
};

class InvalidTaskError extends Error {
  name = 'InvalidTaskError';
}

/** @param {any} message */
function validate(message) {
  if (typeof message?.taskId !== 'string' || !/^task-[0-9a-f]{8}$/.test(message.taskId)) {
    throw new InvalidTaskError('The message has no valid taskId.');
  }
  if (message.type !== TASK_TYPE) return `Unsupported task type '${message.type}'; this worker handles '${TASK_TYPE}'.`;
  if (typeof message.data !== 'string' || !message.data.trim()) return 'The document is empty.';
  if (message.data.length > MAX_DATA_LENGTH) return `The document is longer than ${MAX_DATA_LENGTH} characters.`;
  return undefined;
}

/**
 * The domain span: loads the document (simulated latency) and extracts the invoice fields.
 * @param {{ taskId: string, name: string, data: string }} task
 */
function extract(task) {
  return tracer.startActiveSpan('extract invoice fields', { attributes: { 'taskqueue.task.id': task.taskId, 'taskqueue.document.name': task.name } }, async (span) => {
    try {
      // Stands in for fetching the scanned PDF and running OCR before the text is parsed.
      await delay(120 + randomInt(0, 280));
      span.addEvent('document.loaded', { 'taskqueue.document.size': Buffer.byteLength(task.data) });

      const extraction = extractInvoice(task.data);
      for (const [field, metricName] of Object.entries(FIELD_METRIC_NAMES)) {
        if (extraction.fields[field] !== undefined) fieldsExtracted.add(1, { 'taskqueue.field.name': metricName });
      }
      if (extraction.lineItems.length) fieldsExtracted.add(extraction.lineItems.length, { 'taskqueue.field.name': 'line_item' });

      span.setAttributes({
        'taskqueue.extraction.field_count': Object.keys(extraction.fields).length,
        'taskqueue.extraction.line_items': extraction.lineItems.length,
        ...(extraction.fields.invoiceNumber && { 'taskqueue.invoice.number': String(extraction.fields.invoiceNumber) }),
        ...(extraction.fields.vendor && { 'taskqueue.invoice.vendor': String(extraction.fields.vendor) }),
        ...(typeof extraction.fields.total === 'number' && { 'taskqueue.invoice.total': extraction.fields.total }),
      });
      span.addEvent('fields.extracted', { 'taskqueue.extraction.field_count': Object.keys(extraction.fields).length });
      return extraction;
    } catch (err) {
      const error = /** @type {Error} */ (err);
      span.recordException(error);
      span.setStatus({ code: SpanStatusCode.ERROR, message: error.message });
      span.setAttribute('error.type', error.name);
      throw err;
    } finally {
      span.end();
    }
  });
}

/**
 * @param {any} message
 * @param {import('@opentelemetry/api').Span} span the `process tasks.extract` span
 */
async function handleTask(message, span) {
  const invalid = validate(message);
  const { taskId, name = 'invoice.txt' } = message;
  span.setAttributes({ 'taskqueue.task.id': taskId, 'taskqueue.task.type': TASK_TYPE, 'taskqueue.document.name': name });

  const attributes = { 'taskqueue.task.type': TASK_TYPE };
  const started = performance.now();
  tasksActive.add(1, attributes);
  await publish(STATUS_QUEUE, { taskId, status: 'processing', worker: WORKER, startedAt: new Date().toISOString() });

  /** @type {ReturnType<typeof toResult> | undefined} */
  let result;
  /** @type {{ type: string, message: string } | undefined} */
  let error;
  try {
    if (invalid) throw new InvalidTaskError(invalid);
    const extraction = await extract({ taskId, name, data: message.data });
    result = toResult(extraction);
    logger.info(
      {
        taskId,
        documentName: name,
        invoiceNumber: extraction.fields.invoiceNumber,
        vendor: extraction.fields.vendor,
        total: extraction.fields.total,
        currency: extraction.fields.currency,
        fieldCount: Object.keys(extraction.fields).length,
        lineItems: extraction.lineItems.length,
      },
      `Extracted ${Object.keys(extraction.fields).length} fields and ${extraction.lineItems.length} line items from ${name}: ${result.headline}`,
    );
  } catch (err) {
    const failure = /** @type {Error} */ (err);
    error = { type: failure.name, message: failure.message };
    tasksFailed.add(1, { ...attributes, 'error.type': failure.name });
    span.addEvent('task.failed', { 'error.type': failure.name });
    logger.warn({ taskId, documentName: name, errorType: failure.name, error: failure.message }, `Could not extract fields from ${name}: ${failure.message}`);
  } finally {
    tasksActive.add(-1, attributes);
  }

  const outcome = error ? 'failed' : 'completed';
  const durationMs = Math.round(performance.now() - started);
  tasksProcessed.add(1, { ...attributes, 'taskqueue.task.outcome': outcome });
  taskDuration.record(durationMs / 1000, { ...attributes, 'taskqueue.task.outcome': outcome });
  await publish(RESULTS_QUEUE, { taskId, status: outcome, worker: WORKER, completedAt: new Date().toISOString(), durationMs, result, error });
}

await connect([QUEUE, STATUS_QUEUE, RESULTS_QUEUE], PREFETCH, async (channel) => {
  await consume(channel, QUEUE, handleTask);
  logger.info({ queue: QUEUE, prefetch: PREFETCH }, `Node.js worker consuming ${QUEUE} (up to ${PREFETCH} tasks at a time)`);
});
