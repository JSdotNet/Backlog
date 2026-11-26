// The host-neutral steps of a dispatcher (local ADRs 0038 and 0041), acting on sessions only
// through a session host (session-host-port.mjs). What each step asks Backlog goes through
// `backlog.call(tool, args)`, which answers the tool's result: manager-call.mjs is that call
// from a shell, under the Manager grant. Kept free of Node imports, so any adapter — a skill a
// model follows, or a script — runs the same decisions, and the tests drive them against
// fake-session-host.mjs.
//
// The order a turn takes is the order the steps are written: release what a restart left
// held, deliver answers, then the queue under capacity, then — the queue empty — the mend.
// The wait itself is wait-requests.mjs, whose decisions are wait-logic.mjs.

import { fixersFor, isFixer } from "./mend-logic.mjs";
import { deliveryMessage, portSession, sessionHost } from "./session-host-port.mjs";

// The background line a started session carries, saying who started it and for what.
export const requestBackground = (id) => `Started by dispatch-sessions for Backlog session request ${id}.`;
export const mendBackground = (repository, number) => `Started by dispatch-sessions to mend ${repository}#${number}.`;

// After a restart: gives back every request and delivery claim this dispatcher still holds,
// so none waits out its lease. Answers the ids released.
export async function releaseHeld({ backlog, dispatcher }) {
    const released = [];
    const requests = (await backlog.call("list_session_requests", { state: "claimed" }))?.requests ?? [];
    for (const request of requests.filter((r) => r.claimedBy === dispatcher)) {
        await backlog.call("mark_session_request", { id: request.id, outcome: "released", dispatcher });
        released.push(request.id);
    }
    const deliveries = (await backlog.call("list_deliveries", { state: "claimed" }))?.deliveries ?? [];
    for (const delivery of deliveries.filter((d) => d.claimedBy === dispatcher)) {
        await backlog.call("mark_delivery", { id: delivery.id, outcome: "released", dispatcher });
        released.push(delivery.id);
    }
    return released;
}

// Delivers each pending answer, oldest first, to the session `target(delivery)` names — the id
// the host knows the session by, or null when the host holds no such session, which skips it.
// Claims it first; one another dispatcher holds is left alone. A delivery the host accepted is
// marked delivered; one it refused, or that failed, is released, so it stays pending and comes
// back on a later wait (ADR 0041 §3). Answers one { id, outcome } per delivery looked at.
export async function deliverAnswers({ backlog, host, dispatcher, target }) {
    sessionHost(host);
    const outcomes = [];
    const pending = (await backlog.call("list_deliveries", { state: "pending" }))?.deliveries ?? [];
    for (const delivery of pending) {
        const session = target(delivery);
        if (!session) continue;
        const claim = await backlog.call("claim_delivery", { id: delivery.id, dispatcher });
        if (!claim?.claimed) {
            outcomes.push({ id: delivery.id, outcome: "taken", reason: claim?.refusal ?? null });
            continue;
        }
        let sent;
        try {
            sent = await host.deliver(session, deliveryMessage(delivery.question, delivery.answer));
        } catch (failure) {
            sent = { delivered: false, reason: failure?.message ?? String(failure) };
        }
        const outcome = sent?.delivered === true ? "delivered" : "released";
        await backlog.call("mark_delivery", { id: delivery.id, outcome, dispatcher });
        outcomes.push({ id: delivery.id, outcome, reason: sent?.reason ?? null });
    }
    return outcomes;
}

// The sessions counted against `sessions`, among the open ones `own` admits — every listed one
// when no `own` is given; `own` gets the host's row with the port's fields added, so an adapter's
// own fields (Claude Desktop's `startedBy`) reach it: the working ones, and apart from them the
// working fixers, which the app never counts. With `fixersOnly`, the other working ones are not
// read: the check-in answer already counted them.
export async function countSessions({ host, own, fixersOnly = false }) {
    sessionHost(host);
    const rows = (await host.list()).map((row) => ({ ...row, ...portSession(row) })).filter((s) => !s.archived && (!own || own(s)));
    let working = 0;
    let fixers = 0;
    for (const row of rows) {
        const fixer = isFixer(row.name);
        if (fixersOnly && !fixer) continue;
        if ((await host.state(row.id)) !== "working") continue;
        if (fixer) fixers += 1;
        else working += 1;
    }
    return { working, fixers };
}

// Whether one more session fits, and what holds it when not. `checkIn` is the check_in answer's
// `capacity` (or null where the app measures none); `startsSince` the starts made since that
// answer — a session the host counts itself is not counted twice. A figure the answer lacks —
// absent or null — is measured: sessions through the host,
// usage and the machine through `measure.usage()` (every plan limit's percent) and
// `measure.machine()` (capacity.ps1's line), each only when given.
export async function room({ checkIn, limits, startsSince = 0, cardsOpen = 0, host, own, measure = {} }) {
    const heldBy = [];
    if (checkIn?.hasRoom === false) heldBy.push(...(checkIn.heldBy?.length ? checkIn.heldBy : ["capacity"]));
    const counted = Number.isFinite(checkIn?.workingSessions);
    const { working, fixers } = await countSessions({ host, own, fixersOnly: counted });
    // The app's figure predates the starts since it; the host's own count already holds them.
    const workingSessions = counted ? checkIn.workingSessions + startsSince : working;
    if (workingSessions + cardsOpen + fixers >= limits.sessions && !heldBy.includes("sessions")) heldBy.push("sessions");
    if (heldBy.length === 0 && !Number.isFinite(checkIn?.planUsagePercent) && measure.usage) {
        const percents = (await measure.usage()) ?? [];
        if (percents.some((p) => Number.isFinite(p) && p >= limits.usage)) heldBy.push("usage");
    }
    const unmeasured = !Number.isFinite(checkIn?.cpuPercent) || !Number.isFinite(checkIn?.freeMemoryGb);
    if (heldBy.length === 0 && unmeasured && measure.machine) {
        const machine = await measure.machine();
        if (machine && machine.fits === false) heldBy.push(...(machine.heldBy?.length ? machine.heldBy : ["machine"]));
    }
    return { fits: heldBy.length === 0, heldBy };
}

