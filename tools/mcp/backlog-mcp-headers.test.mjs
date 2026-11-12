// Tests for backlog-mcp-headers.mjs, run with `node --test "tools/mcp/*.test.mjs"`.

import { test } from "node:test";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import net from "node:net";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import { deliveryGrant, parseArgs, pipeName, pipePath, requestToken, resolveToken, settingsPath } from "./backlog-mcp-headers.mjs";

const SCRIPT = join(dirname(fileURLToPath(import.meta.url)), "backlog-mcp-headers.mjs");
const settings = (json) => () => JSON.stringify(json);

test("only BACKLOG_MCP_TOKEN is a static token: the escape hatch", () => {
    assert.equal(resolveToken({ BACKLOG_MCP_TOKEN: "from-env" }), "from-env");
    assert.equal(resolveToken({}), null);
});

test("settings.json answers the delivery grant's id, never a token (local ADR 0052, Phase 1)", () => {
    assert.equal(deliveryGrant(settings({ mcpServer: { grants: { delivery: "g_d", questionHook: "g_q", telemetry: "g_t" } } })), "g_d");
    // A file from before the upgrade: the retired token is not read.
    assert.equal(deliveryGrant(settings({ mcpServer: { token: "retired" } })), null);
    assert.equal(deliveryGrant(() => { throw new Error("ENOENT"); }), null);
    assert.equal(deliveryGrant(() => "not json"), null);
});

test("settings.json sits under LocalApplicationData/Backlog, or Backlog.Debug for --debug", () => {
    assert.equal(settingsPath({ LOCALAPPDATA: "C:\\L" }, "win32", "C:\\H"), join("C:\\L", "Backlog", "settings.json"));
    assert.equal(settingsPath({ LOCALAPPDATA: "C:\\L" }, "win32", "C:\\H", { debug: true }), join("C:\\L", "Backlog.Debug", "settings.json"));
    assert.equal(settingsPath({}, "darwin", "/h"), join("/h", "Library", "Application Support", "Backlog", "settings.json"));
    assert.equal(settingsPath({}, "linux", "/h"), join("/h", ".local", "share", "Backlog", "settings.json"));
});

