// How the plugin's scripts reach the app under a grant (hooks/backlog-grant.mjs), and the
// telemetry forwarder run for real against a stand-in /telemetry route. They spawn processes,
// which `claude plugin test`'s sandbox cannot, so they run under
//   node --test plugins/backlog-tools/tests/backlog-grant.test.mjs

import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, readdir, readFile, rm, utimes, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import { authorization, bearerFrom, endpointFrom, helperPath, settingsPath } from "../hooks/backlog-grant.mjs";
import { fakeBacklogApp } from "./fake-backlog-app.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const FORWARDER = path.join(HERE, "..", "hooks", "telemetry-forwarder.mjs");
const REAL_HELPER = path.join(HERE, "..", "..", "..", "tools", "mcp", "backlog-mcp-headers.mjs");

const grants = { delivery: "g_delivery", questionHook: "g_hook", telemetry: "g_telemetry", manager: "g_manager" };

test("settings.json answers the named grant's id, and the escape hatch only where it records none", () => {
    const settings = JSON.stringify({ mcpServer: { port: 6123, grants } });
    assert.deepEqual(endpointFrom({}, settings, "telemetry", "/telemetry"), { url: "http://127.0.0.1:6123/telemetry", grant: "g_telemetry", token: null });
    assert.deepEqual(endpointFrom({ BACKLOG_MCP_TOKEN: "env" }, settings, "manager", "/mcp"), { url: "http://127.0.0.1:6123/mcp", grant: "g_manager", token: null });
    assert.equal(endpointFrom({ BACKLOG_MCP_PORT: "7001" }, settings, "manager", "/mcp").url, "http://127.0.0.1:7001/mcp");

    // A file from before the upgrade, or one from before the Manager grant: the retired token is never read.
    const retired = JSON.stringify({ mcpServer: { port: 6123, token: "retired" } });
    assert.deepEqual(endpointFrom({}, retired, "telemetry", "/telemetry"), { url: "http://127.0.0.1:6123/telemetry", grant: null, token: null });
    assert.equal(endpointFrom({ BACKLOG_MCP_TOKEN: "env" }, retired, "telemetry", "/telemetry").token, "env");
    assert.equal(endpointFrom({}, JSON.stringify({ mcpServer: { grants: { delivery: "g_d" } } }), "manager", "/mcp").grant, null);

    assert.deepEqual(endpointFrom({}, null, "telemetry", "/telemetry"), { url: "http://127.0.0.1:5757/telemetry", grant: null, token: null });
    assert.deepEqual(endpointFrom({}, "{ half", "telemetry", "/telemetry"), { url: "http://127.0.0.1:5757/telemetry", grant: null, token: null });
});

test("settings.json and the installed headers helper sit in the app's data folder", () => {
    assert.equal(settingsPath({ LOCALAPPDATA: "C:\\Users\\a\\AppData\\Local" }, "win32", "C:\\Users\\a"), "C:\\Users\\a\\AppData\\Local\\Backlog\\settings.json");
    assert.equal(settingsPath({}, "win32", "C:\\Users\\a"), "C:\\Users\\a\\AppData\\Local\\Backlog\\settings.json");
    assert.equal(settingsPath({}, "darwin", "/Users/a"), "/Users/a/Library/Application Support/Backlog/settings.json");
    assert.equal(settingsPath({}, "linux", "/home/a"), "/home/a/.local/share/Backlog/settings.json");
    assert.equal(helperPath({ LOCALAPPDATA: "C:\\L" }, "win32", "C:\\H"), "C:\\L\\Backlog\\hooks\\backlog-mcp-headers.mjs");
});

test("the helper's output yields its bearer, and nothing else does", () => {
    assert.equal(bearerFrom('{"Authorization":"Bearer abc"}'), "Bearer abc");
    assert.equal(bearerFrom('{"Authorization":"Bearer "}'), null);
    assert.equal(bearerFrom('{"Authorization":"Basic abc"}'), null);
    assert.equal(bearerFrom(""), null);
});

test("a recorded grant's token comes from the installed headers helper, run as --grant <id>", async () => {
    const app = await fakeBacklogApp();
    try {
        assert.deepEqual(await authorization({ grant: "g_telemetry", token: null }, "Telemetry", { env: app.env }), { header: "Bearer minted-g_telemetry" });
        const refused = await authorization({ grant: "g_refused", token: null }, "Telemetry", { env: app.env });
        assert.match(refused.error, /refused grant g_refused: revoked/);
        assert.deepEqual(await authorization({ grant: null, token: "env" }, "Telemetry", { env: app.env }), { header: "Bearer env" });
        assert.match((await authorization({ grant: null, token: null }, "Telemetry", { env: app.env })).error, /No Telemetry grant/);
    } finally {
        await app.cleanup();
    }
});

