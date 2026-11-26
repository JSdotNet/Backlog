// The Claude Desktop adapter of the session-host port (session-host-port.mjs, local ADR 0038 §4):
// each of the five operations as the Claude Desktop tool call that does it. The dispatch-sessions
// skill makes these calls itself — only a model turn reaches Claude Desktop's tools — so this
// module is the adapter written down once: `claudeDesktopHost(invoke)` is the port over any
// `invoke(tool, args)`, and the plugin's tests hold the calls to what the skill sends.
//
//   Start       start_session — fresh, its own worktree of the clone folder, the brief as its whole
//               prompt, wake_when_done; `initiation` user_asked, or own_initiative on a turn the
//               person did not type, which leaves a card (`proposal_id`) instead of a session
//   Report id   the session id start_session answered
//   List        Claude Desktop's list_sessions (include_archived, limit 100)
//   Read state  `backlog`'s list_sessions: Waiting is waiting; Claude Desktop's isRunning: working, else idle
//   Deliver     send_message, which wakes an idle session; "delivered" and "queued" are accepted

import { portSession, sessionHost } from "./session-host-port.mjs";

// The start_session arguments for one start through the port.
export function startSessionArguments({ cwd, brief, title, background }, { initiation = "user_asked" } = {}) {
    return { initiation, context: "fresh", cwd, use_worktree: true, prompt: brief, wake_when_done: true, title, background };
}

// The send_message arguments for one delivery through the port.
export function sendMessageArguments(sessionId, message) {
    return { session_id: sessionId, message };
}

// Whether send_message's answer says the host took the message: "delivered" started the turn,
// "queued" runs it once the session's current work ends. Anything else is a refusal.
export function sendAccepted(answer) {
    const text = typeof answer === "string" ? answer : JSON.stringify(answer ?? "");
    return /delivery:\s*(delivered|queued)/i.test(text) || /^\s*"?(delivered|queued)\b/i.test(text);
}

// The id a start_session answer carries, by whichever name it uses.
export function startedId(answer) {
    if (typeof answer === "string") return answer.match(/local_[0-9a-f-]+/i)?.[0] ?? null;
    return answer?.sessionId ?? answer?.session_id ?? answer?.id ?? null;
}

// The port over Claude Desktop. `invoke(tool, args)` calls one tool and answers its result:
// start_session, send_message and list_sessions are Claude Desktop's; `backlog:list_sessions`
// is the `backlog` server's.
// The port starts with `user_asked`: a card (own_initiative) starts no session to report, so the
// skill's card path stays its own, with startSessionArguments' `initiation` option.
export function claudeDesktopHost(invoke) {
    return sessionHost({
        async start(request) {
            return invoke("start_session", startSessionArguments(request));
        },
        async sessionId(answer) {
            const id = startedId(answer);
            if (!id) throw new Error("start_session answered no session id.");
            return id;
        },
        async list() {
            const rows = await invoke("list_sessions", { include_archived: true, limit: 100 });
            return (rows ?? []).map((row) => ({ ...row, ...portSession(row) }));
        },
        async state(id) {
            const known = (await invoke("backlog:list_sessions", {}))?.sessions ?? [];
            if (known.some((s) => s?.desktopSessionId === id && String(s?.state ?? "").toLowerCase() === "waiting")) return "waiting";
            const rows = await invoke("list_sessions", { linked: true, limit: 100 });
            return (rows ?? []).some((row) => (row?.sessionId ?? row?.id) === id && row?.isRunning === true) ? "working" : "idle";
        },
        async deliver(id, message) {
            let answer;
            try {
                answer = await invoke("send_message", sendMessageArguments(id, message));
            } catch (failure) {
                return { delivered: false, reason: failure?.message ?? String(failure) };
            }
            return sendAccepted(answer) ? { delivered: true } : { delivered: false, reason: String(answer) };
        },
    });
}
