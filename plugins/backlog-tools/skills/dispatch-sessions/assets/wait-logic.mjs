// What wait-requests.mjs decides, kept free of Node imports so the plugin's tests, which run in
// a sandbox that loads relative files only, can drive it. Where the route is and the Manager
// grant's token come from ../../../hooks/backlog-grant.mjs.

export const ROUTE = "/session-requests/wait";

// What a call that could not get the Manager grant's token means: said once, like a refusal,
// and asked for again after retryMs. `reason` is the sentence backlog-grant.mjs gave.
export function noToken(reason, { retryMs }) {
    return { line: `no-token ${reason}`, state: "no-token", waitMs: retryMs };
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
