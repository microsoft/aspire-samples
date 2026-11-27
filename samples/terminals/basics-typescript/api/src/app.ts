import { SpanStatusCode, trace, type Span } from '@opentelemetry/api';
import { apiReference } from '@scalar/express-api-reference';
import express, { type ErrorRequestHandler, type Request, type Response } from 'express';
import type { Logger } from 'pino';
import { logger as defaultLogger } from './logger.ts';
import { createOpenApiDocument } from './openapi.ts';
import { ID_PATTERN, ID_RULE, SAMPLE_NOTES, TEXT_RULE } from './rules.ts';
import { noteOperations, noteTextLength, notesListed, storedNotesChanged, tracer } from './telemetry.ts';

export interface NotesDatabase {
    hGetAll(key: string): Promise<Record<string, string>>;
    hGet(key: string, field: string): Promise<string | null | undefined>;
    hSet(key: string, field: string, value: string): Promise<number>;
    hDel(key: string, field: string): Promise<number>;
    ping(): Promise<string>;
}

export interface AppOptions {
    /** Maps POST /dev/notes/seed, the endpoint behind the "Seed sample notes" dashboard command. */
    demoEndpoints?: boolean;
    /** Defaults to the shared pino logger, which the OpenTelemetry pino instrumentation bridges to OTLP. */
    logger?: Logger;
}

const PROBLEM_TYPES: Record<number, string> = {
    400: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
    404: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
    413: 'https://tools.ietf.org/html/rfc9110#section-15.5.14',
    415: 'https://tools.ietf.org/html/rfc9110#section-15.5.16',
    500: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
    503: 'https://tools.ietf.org/html/rfc9110#section-15.6.4',
};

/** Writes an RFC 9457 problem details response that carries the trace id for the dashboard. */
function problem(request: Request, response: Response, status: number, title: string, detail: string, errors?: Record<string, string[]>) {
    return response.status(status).type('application/problem+json').json({
        type: PROBLEM_TYPES[status] ?? 'about:blank',
        title,
        status,
        detail,
        instance: request.originalUrl,
        traceId: trace.getActiveSpan()?.spanContext().traceId,
        ...(errors && { errors }),
    });
}

const validationProblem = (request: Request, response: Response, errors: Record<string, string[]>) =>
    problem(request, response, 400, 'One or more validation errors occurred.', 'See errors for the invalid fields.', errors);

const notFound = (request: Request, response: Response, id: string) =>
    problem(request, response, 404, 'Note not found', `No note has the ID '${id}'. GET /notes lists the stored notes.`);

class RedisRequestError extends Error {}

async function query<T>(operation: Promise<T>): Promise<T> {
    try {
        return await operation;
    } catch (cause) {
        throw new RedisRequestError('Redis request failed', { cause });
    }
}

type Complete = (outcome: string, event?: string) => void;

/** Runs a handler in a `notes.<operation>` span and counts it by outcome; a thrown error counts as `failed`. */
function traced(operation: string, id: string | undefined, handler: (span: Span, complete: Complete) => Promise<unknown>) {
    const attributes = { 'notes.operation': operation, ...(id !== undefined && { 'note.id': id }) };
    return tracer.startActiveSpan(`notes.${operation}`, { attributes }, async span => {
        let outcome: string | undefined;
        try {
            await handler(span, (value, event) => {
                outcome = value;
                span.setAttribute('notes.outcome', value);
                if (event) span.addEvent(event);
            });
        } catch (error) {
            span.recordException(error as Error);
            span.setStatus({ code: SpanStatusCode.ERROR, message: 'The notes store request failed.' });
            throw error;
        } finally {
            noteOperations.add(1, { 'notes.operation': operation, 'notes.outcome': outcome ?? 'failed' });
            span.end();
        }
    });
}

const sortedNotes = (notes: Record<string, string>) => Object.keys(notes).sort().map(id => ({ id, text: notes[id] }));

