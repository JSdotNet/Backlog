#!/usr/bin/env node
// Forwards one Claude Code hook event to the Backlog desktop app, which attributes it to the
// delivery run the session is driving (Backlog's DeliveryRunTelemetry). The dashboards collect
// the same figures from hooks rather than tools; this is the forwarding half, and the app does
// the counting — it reads the transcript the event names, so nothing here does.
//
// Endpoint: POST http://127.0.0.1:<port>/telemetry, beside the app's /mcp, with a token for the
// "Telemetry" grant (mcpServer.grants.telemetry, local ADR 0058, Phase 1), asked for per event
// through the app's installed headers helper — see backlog-grant.mjs. BACKLOG_MCP_TOKEN is sent
// only when no Telemetry grant is recorded. With neither, nothing is posted.
//
// Must never fail or slow the tool call it reports on. Run as the hook, it only reads the event,
// spools it to a file and hands it to a detached copy of itself (`--send <file>`), then exits 0:
// asking the headers helper — a second Node process — and posting both happen off the tool
// call's path, so a busy machine can no longer push the hook past its timeout. The sender drops
// every error, abandons a hung app after SEND_TIMEOUT_MS, and removes the file it was handed.
// BACKLOG_TELEMETRY_SPOOL overrides the spool folder, <system temp>/backlog-telemetry by default.
//
// Senders finish in any order, so the app cannot time a call by when its events arrive. Every
// event carries `backlog_at`, when the hook fired. PreToolUse posts nothing: it leaves its moment
// in a start file named for the call's tool_use_id, and PostToolUse takes it from there as
// `backlog_started_at`. A start file a denied or failed call leaves behind, and a spooled event
// a killed sender leaves, are swept at SessionEnd once they are a day old.

import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { mkdir, readdir, readFile, stat, unlink, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { authorization, readEndpoint } from "./backlog-grant.mjs";

// How long the hook waits for a stdin that never ends.
const STDIN_TIMEOUT_MS = 1000;
// The sender's whole budget, token and post together; nothing waits on it.
const SEND_TIMEOUT_MS = 10000;
// The share of it the headers helper may take; the broker answers in milliseconds.
const HELPER_TIMEOUT_MS = 6000;
// How old a spooled event or start file must be before SessionEnd sweeps it.
const STALE_MS = 24 * 60 * 60 * 1000;
const PREFIX = "backlog-telemetry-";

function readStdin() {
    return new Promise((resolve) => {
        let data = "";
        process.stdin.setEncoding("utf8");
        process.stdin.on("data", (chunk) => (data += chunk));
        process.stdin.on("end", () => resolve(data));
        // A hook invoked with no stdin must not hang the tool call.
        setTimeout(() => resolve(data), STDIN_TIMEOUT_MS).unref();
    });
}

// The start file for one tool call, or null for an id that cannot name a file safely.
function startFile(folder, id) {
    return typeof id === "string" && /^[\w-]{1,128}$/.test(id) ? path.join(folder, `${PREFIX}start-${id}`) : null;
}

// Removes this forwarder's files in `folder` older than STALE_MS.
async function sweep(folder) {
    const cutoff = Date.now() - STALE_MS;
    for (const name of await readdir(folder)) {
        if (!name.startsWith(PREFIX)) continue;
        const file = path.join(folder, name);
        try {
            if ((await stat(file)).mtimeMs < cutoff) await unlink(file);
        } catch {
            // Gone already, or held open by a sender still running.
        }
    }
}

// The hook: stamp and spool the event and start the sender, waiting on nothing it does.
async function handOff() {
    const event = JSON.parse(await readStdin());
    const folder = process.env.BACKLOG_TELEMETRY_SPOOL || path.join(os.tmpdir(), "backlog-telemetry");
    await mkdir(folder, { recursive: true });
    event.backlog_at = new Date().toISOString();

    const start = startFile(folder, event.tool_use_id);
    if (event.hook_event_name === "PreToolUse" && start) {
        await writeFile(start, event.backlog_at, "utf8");
        return;
    }
    if (event.hook_event_name === "PostToolUse" && start) {
        try {
            event.backlog_started_at = (await readFile(start, "utf8")).trim();
            await unlink(start);
        } catch {
            // No start recorded: the app times the call without one.
        }
    }
    // The tool's output is the bulk of a PostToolUse payload and is not forwarded.
    delete event.tool_response;

    const file = path.join(folder, `${PREFIX}${randomUUID()}.json`);
    await writeFile(file, JSON.stringify(event), "utf8");
    // A sender that never starts leaves its file for the sweep: the hook does not wait to learn it.
    spawn(process.execPath, [fileURLToPath(import.meta.url), "--send", file], {
        // Not the session's worktree: a sender still running must not hold it open on Windows.
        cwd: os.tmpdir(),
        detached: true,
        stdio: "ignore",
        windowsHide: true,
    }).unref();

    if (event.hook_event_name === "SessionEnd") await sweep(folder).catch(() => {});
}

// The sender: post the spooled event under the Telemetry grant, then remove the file.
async function send(file) {
    try {
        const body = await readFile(file, "utf8");
        const deadline = Date.now() + SEND_TIMEOUT_MS;
        const target = await readEndpoint("telemetry", "/telemetry");
        const { header } = await authorization(target, "Telemetry", { timeoutMs: HELPER_TIMEOUT_MS });
        // No token is a refusal waiting to happen: the route answers 401 to a call without one.
        if (!header) return;

        // A 401 or 403 — the grant revoked, or its scope switched off — is dropped like any other
        // failure: the session goes on, and the next event asks for a fresh token.
        await fetch(target.url, {
            method: "POST",
            headers: { "content-type": "application/json", authorization: header },
            body,
            signal: AbortSignal.timeout(Math.max(250, deadline - Date.now())),
        });
    } finally {
        await unlink(file).catch(() => {});
    }
}

const [mode, file] = process.argv.slice(2);
(mode === "--send" && file ? send(file) : handOff())
    .catch(() => {})
    .finally(() => process.exit(0));
