#!/usr/bin/env node
// Reports a scheduled run to Backlog's Schedules page (local ADR 0051 §1). One script, four
// Claude Code hook events, told apart by the payload's hook_event_name:
//
// - SessionStart (startup) and UserPromptSubmit: when the session's first prompt opens with
//   `<scheduled-task name="…">`, add context telling the session to look its schedule up once
//   with the scheduler tool and call `report_schedule_run` with event `started` before any
//   other work. Claude Desktop writes a scheduled prompt into the transcript before SessionStart
//   fires; UserPromptSubmit covers a transcript not yet on disk. A marker per session keeps it
//   to once.
// - Stop: POST the run's finish to http://127.0.0.1:<port>/schedule-runs, beside the app's
//   /mcp, with the same bearer token — `skipped` when the final message opens with `Skipped:`,
//   with the session it names as `heldBy`; `failed` when it opens with `Failed:`; else
//   `finished` with its verdict line and its Needs-you rows.
// - StopFailure: Claude Code fires it instead of Stop when the turn ends on an API error (a rate
//   limit, an overloaded or failing server, a billing or authentication error). POST `failed`
//   with the error line the session shows as the verdict.
//
// Must never fail or hold up the run: every error exits 0 with nothing printed, an app that is
// not running refuses the connection at once, and a hung one is abandoned after TIMEOUT_MS.

import { createReadStream } from "node:fs";
import { mkdir, open, readFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import readline from "node:readline";

import {
    endpointFrom,
    failureReport,
    finishReport,
    firstPrompt,
    lastAssistantText,
    promptedAfterStart,
    scheduledTaskName,
    settingsPath,
    startContext,
} from "./schedule-run-logic.mjs";

const TIMEOUT_MS = 2000;

function readStdin() {
    return new Promise((resolve) => {
        let data = "";
        process.stdin.setEncoding("utf8");
        process.stdin.on("data", (chunk) => (data += chunk));
        process.stdin.on("end", () => resolve(data));
        // A hook invoked with no stdin must not hang the run.
        setTimeout(() => resolve(data), TIMEOUT_MS).unref();
    });
}

// Transcript entries in order, one parsed JSON line each; a line that does not parse is skipped.
// `stop` ends the read early once it answers true for an entry.
async function readTranscript(file, stop) {
    const entries = [];
    if (!file) return entries;
    const lines = readline.createInterface({ input: createReadStream(file, { encoding: "utf8" }), crlfDelay: Infinity });
    try {
        for await (const line of lines) {
            if (!line.trim()) continue;
            let entry;
            try {
                entry = JSON.parse(line);
            } catch {
                continue;
            }
            entries.push(entry);
            if (stop && stop(entry)) break;
        }
    } finally {
        lines.close();
    }
    return entries;
}

const opensSession = (entry) =>
    (entry.type === "queue-operation" && entry.operation === "enqueue") || (entry.type === "user" && !entry.isMeta);

// The scheduled task this session runs, read from its first prompt; null when it is no scheduled run.
async function scheduleOf(event) {
    if (event.hook_event_name === "UserPromptSubmit") return scheduledTaskName(event.prompt);
    const head = await readTranscript(event.transcript_path, opensSession);
    return scheduledTaskName(firstPrompt(head));
}

// True the first time it is asked for a session, false after: the start is asked for once.
async function firstTime(sessionId) {
    const dir = path.join(os.tmpdir(), "backlog-tools-schedule-runs");
    await mkdir(dir, { recursive: true });
    try {
        const handle = await open(path.join(dir, `${String(sessionId).replace(/[^A-Za-z0-9_-]/g, "_")}.started`), "wx");
        await handle.close();
        return true;
    } catch {
        return false;
    }
}

async function start(event) {
    if (event.hook_event_name === "SessionStart" && event.source && event.source !== "startup") return;
    const scheduleId = await scheduleOf(event);
    if (!scheduleId || !(await firstTime(event.session_id))) return;
    const output = JSON.stringify({
        hookSpecificOutput: { hookEventName: event.hook_event_name, additionalContext: startContext(scheduleId, event.session_id) },
    });
    // Written before the exit in main's finally, which would cut an asynchronous pipe short.
    await new Promise((resolve) => process.stdout.write(output, resolve));
}

async function finish(event) {
    const scheduleId = await scheduleOf(event);
    if (!scheduleId) return;
    // Stop and StopFailure fire after every turn; once a person has replied in the run's session,
    // its report stands and a later turn is no finish, nor a failure of the run.
    const entries = await readTranscript(event.transcript_path);
    if (promptedAfterStart(entries)) return;
    let report;
    if (event.hook_event_name === "StopFailure") report = failureReport(scheduleId, event.session_id, event);
    else {
        const message = typeof event.last_assistant_message === "string" ? event.last_assistant_message : lastAssistantText(entries);
        report = finishReport(scheduleId, event.session_id, message);
    }

    let settingsText = null;
    try {
        settingsText = await readFile(settingsPath(process.env, process.platform, os.homedir()), "utf8");
    } catch {
        // No settings yet: the app has never served MCP on this machine.
    }
    const { url, token } = endpointFrom(process.env, settingsText);
    const headers = { "content-type": "application/json" };
    if (token) headers.authorization = `Bearer ${token}`;

    await fetch(url, { method: "POST", headers, body: JSON.stringify(report), signal: AbortSignal.timeout(TIMEOUT_MS) });
}

async function main() {
    const event = JSON.parse(await readStdin());
    if (!event.session_id) return;
    if (event.hook_event_name === "Stop" || event.hook_event_name === "StopFailure") await finish(event);
    else if (event.hook_event_name === "SessionStart" || event.hook_event_name === "UserPromptSubmit") await start(event);
}

main()
    .catch(() => {})
    .finally(() => process.exit(0));
