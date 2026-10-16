/**
 * The extension's half of the sync service's device flow, kept free of the
 * vscode API so node:test can drive it with a stub fetch and an in-memory
 * secret store.
 *
 * Every inbox route requires a paired device (local ADR 0005 §Identity). The
 * extension joins the desktop's owner the way a second device does: it redeems
 * a pairing code issued from the desktop's Devices settings, keeps the
 * long-lived credential it is handed in secret storage, and trades that for a
 * short-lived bearer token before each call. It mirrors what
 * `DevicePairingClient` and `SyncTokenProvider` do for the .NET clients.
 */

export interface InboxItem {
    id: string;
    title: string;
    source: string;
    capturedAt: string;
}

/** The slice of VS Code's `SecretStorage` this client uses. */
export interface SecretStore {
    get(key: string): PromiseLike<string | undefined>;
    store(key: string, value: string): PromiseLike<void>;
    delete(key: string): PromiseLike<void>;
}

export type FetchFn = (url: string, init?: RequestInit) => Promise<Response>;

export interface SyncClientOptions {
    fetch: FetchFn;
    secrets: SecretStore;
    now?: () => number;
}

/** What an inbox call came to. `notPaired` and `credentialRejected` both mean
 * "pair again"; they are apart so the prompt can say which happened. */
export type SyncResult<T> =
    | { status: 'ok'; value: T }
    | { status: 'notPaired' }
    | { status: 'credentialRejected' }
    | { status: 'unavailable'; reason: string };

export type PairResult =
    | { status: 'paired'; deviceId: string }
    | { status: 'failed'; reason: string; code?: string };

/** The one secret-storage entry the pairing lives in. One entry rather than
 * two, so a device id is never stored without the credential that goes with it. */
export const DEVICE_SECRET_KEY = 'backlog.sync.device';

/** How long before expiry a token stops being reused — the same two minutes
 * `SyncTokenProvider.RenewalWindow` spends of a thirty-minute token. */
export const TOKEN_RENEWAL_WINDOW_MS = 2 * 60_000;

interface StoredDevice {
    deviceId: string;
    credential: string;
}

interface CachedToken {
    baseUrl: string;
    accessToken: string;
    expiresAt: number;
}

type TokenResult =
    | { status: 'ok'; token: string }
    | { status: 'notPaired' }
    | { status: 'credentialRejected' }
    | { status: 'unavailable'; reason: string };

export class SyncClient {
    private readonly fetch: FetchFn;
    private readonly secrets: SecretStore;
    private readonly now: () => number;
    private token: CachedToken | undefined;

    constructor(options: SyncClientOptions) {
        this.fetch = options.fetch;
        this.secrets = options.secrets;
        this.now = options.now ?? Date.now;
    }

    async isPaired(): Promise<boolean> {
        return (await this.readDevice()) !== undefined;
    }

