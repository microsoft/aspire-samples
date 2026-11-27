/**
 * OpenAPI 3.1 description of the task queue API, served raw at `/openapi.json` and rendered by
 * Scalar at `/scalar`. Keep it in sync with the handlers in index.js and the message shapes the
 * workers send back.
 */
import { MAX_DATA_LENGTH, MAX_NAME_LENGTH, TASK_TYPES, TYPE_NAMES } from './tasks.js';

const ref = (/** @type {string} */ name) => ({ $ref: `#/components/schemas/${name}` });
const json = (/** @type {object} */ schema, /** @type {unknown} */ example) => ({
  'application/json': { schema, ...(example !== undefined && { example }) },
});
const problemJson = (/** @type {object} */ example) => ({ 'application/problem+json': { schema: ref('ProblemDetails'), example } });

const traceId = '4bf92f3577b34da6a3ce929d0e0e4736';
const problemType = {
  400: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  404: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
  503: 'https://tools.ietf.org/html/rfc9110#section-15.6.4',
};

const invoiceData = `From: billing@northwindlogistics.com
Subject: Invoice INV-2026-0418 from Northwind Logistics

Invoice number: INV-2026-0418
Invoice date: 2026-11-02
Due date: 2026-12-02 (Net 30)
PO number: PO-88412

Linehaul Chicago to Denver ..... $4,250.00
Liftgate and inside delivery ..... $310.00
Fuel surcharge (12%) ..... $510.00

Total due: USD 5,070.00`;

const completedTask = {
  id: 'task-7f3a91c2',
  type: 'extract',
  name: 'northwind-invoice-INV-2026-0418.txt',
  data: invoiceData,
  status: 'completed',
  worker: 'worker-node',
  createdAt: '2026-11-27T15:02:11.418Z',
  startedAt: '2026-11-27T15:02:11.431Z',
  completedAt: '2026-11-27T15:02:11.702Z',
  durationMs: 268,
  traceId,
  result: {
    headline: 'Invoice INV-2026-0418 from Northwind Logistics: USD 5,070.00 due 2026-12-02',
    facts: [
      { label: 'Invoice', value: 'INV-2026-0418' },
      { label: 'PO', value: 'PO-88412' },
      { label: 'Total', value: 'USD 5,070.00' },
      { label: 'Due', value: '2026-12-02' },
      { label: 'Line items', value: '3' },
    ],
    details: {
      vendor: 'Northwind Logistics',
      invoiceNumber: 'INV-2026-0418',
      poNumber: 'PO-88412',
      invoiceDate: '2026-11-02',
      dueDate: '2026-12-02',
      currency: 'USD',
      total: 5070,
      lineItems: [
        { description: 'Linehaul Chicago to Denver', amount: 4250 },
        { description: 'Liftgate and inside delivery', amount: 310 },
        { description: 'Fuel surcharge (12%)', amount: 510 },
      ],
    },
  },
};

const queuedTask = {
  id: 'task-7f3a91c2',
  type: 'extract',
  name: 'northwind-invoice-INV-2026-0418.txt',
  data: invoiceData,
  status: 'queued',
  worker: 'worker-node',
  createdAt: '2026-11-27T15:02:11.418Z',
  traceId,
};

const failedTask = {
  id: 'task-c41d07e9',
  type: 'analyze',
  name: 'customer-feedback-22.csv',
  data: 'customer,channel,comment\nAdatum,email,Late delivery',
  status: 'failed',
  worker: 'worker-python',
  createdAt: '2026-11-27T15:04:40.120Z',
  startedAt: '2026-11-27T15:04:40.139Z',
  completedAt: '2026-11-27T15:04:40.611Z',
  durationMs: 472,
  traceId: '0af7651916cd43dd8448eb211c80319c',
  error: { type: 'DatasetError', message: 'The dataset has no numeric columns to analyze (found: customer, channel, comment).' },
};

