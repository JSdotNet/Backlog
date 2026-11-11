// The schedule-run report hooks (local ADR 0051 §1): what they decide, and the hook process run
// for real against a stand-in for the app's /schedule-runs route. They spawn processes, which
// `claude plugin test`'s sandbox cannot, so they run under
//   node --test plugins/backlog-tools/tests/schedule-run-report.test.mjs

import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import fs from "node:fs";
import { createServer } from "node:http";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import {
    endpointFrom,
    finishReport,
    firstPrompt,
    lastAssistantText,
    needsYouRows,
    promptedAfterStart,
    scheduledTaskName,
    startContext,
    verdictLine,
} from "../hooks/schedule-run-logic.mjs";

const HOOKS = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "hooks");
const SCRIPT = path.join(HOOKS, "schedule-run-report.mjs");

const scheduledPrompt = (name) =>
    `<scheduled-task name="${name}" file="C:\\Users\\me\\.claude\\scheduled-tasks\\${name}\\SKILL.md">\nThis is an automated run of a scheduled task.\n</scheduled-task>`;

const report = [
    "## Merge review · JSdotNet/Backlog · 2026-11-11",
    "",
    "**Two ready, one blocked; merge #12 first.**",
    "",
    "### Needs you",
    "| What | Where | Do |",
    "| --- | --- | --- |",
    "| Ready to merge | [#12](https://x/12) | [review comment](https://x/c1) |",
    "| Blocked: red checks | [#13](https://x/13) | — |",
    "",
    "### Reviewed",
    "| Pull request | Author | Door | Verdict | Blocking | Comment |",
    "| --- | --- | --- | --- | --- | --- |",
    "| [#12](https://x/12) | me | two-way | ready | — | [c](https://x/c1) |",
    "",
    "### Run",
    "| Outcome | What | Why |",
    "| --- | --- | --- |",
    "| skipped | #9 | reviewed at head |",
].join("\n");

// ── What the hooks decide ───────────────────────────────────────────────────────────────────

test("a prompt opening with <scheduled-task names its task; one that only mentions it does not", () => {
    assert.equal(scheduledTaskName(scheduledPrompt("update-local")), "update-local");
    assert.equal(scheduledTaskName(`\n  ${scheduledPrompt("merge-review")}`), "merge-review");
    assert.equal(scheduledTaskName("SessionStart: when the first prompt opens with `<scheduled-task`, …"), null);
    assert.equal(scheduledTaskName("<scheduled-task file=\"x\">no name</scheduled-task>"), null);
    assert.equal(scheduledTaskName(undefined), null);
});

test("the first prompt is the queued one Claude Desktop writes before SessionStart, else the first user message", () => {
    assert.equal(
        firstPrompt([
            { type: "queue-operation", operation: "enqueue", content: scheduledPrompt("a") },
            { type: "queue-operation", operation: "dequeue" },
            { type: "user", message: { content: "later" } },
        ]),
        scheduledPrompt("a"),
    );
    assert.equal(
        firstPrompt([
            { type: "attachment", attachment: {} },
            { type: "user", isMeta: true, message: { content: "<local-command-caveat>" } },
            { type: "user", message: { content: [{ type: "text", text: scheduledPrompt("b") }] } },
        ]),
        scheduledPrompt("b"),
    );
    assert.equal(firstPrompt([{ type: "attachment" }]), null);
});

test("the last assistant message joins the text of the entries that share its id and skips tool calls", () => {
    const entries = [
        { type: "assistant", message: { id: "m1", content: [{ type: "text", text: "working" }] } },
        { type: "assistant", message: { id: "m2", content: [{ type: "tool_use", name: "Bash" }] } },
        { type: "user", message: { content: [{ type: "tool_result", content: "ok" }] } },
        { type: "assistant", message: { id: "m3", content: [{ type: "thinking", thinking: "…" }] } },
        { type: "assistant", message: { id: "m3", content: [{ type: "text", text: "## Report" }] } },
        { type: "assistant", message: { id: "m3", content: [{ type: "text", text: "**Done.**" }] } },
        { type: "assistant", isSidechain: true, message: { id: "s1", content: [{ type: "text", text: "sub-agent" }] } },
    ];
    assert.equal(lastAssistantText(entries), "## Report\n**Done.**");
    assert.equal(lastAssistantText([]), null);
});

