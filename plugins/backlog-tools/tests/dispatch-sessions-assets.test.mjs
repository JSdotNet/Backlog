// The dispatch-sessions skill's two asset processes, run for real: the wait script against a
// stand-in for the app's /session-requests/wait route, and the PowerShell capacity probe.
// They spawn processes, which `claude plugin test`'s sandbox cannot, so they run under
//   node --test plugins/backlog-tools/tests/dispatch-sessions-assets.test.mjs

import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createServer } from "node:http";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import { dispatcherName } from "../skills/dispatch-sessions/assets/wait-logic.mjs";

const ASSETS = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "skills", "dispatch-sessions", "assets");
const request = (id) => ({ kind: "request", id, requestKind: "entry", repository: "JSdotNet/Backlog", brief: "/backlog-tools:run-plan-item entry `x`:" });
const delivery = (id) => ({ kind: "delivery", id, sessionId: "cli-1", desktopSessionId: "local_1", question: "Which database?", answer: "Postgres" });

test("the wait script sends the token and prints one line per delivery and per request", async () => {
    const answers = [delivery("d1"), delivery("d1"), request("r1"), null, request("r1"), request("r2")];
    const authorizations = [];
    const server = createServer((req, res) => {
        authorizations.push(req.headers.authorization);
        const next = answers.shift();
        if (next === undefined) res.statusCode = 404;
        else if (next === null) res.statusCode = 204;
        else {
            res.setHeader("content-type", "application/json");
            res.write(JSON.stringify(next));
        }
        res.end();
    });
    await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
    const { port } = server.address();

    const child = spawn(process.execPath, [path.join(ASSETS, "wait-requests.mjs"), "--retry", "50"], {
        env: { ...process.env, BACKLOG_MCP_PORT: String(port), BACKLOG_MCP_TOKEN: "t0k" },
    });
    const lines = [];
    try {
        await new Promise((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error(`timed out after ${JSON.stringify(lines)}`)), 10_000);
            let buffer = "";
            child.stdout.on("data", (chunk) => {
                buffer += chunk;
                const parts = buffer.split(/\r?\n/);
                buffer = parts.pop();
                lines.push(...parts);
                if (lines.includes("flag-off")) {
                    clearTimeout(timer);
                    resolve();
                }
            });
        });
    } finally {
        child.kill();
        server.close();
    }

    assert.deepEqual(lines, [
        `ready dispatcher=${dispatcherName(os.hostname())} url=http://127.0.0.1:${port}/session-requests/wait`,
        "delivery d1 local_1",
        "request r1 entry JSdotNet/Backlog",
        "request r2 entry JSdotNet/Backlog",
        "flag-off",
    ]);
    assert.ok(authorizations.every((header) => header === "Bearer t0k"));
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

    const child = spawn(process.execPath, [path.join(ASSETS, "wait-requests.mjs"), "--retry", "50"], {
        env: { ...process.env, BACKLOG_MCP_PORT: String(port), BACKLOG_MCP_TOKEN: "t0k" },
    });
    let out = "";
    child.stdout.on("data", (chunk) => (out += chunk));
    await new Promise((resolve) => setTimeout(resolve, 1_500));
    child.kill();

    assert.deepEqual(out.trim().split(/\r?\n/).slice(1), ["unreachable"]);
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