async function run(env, settingsJson, args = []) {
    const root = await mkdtemp(join(tmpdir(), "backlog-mcp-headers-"));
    try {
        const base = { PATH: process.env.PATH, LOCALAPPDATA: root, XDG_DATA_HOME: root, HOME: root, USERPROFILE: root };
        if (settingsJson) {
            const file = settingsPath(base, process.platform, root);
            await mkdir(dirname(file), { recursive: true });
            await writeFile(file, JSON.stringify(settingsJson));
        }
        return await runAsync(args, { ...base, ...env });
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

test("BACKLOG_MCP_TOKEN prints as the Authorization header headersHelper expects", async () => {
    const result = await run({ BACKLOG_MCP_TOKEN: "abc" }, null);
    assert.equal(result.status, 0);
    assert.deepEqual(JSON.parse(result.stdout), { Authorization: "Bearer abc" });
});

test("the retired mcpServer.token in settings.json is never printed", async () => {
    const result = await run({}, { mcpServer: { token: "retired" } });
    assert.equal(result.status, 1);
    assert.equal(result.stdout, "");
    assert.match(result.stderr, /No Backlog MCP grant/);
});

test("no grant recorded exits 1 with the reason, and prints no header", async () => {
    const result = await run({}, null);
    assert.equal(result.status, 1);
    assert.equal(result.stdout, "");
    assert.match(result.stderr, /No Backlog MCP grant/);
});

// --grant (local ADR 0052 §3): the token comes from the desktop app's broker pipe.

test("no flags is the recorded delivery grant, then the escape hatch", () => {
    assert.deepEqual(parseArgs([]), { grant: null, debug: false });
});

test("--grant takes the id as the next argument or after =", () => {
    assert.deepEqual(parseArgs(["--grant", "g_abc"]), { grant: "g_abc", debug: false });
    assert.deepEqual(parseArgs(["--grant=g_abc", "--debug"]), { grant: "g_abc", debug: true });
});

test("--grant without an id, and an unknown flag, are errors", () => {
    assert.throws(() => parseArgs(["--grant"]), /needs a grant id/);
    assert.throws(() => parseArgs(["--grant", "--debug"]), /needs a grant id/);
    assert.throws(() => parseArgs(["--grant="]), /needs a grant id/);
    assert.throws(() => parseArgs(["--grnat", "g_abc"]), /Unknown argument --grnat/);
});

test("the pipe is the installation's and the user's, as McpTokenBroker.PipeNameFor names it", () => {
    assert.equal(pipeName({}, { username: "jobsc" }), "backlog-mcp-broker-jobsc");
    assert.equal(pipeName({}, { debug: true, username: "jobsc" }), "backlog-debug-mcp-broker-jobsc");
    assert.equal(pipeName({}, { username: "Jo Bs" }), "backlog-mcp-broker-jo_bs");
    assert.equal(pipeName({}, { username: "jöb\\sc" }), "backlog-mcp-broker-j_b_sc");
    assert.equal(pipePath("backlog-mcp-broker-jobsc"), String.raw`\\.\pipe\backlog-mcp-broker-jobsc`);
});

test("BACKLOG_MCP_BROKER_PIPE replaces the whole name", () => {
    assert.equal(pipeName({ BACKLOG_MCP_BROKER_PIPE: "custom" }, { debug: true, username: "jobsc" }), "custom");
});

// A pipe on Windows, a socket file elsewhere: what net.connect reaches either way.
function fakeAddress() {
    const name = `backlog-mcp-headers-test-${process.pid}-${Math.random().toString(36).slice(2)}`;
    return { name, address: process.platform === "win32" ? pipePath(name) : join(tmpdir(), `${name}.sock`) };
}

// A fake broker: records the request line and answers with `reply` (a string, sent as is).
async function fakeBroker(reply) {
    const { name, address } = fakeAddress();
    const requests = [];
    const server = net.createServer((socket) => {
        let received = "";
        socket.setEncoding("utf8");
        socket.on("data", (chunk) => {
            received += chunk;
            if (!received.includes("\n")) return;
            requests.push(received);
            if (reply !== null) socket.end(reply);
            else socket.destroy();
        });
    });
    await new Promise((resolve) => server.listen(address, resolve));
    return { name, address, requests, close: () => new Promise((resolve) => server.close(resolve)) };
}

test("requestToken sends one JSON line and resolves to the token in the reply", async () => {
    const broker = await fakeBroker(JSON.stringify({ token: "a.b.c", expiresAt: "2026-11-11T09:45:00Z" }) + "\n");
    try {
        assert.equal(await requestToken("g_abc", () => net.connect(broker.address)), "a.b.c");
        assert.deepEqual(broker.requests, ['{"grant":"g_abc"}\n']);
    } finally {
        await broker.close();
    }
});

test("requestToken rejects with the broker's refusal", async () => {
    const broker = await fakeBroker(JSON.stringify({ error: "revoked" }) + "\n");
    try {
        await assert.rejects(requestToken("g_abc", () => net.connect(broker.address)), /refused grant g_abc: revoked/);
    } finally {
        await broker.close();
    }
});

test("requestToken rejects a reply that is not JSON, and a connection closed without one", async () => {
    const garbled = await fakeBroker("not json\n");
    try {
        await assert.rejects(requestToken("g_abc", () => net.connect(garbled.address)), /not JSON/);
    } finally {
        await garbled.close();
    }

    const silent = await fakeBroker(null);
    try {
        await assert.rejects(requestToken("g_abc", () => net.connect(silent.address)), /without a reply/);
    } finally {
        await silent.close();
    }
});

test("requestToken says the app is not running when there is no pipe", async () => {
    const { address } = fakeAddress();
    await assert.rejects(requestToken("g_abc", () => net.connect(address)), /not running: start the Backlog desktop app/);
});

function runAsync(args, env) {
    return new Promise((resolve) => {
        const child = spawn(process.execPath, [SCRIPT, ...args], { env: { PATH: process.env.PATH, ...env } });
        let stdout = "";
        let stderr = "";
        child.stdout.setEncoding("utf8").on("data", (chunk) => (stdout += chunk));
        child.stderr.setEncoding("utf8").on("data", (chunk) => (stderr += chunk));
        child.on("close", (status) => resolve({ status, stdout, stderr }));
    });
}

test("--grant prints the broker's token as the header, without reading settings.json or BACKLOG_MCP_TOKEN", { skip: process.platform !== "win32" && "the broker is a Windows named pipe" }, async () => {
    const broker = await fakeBroker(JSON.stringify({ token: "a.b.c", expiresAt: "2026-11-11T09:45:00Z" }) + "\n");
    try {
        const result = await runAsync(["--grant", "g_abc"], { BACKLOG_MCP_BROKER_PIPE: broker.name, BACKLOG_MCP_TOKEN: "shared" });
        assert.equal(result.status, 0, result.stderr);
        assert.deepEqual(JSON.parse(result.stdout), { Authorization: "Bearer a.b.c" });
    } finally {
        await broker.close();
    }
});

test("no flags asks the broker for the delivery grant settings.json records", { skip: process.platform !== "win32" && "the broker is a Windows named pipe" }, async () => {
    const broker = await fakeBroker(JSON.stringify({ token: "d.e.f", expiresAt: "2026-11-11T09:45:00Z" }) + "\n");
    try {
        const result = await run(
            // A token copied into the environment by hand before the upgrade does not win.
            { BACKLOG_MCP_BROKER_PIPE: broker.name, BACKLOG_MCP_TOKEN: "retired" },
            { mcpServer: { token: "retired", grants: { delivery: "g_d", questionHook: "g_q", telemetry: "g_t" } } });
        assert.equal(result.status, 0, result.stderr);
        assert.deepEqual(JSON.parse(result.stdout), { Authorization: "Bearer d.e.f" });
        assert.deepEqual(broker.requests, ['{"grant":"g_d"}\n']);
    } finally {
        await broker.close();
    }
});

test("--grant exits 1 with the refusal on stderr and prints no header", { skip: process.platform !== "win32" && "the broker is a Windows named pipe" }, async () => {
    const broker = await fakeBroker(JSON.stringify({ error: "unknown-grant" }) + "\n");
    try {
        const result = await runAsync(["--grant", "g_abc"], { BACKLOG_MCP_BROKER_PIPE: broker.name });
        assert.equal(result.status, 1);
        assert.equal(result.stdout, "");
        assert.match(result.stderr, /refused grant g_abc: unknown-grant/);
    } finally {
        await broker.close();
    }
});

test("--grant with no broker exits 1 with the reason and prints no header", async () => {
    const result = await runAsync(["--grant", "g_abc"], { BACKLOG_MCP_BROKER_PIPE: fakeAddress().name, BACKLOG_MCP_TOKEN: "shared" });
    assert.equal(result.status, 1);
    assert.equal(result.stdout, "");
    assert.match(result.stderr, /token broker/);
});

test("an unknown flag exits 1 and prints no header", async () => {
    const result = await runAsync(["--grnat", "g_abc"], { BACKLOG_MCP_TOKEN: "shared" });
    assert.equal(result.status, 1);
    assert.equal(result.stdout, "");
    assert.match(result.stderr, /Unknown argument/);
});