export function createApp(redis: NotesDatabase, { demoEndpoints = false, logger = defaultLogger }: AppOptions = {}) {
    const app = express();
    const openApiDocument = createOpenApiDocument({ demoEndpoints });
    app.disable('x-powered-by');
    app.use(express.json({ limit: '10kb' }));
    app.get('/health', async (_request, response) => {
        await query(redis.ping());
        response.json({ status: 'healthy' });
    });
    app.get('/openapi.json', (_request, response) => {
        response.json(openApiDocument);
    });
    app.use('/scalar', apiReference({
        content: openApiDocument,
        theme: 'purple',
        _integration: 'express',
        metaData: { title: 'Terminal Notes API reference' },
    }));
    app.param('id', (request, response, next, id: string) => {
        if (!ID_PATTERN.test(id)) {
            const operation = request.method === 'PUT' ? 'write' : request.method === 'DELETE' ? 'delete' : 'read';
            noteOperations.add(1, { 'notes.operation': operation, 'notes.outcome': 'invalid' });
            logger.warn({ operation, validationError: ID_RULE }, `Rejected ${operation} request: ${ID_RULE}`);
            validationProblem(request, response, { id: [ID_RULE] });
            return;
        }
        next();
    });
    // Demo-only, unauthenticated CRUD routes: every caller can read, replace, and delete notes.
    // Use non-sensitive, disposable data and keep this HTTP API private.
    app.get('/notes', (_request, response) => traced('list', undefined, async (span, complete) => {
        const notes = sortedNotes(await query(redis.hGetAll('notes')));
        span.setAttribute('notes.count', notes.length);
        complete('ok');
        notesListed(notes.length);
        logger.info({ noteCount: notes.length }, `Listed ${notes.length} notes`);
        response.json(notes);
    }));
    app.get('/notes/:id', (request, response) => traced('read', request.params.id, async (span, complete) => {
        const id = request.params.id;
        const text = await query(redis.hGet('notes', id));
        if (text == null) {
            complete('not_found');
            logger.warn({ noteId: id }, `Note ${id} was not found`);
            notFound(request, response, id);
            return;
        }
        span.setAttribute('note.text.length', text.length);
        complete('ok');
        logger.info({ noteId: id, textLength: text.length }, `Read note ${id} (${text.length} characters)`);
        response.json({ id, text });
    }));
    app.put('/notes/:id', (request, response) => traced('write', request.params.id, async (span, complete) => {
        if (!request.is('application/json')) {
            complete('invalid');
            problem(request, response, 415, 'Unsupported media type', 'Send the note as application/json.');
            return;
        }
        const text: unknown = request.body?.text;
        if (typeof text !== 'string' || !text.trim() || text.length > 1000) {
            complete('invalid');
            logger.warn({ operation: 'write', validationError: TEXT_RULE }, `Rejected write request: ${TEXT_RULE}`);
            validationProblem(request, response, { text: [TEXT_RULE] });
            return;
        }
        const id = request.params.id;
        span.setAttribute('note.text.length', text.length);
        const created = Boolean(await query(redis.hSet('notes', id, text)));
        const outcome = created ? 'created' : 'replaced';
        complete(outcome, `note.${outcome}`);
        noteTextLength.record(text.length, { 'notes.outcome': outcome });
        if (created) {
            storedNotesChanged(1);
            response.location(`/notes/${id}`);
        }
        logger.info({ noteId: id, textLength: text.length }, `${created ? 'Created' : 'Replaced'} note ${id} (${text.length} characters)`);
        response.status(created ? 201 : 200).json({ id, text });
    }));
    app.delete('/notes/:id', (request, response) => traced('delete', request.params.id, async (_span, complete) => {
        const id = request.params.id;
        if (!await query(redis.hDel('notes', id))) {
            complete('not_found');
            logger.warn({ noteId: id }, `Note ${id} was not found`);
            notFound(request, response, id);
            return;
        }
        complete('deleted', 'note.deleted');
        storedNotesChanged(-1);
        logger.info({ noteId: id }, `Deleted note ${id}`);
        response.sendStatus(204);
    }));
    if (demoEndpoints) {
        app.post('/dev/notes/seed', (_request, response) => traced('seed', undefined, async (span, complete) => {
            let created = 0;
            for (const sample of SAMPLE_NOTES) {
                const isNew = Boolean(await query(redis.hSet('notes', sample.id, sample.text)));
                created += Number(isNew);
                noteTextLength.record(sample.text.length, { 'notes.outcome': isNew ? 'created' : 'replaced' });
                span.addEvent(isNew ? 'note.created' : 'note.replaced', { 'note.id': sample.id, 'note.text.length': sample.text.length });
            }
            const notes = sortedNotes(await query(redis.hGetAll('notes')));
            const replaced = SAMPLE_NOTES.length - created;
            span.setAttribute('notes.count', notes.length);
            complete('ok');
            notesListed(notes.length);
            logger.info(
                { sampleCount: SAMPLE_NOTES.length, createdCount: created, replacedCount: replaced, noteCount: notes.length },
                `Seeded ${SAMPLE_NOTES.length} sample notes (${created} created, ${replaced} replaced); ${notes.length} notes stored`,
            );
            response.json({ created, replaced, notes });
        }));
    }
    const handleError: ErrorRequestHandler = (error: unknown, request, response, _next) => {
        if (error && typeof error === 'object' && 'type' in error) {
            if (error.type === 'entity.parse.failed') {
                logger.warn('Rejected a request body that is not valid JSON');
                validationProblem(request, response, { body: ['The request body is not valid JSON.'] });
                return;
            }
            if (error.type === 'entity.too.large') {
                logger.warn('Rejected a request body larger than 10 KiB');
                problem(request, response, 413, 'Request body too large', 'Send at most 10 KiB of JSON.');
                return;
            }
        }
        if (error instanceof RedisRequestError) {
            logger.error({ err: error.cause }, 'Redis request failed');
            problem(request, response, 503, 'Redis is unavailable', "The notes store couldn't be reached. Check the redis resource in the Aspire dashboard.");
            return;
        }
        logger.error({ err: error }, 'Request failed');
        problem(request, response, 500, 'An unexpected error occurred', 'The server failed to process the request.');
    };
    app.use(handleError);
    return app;
}
