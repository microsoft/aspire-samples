/**
 * Polyglot task queue API: routes back-office documents to Python, C# and Node.js workers over
 * RabbitMQ and keeps a live task board.
 *
 * instrumentation.js is preloaded through NODE_OPTIONS (see apphost.mts). In run mode the Vite dev
 * server proxies `/tasks` and `/health` here; in publish mode this app also serves the built UI.
 */
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { apiReference } from '@scalar/express-api-reference';
import express from 'express';
import * as board from './board.js';
import { logger } from './logger.js';
import { connect, consume, isConnected, RESULTS_QUEUE, STATUS_QUEUE } from './messaging.js';
import { buildOpenApiDocument } from './openapi.js';
import { ProblemError, validationProblem, writeProblem } from './problems.js';
import { SAMPLE_DOCUMENTS, simulateBurst, submitSamples } from './samples.js';
import { onResult, onStatus, parseSubmission, submitTask, TASK_QUEUES } from './tasks.js';

const app = express();
const port = Number(process.env.PORT ?? 3000);
const simulationEnabled = process.env.ENABLE_BURST_SIMULATION === 'true';
const openApiDocument = buildOpenApiDocument({ simulation: simulationEnabled });

app.disable('x-powered-by');
app.use(express.json({ limit: '64kb' }));
// Publish mode: the frontend's build output is copied into ./public (see apphost.mts).
app.use(express.static(join(dirname(fileURLToPath(import.meta.url)), 'public')));

/**
 * Parses an optional whole number within bounds.
 * @param {unknown} value
 * @param {{ name: string, min: number, max: number, fallback: number }} options
 */
function wholeNumber(value, { name, min, max, fallback }) {
  if (value === undefined) return fallback;
  const number = typeof value === 'string' && /^\d+$/.test(value) ? Number(value) : NaN;
  if (!Number.isInteger(number) || number < min || number > max) {
    throw validationProblem({ [name]: [`${name} must be a whole number from ${min} to ${max}.`] });
  }
  return number;
}

/**
 * Wraps a handler so expected problems (4xx) are written right away. They don't reach Express's
 * error handling, so the server span isn't marked as failed for a client error.
 * @param {(req: import('express').Request, res: import('express').Response) => Promise<void> | void} handler
 * @returns {import('express').RequestHandler}
 */
const route = (handler) => async (req, res, next) => {
  try {
    await handler(req, res);
  } catch (err) {
    if (err instanceof ProblemError && err.status < 500) writeProblem(req, res, err);
    else next(err);
  }
};

app.get('/', (_req, res) => {
  res.redirect('/scalar');
});

app.get('/health', (_req, res) => {
  const connected = isConnected();
  res.status(connected ? 200 : 503).json({
    status: connected ? 'healthy' : 'unhealthy',
    rabbitmq: connected ? 'connected' : 'disconnected',
    tasksOnBoard: board.snapshot().totals.all,
  });
});

app.get('/tasks', route((req, res) => {
  const limit = wholeNumber(req.query.limit, { name: 'limit', min: 1, max: 500, fallback: 100 });
  res.set('X-Total-Count', String(board.snapshot().totals.all));
  res.json(board.list(limit));
}));

app.post('/tasks', route(async (req, res) => {
  const task = await submitTask(parseSubmission(req.body));
  res.status(202).location(`/tasks/${task.id}`).json(task);
}));

app.delete('/tasks', (_req, res) => {
  const cleared = board.clear();
  logger.info({ cleared }, `Cleared ${cleared} tasks from the board`);
  res.json({ cleared });
});

app.get('/tasks/stream', (req, res) => {
  res.set({ 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
  res.flushHeaders();
  /** @param {ReturnType<typeof board.snapshot>} snapshot */
  const send = (snapshot) => res.write(`data: ${JSON.stringify(snapshot)}\n\n`);
  send(board.snapshot());
  const unsubscribe = board.subscribe(send);
  const keepAlive = setInterval(() => res.write(': keep-alive\n\n'), 15_000);
  req.on('close', () => {
    clearInterval(keepAlive);
    unsubscribe();
  });
});

app.get('/tasks/samples', (_req, res) => {
  res.json(SAMPLE_DOCUMENTS);
});

app.post('/tasks/samples', route(async (_req, res) => {
  res.status(202).json(await submitSamples());
}));

app.get('/tasks/:taskId', route((req, res) => {
  const task = board.get(req.params.taskId);
  if (!task) {
    throw new ProblemError(404, 'Task not found', `No task has the id '${req.params.taskId}'. Tasks are kept on the board until it is cleared (up to 500).`);
  }
  res.json(task);
}));

if (simulationEnabled) {
  // Also exposed as the "Simulate burst" dashboard command (run mode only, see apphost.mts).
  app.post('/simulate/burst', route(async (req, res) => {
    const count = wholeNumber(req.query.tasks, { name: 'tasks', min: 1, max: 100, fallback: 24 });
    res.status(202).json(await simulateBurst(count));
  }));
}

app.get('/openapi.json', (_req, res) => {
  res.json(openApiDocument);
});

app.use(
  '/scalar',
  apiReference({
    content: openApiDocument,
    theme: 'kepler',
    _integration: 'express',
    metaData: { title: 'Polyglot Task Queue API reference' },
  }),
);

app.use((req, res) => {
  writeProblem(req, res, new ProblemError(404, 'Not found', `${req.method} ${req.originalUrl} is not an API operation. See /scalar.`));
});

/** @type {import('express').ErrorRequestHandler} */
const errorHandler = (err, req, res, _next) => {
  if (err instanceof ProblemError) {
    writeProblem(req, res, err);
    return;
  }
  // Body parser errors (malformed JSON, oversized body) carry a 4xx status.
  if (typeof err?.status === 'number' && err.status >= 400 && err.status < 500) {
    writeProblem(
      req,
      res,
      err.status === 413
        ? new ProblemError(413, 'Request body too large', 'The request body must be 64 KB or smaller.')
        : validationProblem({ body: [err.type === 'entity.parse.failed' ? 'The request body is not valid JSON.' : String(err.message)] }),
    );
    return;
  }
  logger.error({ error: err?.message }, 'Unhandled error');
  writeProblem(req, res, new ProblemError(500, 'Unexpected error', 'The server failed to process the request.'));
};
app.use(errorHandler);

app.listen(port, () => {
  logger.info({ port, simulationEnabled }, `Task queue API listening on port ${port}`);
});

await connect([...TASK_QUEUES, STATUS_QUEUE, RESULTS_QUEUE], async (channel) => {
  await consume(channel, STATUS_QUEUE, onStatus);
  await consume(channel, RESULTS_QUEUE, onResult);
});