const taskNotFound = {
  type: problemType[404],
  title: 'Task not found',
  status: 404,
  detail: "No task has the id 'task-00000000'. Tasks are kept on the board until it is cleared (up to 500).",
  instance: '/tasks/task-00000000',
  traceId,
};

const brokerDown = {
  type: problemType[503],
  title: 'Message broker unavailable',
  status: 503,
  detail: 'RabbitMQ is not reachable, so the task could not be queued. Try again shortly.',
  instance: '/tasks',
  traceId,
};

const routingTable = TYPE_NAMES.map((type) => {
  const { queue, worker, label, description } = TASK_TYPES[type];
  return `- \`${type}\`: **${label}**, published to \`${queue}\` and processed by \`${worker}\`. ${description}`;
}).join('\n');

/** @param {{ simulation: boolean }} options */
export function buildOpenApiDocument({ simulation }) {
  return {
    openapi: '3.1.0',
    info: {
      title: 'Polyglot Task Queue API',
      version: '1.0.0',
      summary: 'Routes back-office documents to Python, C# and Node.js workers over RabbitMQ.',
      description: [
        'Submit a document as a task and the API publishes it to the RabbitMQ work queue for its type. The worker written in the language best suited to the job picks it up, reports `processing` on the `task_status` queue and sends its outcome to the `results` queue. The API keeps the latest tasks on an in-memory board that the UI follows over server-sent events.',
        '',
        routingTable,
        '',
        '**Tracing.** Every message carries the W3C `traceparent` header, so one submission is one trace across the browser, this API, RabbitMQ and the worker. Each task returns its `traceId`; paste it into the Aspire dashboard to open the trace.',
        '',
        'Errors use [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details with a `traceId`.',
      ].join('\n'),
      license: { name: 'MIT', identifier: 'MIT' },
    },
    servers: [{ url: '/', description: 'This API instance' }],
    tags: [
      { name: 'Tasks', description: 'Submit documents and follow them through the workers.' },
      { name: 'Samples', description: 'Ready-made documents, one per task type.' },
      ...(simulation ? [{ name: 'Simulation', description: 'Development-only load generator (enabled in run mode by the AppHost).' }] : []),
      { name: 'System', description: 'Operational endpoints.' },
    ],
    paths: {
      '/tasks': {
        get: {
          tags: ['Tasks'],
          operationId: 'listTasks',
          summary: 'List tasks',
          description: 'Returns the most recent tasks on the board, newest first. The `X-Total-Count` header has the number of tasks on the board.',
          parameters: [
            { name: 'limit', in: 'query', description: 'How many tasks to return.', schema: { type: 'integer', minimum: 1, maximum: 500, default: 100 }, example: 20 },
          ],
          responses: {
            200: {
              description: 'The latest tasks.',
              headers: { 'X-Total-Count': { description: 'Tasks on the board.', schema: { type: 'integer' }, example: 42 } },
              content: json({ type: 'array', items: ref('Task') }, [completedTask, failedTask]),
            },
            400: {
              description: '`limit` is not a whole number from 1 to 500.',
              content: problemJson({
                type: problemType[400],
                title: 'One or more validation errors occurred.',
                status: 400,
                detail: 'See errors for the invalid fields.',
                instance: '/tasks?limit=0',
                traceId,
                errors: { limit: ['limit must be a whole number from 1 to 500.'] },
              }),
            },
          },
        },
        post: {
          tags: ['Tasks'],
          operationId: 'submitTask',
          summary: 'Submit a task',
          description: [
            'Publishes the document to the work queue for its `type` and returns the queued task right away (`202 Accepted`). The worker processes it in the background:',
            '',
            routingTable,
            '',
            'Follow the task with `GET /tasks/{taskId}` (the `Location` header) or the `GET /tasks/stream` event stream. Its `traceId` identifies the trace that crosses the API, RabbitMQ and the worker.',
          ].join('\n'),
          requestBody: {
            required: true,
            content: {
              'application/json': {
                schema: ref('SubmitTaskRequest'),
                examples: {
                  extract: { summary: 'Invoice field extraction (Node.js)', value: { type: 'extract', name: 'northwind-invoice-INV-2026-0418.txt', data: invoiceData } },
                  analyze: {
                    summary: 'Dataset analysis (Python)',
                    value: { type: 'analyze', name: 'q3-regional-sales.csv', data: 'region,month,orders,revenue\nWest,2026-07,412,186420\nWest,2026-08,438,197310\nEast,2026-07,305,139080' },
                  },
                  report: {
                    summary: 'Report generation (C#)',
                    value: { type: 'report', name: 'q3-operations-review.md', data: '# Q3 operations review: Reno fulfillment\n\n## Highlights\n- On-time delivery rose to 96.4% against a 95% target.\n\n## Action items\n- [ ] Lock in peak capacity with Contoso Freight by October 15 (owner: Priya Raman).' },
                  },
                },
              },
            },
          },
          responses: {
            202: {
              description: 'The task is queued.',
              headers: { Location: { description: 'The task URL.', schema: { type: 'string' }, example: '/tasks/task-7f3a91c2' } },
              content: json(ref('Task'), queuedTask),
            },
            400: {
              description: 'The task type is unknown, or the document is missing or too long.',
              content: problemJson({
                type: problemType[400],
                title: 'One or more validation errors occurred.',
                status: 400,
                detail: 'See errors for the invalid fields.',
                instance: '/tasks',
                traceId,
                errors: { type: [`type must be one of: ${TYPE_NAMES.join(', ')}.`], data: ['data is required: the document text to process.'] },
              }),
            },
            503: { $ref: '#/components/responses/BrokerUnavailable' },
          },
        },
        delete: {
          tags: ['Tasks'],
          operationId: 'clearTasks',
          summary: 'Clear the board',
          description: 'Removes every task from the board. Messages already in RabbitMQ are still processed; their results are ignored.',
          responses: { 200: { description: 'How many tasks were removed.', content: json(ref('ClearResult'), { cleared: 42 }) } },
        },
      },
      '/tasks/{taskId}': {
        get: {
          tags: ['Tasks'],
          operationId: 'getTask',
          summary: 'Get a task',
          description: 'Returns the task with its status, the worker that picked it up and, once finished, its result or error.',
          parameters: [{ name: 'taskId', in: 'path', required: true, description: 'The id returned when the task was submitted.', schema: { type: 'string', pattern: '^task-[0-9a-f]{8}$' }, example: 'task-7f3a91c2' }],
          responses: {
            200: {
              description: 'The task.',
              content: {
                'application/json': {
                  schema: ref('Task'),
                  examples: {
                    completed: { summary: 'Completed by the Node.js worker', value: completedTask },
                    failed: { summary: 'Failed in the Python worker', value: failedTask },
                  },
                },
              },
            },
            404: { description: 'No task has this id.', content: problemJson(taskNotFound) },
          },
        },
      },
      '/tasks/stream': {
        get: {
          tags: ['Tasks'],
          operationId: 'streamTasks',
          summary: 'Stream the board',
          description: 'A [server-sent event](https://html.spec.whatwg.org/multipage/server-sent-events.html) stream that the UI follows. Each `data:` event is a snapshot of the board: the 50 latest tasks, totals by status, and per task type (one type per worker) the counts by status and the average processing time. A snapshot is sent on connect and whenever a task changes.',
          responses: {
            200: {
              description: 'The event stream.',
              content: {
                'text/event-stream': {
                  schema: { type: 'string' },
                  example: `data: ${JSON.stringify({
                    tasks: [completedTask],
                    totals: { queued: 0, processing: 0, completed: 1, failed: 0, all: 1 },
                    workers: { extract: { queued: 0, processing: 0, completed: 1, failed: 0, averageDurationMs: 268 } },
                  })}\n\n`,
                },
              },
            },
          },
        },
      },
      '/tasks/samples': {
        get: {
          tags: ['Samples'],
          operationId: 'listSamples',
          summary: 'List sample documents',
          description: 'One realistic document per task type: a regional sales CSV, operations review notes and an emailed invoice.',
          responses: { 200: { description: 'The sample documents.', content: json({ type: 'array', items: ref('SampleDocument') }) } },
        },
        post: {
          tags: ['Samples'],
          operationId: 'submitSamples',
          summary: 'Submit sample tasks',
          description: 'Queues the three sample documents in one request, so the Python, C# and Node.js workers each get one. The request is one trace across all three workers. Also available as the **Submit sample tasks** command on the `api` resource in the Aspire dashboard.',
          responses: {
            202: { description: 'The queued tasks, one per worker.', content: json({ type: 'array', items: ref('Task') }) },
            503: { $ref: '#/components/responses/BrokerUnavailable' },
          },
        },
      },
      ...(simulation && {
        '/simulate/burst': {
          post: {
            tags: ['Simulation'],
            operationId: 'simulateBurst',
            summary: 'Simulate a burst of tasks',
            description: 'Queues many tasks at once, spread evenly over the three task types. Every 8th task is a document its worker rejects (a CSV without numbers, notes that are too short, a delivery note instead of an invoice), so failures show up as well. Each task starts its own trace, linked to the burst. Also available as the **Simulate burst** dashboard command.',
            parameters: [{ name: 'tasks', in: 'query', description: 'How many tasks to queue.', schema: { type: 'integer', minimum: 1, maximum: 100, default: 24 } }],
            responses: {
              202: {
                description: 'What was queued.',
                content: json(ref('BurstSummary'), { submitted: 24, byType: { analyze: 8, report: 8, extract: 8 }, expectedFailures: 3, taskIds: ['task-1b2c3d4e', 'task-5f6a7b8c', '...'] }),
              },
              400: {
                description: '`tasks` is not a whole number from 1 to 100.',
                content: problemJson({ type: problemType[400], title: 'One or more validation errors occurred.', status: 400, detail: 'See errors for the invalid fields.', instance: '/simulate/burst?tasks=500', traceId, errors: { tasks: ['tasks must be a whole number from 1 to 100.'] } }),
              },
              503: { $ref: '#/components/responses/BrokerUnavailable' },
            },
          },
        },
      }),
      '/health': {
        get: {
          tags: ['System'],
          operationId: 'getHealth',
          summary: 'Health check',
          description: 'Healthy when the API is connected to RabbitMQ. The AppHost uses it as the `api` resource health check.',
          responses: {
            200: { description: 'Connected to RabbitMQ.', content: json(ref('Health'), { status: 'healthy', rabbitmq: 'connected', tasksOnBoard: 42 }) },
            503: { description: 'Not connected to RabbitMQ.', content: json(ref('Health'), { status: 'unhealthy', rabbitmq: 'disconnected', tasksOnBoard: 0 }) },
          },
        },
      },
    },
    components: {
      responses: {
        BrokerUnavailable: { description: 'RabbitMQ is not reachable.', content: problemJson(brokerDown) },
      },
      schemas: {
        TaskType: {
          type: 'string',
          enum: TYPE_NAMES,
          description: TYPE_NAMES.map((type) => `- \`${type}\`: ${TASK_TYPES[type].label} (${TASK_TYPES[type].worker})`).join('\n'),
          example: 'extract',
        },
        TaskStatus: {
          type: 'string',
          enum: ['queued', 'processing', 'completed', 'failed'],
          description: '`queued` when published, `processing` once a worker picks it up, then `completed` or `failed`.',
          example: 'completed',
        },
        SubmitTaskRequest: {
          type: 'object',
          required: ['type', 'data'],
          properties: {
            type: ref('TaskType'),
            name: { type: 'string', maxLength: MAX_NAME_LENGTH, description: 'Document name shown on the board.', example: 'northwind-invoice-INV-2026-0418.txt' },
            data: { type: 'string', minLength: 1, maxLength: MAX_DATA_LENGTH, description: 'The document: CSV for `analyze`, Markdown notes for `report`, invoice text for `extract`.' },
          },
        },
        Task: {
          type: 'object',
          required: ['id', 'type', 'name', 'data', 'status', 'worker', 'createdAt'],
          properties: {
            id: { type: 'string', example: 'task-7f3a91c2' },
            type: ref('TaskType'),
            name: { type: 'string', example: 'northwind-invoice-INV-2026-0418.txt' },
            data: { type: 'string', description: 'The submitted document.' },
            status: ref('TaskStatus'),
            worker: { type: 'string', enum: ['worker-python', 'worker-csharp', 'worker-node'], description: 'The worker the task is routed to (and, once picked up, the one processing it).' },
            createdAt: { type: 'string', format: 'date-time' },
            startedAt: { type: 'string', format: 'date-time', description: 'When the worker picked the task up.' },
            completedAt: { type: 'string', format: 'date-time' },
            durationMs: { type: 'integer', description: "The worker's processing time in milliseconds.", example: 268 },
            traceId: { type: 'string', description: 'W3C trace id of the submission. Search for it in the Aspire dashboard.', example: traceId },
            result: ref('TaskResult'),
            error: ref('TaskError'),
          },
        },
        TaskResult: {
          type: 'object',
          description: 'The common result envelope every worker returns, whatever its language.',
          required: ['headline', 'facts', 'details'],
          properties: {
            headline: { type: 'string', description: 'One-line summary of the outcome.', example: completedTask.result.headline },
            facts: { type: 'array', items: ref('Fact'), description: 'Key figures, formatted for display.' },
            details: { type: 'object', additionalProperties: true, description: 'Task-type specific output, such as per-group totals, report sections or invoice line items.' },
          },
        },
        Fact: {
          type: 'object',
          required: ['label', 'value'],
          properties: { label: { type: 'string', example: 'Total' }, value: { type: 'string', example: 'USD 5,070.00' } },
        },
        TaskError: {
          type: 'object',
          required: ['type', 'message'],
          properties: { type: { type: 'string', example: 'DatasetError' }, message: { type: 'string', example: failedTask.error.message } },
        },
        SampleDocument: {
          type: 'object',
          required: ['type', 'name', 'data', 'worker'],
          properties: { type: ref('TaskType'), name: { type: 'string' }, data: { type: 'string' }, worker: { type: 'string', example: 'worker-node' } },
        },
        ClearResult: { type: 'object', required: ['cleared'], properties: { cleared: { type: 'integer', example: 42 } } },
        BurstSummary: {
          type: 'object',
          required: ['submitted', 'byType', 'expectedFailures', 'taskIds'],
          properties: {
            submitted: { type: 'integer' },
            byType: { type: 'object', additionalProperties: { type: 'integer' } },
            expectedFailures: { type: 'integer', description: 'Tasks with documents their worker will reject.' },
            taskIds: { type: 'array', items: { type: 'string' } },
          },
        },
        Health: {
          type: 'object',
          required: ['status', 'rabbitmq', 'tasksOnBoard'],
          properties: {
            status: { type: 'string', enum: ['healthy', 'unhealthy'] },
            rabbitmq: { type: 'string', enum: ['connected', 'disconnected'] },
            tasksOnBoard: { type: 'integer' },
          },
        },
        ProblemDetails: {
          type: 'object',
          description: 'RFC 9457 problem details.',
          required: ['type', 'title', 'status'],
          properties: {
            type: { type: 'string', format: 'uri' },
            title: { type: 'string' },
            status: { type: 'integer' },
            detail: { type: 'string' },
            instance: { type: 'string' },
            traceId: { type: 'string', description: 'Trace id of the failed request, for the Aspire dashboard.' },
            errors: { type: 'object', additionalProperties: { type: 'array', items: { type: 'string' } }, description: 'Validation messages by field (400 only).' },
          },
        },
      },
    },
  };
}