test("no installed headers helper is said, not run", async () => {
    const app = await fakeBacklogApp({ helper: false });
    try {
        assert.match((await authorization({ grant: "g_telemetry", token: null }, "Telemetry", { env: app.env })).error, /No headers helper at/);
    } finally {
        await app.cleanup();
    }
});

// Answers once `check` holds, or false after `ms`.
async function waitFor(check, ms = 15000) {
    for (const until = Date.now() + ms; Date.now() < until; ) {
        if (await check()) return true;
        await new Promise((resolve) => setTimeout(resolve, 50));
    }
    return false;
}

// Runs the forwarder hook on one event against a stand-in /telemetry answering `status` after
// `delayMs`, and answers what reached it, the hook's exit code and how long the hook took. The
// hook hands the post to a detached sender, so this waits for the sender to remove its spooled
// file before reading what arrived.
async function forward(
    settingsFor,
    {
        env = {},
        status = 202,
        delayMs = 0,
        helper,
        events = [{ hook_event_name: "PostToolUse", session_id: "s1", tool_name: "Bash", tool_response: "bulk" }],
    } = {},
) {
    const received = [];
    const server = createServer((req, res) => {
        let body = "";
        req.on("data", (chunk) => (body += chunk));
        req.on("end", () => {
            const entry = { url: req.url, authorization: req.headers.authorization, body: JSON.parse(body), answered: null };
            received.push(entry);
            // Whether the answer reached a caller still waiting for it, or one that had given up.
            res.on("close", () => (entry.answered = res.writableFinished));
            setTimeout(() => {
                res.statusCode = status;
                res.end();
            }, delayMs);
        });
    });
    await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
    const { port } = server.address();
    const app = await fakeBacklogApp({ settings: settingsFor(port), helper });
    const spool = await mkdtemp(path.join(os.tmpdir(), "backlog-telemetry-spool-"));
    try {
        let code = 0;
        let hookMs = 0;
        for (const event of events) {
            const started = Date.now();
            const child = spawn(process.execPath, [FORWARDER], { env: { ...app.env, ...env, BACKLOG_TELEMETRY_SPOOL: spool } });
            child.stdin.end(JSON.stringify(event));
            code ||= await new Promise((resolve) => child.on("exit", resolve));
            hookMs = Math.max(hookMs, Date.now() - started);
        }
        const drained = await waitFor(
            async () => (await readdir(spool)).length === 0 && received.every((entry) => entry.answered !== null),
        );
        return { received, code, hookMs, drained };
    } finally {
        server.closeAllConnections();
        server.close();
        await app.cleanup();
        await rm(spool, { recursive: true, force: true });
    }
}

test("the hook returns before the post is answered, and the detached sender still delivers it", async () => {
    // The app answers after three seconds: the hook must be gone by then, and the post still answered.
    const { received, code, hookMs, drained } = await forward((port) => ({ mcpServer: { port, grants } }), { delayMs: 3000 });
    assert.equal(code, 0);
    assert.ok(hookMs < 3000, `the hook took ${hookMs} ms, waiting on a post it should hand off`);
    assert.ok(drained, "the sender left its spooled event behind");
    assert.equal(received.length, 1);
    assert.equal(received[0].answered, true, "the post was abandoned before the app answered it");
});

test("a PreToolUse posts nothing; its PostToolUse carries the start and the hook's own moment", async () => {
    const call = { session_id: "s1", tool_name: "Edit", tool_use_id: "toolu_01AbC" };
    const { received, code, drained } = await forward((port) => ({ mcpServer: { port, grants } }), {
        events: [
            { ...call, hook_event_name: "PreToolUse" },
            { ...call, hook_event_name: "PostToolUse" },
        ],
    });
    assert.equal(code, 0);
    assert.ok(drained, "the start file or a spooled event was left behind");
    assert.equal(received.length, 1);
    const { backlog_started_at: startedAt, backlog_at: at } = received[0].body;
    assert.ok(Date.parse(startedAt) <= Date.parse(at), `${startedAt} should not be after ${at}`);
});

