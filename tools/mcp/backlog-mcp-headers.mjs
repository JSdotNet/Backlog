// headersHelper for the `backlog` server in .mcp.json: prints the Authorization header Claude
// Code sends to the MCP endpoint inside the running desktop app.
//
// The token comes from BACKLOG_MCP_TOKEN, else from the app's own settings.json — the order
// plugins/backlog-tools/hooks/telemetry-forwarder.mjs reads it in. A static
// `Bearer ${BACKLOG_MCP_TOKEN}` header only works on a machine that copied the token into its
// environment by hand, and every other session connects with an empty one and gets a 401.
// Claude Code runs this at connect and again after a 401 or 403, so a token the app re-mints
// is picked up without restarting the session.
//
// No token yet (the app has never served MCP here) exits 1 with the reason on stderr rather
// than printing an empty header, so the failure says what to do instead of reading as a 401.

import { readFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

// Where the app keeps settings.json: .NET's LocalApplicationData, then the Backlog folder.
export function settingsPath(env = process.env, platform = process.platform, home = os.homedir()) {
    const localData =
        platform === "win32"
            ? env.LOCALAPPDATA || path.join(home, "AppData", "Local")
            : platform === "darwin"
              ? path.join(home, "Library", "Application Support")
              : env.XDG_DATA_HOME || path.join(home, ".local", "share");
    return path.join(localData, "Backlog", "settings.json");
}

export function resolveToken(env = process.env, readSettings = () => readFileSync(settingsPath(env), "utf8")) {
    if (env.BACKLOG_MCP_TOKEN) return env.BACKLOG_MCP_TOKEN;
    try {
        return JSON.parse(readSettings()).mcpServer?.token || null;
    } catch {
        return null;
    }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const token = resolveToken();
    if (!token) {
        process.stderr.write(
            `No Backlog MCP token: set BACKLOG_MCP_TOKEN, or switch on the MCP server in the Backlog app so it writes one to ${settingsPath()}.\n`,
        );
        process.exit(1);
    }
    process.stdout.write(JSON.stringify({ Authorization: `Bearer ${token}` }));
}