test("a person's reply after the scheduled prompt is told from tool results and host notices", () => {
    const start = { type: "user", message: { content: scheduledPrompt("a") } };
    const toolResult = { type: "user", message: { content: [{ type: "tool_result", content: "ok" }] } };
    const notice = { type: "user", message: { content: "<task-notification>done</task-notification>" } };
    const meta = { type: "user", isMeta: true, message: { content: "Caveat" } };
    assert.equal(promptedAfterStart([start, toolResult, notice, meta]), false);
    assert.equal(promptedAfterStart([start, toolResult, { type: "user", message: { content: [{ type: "text", text: "merge #12" }] } }]), true);
});

test("the start context names the schedule id, the session id, the scheduler tool and report_schedule_run", () => {
    const text = startContext("merge-review", "sess-1");
    assert.match(text, /`merge-review`/);
    assert.match(text, /sessionId `sess-1`/);
    assert.match(text, /list_scheduled_tasks/);
    assert.match(text, /report_schedule_run/);
    assert.match(text, /event `started`/);
    assert.match(text, /cron/);
    assert.match(text, /repository/);
    assert.match(text, /[Bb]efore any other work/);
});

test("the verdict line is the bold sentence, else the first line of prose", () => {
    assert.equal(verdictLine(report), "Two ready, one blocked; merge #12 first.");
    assert.equal(verdictLine("## Heading\n\n| a |\n\nAuth failed for all three repos.\nMore."), "Auth failed for all three repos.");
    // A bold label is no verdict: a report outside the frame reads its first line of prose.
    assert.equal(verdictLine("## Nightly update\n\n| a | b |\n\n**Notes:**\n- Auth was switched first."), "Auth was switched first.");
    assert.equal(verdictLine("**Notes:**\n\n**All three repos are up to date.**"), "All three repos are up to date.");
    assert.equal(verdictLine("**Two ready.** Merge #12 first."), "Two ready.");
    assert.equal(verdictLine("Merge **#12** first."), "Merge #12 first.");
    assert.equal(verdictLine(""), null);
});

test("the Needs-you rows are the table body under every Needs-you heading, and its bullets", () => {
    assert.deepEqual(needsYouRows(report), [
        "Ready to merge · [#12](https://x/12) · [review comment](https://x/c1)",
        "Blocked: red checks · [#13](https://x/13)",
    ]);
    const sweep = [
        "### Needs you: validate the drafts",
        "| Draft | Issue |",
        "| --- | --- |",
        "| [#5](u) | [#4](u) |",
        "### Needs you: answer the proposals",
        "- Pick a storage option for #7",
        "### Run",
        "| skipped | x | y |",
    ].join("\n");
    assert.deepEqual(needsYouRows(sweep), ["[#5](u) · [#4](u)", "Pick a storage option for #7"]);
    assert.deepEqual(needsYouRows("## T\n\n**Nothing found.**\n\n### Run\n| a | b |"), []);
    // A bundle nests each job's report a level down; its top section already gathers their rows.
    const bundle = [
        "### Needs you",
        "| What | Where | Do |",
        "| --- | --- | --- |",
        "| Red checks | [#3](u) | — |",
        "### Digest",
        "#### Needs you",
        "| What | Where | Do |",
        "| --- | --- | --- |",
        "| Red checks | [#3](u) | — |",
    ].join("\n");
    assert.deepEqual(needsYouRows(bundle), ["Red checks · [#3](u)"]);
});

