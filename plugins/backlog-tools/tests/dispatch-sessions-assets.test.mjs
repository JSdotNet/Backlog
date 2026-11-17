// The dispatch-sessions skill's asset processes, run for real: the wait script against a
// stand-in for the app's /session-requests/wait route, manager-call.mjs against a stand-in /mcp,
// and the PowerShell capacity probe. They spawn processes, which `claude plugin test`'s sandbox
// cannot, so they run under
//   node --test plugins/backlog-tools/tests/dispatch-sessions-assets.test.mjs
// Each runs in a fake app data folder (fake-backlog-app.mjs) whose settings.json records the
// Manager grant and whose headers helper mints "minted-<grant>".

import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createServer } from "node:http";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import { dispatcherName } from "../skills/dispatch-sessions/assets/wait-logic.mjs";
import { fakeBacklogApp } from "./fake-backlog-app.mjs";

const ASSETS = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "skills", "dispatch-sessions", "assets");
const request = (id) => ({ kind: "request", id, requestKind: "entry", repository: "JSdotNet/Backlog", brief: "/backlog-tools:run-plan-item entry `x`:" });
const delivery = (id) => ({ kind: "delivery", id, sessionId: "cli-1", desktopSessionId: "local_1", question: "Which database?", answer: "Postgres" });
const grants = { delivery: "g_delivery", questionHook: "g_hook", telemetry: "g_telemetry", manager: "g_manager" };

// A stand-in route answering `handle(req, body)` → { status, json }, recording each call.
async function stand(handle) {
    const calls = [];
    const server = createServer((req, res) => {
        let body = "";
        req.on("data", (chunk) => (body += chunk));
        req.on("end", () => {
            calls.push({ method: req.method, url: req.url, authorization: req.headers.authorization, body: body ? JSON.parse(body) : null });
            const { status = 200, json } = handle(req, body) ?? {};
            res.statusCode = status;
            if (json !== undefined) {
                res.setHeader("content-type", "application/json");
                res.write(JSON.stringify(json));
            }
            res.end();
        });
    });
    await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
    return { calls, port: server.address().port, close: () => server.close() };
}

// Runs the wait script until `until(lines)` holds, then `lingerMs` longer, and answers its lines.
async function waitLines(env, until, lingerMs = 0) {
    const child = spawn(process.execPath, [path.join(ASSETS, "wait-requests.mjs"), "--retry", "50"], { env });
    const lines = [];
    try {
        await new Promise((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error(`timed out after ${JSON.stringify(lines)}`)), 15_000);
            let buffer = "";
            child.stdout.on("data", (chunk) => {
                buffer += chunk;
                const parts = buffer.split(/\r?\n/);
                buffer = parts.pop();
                lines.push(...parts);
                if (until(lines)) {
                    clearTimeout(timer);
                    resolve();
                }
            });
        });
        await new Promise((resolve) => setTimeout(resolve, lingerMs));
    } finally {
        child.kill();
    }
    return lines;
}

test("the wait script sends the Manager grant's token and prints one line per delivery and per request", async () => {
    const answers = [delivery("d1"), delivery("d1"), request("r1"), null, request("r1"), request("r2")];
    const route = await stand(() => {
        const next = answers.shift();
        if (next === undefined) return { status: 404 };
        if (next === null) return { status: 204 };
        return { json: next };
    });
    const app = await fakeBacklogApp({ settings: { mcpServer: { port: route.port, grants } } });
    try {
        const lines = await waitLines({ ...app.env, BACKLOG_MCP_TOKEN: "not-sent" }, (seen) => seen.includes("flag-off"));
        assert.deepEqual(lines, [
            `ready dispatcher=${dispatcherName(os.hostname())} url=http://127.0.0.1:${route.port}/session-requests/wait`,
            "delivery d1 local_1",
            "request r1 entry JSdotNet/Backlog",
            "request r2 entry JSdotNet/Backlog",
            "flag-off",
        ]);
        assert.ok(route.calls.every((call) => call.authorization === "Bearer minted-g_manager"));
    } finally {
        route.close();
        await app.cleanup();
    }
});

