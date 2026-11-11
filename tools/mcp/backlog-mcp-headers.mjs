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
//
// --grant <id> (local ADR 0052 §3) asks the app's token broker for a 15-minute token for that
// grant instead, and never reads settings.json or BACKLOG_MCP_TOKEN. The broker is a named
// pipe only this Windows user can open: \\.\pipe\backlog-mcp-broker-<user> for the installed
// app, \\.\pipe\backlog-debug-mcp-broker-<user> for a Debug build (pass --debug), <user> being
// the Windows user name lower-cased with anything outside [a-z0-9._-] as "_" — the name
// McpTokenBroker.PipeNameFor gives. BACKLOG_MCP_BROKER_PIPE replaces the whole name. One JSON
// line goes out, {"grant":"<id>"}, and one comes back, {"token":…,"expiresAt":…} or
// {"error":…}. No pipe, or a refusal, exits 1 with the reason on stderr.

import { readFileSync } from "node:fs";
import net from "node:net";
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

// The flags: --grant <id> or --grant=<id>, and --debug. Anything else is an error, so a typo
// never falls back to the shared token silently.
export function parseArgs(argv) {
    const options = { grant: null, debug: false };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === "--debug") options.debug = true;
        else if (arg === "--grant") {
            const value = argv[++i];
            if (!value || value.startsWith("--")) throw new Error("--grant needs a grant id, as in --grant g_9xq1…");
            options.grant = value;
        } else if (arg.startsWith("--grant=")) {
            options.grant = arg.slice("--grant=".length);
            if (!options.grant) throw new Error("--grant needs a grant id, as in --grant=g_9xq1…");
        } else throw new Error(`Unknown argument ${arg}. The flags are --grant <id> and --debug.`);
    }
    return options;
}

// The broker's pipe name, without the \\.\pipe\ prefix.
export function pipeName(env = process.env, { debug = false, username = os.userInfo().username } = {}) {
    if (env.BACKLOG_MCP_BROKER_PIPE) return env.BACKLOG_MCP_BROKER_PIPE;
    const user = username.toLowerCase().replace(/[^a-z0-9._-]/g, "_");
    return `${debug ? "backlog-debug" : "backlog"}-mcp-broker-${user}`;
}

export function pipePath(name) {
    return `\\\\.\\pipe\\${name}`;
}

// Asks the broker for a token for `grant` over the socket `connect` opens, and resolves to it.
// Rejects with the reason on no pipe, a refusal, a reply that is not one, or no reply in time.
export function requestToken(grant, connect, { timeoutMs = 5000 } = {}) {
    return new Promise((resolve, reject) => {
        const socket = connect();
        let received = "";
        let settled = false;
        const finish = (error, token) => {
            if (settled) return;
            settled = true;
            clearTimeout(timer);
            socket.destroy();
            if (error) reject(error);
            else resolve(token);
        };
        const timer = setTimeout(() => finish(new Error("The Backlog token broker did not answer in time.")), timeoutMs);

        socket.setEncoding("utf8");
        socket.on("connect", () => socket.write(JSON.stringify({ grant }) + "\n"));
        socket.on("data", (chunk) => {
            received += chunk;
            const newline = received.indexOf("\n");
            if (newline < 0) return;
            let reply;
            try {
                reply = JSON.parse(received.slice(0, newline));
            } catch {
                return finish(new Error("The Backlog token broker answered with something that is not JSON."));
            }
            if (typeof reply?.token === "string" && reply.token) return finish(null, reply.token);
            finish(new Error(`The Backlog token broker refused grant ${grant}: ${reply?.error ?? "no token in its reply"}.`));
        });
        socket.on("error", (error) =>
            finish(
                error.code === "ENOENT"
                    ? new Error("The Backlog token broker is not running: start the Backlog desktop app.")
                    : new Error(`Could not reach the Backlog token broker: ${error.message}`),
            ),
        );
        socket.on("close", () => finish(new Error("The Backlog token broker closed the connection without a reply.")));
    });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    let options;
    try {
        options = parseArgs(process.argv.slice(2));
    } catch (error) {
        process.stderr.write(`${error.message}\n`);
        process.exit(1);
    }

    if (options.grant) {
        const name = pipeName(process.env, { debug: options.debug });
        try {
            const token = await requestToken(options.grant, () => net.connect(pipePath(name)));
            process.stdout.write(JSON.stringify({ Authorization: `Bearer ${token}` }));
        } catch (error) {
            process.stderr.write(`${error.message} (pipe ${pipePath(name)})\n`);
            process.exit(1);
        }
    } else {
        const token = resolveToken();
        if (!token) {
            process.stderr.write(
                `No Backlog MCP token: set BACKLOG_MCP_TOKEN, or switch on the MCP server in the Backlog app so it writes one to ${settingsPath()}.\n`,
            );
            process.exit(1);
        }
        process.stdout.write(JSON.stringify({ Authorization: `Bearer ${token}` }));
    }
}