test("a final message opening with Skipped: is a skip; anything else is a finish with verdict and rows", () => {
    assert.deepEqual(finishReport("s", "id", "Skipped: the run started 2026-11-10T02:00 is not archived yet\n"), {
        scheduleId: "s",
        sessionId: "id",
        event: "skipped",
        verdict: "Skipped: the run started 2026-11-10T02:00 is not archived yet",
    });
    assert.deepEqual(finishReport("s", "id", report), {
        scheduleId: "s",
        sessionId: "id",
        event: "finished",
        verdict: "Two ready, one blocked; merge #12 first.",
        needsYou: [
            "Ready to merge · [#12](https://x/12) · [review comment](https://x/c1)",
            "Blocked: red checks · [#13](https://x/13)",
        ],
    });
    // A finish with no Needs-you rows still sends the empty list, so an earlier report's rows clear.
    assert.deepEqual(finishReport("s", "id", null), { scheduleId: "s", sessionId: "id", event: "finished", verdict: null, needsYou: [] });
});

test("the endpoint is the environment's, then settings.json's, then the default port, on /schedule-runs", () => {
    assert.deepEqual(endpointFrom({ BACKLOG_MCP_PORT: "6000", BACKLOG_MCP_TOKEN: "t" }, null), { url: "http://127.0.0.1:6000/schedule-runs", token: "t" });
    assert.deepEqual(endpointFrom({}, JSON.stringify({ mcpServer: { port: 6001, token: "s" } })), { url: "http://127.0.0.1:6001/schedule-runs", token: "s" });
    assert.deepEqual(endpointFrom({}, "{half"), { url: "http://127.0.0.1:5757/schedule-runs", token: null });
});

test("hooks.json runs the report script on SessionStart, UserPromptSubmit and Stop", () => {
    const hooks = JSON.parse(fs.readFileSync(path.join(HOOKS, "hooks.json"), "utf8")).hooks;
    const commands = (event) => hooks[event].flatMap((group) => group.hooks.map((hook) => hook.command));
    for (const event of ["SessionStart", "UserPromptSubmit", "Stop"]) {
        assert.ok(
            commands(event).some((command) => command.includes("hooks/schedule-run-report.mjs")),
            `${event} does not run schedule-run-report.mjs`,
        );
    }
    // Every other session's prompt costs no process: the shell lets only a scheduled one through.
    assert.ok(commands("UserPromptSubmit").some((command) => command.includes("*'<scheduled-task'*)")));
});

// ── The hook process, run for real ──────────────────────────────────────────────────────────

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "schedule-run-report-test-"));

function transcript(entries) {
    const file = path.join(tmp, `${randomUUID()}.jsonl`);
    fs.writeFileSync(file, entries.map((entry) => JSON.stringify(entry)).join("\n") + "\n");
    return file;
}

function runHook(payload, env = {}) {
    return new Promise((resolve) => {
        const started = Date.now();
        // A LOCALAPPDATA with no Backlog settings, so the machine's own app is never reached.
        const child = spawn(process.execPath, [SCRIPT], {
            env: { ...process.env, LOCALAPPDATA: tmp, XDG_DATA_HOME: tmp, TEMP: tmp, TMP: tmp, TMPDIR: tmp, BACKLOG_MCP_PORT: "", BACKLOG_MCP_TOKEN: "", ...env },
        });
        let stdout = "";
        let stderr = "";
        child.stdout.on("data", (chunk) => (stdout += chunk));
        child.stderr.on("data", (chunk) => (stderr += chunk));
        child.on("close", (code) => resolve({ code, stdout, stderr, ms: Date.now() - started }));
        child.stdin.end(JSON.stringify(payload));
    });
}

async function standIn(status = 200) {
    const received = [];
    const server = createServer((req, res) => {
        let body = "";
        req.on("data", (chunk) => (body += chunk));
        req.on("end", () => {
            received.push({ method: req.method, url: req.url, authorization: req.headers.authorization, body: JSON.parse(body) });
            res.statusCode = status;
            res.end("{}");
        });
    });
    await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
    return { received, port: server.address().port, close: () => new Promise((resolve) => server.close(resolve)) };
}