    /** Redeems a pairing code and stores the credential before returning, since
     * the service shows it exactly once. A failure stores nothing. */
    async pair(baseUrl: string, code: string, deviceName: string): Promise<PairResult> {
        const trimmed = code.trim();
        if (trimmed.length === 0) {
            return { status: 'failed', reason: 'Enter the pairing code shown in the desktop app.' };
        }

        let response: Response;
        try {
            response = await this.fetch(`${baseUrl}/api/sync/devices/pair`, {
                method: 'POST',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ code: trimmed, deviceName })
            });
        } catch (error) {
            return { status: 'failed', reason: `the sync service could not be reached (${describe(error)})` };
        }

        if (!response.ok) {
            const problem = await readProblem(response);
            return problem.code === undefined
                ? { status: 'failed', reason: problem.detail }
                : { status: 'failed', reason: problem.detail, code: problem.code };
        }

        const registration = (await response.json().catch(() => ({}))) as Partial<StoredDevice>;
        if (typeof registration.deviceId !== 'string' || typeof registration.credential !== 'string') {
            return { status: 'failed', reason: 'the sync service answered without a device credential' };
        }

        const device: StoredDevice = { deviceId: registration.deviceId, credential: registration.credential };
        await this.secrets.store(DEVICE_SECRET_KEY, JSON.stringify(device));
        this.token = undefined;
        return { status: 'paired', deviceId: device.deviceId };
    }

    /** Forgets the pairing and the token minted from it. */
    async unpair(): Promise<void> {
        this.token = undefined;
        await this.secrets.delete(DEVICE_SECRET_KEY);
    }

    async listInbox(baseUrl: string): Promise<SyncResult<InboxItem[]>> {
        return this.send(baseUrl, { method: 'GET' }, async response => (await response.json()) as InboxItem[]);
    }

    async capture(baseUrl: string, title: string): Promise<SyncResult<void>> {
        return this.send(
            baseUrl,
            {
                method: 'POST',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ title, source: 'vscode' })
            },
            async () => undefined);
    }

    /** One inbox call with a bearer token. A 401 drops the token and tries once
     * more with a fresh one: the token may have been revoked or outlived, which
     * a new one fixes. A second 401 is reported, not retried. */
    private async send<T>(
        baseUrl: string,
        init: RequestInit,
        read: (response: Response) => Promise<T>): Promise<SyncResult<T>> {
        for (let attempt = 0; attempt < 2; attempt++) {
            const token = await this.accessToken(baseUrl);
            if (token.status !== 'ok') {
                return token;
            }

            let response: Response;
            try {
                response = await this.fetch(`${baseUrl}/api/sync/inbox`, {
                    ...init,
                    headers: { ...(init.headers as Record<string, string> | undefined), authorization: `Bearer ${token.token}` }
                });
            } catch (error) {
                return { status: 'unavailable', reason: describe(error) };
            }

            if (response.status === 401 && attempt === 0) {
                this.token = undefined;
                continue;
            }

            if (!response.ok) {
                return { status: 'unavailable', reason: `HTTP ${response.status}` };
            }

            try {
                return { status: 'ok', value: await read(response) };
            } catch (error) {
                return { status: 'unavailable', reason: describe(error) };
            }
        }

        return { status: 'unavailable', reason: 'HTTP 401' };
    }

    private async accessToken(baseUrl: string): Promise<TokenResult> {
        const device = await this.readDevice();
        if (device === undefined) {
            this.token = undefined;
            return { status: 'notPaired' };
        }

        const cached = this.token;
        if (cached !== undefined
            && cached.baseUrl === baseUrl
            && cached.expiresAt - this.now() >= TOKEN_RENEWAL_WINDOW_MS) {
            return { status: 'ok', token: cached.accessToken };
        }

        this.token = undefined;

        let response: Response;
        try {
            response = await this.fetch(`${baseUrl}/api/sync/devices/token`, {
                method: 'POST',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ deviceId: device.deviceId, credential: device.credential })
            });
        } catch (error) {
            return { status: 'unavailable', reason: describe(error) };
        }

        if (response.status === 401) {
            // The token endpoint is anonymous and authenticates only the
            // credential in the body, so its 401 is a verdict on the pairing.
            // Sending the same credential again cannot fix that; pairing can.
            await this.secrets.delete(DEVICE_SECRET_KEY);
            return { status: 'credentialRejected' };
        }

        if (!response.ok) {
            return { status: 'unavailable', reason: `HTTP ${response.status}` };
        }

        const body = (await response.json().catch(() => ({}))) as { accessToken?: unknown; expiresAt?: unknown };
        const expiresAt = typeof body.expiresAt === 'string' ? Date.parse(body.expiresAt) : NaN;
        if (typeof body.accessToken !== 'string' || body.accessToken.length === 0 || Number.isNaN(expiresAt)) {
            return { status: 'unavailable', reason: 'the token endpoint answered without a token' };
        }

        this.token = { baseUrl, accessToken: body.accessToken, expiresAt };
        return { status: 'ok', token: body.accessToken };
    }

    private async readDevice(): Promise<StoredDevice | undefined> {
        const raw = await this.secrets.get(DEVICE_SECRET_KEY);
        if (raw === undefined) {
            return undefined;
        }
        try {
            const parsed = JSON.parse(raw) as Partial<StoredDevice>;
            return typeof parsed.deviceId === 'string' && typeof parsed.credential === 'string'
                ? { deviceId: parsed.deviceId, credential: parsed.credential }
                : undefined;
        } catch {
            return undefined;
        }
    }
}

/** The service's ProblemDetails `detail` and `code`, or the status when the
 * body is not one. */
async function readProblem(response: Response): Promise<{ detail: string; code?: string }> {
    try {
        const body = (await response.json()) as { detail?: unknown; code?: unknown };
        const detail = typeof body.detail === 'string' && body.detail.length > 0 ? body.detail : `HTTP ${response.status}`;
        return typeof body.code === 'string' ? { detail, code: body.code } : { detail };
    } catch {
        return { detail: `HTTP ${response.status}` };
    }
}

function describe(error: unknown): string {
    return error instanceof Error ? error.message : String(error);
}