// Dispatches one Requested request: claims it, starts its session in its clone folder with the
// app's brief verbatim, and reports the id the host gave it. A start that fails gives the claim
// back. A late `started` still counts while this dispatcher holds the claim (ADR 0038 §3); when
// another dispatcher claimed it meanwhile, the claim is never released and the outcome is
// `double`, naming both sessions so the person stops one. A session started whose id the host
// cannot say is `unreported`: the claim is kept, since releasing it would start a second one.
export async function dispatchRequest(request, { backlog, host, dispatcher, title }) {
    sessionHost(host);
    const claim = await backlog.call("claim_session_request", { id: request.id, dispatcher });
    if (!claim?.claimed) return { id: request.id, outcome: "taken", reason: claim?.refusal ?? null };
    let handle;
    try {
        handle = await host.start({ cwd: request.cloneFolder, brief: request.brief, title, background: requestBackground(request.id) });
    } catch (failure) {
        await backlog.call("mark_session_request", { id: request.id, outcome: "released", dispatcher });
        return { id: request.id, outcome: "released", reason: failure?.message ?? String(failure) };
    }
    let sessionId;
    try {
        sessionId = await host.sessionId(handle);
    } catch (failure) {
        return { id: request.id, outcome: "unreported", reason: failure?.message ?? String(failure) };
    }
    const mark = await backlog.call("mark_session_request", { id: request.id, outcome: "started", sessionId, dispatcher });
    if (mark?.marked === false) {
        const holder = mark.request?.claimedBy ?? null;
        if (!holder || holder === dispatcher) return { id: request.id, outcome: "refused", sessionId, reason: mark.refusal ?? null };
        return { id: request.id, outcome: "double", sessionId, holder: mark.request?.claimedBy ?? null, otherSession: mark.request?.sessionId ?? null, reason: mark.refusal ?? null };
    }
    return { id: request.id, outcome: "started", sessionId };
}

// The queue, in the order listed — the person's order, never re-sorted. A paused queue starts
// nothing. Before each start, room() decides; the first that does not fit holds the rest.
// `titleOf(request)` is the session's title (an entry's title, a plan's +tag).
export async function dispatchQueue({ backlog, host, dispatcher, titleOf, checkIn, limits, own, measure, cardsOpen = 0 }) {
    const listed = await backlog.call("list_session_requests", { state: "requested" });
    if (listed?.paused) return { paused: true, held: null, outcomes: [] };
    const outcomes = [];
    let startsSince = 0;
    for (const request of listed?.requests ?? []) {
        const fit = await room({ checkIn, limits, startsSince, cardsOpen, host, own, measure });
        if (!fit.fits) return { paused: false, held: fit.heldBy, outcomes };
        const outcome = await dispatchRequest(request, { backlog, host, dispatcher, title: await titleOf(request) });
        if (["started", "double", "unreported", "refused"].includes(outcome.outcome)) startsSince += 1;
        outcomes.push(outcome);
    }
    // None left: the caller goes on to the mend (step 8).
    return { paused: false, held: null, outcomes, empty: true, startsSince };
}

// The mend, once the queue is empty and not paused: per clone folder, `readPullRequests(folder)`
// answers { repository, pullRequests }; each fixer fixersFor prints starts in that folder while
// room() says it fits. Answers one line per pull request looked at, with its outcome.
export async function mend({ host, folders, readPullRequests, checkIn, limits, own, measure, startsSince = 0, cardsOpen = 0 }) {
    sessionHost(host);
    const outcomes = [];
    for (const folder of folders) {
        let read;
        try {
            read = await readPullRequests(folder);
        } catch (failure) {
            outcomes.push({ folder, outcome: "error", reason: failure?.message ?? String(failure) });
            continue;
        }
        const sessions = await host.list();
        for (const fixer of fixersFor(read.repository, read.pullRequests, sessions)) {
            if (fixer.skip) {
                outcomes.push({ ...fixer, outcome: fixer.skip });
                continue;
            }
            const fit = await room({ checkIn, limits, startsSince, cardsOpen, host, own, measure });
            if (!fit.fits) return { held: fit.heldBy, outcomes };
            try {
                const handle = await host.start({ cwd: folder, brief: fixer.brief, title: fixer.title, background: mendBackground(fixer.repository, fixer.number) });
                // A fixer started here counts once, as a working fixer, never also as a start.
                outcomes.push({ repository: fixer.repository, number: fixer.number, outcome: "started", sessionId: await host.sessionId(handle) });
            } catch (failure) {
                outcomes.push({ repository: fixer.repository, number: fixer.number, outcome: "error", reason: failure?.message ?? String(failure) });
            }
        }
    }
    return { held: null, outcomes };
}