test("SessionStart adds the start context once for a scheduled session, and nothing for another", async () => {
    const sessionId = randomUUID();
    const file = transcript([
        { type: "queue-operation", operation: "enqueue", content: scheduledPrompt("merge-review") },
        { type: "queue-operation", operation: "dequeue" },
    ]);
    const payload = { hook_event_name: "SessionStart", source: "startup", session_id: sessionId, transcript_path: file };

    const first = await runHook(payload);
    assert.equal(first.code, 0);
    const output = JSON.parse(first.stdout);
    assert.equal(output.hookSpecificOutput.hookEventName, "SessionStart");
    assert.equal(output.hookSpecificOutput.additionalContext, startContext("merge-review", sessionId));

    // Once per session: a second start for it — or the UserPromptSubmit fallback — adds nothing.
    assert.equal((await runHook(payload)).stdout, "");
    assert.equal((await runHook({ hook_event_name: "UserPromptSubmit", session_id: sessionId, prompt: scheduledPrompt("merge-review") })).stdout, "");

    const plain = transcript([{ type: "queue-operation", operation: "enqueue", content: "Fix the build. Mention `<scheduled-task` here." }]);
    const other = await runHook({ hook_event_name: "SessionStart", source: "startup", session_id: randomUUID(), transcript_path: plain });
    assert.equal(other.code, 0);
    assert.equal(other.stdout, "");

    // A resumed or compacted session already reported its start.
    const resumed = await runHook({ hook_event_name: "SessionStart", source: "resume", session_id: randomUUID(), transcript_path: file });
    assert.equal(resumed.stdout, "");
});

test("UserPromptSubmit adds the start context when the transcript had no prompt yet", async () => {
    const sessionId = randomUUID();
    const missing = path.join(tmp, "not-written-yet.jsonl");
    assert.equal((await runHook({ hook_event_name: "SessionStart", source: "startup", session_id: sessionId, transcript_path: missing })).stdout, "");

    const run = await runHook({ hook_event_name: "UserPromptSubmit", session_id: sessionId, prompt: scheduledPrompt("tech-update") });
    assert.equal(run.code, 0);
    const output = JSON.parse(run.stdout);
    assert.equal(output.hookSpecificOutput.hookEventName, "UserPromptSubmit");
    assert.match(output.hookSpecificOutput.additionalContext, /`tech-update`/);
});

test("Stop posts the finish with the verdict line and the Needs-you rows, behind the token", async () => {
    const app = await standIn();
    try {
        const sessionId = randomUUID();
        const file = transcript([{ type: "user", message: { content: scheduledPrompt("merge-review") } }]);
        const run = await runHook(
            { hook_event_name: "Stop", session_id: sessionId, transcript_path: file, last_assistant_message: report },
            { BACKLOG_MCP_PORT: String(app.port), BACKLOG_MCP_TOKEN: "t0k" },
        );
        assert.equal(run.code, 0);
        assert.equal(run.stdout, "");
        assert.deepEqual(app.received, [
            {
                method: "POST",
                url: "/schedule-runs",
                authorization: "Bearer t0k",
                body: finishReport("merge-review", sessionId, report),
            },
        ]);
        assert.equal(app.received[0].body.event, "finished");
    } finally {
        await app.close();
    }
});

test("Stop reads the final message from the transcript when the payload has none, and posts a skip", async () => {
    const app = await standIn();
    try {
        const sessionId = randomUUID();
        const file = transcript([
            { type: "queue-operation", operation: "enqueue", content: scheduledPrompt("weekly-retro") },
            { type: "user", message: { content: scheduledPrompt("weekly-retro") } },
            { type: "assistant", message: { id: "m1", content: [{ type: "tool_use", name: "mcp__scheduled-tasks__list_task_runs" }] } },
            { type: "assistant", message: { id: "m2", content: [{ type: "text", text: "Skipped: the run started 2026-11-04T07:00 is not archived yet" }] } },
        ]);
        await runHook({ hook_event_name: "Stop", session_id: sessionId, transcript_path: file }, { BACKLOG_MCP_PORT: String(app.port), BACKLOG_MCP_TOKEN: "t" });
        assert.equal(app.received.length, 1);
        assert.deepEqual(app.received[0].body, {
            scheduleId: "weekly-retro",
            sessionId,
            event: "skipped",
            verdict: "Skipped: the run started 2026-11-04T07:00 is not archived yet",
        });
    } finally {
        await app.close();
    }
});

