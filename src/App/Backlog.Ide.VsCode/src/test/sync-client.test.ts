import { test } from 'node:test';
import * as assert from 'node:assert/strict';

import { DEVICE_SECRET_KEY, FetchFn, SecretStore, SyncClient, TOKEN_RENEWAL_WINDOW_MS } from '../sync-client';

const baseUrl = 'https://sync.test';
const minute = 60_000;

class MemorySecrets implements SecretStore {
    readonly values = new Map<string, string>();

    async get(key: string): Promise<string | undefined> {
        return this.values.get(key);
    }

    async store(key: string, value: string): Promise<void> {
        this.values.set(key, value);
    }

    async delete(key: string): Promise<void> {
        this.values.delete(key);
    }
}

interface Call {
    path: string;
    method: string;
    authorization: string | null;
    body: unknown;
}

/** A fetch that records every call and answers through `respond`, so a test
 * states the service's half of the exchange and reads back what was sent. */
function stubFetch(respond: (call: Call, calls: Call[]) => Response): { fetch: FetchFn; calls: Call[] } {
    const calls: Call[] = [];
    const fetch: FetchFn = async (url, init) => {
        const headers = new Headers(init?.headers);
        // Compared by parsed origin, not by string prefix: a prefix match would also
        // accept https://sync.test.example.com and record it as one of ours.
        const target = new URL(url);
        const call: Call = {
            path: target.origin === new URL(baseUrl).origin ? target.pathname + target.search : url,
            method: init?.method ?? 'GET',
            authorization: headers.get('authorization'),
            body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined
        };
        calls.push(call);
        return respond(call, calls);
    };
    return { fetch, calls };
}

