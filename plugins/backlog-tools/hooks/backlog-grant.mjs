// How the plugin's scripts reach the Backlog desktop app under a grant of their own (local ADR
// 0052, Phase 1): the telemetry forwarder under the "Telemetry" grant, dispatch-sessions under
// the "Manager" grant. Shared by hooks/telemetry-forwarder.mjs and the dispatch-sessions assets.
//
// The app records each grant's id in its settings.json under mcpServer.grants (`telemetry`,
// `manager`, …) — an id is a label, not a password — and holds no token there. A token is asked
// for through the headers helper the app installs beside its question hook,
// <app data>\hooks\backlog-mcp-headers.mjs, run as `--grant <id>`: it asks the app's token
// broker over this Windows user's named pipe and prints {"Authorization":"Bearer …"} for a
// 15-minute token. Nothing here keeps a token past the call it was asked for.
//
// BACKLOG_MCP_TOKEN, the dev or CI escape hatch, is sent only when settings.json records no
// such grant, as the headers helper and the question hook do. BACKLOG_MCP_PORT overrides the
// port, else mcpServer.port, else 5757.

import { execFile } from "node:child_process";
import { existsSync } from "node:fs";
import { readFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";

export const DEFAULT_PORT = 5757;

// How long the headers helper may take: it gives the broker five seconds itself.
const HELPER_TIMEOUT_MS = 8000;

// .NET's LocalApplicationData, then the Backlog folder: the installed app's data folder.
export function appDataFolder(env = process.env, platform = process.platform, home = os.homedir()) {
    const join = platform === "win32" ? path.win32.join : path.posix.join;
    const localData =
        platform === "win32"
            ? env.LOCALAPPDATA || join(home, "AppData", "Local")
            : platform === "darwin"
              ? join(home, "Library", "Application Support")
              : env.XDG_DATA_HOME || join(home, ".local", "share");
    return join(localData, "Backlog");
}

export function settingsPath(env = process.env, platform = process.platform, home = os.homedir()) {
    return (platform === "win32" ? path.win32 : path.posix).join(appDataFolder(env, platform, home), "settings.json");
}

// Where the app writes the headers helper on every start (McpHeadersHelperScript.PathUnder).
export function helperPath(env = process.env, platform = process.platform, home = os.homedir()) {
    return (platform === "win32" ? path.win32 : path.posix).join(appDataFolder(env, platform, home), "hooks", "backlog-mcp-headers.mjs");
}

// The URL for `route`, the id of the grant settings.json records under mcpServer.grants[key],
// and — only when it records none — the escape hatch's token. settingsText is null when the
// file could not be read; one that does not parse reads as none. mcpServer.token is never read.
export function endpointFrom(env, settingsText, key, route) {
    let mcp = {};
    if (settingsText) {
        try {
            mcp = JSON.parse(settingsText)?.mcpServer || {};
        } catch {
            // A half-written settings.json reads as none.
        }
    }
    const port = Number(env.BACKLOG_MCP_PORT) || Number(mcp.port) || DEFAULT_PORT;
    const recorded = mcp.grants?.[key];
    const grant = typeof recorded === "string" && recorded ? recorded : null;
    return { url: `http://127.0.0.1:${port}${route}`, grant, token: grant ? null : env.BACKLOG_MCP_TOKEN || null };
}

// The Authorization value in what the headers helper printed, or null.
export function bearerFrom(stdout) {
    try {
        const value = JSON.parse(stdout)?.Authorization;
        return typeof value === "string" && value.startsWith("Bearer ") && value.length > "Bearer ".length ? value : null;
    } catch {
        return null;
    }
}

// endpointFrom over the settings.json on disk.
export async function readEndpoint(key, route, env = process.env) {
    let text = null;
    try {
        text = await readFile(settingsPath(env), "utf8");
    } catch {
        // No settings yet: the app has never started on this machine.
    }
    return endpointFrom(env, text, key, route);
}

// The Authorization value for an endpoint readEndpoint answered: { header } or { error }, the
// error a sentence saying why there is none. `label` names the grant in that sentence; a helper
// that has not answered within timeoutMs is abandoned.
export function authorization({ grant, token }, label, { env = process.env, timeoutMs = HELPER_TIMEOUT_MS } = {}) {
    if (!grant) {
        return Promise.resolve(
            token
                ? { header: `Bearer ${token}` }
                : { error: `No ${label} grant: start the Backlog desktop app once so it records one in ${settingsPath(env)}.` },
        );
    }
    const helper = helperPath(env);
    if (!existsSync(helper)) {
        return Promise.resolve({ error: `No headers helper at ${helper}: start the Backlog desktop app once so it installs it.` });
    }
    return new Promise((resolve) => {
        execFile(process.execPath, [helper, "--grant", grant], { env, timeout: timeoutMs, windowsHide: true }, (failure, stdout, stderr) => {
            const header = failure ? null : bearerFrom(stdout);
            if (header) return resolve({ header });
            const reason = String(stderr || "").trim().split(/\r?\n/)[0];
            resolve({ error: reason || `The headers helper printed no token for the ${label} grant ${grant}.` });
        });
    });
}