test("Stop posts nothing once a person has replied in the run's session", async () => {
    const app = await standIn();
    try {
        const file = transcript([
            { type: "user", message: { content: scheduledPrompt("merge-review") } },
            { type: "assistant", message: { id: "m1", content: [{ type: "text", text: report }] } },
            { type: "user", message: { content: "Merge #12 for me." } },
            { type: "assistant", message: { id: "m2", content: [{ type: "text", text: "Merged #12." }] } },
        ]);
        const run = await runHook(
            { hook_event_name: "Stop", session_id: randomUUID(), transcript_path: file, last_assistant_message: "Merged #12." },
            { BACKLOG_MCP_PORT: String(app.port), BACKLOG_MCP_TOKEN: "t" },
        );
        assert.equal(run.code, 0);
        assert.deepEqual(app.received, []);
    } finally {
        await app.close();
    }
});

test("Stop posts nothing for a session no schedule started", async () => {
    const app = await standIn();
    try {
        const file = transcript([{ type: "user", message: { content: "Add a hook that reports `<scheduled-task` runs." } }]);
        const run = await runHook(
            { hook_event_name: "Stop", session_id: randomUUID(), transcript_path: file, last_assistant_message: "**Done.**" },
            { BACKLOG_MCP_PORT: String(app.port), BACKLOG_MCP_TOKEN: "t" },
        );
        assert.equal(run.code, 0);
        assert.deepEqual(app.received, []);
    } finally {
        await app.close();
    }
});

test("Stop exits quietly when Backlog is closed, refuses the report, or the input is not JSON", async () => {
    // A port nothing listens on: bind one, then free it.
    const probe = createServer();
    await new Promise((resolve) => probe.listen(0, "127.0.0.1", resolve));
    const closedPort = probe.address().port;
    await new Promise((resolve) => probe.close(resolve));

    const file = transcript([{ type: "user", message: { content: scheduledPrompt("merge-review") } }]);
    const payload = { hook_event_name: "Stop", session_id: randomUUID(), transcript_path: file, last_assistant_message: report };

    const closed = await runHook(payload, { BACKLOG_MCP_PORT: String(closedPort), BACKLOG_MCP_TOKEN: "t" });
    assert.deepEqual({ code: closed.code, stdout: closed.stdout, stderr: closed.stderr }, { code: 0, stdout: "", stderr: "" });
    assert.ok(closed.ms < 4000, `took ${closed.ms} ms`);

    for (const status of [404, 503]) {
        const app = await standIn(status);
        try {
            const refused = await runHook(payload, { BACKLOG_MCP_PORT: String(app.port), BACKLOG_MCP_TOKEN: "t" });
            assert.deepEqual({ code: refused.code, stdout: refused.stdout, stderr: refused.stderr }, { code: 0, stdout: "", stderr: "" });
            assert.equal(app.received.length, 1);
        } finally {
            await app.close();
        }
    }

    const garbage = await new Promise((resolve) => {
        const child = spawn(process.execPath, [SCRIPT]);
        let out = "";
        child.stdout.on("data", (chunk) => (out += chunk));
        child.stderr.on("data", (chunk) => (out += chunk));
        child.on("close", (code) => resolve({ code, out }));
        child.stdin.end("not json");
    });
    assert.deepEqual(garbage, { code: 0, out: "" });
});

test.after(() => fs.rmSync(tmp, { recursive: true, force: true }));
