// What wait-requests.mjs decides, kept free of Node imports so the plugin's tests, which run in
// a sandbox that loads relative files only, can drive it.

export const DEFAULT_PORT = 5757;
export const ROUTE = "/session-requests/wait";

// Where the app keeps settings.json: .NET's LocalApplicationData, then the Backlog folder —
// the same place hooks/telemetry-forwarder.mjs reads.
export function settingsPath(env, platform, home) {
    const sep = platform === "win32" ? "\\" : "/";
    const join = (...parts) => parts.map((part, i) => (i === 0 ? part.replace(/[\\/]+$/, "") : part)).join(sep);
    const localData =
        platform === "win32"
            ? env.LOCALAPPDATA || join(home, "AppData", "Local")
            : platform === "darwin"
              ? join(home, "Library", "Application Support")
              : env.XDG_DATA_HOME || join(home, ".local", "share");
    return join(localData, "Backlog", "settings.json");
}

// BACKLOG_MCP_PORT / BACKLOG_MCP_TOKEN first, then the app's settings.json text (null when it
// could not be read), then the app's default port.
export function endpointFrom(env, settingsText) {
    let port = Number(env.BACKLOG_MCP_PORT) || null;
    let token = env.BACKLOG_MCP_TOKEN || null;
    if ((!port || !token) && settingsText) {
        try {
            const mcp = JSON.parse(settingsText).mcpServer || {};
            port ||= Number(mcp.port) || null;
            token ||= mcp.token || null;
        } catch {
            // A half-written settings.json reads as none.
        }
    }
    return { url: `http://127.0.0.1:${port || DEFAULT_PORT}${ROUTE}`, token };
}

// The dispatcher name a restarted Manager on this machine claims with again.
export function dispatcherName(host) {
    return `dispatch-sessions-${String(host).toLowerCase().replace(/[^a-z0-9-]/g, "-")}`;
}

// What one answer from the route means: the line to print (or null), the state, and how long
// to wait before the next call. `printed` holds what this Monitor already printed.
//
// The route answers one item, `kind` `delivery` (an answer for a session, local ADR 0041) before
// `kind` `request` (a session request, its entry-or-plan kind as `requestKind`). It answers the
// same item until a dispatcher claims it, so each is printed once: the Manager's /loop tick, not
// this script, decides when a held queue is looked at again, and a re-armed Monitor starts with
// nothing printed. A delivery is forgotten once the route answers another item or nothing, so
// one the Manager released is printed again when the route offers it again.
export function decide(status, body, printed, { retryMs, retryAfterMs }) {
    if (status === 200) {
        let item;
        try {
            item = JSON.parse(body);
        } catch {
            return { line: "unavailable status=200", state: "unavailable", waitMs: retryMs };
        }
        const key = item.kind === "delivery" ? `delivery:${item.id}` : item.id;
        forgetDeliveriesBut(printed, key);
        if (printed.has(key)) return { line: null, state: "seen", waitMs: retryMs };
        printed.add(key);
        if (item.kind === "delivery") {
            return { line: `delivery ${item.id} ${item.desktopSessionId}`, state: "delivery", waitMs: 0 };
        }
        return { line: `request ${item.id} ${item.requestKind ?? item.kind} ${item.repository}`, state: "request", waitMs: 0 };
    }
    if (status === 204) {
        forgetDeliveriesBut(printed, null);
        return { line: null, state: "idle", waitMs: 0 };
    }
    if (status === 404) return { line: "flag-off", state: "flag-off", waitMs: retryMs };
    if (status === 401 || status === 403) return { line: "unauthorized", state: "unauthorized", waitMs: retryMs };
    if (status === 503) return { line: "unavailable", state: "unavailable", waitMs: retryAfterMs ?? retryMs };
    return { line: `unavailable status=${status}`, state: "unavailable", waitMs: retryMs };
}

// A request or a delivery is news every time; anything else only when it differs from the
// previous answer.
export function isNews(outcome, previousState) {
    return outcome.state === "request" || outcome.state === "delivery" || (outcome.line !== null && outcome.state !== previousState);
}

function forgetDeliveriesBut(printed, key) {
    for (const seen of [...printed]) {
        if (seen.startsWith("delivery:") && seen !== key) printed.delete(seen);
    }
}
