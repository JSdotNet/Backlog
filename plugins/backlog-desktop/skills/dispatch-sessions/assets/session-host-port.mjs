// The session-host port of local ADR 0038 §4: the five operations a dispatcher acts on sessions
// through, whatever host runs them. Each host is an adapter that implements them with its own
// commands, in its own plugin; this module names no host. Kept free of Node imports, like the
// other shared logic, so a sandbox that loads relative files only can drive it.
//
// An adapter is an object with five async methods:
//
//   start({ cwd, brief, title, background })  → a handle for the session it started in a new
//                                                worktree of `cwd`, with `brief` as its whole prompt
//   sessionId(handle)                          → the id the host gave that session, a string
//   list()                                     → [{ id, name, prNumber?, archived? }] — the sessions
//                                                the host holds, archived ones included where it keeps any
//   state(id)                                  → "working" | "waiting" | "idle"
//   deliver(id, message)                       → sends one message to one session, starting a turn
//                                                when it is idle (local ADR 0041 §3); throws, or
//                                                answers { delivered: false, reason }, when it cannot
//
// The port takes the clone folder, the fixed brief, a session id and a message built only from a
// stored question and its accepted answer — never entry text — so the trust rule of ADR 0038 §1
// holds whatever host is behind it.

export const OPERATIONS = Object.freeze(["start", "sessionId", "list", "state", "deliver"]);

export const STATES = Object.freeze(["working", "waiting", "idle"]);

// The host, checked: every operation is a function. Throws naming what is missing, so an adapter
// that cannot answer all five never dispatches (ADR 0038, Consequences).
export function sessionHost(host) {
    const missing = OPERATIONS.filter((operation) => typeof host?.[operation] !== "function");
    if (missing.length > 0) throw new TypeError(`The session host does not implement ${missing.join(", ")}.`);
    return host;
}

// One row of an adapter's list() in the port's shape. An adapter whose host names its fields
// otherwise — Claude Desktop's list_sessions says `title` and `isArchived`, and its `name` is a
// linked session's short handle, not its title — is read the same way: a `title` wins.
export function portSession(row) {
    return {
        id: row?.id ?? row?.sessionId ?? null,
        name: String(row?.title ?? row?.name ?? ""),
        prNumber: Number.isInteger(row?.prNumber) ? row.prNumber : null,
        archived: (row?.archived ?? row?.isArchived) === true,
    };
}

// The message a delivery sends: the stored question and the accepted answer, both verbatim.
export function deliveryMessage(question, answer) {
    return `You asked: ${question}. The answer from Backlog: ${answer}`;
}
