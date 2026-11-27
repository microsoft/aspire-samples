/**
 * OpenAPI 3.1 description of the notes API, served raw at `/openapi.json` and rendered by Scalar
 * at `/scalar`. Keep it in sync with the routes in app.ts.
 */
import { ID_RULE, SAMPLE_NOTES, TEXT_RULE } from './rules.ts';

const ref = (name: string) => ({ $ref: `#/components/schemas/${name}` });
const json = (schema: object, example: unknown) => ({ 'application/json': { schema, example } });
const problemJson = (example: object) => ({ 'application/problem+json': { schema: ref('ProblemDetails'), example } });

const exampleTraceId = '4bf92f3577b34da6a3ce929d0e0e4736';
const validationTitle = 'One or more validation errors occurred.';

const idParameter = {
    name: 'id',
    in: 'path',
    required: true,
    description: 'The note ID: 1-40 lowercase letters, digits or hyphens.',
    schema: { type: 'string', pattern: '^[a-z0-9-]{1,40}$', minLength: 1, maxLength: 40, examples: ['welcome'] },
};

const invalidId = {
    description: 'The note ID is invalid.',
    content: problemJson({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: validationTitle,
        status: 400,
        detail: 'See errors for the invalid fields.',
        instance: '/notes/Team_Notes',
        traceId: exampleTraceId,
        errors: { id: [ID_RULE] },
    }),
};

const notFound = {
    description: 'No note has this ID.',
    content: problemJson({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
        title: 'Note not found',
        status: 404,
        detail: "No note has the ID 'welcome'. GET /notes lists the stored notes.",
        instance: '/notes/welcome',
        traceId: exampleTraceId,
    }),
};

const unavailable = {
    description: 'Redis could not be reached.',
    content: problemJson({
        type: 'https://tools.ietf.org/html/rfc9110#section-15.6.4',
        title: 'Redis is unavailable',
        status: 503,
        detail: "The notes store couldn't be reached. Check the redis resource in the Aspire dashboard.",
        instance: '/notes',
        traceId: exampleTraceId,
    }),
};

const welcome = { id: 'welcome', text: 'Edited from Redis' };