test("the wait script says once that the route refused the Manager grant (401)", async () => {
    const route = await stand(() => ({ status: 401 }));
    const app = await fakeBacklogApp({ settings: { mcpServer: { port: route.port, grants } } });
    try {
        // The script keeps asking, each time under a fresh token, and says it once.
        const lines = await waitLines(app.env, (seen) => seen.includes("unauthorized"), 2_000);
        assert.deepEqual(lines.slice(1), ["unauthorized"]);
        assert.ok(route.calls.length >= 3, `only ${route.calls.length} calls`);
        assert.ok(route.calls.every((call) => call.authorization === "Bearer minted-g_manager"));
    } finally {
        route.close();
        await app.cleanup();
    }
});

test("the wait script says once why it has no Manager token, and calls nothing without one", async () => {
    const route = await stand(() => ({ status: 204 }));
    const app = await fakeBacklogApp({ settings: { mcpServer: { port: route.port, grants: { ...grants, manager: "g_refused" } } } });
    try {
        const lines = await waitLines(app.env, (seen) => seen.length >= 2);
        assert.match(lines[1], /^no-token The Backlog token broker refused grant g_refused: revoked\.$/);
        assert.equal(route.calls.length, 0);
    } finally {
        route.close();
        await app.cleanup();
    }
});

test("the wait script prints the dispatcher name alone with --name", () => {
    const run = spawnSync(process.execPath, [path.join(ASSETS, "wait-requests.mjs"), "--name"], { encoding: "utf8" });
    assert.equal(run.status, 0);
    assert.equal(run.stdout.trim(), dispatcherName(os.hostname()));
});

test("the wait script says once that the app is unreachable", async () => {
    // A port nothing listens on: bind one, then free it.
    const probe = createServer();
    await new Promise((resolve) => probe.listen(0, "127.0.0.1", resolve));
    const { port } = probe.address();
    await new Promise((resolve) => probe.close(resolve));

    const app = await fakeBacklogApp({ settings: { mcpServer: { port, grants } } });
    try {
        const child = spawn(process.execPath, [path.join(ASSETS, "wait-requests.mjs"), "--retry", "50"], { env: app.env });
        let out = "";
        child.stdout.on("data", (chunk) => (out += chunk));
        await new Promise((resolve) => setTimeout(resolve, 2_000));
        child.kill();
        assert.deepEqual(out.trim().split(/\r?\n/).slice(1), ["unreachable"]);
    } finally {
        await app.cleanup();
    }
});

function managerCall(env, ...args) {
    return new Promise((resolve) => {
        const child = spawn(process.execPath, [path.join(ASSETS, "manager-call.mjs"), ...args], { env });
        let stdout = "";
        let stderr = "";
        child.stdout.on("data", (chunk) => (stdout += chunk));
        child.stderr.on("data", (chunk) => (stderr += chunk));
        child.on("exit", (code) => resolve({ code, stdout, stderr }));
    });
}

test("manager-call sends one tools/call to /mcp under the Manager grant and prints the result", async () => {
    const route = await stand(() => ({
        json: { jsonrpc: "2.0", id: 1, result: { content: [{ type: "text", text: "{}" }], structuredContent: { claimed: true, id: "r1" } } },
    }));
    const app = await fakeBacklogApp({ settings: { mcpServer: { port: route.port, grants } } });
    try {
        const run = await managerCall(app.env, "claim_session_request", JSON.stringify({ id: "r1", dispatcher: "dispatch-sessions-pc" }));
        assert.equal(run.code, 0, run.stderr);
        assert.deepEqual(JSON.parse(run.stdout), { claimed: true, id: "r1" });
        assert.equal(route.calls.length, 1);
        assert.equal(route.calls[0].url, "/mcp");
        assert.equal(route.calls[0].authorization, "Bearer minted-g_manager");
        assert.deepEqual(route.calls[0].body.params, { name: "claim_session_request", arguments: { id: "r1", dispatcher: "dispatch-sessions-pc" } });
    } finally {
        route.close();
        await app.cleanup();
    }
});

