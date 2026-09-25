// Tests for backlog-mcp-headers.mjs, run with `node --test "tools/mcp/*.test.mjs"`.

import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import { resolveToken, settingsPath } from "./backlog-mcp-headers.mjs";

const SCRIPT = join(dirname(fileURLToPath(import.meta.url)), "backlog-mcp-headers.mjs");
const settings = (json) => () => JSON.stringify(json);

test("the environment wins over settings.json", () => {
    assert.equal(resolveToken({ BACKLOG_MCP_TOKEN: "from-env" }, settings({ mcpServer: { token: "from-file" } })), "from-env");
});

test("settings.json answers when the environment does not", () => {
    assert.equal(resolveToken({}, settings({ mcpServer: { token: "from-file" } })), "from-file");
});

test("no token anywhere resolves to null", () => {
    assert.equal(resolveToken({}, settings({})), null);
    assert.equal(resolveToken({}, () => { throw new Error("ENOENT"); }), null);
    assert.equal(resolveToken({}, () => "not json"), null);
});

test("settings.json sits under LocalApplicationData/Backlog", () => {
    assert.equal(settingsPath({ LOCALAPPDATA: "C:\\L" }, "win32", "C:\\H"), join("C:\\L", "Backlog", "settings.json"));
    assert.equal(settingsPath({}, "darwin", "/h"), join("/h", "Library", "Application Support", "Backlog", "settings.json"));
    assert.equal(settingsPath({}, "linux", "/h"), join("/h", ".local", "share", "Backlog", "settings.json"));
});

async function run(env, settingsJson) {
    const root = await mkdtemp(join(tmpdir(), "backlog-mcp-headers-"));
    try {
        const base = { PATH: process.env.PATH, LOCALAPPDATA: root, XDG_DATA_HOME: root, HOME: root, USERPROFILE: root };
        if (settingsJson) {
            const file = settingsPath(base, process.platform, root);
            await mkdir(dirname(file), { recursive: true });
            await writeFile(file, JSON.stringify(settingsJson));
        }
        return spawnSync(process.execPath, [SCRIPT], { env: { ...base, ...env }, encoding: "utf8" });
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

test("prints the Authorization header as the JSON object headersHelper expects", async () => {
    const result = await run({}, { mcpServer: { token: "abc" } });
    assert.equal(result.status, 0);
    assert.deepEqual(JSON.parse(result.stdout), { Authorization: "Bearer abc" });
});

test("no token exits 1 with the reason, and prints no header", async () => {
    const result = await run({}, null);
    assert.equal(result.status, 1);
    assert.equal(result.stdout, "");
    assert.match(result.stderr, /No Backlog MCP token/);
});