function json(status: number, body: unknown): Response {
    return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

function problem(status: number, code: string, detail: string): Response {
    return new Response(
        JSON.stringify({ type: `https://backlog.jsdotnet.dev/problems/${code}`, status, detail, code }),
        { status, headers: { 'content-type': 'application/problem+json' } });
}

const tokenFor = (n: number, expiresAt: number) =>
    json(200, { accessToken: `token-${n}`, expiresAt: new Date(expiresAt).toISOString(), tokenType: 'Bearer' });

async function pairedSecrets(): Promise<MemorySecrets> {
    const secrets = new MemorySecrets();
    await secrets.store(DEVICE_SECRET_KEY, JSON.stringify({ deviceId: 'device-1', credential: 'secret-credential' }));
    return secrets;
}

const inboxItem = { id: 'item-1', title: 'From the phone', source: 'mobile', capturedAt: '2026-10-15T08:00:00Z' };

test('pairing redeems the code and keeps the device id and credential in secret storage', async () => {
    const secrets = new MemorySecrets();
    const { fetch, calls } = stubFetch(() =>
        json(201, { ownerId: 'owner-1', deviceId: 'device-1', credential: 'secret-credential' }));
    const client = new SyncClient({ fetch, secrets });

    const result = await client.pair(baseUrl, '  ABCD-1234 ', 'VS Code on laptop');

    assert.deepEqual(result, { status: 'paired', deviceId: 'device-1' });
    assert.equal(calls.length, 1);
    assert.equal(calls[0].method, 'POST');
    assert.equal(calls[0].path, '/api/sync/devices/pair');
    assert.equal(calls[0].authorization, null);
    assert.deepEqual(calls[0].body, { code: 'ABCD-1234', deviceName: 'VS Code on laptop' });
    assert.deepEqual(
        JSON.parse(secrets.values.get(DEVICE_SECRET_KEY) ?? 'null'),
        { deviceId: 'device-1', credential: 'secret-credential' });
    assert.equal(await client.isPaired(), true);
});

test('an expired pairing code reports the service detail and stores nothing', async () => {
    const secrets = new MemorySecrets();
    const { fetch } = stubFetch(() =>
        problem(409, 'pairing.code_expired', 'That pairing code has expired. Ask for a new one.'));
    const client = new SyncClient({ fetch, secrets });

    const result = await client.pair(baseUrl, 'ABCD-1234', 'VS Code on laptop');

    assert.deepEqual(result, {
        status: 'failed',
        code: 'pairing.code_expired',
        reason: 'That pairing code has expired. Ask for a new one.'
    });
    assert.equal(secrets.values.size, 0);
    assert.equal(await client.isPaired(), false);
});

test('a mistyped pairing code without a problem body still fails readably and stores nothing', async () => {
    const secrets = new MemorySecrets();
    const { fetch } = stubFetch(() => new Response('', { status: 404 }));
    const client = new SyncClient({ fetch, secrets });

    const result = await client.pair(baseUrl, 'WRONG', 'VS Code on laptop');

    assert.equal(result.status, 'failed');
    assert.match(result.status === 'failed' ? result.reason : '', /404/);
    assert.equal(secrets.values.size, 0);
});

test('an empty pairing code is refused before any request', async () => {
    const secrets = new MemorySecrets();
    const { fetch, calls } = stubFetch(() => json(201, {}));
    const client = new SyncClient({ fetch, secrets });

    const result = await client.pair(baseUrl, '   ', 'VS Code on laptop');

    assert.equal(result.status, 'failed');
    assert.equal(calls.length, 0);
    assert.equal(secrets.values.size, 0);
});

test('listing and capturing send the bearer token the token endpoint issued', async () => {
    const now = Date.parse('2026-10-15T09:00:00Z');
    const { fetch, calls } = stubFetch(call => {
        if (call.path === '/api/sync/devices/token') return tokenFor(1, now + 30 * minute);
        if (call.method === 'GET') return json(200, [inboxItem]);
        return json(201, { ...inboxItem, title: 'From the editor', source: 'vscode' });
    });
    const client = new SyncClient({ fetch, secrets: await pairedSecrets(), now: () => now });

    const listed = await client.listInbox(baseUrl);
    const captured = await client.capture(baseUrl, 'From the editor');

    assert.deepEqual(listed, { status: 'ok', value: [inboxItem] });
    assert.deepEqual(captured, { status: 'ok', value: undefined });

    const token = calls.find(c => c.path === '/api/sync/devices/token');
    assert.deepEqual(token?.body, { deviceId: 'device-1', credential: 'secret-credential' });
    assert.equal(token?.authorization, null);

    const inbox = calls.filter(c => c.path === '/api/sync/inbox');
    assert.deepEqual(inbox.map(c => c.method), ['GET', 'POST']);
    assert.deepEqual(inbox.map(c => c.authorization), ['Bearer token-1', 'Bearer token-1']);
    assert.deepEqual(inbox[1].body, { title: 'From the editor', source: 'vscode' });
});

test('a token is reused until the renewal window and then renewed', async () => {
    let now = Date.parse('2026-10-15T09:00:00Z');
    const expiresAt = now + 30 * minute;
    let issued = 0;
    const { fetch, calls } = stubFetch(call => {
        if (call.path === '/api/sync/devices/token') return tokenFor(++issued, expiresAt + (issued - 1) * 30 * minute);
        return json(200, []);
    });
    const client = new SyncClient({ fetch, secrets: await pairedSecrets(), now: () => now });

    await client.listInbox(baseUrl);
    now += 10 * minute;
    await client.listInbox(baseUrl);

    assert.equal(calls.filter(c => c.path === '/api/sync/devices/token').length, 1);

    now = expiresAt - TOKEN_RENEWAL_WINDOW_MS + 1;
    await client.listInbox(baseUrl);

    assert.equal(calls.filter(c => c.path === '/api/sync/devices/token').length, 2);
    assert.deepEqual(
        calls.filter(c => c.path === '/api/sync/inbox').map(c => c.authorization),
        ['Bearer token-1', 'Bearer token-1', 'Bearer token-2']);
});

test('a 401 from the inbox drops the cached token and retries once with a fresh one', async () => {
    const now = Date.parse('2026-10-15T09:00:00Z');
    let issued = 0;
    const { fetch, calls } = stubFetch(call => {
        if (call.path === '/api/sync/devices/token') return tokenFor(++issued, now + 30 * minute);
        return call.authorization === 'Bearer token-1' ? new Response('', { status: 401 }) : json(200, [inboxItem]);
    });
    const secrets = await pairedSecrets();
    const client = new SyncClient({ fetch, secrets, now: () => now });

    const result = await client.listInbox(baseUrl);

    assert.deepEqual(result, { status: 'ok', value: [inboxItem] });
    assert.deepEqual(
        calls.map(c => `${c.path} ${c.authorization ?? '-'}`),
        [
            '/api/sync/devices/token -',
            '/api/sync/inbox Bearer token-1',
            '/api/sync/devices/token -',
            '/api/sync/inbox Bearer token-2'
        ]);
    assert.equal(await client.isPaired(), true, 'a refused token says nothing about the credential');
});

test('an inbox that refuses the fresh token too is retried only once', async () => {
    const now = Date.parse('2026-10-15T09:00:00Z');
    let issued = 0;
    const { fetch, calls } = stubFetch(call => {
        if (call.path === '/api/sync/devices/token') return tokenFor(++issued, now + 30 * minute);
        return new Response('', { status: 401 });
    });
    const client = new SyncClient({ fetch, secrets: await pairedSecrets(), now: () => now });

    const result = await client.capture(baseUrl, 'Twice refused');

    assert.equal(result.status, 'unavailable');
    assert.equal(calls.filter(c => c.path === '/api/sync/inbox').length, 2);
});

test('a 401 from the token endpoint clears the stored credential and asks to re-pair', async () => {
    const { fetch, calls } = stubFetch(call => {
        if (call.path === '/api/sync/devices/token') {
            return problem(401, 'device.credential_invalid', 'This device is not recognised.');
        }
        return json(200, []);
    });
    const secrets = await pairedSecrets();
    const client = new SyncClient({ fetch, secrets });

    const result = await client.listInbox(baseUrl);

    assert.deepEqual(result, { status: 'credentialRejected' });
    assert.equal(secrets.values.has(DEVICE_SECRET_KEY), false);
    assert.equal(calls.filter(c => c.path === '/api/sync/inbox').length, 0);
    assert.equal(await client.isPaired(), false);
});

test('a token endpoint that is down keeps the pairing and reports the service unavailable', async () => {
    const { fetch, calls } = stubFetch(() => problem(503, 'sync.replica_unavailable', 'Try again shortly.'));
    const secrets = await pairedSecrets();
    const client = new SyncClient({ fetch, secrets });

    const result = await client.listInbox(baseUrl);

    assert.equal(result.status, 'unavailable');
    assert.equal(secrets.values.has(DEVICE_SECRET_KEY), true);
    assert.equal(calls.filter(c => c.path === '/api/sync/inbox').length, 0);
});

test('an unpaired extension makes no inbox request and asks to pair', async () => {
    const { fetch, calls } = stubFetch(() => json(200, []));
    const client = new SyncClient({ fetch, secrets: new MemorySecrets() });

    assert.deepEqual(await client.listInbox(baseUrl), { status: 'notPaired' });
    assert.deepEqual(await client.capture(baseUrl, 'Nowhere to go'), { status: 'notPaired' });
    assert.equal(calls.length, 0);
});

test('forgetting the pairing drops the credential and the cached token', async () => {
    const now = Date.parse('2026-10-15T09:00:00Z');
    const { fetch, calls } = stubFetch(call =>
        call.path === '/api/sync/devices/token' ? tokenFor(1, now + 30 * minute) : json(200, []));
    const secrets = await pairedSecrets();
    const client = new SyncClient({ fetch, secrets, now: () => now });

    await client.listInbox(baseUrl);
    await client.unpair();

    assert.equal(secrets.values.has(DEVICE_SECRET_KEY), false);
    assert.deepEqual(await client.listInbox(baseUrl), { status: 'notPaired' });
    assert.equal(calls.filter(c => c.path === '/api/sync/inbox').length, 1);
});

test('no failure message carries the credential', async () => {
    const { fetch } = stubFetch(call =>
        call.path === '/api/sync/devices/token'
            ? new Response('', { status: 500 })
            : json(200, []));
    const client = new SyncClient({ fetch, secrets: await pairedSecrets() });

    const result = await client.listInbox(baseUrl);

    assert.equal(result.status, 'unavailable');
    assert.doesNotMatch(JSON.stringify(result), /secret-credential/);
});
