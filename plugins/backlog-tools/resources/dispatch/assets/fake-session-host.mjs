// A session host held in memory, for tests and for a dispatcher run with no host behind it. It
// implements the five operations of session-host-port.mjs and records every call, so a test can
// say what the dispatcher asked the host to do. Nothing is started and nothing is sent.
//
//   const host = fakeSessionHost({ sessions: [{ id: "s1", name: "x", state: "working" }] });
//   host.calls            every call, in order: { operation, args }
//   host.sessions         the sessions it holds: { id, name, prNumber, archived, state, cwd, brief, inbox }
//   host.refuse.start     a reason: the next start throws it (then clears)
//   host.refuse.deliver   a reason: every deliver answers { delivered: false, reason }
//   host.gone(id)         the session ends: it leaves the list, and a deliver to it throws

import { STATES, sessionHost } from "./session-host-port.mjs";

export function fakeSessionHost({ sessions = [], prefix = "fake-" } = {}) {
    const calls = [];
    const held = sessions.map((s) => ({ prNumber: null, archived: false, state: "idle", inbox: [], ...s }));
    const refuse = { start: null, deliver: null };
    let next = held.length;

    const find = (id) => held.find((s) => s.id === id && !s.gone);

    const host = {
        calls,
        sessions: held,
        refuse,
        gone(id) {
            const session = find(id);
            if (session) session.gone = true;
        },
        async start({ cwd, brief, title, background } = {}) {
            calls.push({ operation: "start", args: { cwd, brief, title, background } });
            if (refuse.start) {
                const reason = refuse.start;
                refuse.start = null;
                throw new Error(reason);
            }
            const session = { id: `${prefix}${++next}`, name: String(title ?? ""), prNumber: null, archived: false, state: "working", cwd, brief, inbox: [] };
            held.push(session);
            return { session: session.id };
        },
        async sessionId(handle) {
            calls.push({ operation: "sessionId", args: { handle } });
            return handle.session;
        },
        async list() {
            calls.push({ operation: "list", args: {} });
            return held.filter((s) => !s.gone).map(({ id, name, prNumber, archived }) => ({ id, name, prNumber, archived }));
        },
        async state(id) {
            calls.push({ operation: "state", args: { id } });
            const session = find(id);
            if (!session) throw new Error(`No session ${id}.`);
            return STATES.includes(session.state) ? session.state : "idle";
        },
        async deliver(id, message) {
            calls.push({ operation: "deliver", args: { id, message } });
            if (refuse.deliver) return { delivered: false, reason: refuse.deliver };
            const session = find(id);
            if (!session) throw new Error(`No session ${id}.`);
            session.inbox.push(message);
            if (session.state === "idle") session.state = "working";
            return { delivered: true };
        },
    };
    return sessionHost(host);
}
