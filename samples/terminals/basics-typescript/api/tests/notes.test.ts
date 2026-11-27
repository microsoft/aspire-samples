import assert from 'node:assert/strict';
import { once } from 'node:events';
import { test } from 'node:test';
import { pino } from 'pino';
import { createApp, type AppOptions, type NotesDatabase } from '../src/app.ts';

async function fixture(options?: AppOptions) {
    const notes = new Map<string, string>();
    let unavailable = false;
    const check = () => {
        if (unavailable) throw new Error('Redis is unavailable');
    };
    const redis: NotesDatabase = {
        async hGetAll() { check(); return Object.fromEntries(notes); },
        async hGet(_key, id) { check(); return notes.get(id) ?? null; },
        async hSet(_key, id, text) {
            check();
            const created = !notes.has(id);
            notes.set(id, text);
            return Number(created);
        },
        async hDel(_key, id) { check(); return Number(notes.delete(id)); },
        async ping() { check(); return 'PONG'; },
    };
    const server = createApp(redis, { logger: pino({ level: 'silent' }), ...options }).listen(0, '127.0.0.1');
    await once(server, 'listening');
    const address = server.address();
    assert(address && typeof address !== 'string');
    const request = (path: string, init?: RequestInit) => fetch(`http://127.0.0.1:${address.port}${path}`, init);
    const put = (id: string, text: unknown) => request(`/notes/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text }),
    });
    return {
        notes, request, put,
        fail: () => { unavailable = true; },
        async close() {
            await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
        },
    };
}

test('CRUD round trip and direct Redis edits', async () => {
    const app = await fixture();
    try {
        assert.deepEqual(await (await app.request('/notes')).json(), []);
        const created = await app.put('welcome', 'Hello from Slumber!');
        assert.equal(created.status, 201);
        assert.equal(created.headers.get('location'), '/notes/welcome');
        assert.deepEqual(await created.json(), { id: 'welcome', text: 'Hello from Slumber!' });
        assert.equal((await app.put('welcome', 'Updated')).status, 200);
        app.notes.set('welcome', 'Edited from Redis');
        assert.deepEqual(await (await app.request('/notes/welcome')).json(), { id: 'welcome', text: 'Edited from Redis' });
        await app.put('aaa', 'First');
        assert.deepEqual(await (await app.request('/notes')).json(), [
            { id: 'aaa', text: 'First' }, { id: 'welcome', text: 'Edited from Redis' },
        ]);
        assert.equal((await app.request('/notes/welcome', { method: 'DELETE' })).status, 204);
        assert.equal((await app.request('/notes/welcome')).status, 404);
        assert.equal((await app.request('/notes/welcome', { method: 'DELETE' })).status, 404);
    } finally {
        await app.close();
    }
});

test('invalid bodies, exact limits and IDs', async () => {
    const app = await fixture();
    try {
        for (const text of [null, 1, '', '   ', 'x'.repeat(1001)]) {
            assert.equal((await app.put('invalid', text)).status, 400);
        }
        for (const body of ['{}', '[]', '{']) {
            assert.equal((await app.request('/notes/invalid', {
                method: 'PUT', headers: { 'Content-Type': 'application/json' }, body,
            })).status, 400);
        }
        assert.equal(app.notes.size, 0);
        assert.equal((await app.put('a'.repeat(40), 'x'.repeat(1000))).status, 201);
        for (const id of ['UPPER', 'under_score', 'a%0A', 'a'.repeat(41)]) {
            assert.equal((await app.request(`/notes/${id}`)).status, 400);
            assert.equal((await app.put(id, 'No')).status, 400);
            assert.equal((await app.request(`/notes/${id}`, { method: 'DELETE' })).status, 400);
        }
        assert.equal((await app.request('/notes/invalid', {
            method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: 'x'.repeat(11000),
        })).status, 413);
        assert.equal((await app.request('/notes/invalid', {
            method: 'PUT', headers: { 'Content-Type': 'text/plain' }, body: 'Hello',
        })).status, 415);
    } finally {
        await app.close();
    }
});

test('dependency failures return 503, not an empty collection or healthy status', async () => {
    const app = await fixture();
    try {
        assert.equal((await app.request('/health')).status, 200);
        app.fail();
        for (const path of ['/health', '/notes', '/notes/welcome']) {
            const response = await app.request(path);
            assert.equal(response.status, 503);
            assert.match(response.headers.get('content-type')!, /application\/problem\+json/);
        }
        assert.equal((await app.put('welcome', 'No')).status, 503);
        assert.equal((await app.request('/notes/welcome', { method: 'DELETE' })).status, 503);
    } finally {
        await app.close();
    }
});

test('seed writes sample notes and leaves the welcome note alone', async () => {
    const app = await fixture({ demoEndpoints: true });
    try {
        app.notes.set('welcome', 'Edited from Redis');
        app.notes.set('standup', 'Stale standup notes');
        const response = await app.request('/dev/notes/seed', { method: 'POST' });
        assert.equal(response.status, 200);
        const result = await response.json();
        assert.deepEqual([result.created, result.replaced, result.notes.length], [4, 1, 6]);
        assert.equal(app.notes.get('welcome'), 'Edited from Redis');
        assert.notEqual(app.notes.get('standup'), 'Stale standup notes');
    } finally {
        await app.close();
    }
});

test('the seed endpoint and its OpenAPI operation exist only when enabled', async () => {
    for (const demoEndpoints of [false, true]) {
        const app = await fixture({ demoEndpoints });
        try {
            const document = await (await app.request('/openapi.json')).json();
            assert.deepEqual(Object.keys(document.paths['/notes/{id}']), ['parameters', 'get', 'put', 'delete']);
            assert.equal('/dev/notes/seed' in document.paths, demoEndpoints);
            assert.equal((await app.request('/dev/notes/seed', { method: 'POST' })).status, demoEndpoints ? 200 : 404);
        } finally {
            await app.close();
        }
    }
});