export function createOpenApiDocument({ demoEndpoints }: { demoEndpoints: boolean }) {
    return {
        openapi: '3.1.0',
        info: {
            title: 'Terminal Notes API',
            version: '1.0.0',
            summary: 'Plain-text notes stored in a Redis hash.',
            description: [
                'Plain-text notes for the Aspire terminal samples. Each note is a field of the Redis hash `notes`, so you can create one from the Slumber REST client in the dashboard, change it with `HSET` in the Redis REPL, and read the new value back here: the API has no cache.',
                '',
                'IDs are 1-40 lowercase letters, digits or hyphens, and text is 1-1000 characters. Errors are returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details with the request\'s `traceId`, so a failed call can be pasted into the Aspire dashboard\'s trace search. This local demo has no authentication.',
            ].join('\n'),
        },
        servers: [{ url: '/', description: 'This API instance' }],
        tags: [
            { name: 'Notes', description: 'Create, read, replace and delete notes stored in Redis.' },
            ...(demoEndpoints ? [{ name: 'Demo', description: 'Run-mode operations behind the Aspire dashboard commands.' }] : []),
        ],
        paths: {
            '/notes': {
                get: {
                    operationId: 'listNotes',
                    tags: ['Notes'],
                    summary: 'List notes',
                    description: 'Returns every note in the Redis hash `notes`, ordered by ID. The API has no cache, so values changed in the Redis REPL appear immediately.',
                    responses: {
                        200: { description: 'The stored notes.', content: json({ type: 'array', items: ref('Note') }, [welcome, { id: SAMPLE_NOTES[0].id, text: SAMPLE_NOTES[0].text }]) },
                        503: unavailable,
                    },
                },
            },
            '/notes/{id}': {
                parameters: [idParameter],
                get: {
                    operationId: 'getNote',
                    tags: ['Notes'],
                    summary: 'Read a note',
                    description: 'Returns one note. Try it after `HSET notes welcome "Edited from Redis"` in the Redis REPL: the new text comes straight from Redis.',
                    responses: {
                        200: { description: 'The note.', content: json(ref('Note'), welcome) },
                        400: invalidId,
                        404: notFound,
                        503: unavailable,
                    },
                },
                put: {
                    operationId: 'putNote',
                    tags: ['Notes'],
                    summary: 'Create or replace a note',
                    description: 'Stores the text in the Redis hash `notes` under this ID: `201 Created` for a new note, `200 OK` when it replaces existing text. The request body is limited to 10 KiB and must be `application/json`.',
                    requestBody: { required: true, content: json(ref('NoteContent'), { text: 'Hello from Slumber!' }) },
                    responses: {
                        200: { description: 'Existing text was replaced.', content: json(ref('Note'), { id: 'welcome', text: 'Updated from Slumber!' }) },
                        201: {
                            description: 'The note was created.',
                            headers: { Location: { description: 'The new note\'s URL.', schema: { type: 'string', examples: ['/notes/welcome'] } } },
                            content: json(ref('Note'), { id: 'welcome', text: 'Hello from Slumber!' }),
                        },
                        400: {
                            description: 'The note ID, the JSON body or its text is invalid.',
                            content: problemJson({
                                type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
                                title: validationTitle,
                                status: 400,
                                detail: 'See errors for the invalid fields.',
                                instance: '/notes/welcome',
                                traceId: exampleTraceId,
                                errors: { text: [TEXT_RULE] },
                            }),
                        },
                        413: { description: 'The request body is larger than 10 KiB.', content: problemJson({ type: 'https://tools.ietf.org/html/rfc9110#section-15.5.14', title: 'Request body too large', status: 413, detail: 'Send at most 10 KiB of JSON.', instance: '/notes/welcome', traceId: exampleTraceId }) },
                        415: { description: 'The body is not `application/json`.', content: problemJson({ type: 'https://tools.ietf.org/html/rfc9110#section-15.5.16', title: 'Unsupported media type', status: 415, detail: 'Send the note as application/json.', instance: '/notes/welcome', traceId: exampleTraceId }) },
                        503: unavailable,
                    },
                },
                delete: {
                    operationId: 'deleteNote',
                    tags: ['Notes'],
                    summary: 'Delete a note',
                    description: 'Removes the note\'s field from the Redis hash `notes`.',
                    responses: {
                        204: { description: 'The note was deleted.' },
                        400: invalidId,
                        404: notFound,
                        503: unavailable,
                    },
                },
            },
            ...(demoEndpoints && {
                '/dev/notes/seed': {
                    post: {
                        operationId: 'seedNotes',
                        tags: ['Demo'],
                        summary: 'Seed sample notes',
                        description: `Run mode only. Writes ${SAMPLE_NOTES.length} realistic team notes (or restores their text) and returns every stored note. The \`welcome\` note used by the walkthrough is left alone. Backs the **Seed sample notes** dashboard command.`,
                        responses: {
                            200: {
                                description: 'The sample notes were written.',
                                content: json(ref('SeedResult'), { created: 4, replaced: 1, notes: [SAMPLE_NOTES[3], welcome] }),
                            },
                            503: unavailable,
                        },
                    },
                },
            }),
        },
        components: {
            schemas: {
                Note: {
                    type: 'object',
                    required: ['id', 'text'],
                    properties: {
                        id: { type: 'string', description: 'The note ID, which is also its field name in the Redis hash `notes`.', examples: ['welcome'] },
                        text: { type: 'string', description: 'The note text, stored as the hash field\'s value.', examples: ['Edited from Redis'] },
                    },
                },
                NoteContent: {
                    type: 'object',
                    required: ['text'],
                    properties: {
                        text: { type: 'string', minLength: 1, maxLength: 1000, description: 'The note text: 1-1000 characters, not just whitespace.', examples: ['Hello from Slumber!'] },
                    },
                },
                SeedResult: {
                    type: 'object',
                    required: ['created', 'replaced', 'notes'],
                    properties: {
                        created: { type: 'integer', description: 'Sample notes that didn\'t exist yet.' },
                        replaced: { type: 'integer', description: 'Sample notes whose text was restored.' },
                        notes: { type: 'array', items: ref('Note'), description: 'Every stored note after seeding, including notes that aren\'t samples.' },
                    },
                },
                ProblemDetails: {
                    type: 'object',
                    description: 'An [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details response.',
                    properties: {
                        type: { type: 'string', format: 'uri-reference' },
                        title: { type: 'string' },
                        status: { type: 'integer' },
                        detail: { type: 'string' },
                        instance: { type: 'string' },
                        traceId: { type: 'string', description: 'The W3C trace id of the failed request; search for it in the dashboard.' },
                        errors: {
                            type: 'object',
                            description: 'Validation messages keyed by field (400 only).',
                            additionalProperties: { type: 'array', items: { type: 'string' } },
                        },
                    },
                },
            },
        },
    };
}