test("SessionEnd sweeps this forwarder's files a day old, and nothing else", async () => {
    const spool = await mkdtemp(path.join(os.tmpdir(), "backlog-telemetry-spool-"));
    try {
        const old = new Date(Date.now() - 25 * 60 * 60 * 1000);
        for (const name of ["backlog-telemetry-start-toolu_stale", "backlog-telemetry-lost.json", "someone-elses.json"]) {
            await writeFile(path.join(spool, name), "x");
            await utimes(path.join(spool, name), old, old);
        }
        await writeFile(path.join(spool, "backlog-telemetry-start-toolu_fresh"), "x");
        const child = spawn(process.execPath, [FORWARDER], { env: { ...process.env, BACKLOG_TELEMETRY_SPOOL: spool, LOCALAPPDATA: spool, BACKLOG_MCP_TOKEN: "", BACKLOG_MCP_PORT: "9" } });
        child.stdin.end(JSON.stringify({ hook_event_name: "SessionEnd", session_id: "s1" }));
        assert.equal(await new Promise((resolve) => child.on("exit", resolve)), 0);
        assert.ok(await waitFor(async () => !(await readdir(spool)).some((name) => name.endsWith(".json") && name.startsWith("backlog-"))));
        assert.deepEqual((await readdir(spool)).sort(), ["backlog-telemetry-start-toolu_fresh", "someone-elses.json"]);
    } finally {
        await rm(spool, { recursive: true, force: true });
    }
});

test("a hook with no event spools nothing and exits 0", async () => {
    const spool = await mkdtemp(path.join(os.tmpdir(), "backlog-telemetry-spool-"));
    try {
        const child = spawn(process.execPath, [FORWARDER], { env: { ...process.env, BACKLOG_TELEMETRY_SPOOL: spool } });
        child.stdin.end("not json");
        assert.equal(await new Promise((resolve) => child.on("exit", resolve)), 0);
        assert.deepEqual(await readdir(spool), []);
    } finally {
        await rm(spool, { recursive: true, force: true });
    }
});

test("the forwarder posts each event under the Telemetry grant's token, without the tool's output", async () => {
    const { received, code } = await forward((port) => ({ mcpServer: { port, grants } }), { env: { BACKLOG_MCP_TOKEN: "not-sent" } });
    assert.equal(code, 0);
    assert.equal(received.length, 1);
    assert.equal(received[0].url, "/telemetry");
    assert.equal(received[0].authorization, "Bearer minted-g_telemetry");
    assert.equal(received[0].body.tool_response, undefined);
    assert.equal(received[0].body.tool_name, "Bash");
});

test("a 401 from the route costs the session nothing: the forwarder exits 0", async () => {
    const { received, code } = await forward((port) => ({ mcpServer: { port, grants } }), { status: 401 });
    assert.equal(code, 0);
    assert.equal(received.length, 1);
});

test("a refused grant posts nothing and exits 0", async () => {
    const { received, code } = await forward((port) => ({ mcpServer: { port, grants: { ...grants, telemetry: "g_refused" } } }));
    assert.equal(code, 0);
    assert.equal(received.length, 0);
});

test("the retired mcpServer.token is never sent; BACKLOG_MCP_TOKEN is, where no grant is recorded", async () => {
    const retired = (port) => ({ mcpServer: { port, token: "retired" } });
    assert.equal((await forward(retired)).received.length, 0);
    const { received } = await forward(retired, { env: { BACKLOG_MCP_TOKEN: "escape" } });
    assert.equal(received[0].authorization, "Bearer escape");
});

test(
    "the forwarder takes its token from the real headers helper and a broker pipe",
    { skip: process.platform !== "win32" && "the broker is a Windows named pipe" },
    async () => {
        const pipe = `backlog-tools-test-broker-${process.pid}`;
        const asked = [];
        const broker = net.createServer((socket) => {
            socket.setEncoding("utf8");
            socket.on("data", (line) => {
                const { grant } = JSON.parse(line);
                asked.push(grant);
                socket.end(JSON.stringify({ token: `broker-${grant}`, expiresAt: "2099-01-01T00:00:00Z" }) + "\n");
            });
        });
        await new Promise((resolve) => broker.listen(`\\\\.\\pipe\\${pipe}`, resolve));
        try {
            const { received, code } = await forward((port) => ({ mcpServer: { port, grants } }), {
                env: { BACKLOG_MCP_BROKER_PIPE: pipe },
                helper: await readFile(REAL_HELPER, "utf8"),
            });
            assert.equal(code, 0);
            assert.deepEqual(asked, ["g_telemetry"]);
            assert.equal(received[0].authorization, "Bearer broker-g_telemetry");
        } finally {
            broker.close();
        }
    },
);
