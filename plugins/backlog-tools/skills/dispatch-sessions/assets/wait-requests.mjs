#!/usr/bin/env node
// The dispatch-sessions skill's only wait: run under a Monitor, it loops on the Backlog app's
// GET /session-requests/wait long poll (local ADR 0037 §4) and prints one line per request, so
// the Manager session spends no model turn while nothing is queued.
//
// Port and token are found the way hooks/telemetry-forwarder.mjs finds them: BACKLOG_MCP_PORT /
// BACKLOG_MCP_TOKEN, else the app's own settings.json, read again after every failure so an app
// restarted on another port or token is found without restarting the Monitor.
//
// Lines, each one event for the Manager:
//   ready dispatcher=<name> url=<url>   once, at start
//   request <id> <kind> <repository>    a Requested request, oldest first, once per Monitor
//   flag-off | unauthorized | unreachable | unavailable[ status=<n>]   once per change of state
//   stopped <reason>                    the script ended; the Monitor needs re-arming
//
// `--name` prints the dispatcher name alone and exits, so the Manager has it in the same turn.
// `--retry <ms>` (default 30000) is the pause after a failure or a request already printed.
// The decisions are in wait-logic.mjs, which the plugin's tests drive.

import { readFile } from "node:fs/promises";
import os from "node:os";

import { decide, dispatcherName, endpointFrom, isNews, settingsPath } from "./wait-logic.mjs";

// The route holds a call for 50 seconds; give it room before abandoning the call.
const CALL_TIMEOUT_MS = 70_000;

async function endpoint() {
    let text = null;
    try {
        text = await readFile(settingsPath(process.env, process.platform, os.homedir()), "utf8");
    } catch {
        // No settings yet: the app has never served MCP on this machine.
    }
    return endpointFrom(process.env, text);
}

function option(args, name, fallback) {
    const at = args.indexOf(name);
    const value = at >= 0 ? Number(args[at + 1]) : NaN;
    return Number.isFinite(value) && value >= 0 ? value : fallback;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function main(args) {
    if (args.includes("--name")) {
        console.log(dispatcherName(os.hostname()));
        return;
    }
    const retryMs = option(args, "--retry", 30_000);
    const printed = new Set();
    let previous = null;
    let { url, token } = await endpoint();

    console.log(`ready dispatcher=${dispatcherName(os.hostname())} url=${url}`);

    while (true) {
        let outcome;
        try {
            const headers = token ? { authorization: `Bearer ${token}` } : {};
            const response = await fetch(url, { headers, signal: AbortSignal.timeout(CALL_TIMEOUT_MS) });
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

        if (isNews(outcome, previous)) console.log(outcome.line);
        previous = outcome.state;

        if (outcome.waitMs > 0) {
            await sleep(outcome.waitMs);
            if (outcome.state !== "seen") ({ url, token } = await endpoint());
        }
    }
}

main(process.argv.slice(2)).catch((failure) => {
    console.log(`stopped ${failure?.message ?? failure}`);
    process.exit(1);
});