test("manager-call reads a server-sent-event answer too", async () => {
    const sse = createServer((req, res) => {
        req.resume();
        req.on("end", () => {
            res.setHeader("content-type", "text/event-stream");
            res.end(`event: message\ndata: ${JSON.stringify({ jsonrpc: "2.0", id: 1, result: { content: [{ type: "text", text: '{"requests":[]}' }] } })}\n\n`);
        });
    });
    await new Promise((resolve) => sse.listen(0, "127.0.0.1", resolve));
    const app = await fakeBacklogApp({ settings: { mcpServer: { port: sse.address().port, grants } } });
    try {
        const run = await managerCall(app.env, "list_session_requests", '{"state":"requested"}');
        assert.equal(run.code, 0, run.stderr);
        assert.deepEqual(JSON.parse(run.stdout), { requests: [] });
    } finally {
        sse.close();
        await app.cleanup();
    }
});

test("manager-call exits 1 with the reason on a 401, a tool error, or no Manager token", async () => {
    const answers = [{ status: 401 }, { json: { jsonrpc: "2.0", id: 1, result: { isError: true, content: [{ type: "text", text: "unknown request r9" }] } } }];
    const route = await stand(() => answers.shift());
    const app = await fakeBacklogApp({ settings: { mcpServer: { port: route.port, grants } } });
    const refusedApp = await fakeBacklogApp({ settings: { mcpServer: { port: route.port, grants: { ...grants, manager: "g_refused" } } } });
    try {
        const unauthorized = await managerCall(app.env, "list_deliveries", "{}");
        assert.equal(unauthorized.code, 1);
        assert.equal(unauthorized.stdout, "");
        assert.match(unauthorized.stderr, /list_deliveries: Backlog refused the Manager grant \(401\)/);

        const toolError = await managerCall(app.env, "mark_session_request", '{"id":"r9"}');
        assert.equal(toolError.code, 1);
        assert.match(toolError.stderr, /mark_session_request: unknown request r9/);

        const noToken = await managerCall(refusedApp.env, "list_deliveries", "{}");
        assert.equal(noToken.code, 1);
        assert.match(noToken.stderr, /refused grant g_refused/);
        assert.equal(route.calls.length, 2);
    } finally {
        route.close();
        await app.cleanup();
        await refusedApp.cleanup();
    }
});

test("manager-call says how to call it, and refuses arguments that are not JSON", async () => {
    const app = await fakeBacklogApp({ settings: { mcpServer: { grants } } });
    try {
        assert.equal((await managerCall(app.env)).code, 1);
        const bad = await managerCall(app.env, "list_deliveries", "{ half");
        assert.equal(bad.code, 1);
        assert.match(bad.stderr, /not JSON/);
    } finally {
        await app.cleanup();
    }
});

const shell = process.platform === "win32" ? "powershell.exe" : "pwsh";
const probe = (...args) => {
    const run = spawnSync(shell, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path.join(ASSETS, "capacity.ps1"), "-Seconds", "1", ...args], {
        encoding: "utf8",
    });
    assert.equal(run.status, 0, run.stderr);
    return JSON.parse(run.stdout.trim());
};

test("the capacity probe reports CPU and free memory against the thresholds", { skip: process.platform !== "win32" && "CIM is Windows-only" }, () => {
    const roomy = probe("-MaxCpu", "100", "-MinFreeGb", "0");
    assert.equal(typeof roomy.cpuPercent, "number");
    assert.ok(roomy.freeGb > 0);
    assert.equal(roomy.fits, roomy.cpuPercent < 100);
    assert.deepEqual(roomy.heldBy, roomy.fits ? [] : ["cpu"]);

    const short = probe("-MaxCpu", "100", "-MinFreeGb", "1024");
    assert.equal(short.fits, false);
    assert.ok(short.heldBy.includes("memory"));
    assert.equal(short.minFreeGb, 1024);

    const busy = probe("-MaxCpu", "1", "-MinFreeGb", "0");
    assert.equal(busy.fits, busy.cpuPercent < 1);
});
