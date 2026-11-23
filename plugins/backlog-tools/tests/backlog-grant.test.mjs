// How the plugin's scripts reach the app under a grant (hooks/backlog-grant.mjs), and the
// telemetry forwarder run for real against a stand-in /telemetry route. They spawn processes,
// which `claude plugin test`'s sandbox cannot, so they run under
//   node --test plugins/backlog-tools/tests/backlog-grant.test.mjs

import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import { createServer } from "node:http";
import net from "node:net";
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

// Runs the forwarder on one event against a stand-in /telemetry answering `status`, and answers
// what reached it and the forwarder's exit code.
async function forward(settingsFor, { env = {}, status = 202, helper } = {}) {
    const received = [];
    const server = createServer((req, res) => {
        let body = "";
        req.on("data", (chunk) => (body += chunk));
        req.on("end", () => {
            received.push({ url: req.url, authorization: req.headers.authorization, body: JSON.parse(body) });
            res.statusCode = status;
            res.end();
        });
    });
    await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
    const { port } = server.address();
    const app = await fakeBacklogApp({ settings: settingsFor(port), helper });
    try {
        const child = spawn(process.execPath, [FORWARDER], { env: { ...app.env, ...env } });
        child.stdin.end(JSON.stringify({ hook_event_name: "PostToolUse", session_id: "s1", tool_name: "Bash", tool_response: "bulk" }));
        const code = await new Promise((resolve) => child.on("exit", resolve));
        return { received, code };
    } finally {
        server.close();
        await app.cleanup();
    }
}

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
