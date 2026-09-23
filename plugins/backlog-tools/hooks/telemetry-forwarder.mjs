#!/usr/bin/env node
// Forwards one Claude Code hook event to the Backlog desktop app, which attributes it to the
// delivery run the session is driving (Backlog's DeliveryRunTelemetry). The dashboards collect
// the same figures from hooks rather than tools; this is the forwarding half, and the app does
// the counting — it reads the transcript the event names, so nothing here does.
//
// Endpoint: POST http://127.0.0.1:<port>/telemetry, beside the app's /mcp, with the same bearer
// token. Port and token come from BACKLOG_MCP_PORT / BACKLOG_MCP_TOKEN — the variables the
// repository's .mcp.json registration reads — else from the app's own settings.json.
//
// Must never fail or slow the tool call it reports on: every error exits 0, an app that is not
// running refuses the connection at once, and a hung one is abandoned after TIMEOUT_MS.

import { readFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";

const TIMEOUT_MS = 2000;
const DEFAULT_PORT = 5757;

function readStdin() {
    return new Promise((resolve) => {
        let data = "";
        process.stdin.setEncoding("utf8");
        process.stdin.on("data", (chunk) => (data += chunk));
        process.stdin.on("end", () => resolve(data));
        // A hook invoked with no stdin must not hang the tool call.
        setTimeout(() => resolve(data), TIMEOUT_MS).unref();
    });
}

// Where the app keeps settings.json: .NET's LocalApplicationData, then the Backlog folder.
function settingsPath() {
    const home = os.homedir();
    const localData =
        process.platform === "win32"
            ? process.env.LOCALAPPDATA || path.join(home, "AppData", "Local")
            : process.platform === "darwin"
              ? path.join(home, "Library", "Application Support")
              : process.env.XDG_DATA_HOME || path.join(home, ".local", "share");
    return path.join(localData, "Backlog", "settings.json");
}

async function endpoint() {
    let port = Number(process.env.BACKLOG_MCP_PORT) || null;
    let token = process.env.BACKLOG_MCP_TOKEN || null;
    if (!port || !token) {
        try {
            const mcp = JSON.parse(await readFile(settingsPath(), "utf8")).mcpServer || {};
            port ||= Number(mcp.port) || null;
            token ||= mcp.token || null;
        } catch {
            // No settings yet: the app has never served MCP on this machine.
        }
    }
    return { url: `http://127.0.0.1:${port || DEFAULT_PORT}/telemetry`, token };
}

async function main() {
    const raw = await readStdin();
    const event = JSON.parse(raw);
    // The tool's output is the bulk of a PostToolUse payload and nothing the app records reads it.
    delete event.tool_response;

    const { url, token } = await endpoint();
    const headers = { "content-type": "application/json" };
    if (token) headers.authorization = `Bearer ${token}`;

    await fetch(url, {
        method: "POST",
        headers,
        body: JSON.stringify(event),
        signal: AbortSignal.timeout(TIMEOUT_MS),
    });
}

main()
    .catch(() => {})
    .finally(() => process.exit(0));
