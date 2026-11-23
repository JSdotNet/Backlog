#!/usr/bin/env node
// The dispatch-sessions skill's only wait: run under a Monitor, it loops on the Backlog app's
// GET /session-requests/wait long poll (local ADR 0038 §4) and prints one line per answer
// delivery (local ADR 0041) and per request, so the Manager spends no model turn while nothing waits.
//
// The route takes sessions:dispatch, which only the "Manager" grant holds (local ADR 0058 §4):
// each call carries a fresh token for mcpServer.grants.manager from the app's installed headers
// helper, so an app restarted on another port, or a token past its 15 minutes, is found without
// restarting the Monitor. BACKLOG_MCP_TOKEN is sent only when no Manager grant is recorded. See
// ../../../hooks/backlog-grant.mjs.
//
// Lines, each one event for the Manager:
//   ready dispatcher=<name> url=<url>   once, at start
//   delivery <id> <desktop-session-id>  an answer for a session, before any request, once per offer
//   request <id> <kind> <repository>    the Requested request at the head of the queue, once per Monitor
//   flag-off | unauthorized | unreachable | unavailable[ status=<n>] | no-token <reason>
//                                       once per change of state
//   stopped <reason>                    the script ended; the Monitor needs re-arming
//
// `--name` prints the dispatcher name alone and exits, so the Manager has it in the same turn.
// `--retry <ms>` (default 30000) is the pause after a failure or a request already printed.
// `--session <id>` is the Manager's own session id: each wait then counts as its loop check-in
// (local ADR 0051 §3). Without it the wait checks nothing in.
// The decisions are in wait-logic.mjs, which the plugin's tests drive.

import os from "node:os";

import { authorization, readEndpoint } from "../../../hooks/backlog-grant.mjs";
import { decide, dispatcherName, isNews, noToken, waitRoute } from "./wait-logic.mjs";

// The route holds a call for 50 seconds; give it room before abandoning the call.
const CALL_TIMEOUT_MS = 70_000;

function option(args, name, fallback) {
    const at = args.indexOf(name);
    const value = at >= 0 ? Number(args[at + 1]) : NaN;
    return Number.isFinite(value) && value >= 0 ? value : fallback;
}

function text(args, name) {
    const at = args.indexOf(name);
    const value = at >= 0 ? args[at + 1] : undefined;
    return value && !value.startsWith("--") ? value : null;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function main(args) {
    if (args.includes("--name")) {
        console.log(dispatcherName(os.hostname()));
        return;
    }
    const retryMs = option(args, "--retry", 30_000);
    const sessionId = text(args, "--session");
    const printed = new Set();
    let previous = null;
    const route = waitRoute(sessionId);

    console.log(`ready dispatcher=${dispatcherName(os.hostname())} url=${(await readEndpoint("manager", route)).url}`);

    while (true) {
        let outcome;
        const target = await readEndpoint("manager", route);
        const { header, error } = await authorization(target, "Manager");
        if (!header) outcome = noToken(error, { retryMs });
        else {
            try {
                const response = await fetch(target.url, { headers: { authorization: header }, signal: AbortSignal.timeout(CALL_TIMEOUT_MS) });
                const body = await response.text();
                const retryAfter = Number(response.headers.get("retry-after"));
                outcome = decide(response.status, body, printed, {
                    retryMs,
                    retryAfterMs: Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : undefined,
                });
            } catch (failure) {
                // A timeout is a hung call: ask again. Anything else is an app that is not running.
                outcome =
                    failure?.name === "TimeoutError"
                        ? { line: null, state: "idle", waitMs: 0 }
                        : { line: "unreachable", state: "unreachable", waitMs: retryMs };
            }
        }

        if (isNews(outcome, previous)) console.log(outcome.line);
        previous = outcome.state;

        if (outcome.waitMs > 0) await sleep(outcome.waitMs);
    }
}

main(process.argv.slice(2)).catch((failure) => {
    console.log(`stopped ${failure?.message ?? failure}`);
    process.exit(1);
});
