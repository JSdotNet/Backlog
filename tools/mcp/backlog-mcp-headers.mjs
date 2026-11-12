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
// {"error":…}. No pipe, or a refusal, exits 1 with the reason on stderr. A grant whose
// registration the person has not allowed yet is refused "pending", and gets no token.
//
// --register "<name>" --scopes a,b (local ADR 0052 §5) lets an agent register itself: it asks
// the broker for a pending grant, then writes the grant's id into the agent's own MCP config
// (--config <path>, else .mcp.json in the current folder) as the `backlog` server's
// headersHelper --grant. The grant holds no scope and gets no token until the person allows
// the request in Backlog, with Windows Hello. At most one request per name waits at a time and
// five in all, and a declined name cannot ask again for a day; the broker says which refused.
//
// --request opens the request window for the grant's waiting request, focusing it when it is
// open already: --grant <id>, or the grant the config names. It changes nothing. An agent that
// already has a grant and wants more asks with the request_access MCP tool instead.
//
// Neither flag grants anything: only the person answers a request, in the desktop app.

import { readFileSync, writeFileSync, existsSync } from "node:fs";
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

// The flags: --grant <id> or --grant=<id>, and --debug; and --register <name>, --scopes <list>,
// --request and --config <path> for an agent registering itself. Anything else is an error, so
// a typo never falls back to the shared token silently. The registration keys appear only when
// one of their flags is given.
export function parseArgs(argv) {
    const options = { grant: null, debug: false };
    const valueOf = (flag, i) => {
        const value = argv[i];
        if (!value || value.startsWith("--")) throw new Error(`${flag} needs a value.`);
        return value;
    };
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === "--debug") options.debug = true;
        else if (arg === "--register") options.register = valueOf("--register", ++i).trim();
        else if (arg.startsWith("--register=")) options.register = arg.slice("--register=".length).trim();
        else if (arg === "--scopes") options.scopes = splitScopes(valueOf("--scopes", ++i));
        else if (arg.startsWith("--scopes=")) options.scopes = splitScopes(arg.slice("--scopes=".length));
        else if (arg === "--config") options.config = valueOf("--config", ++i);
        else if (arg.startsWith("--config=")) options.config = arg.slice("--config=".length);
        else if (arg === "--request") options.request = true;
        else if (arg === "--grant") {
            const value = argv[++i];
            if (!value || value.startsWith("--")) throw new Error("--grant needs a grant id, as in --grant g_9xq1…");
            options.grant = value;
        } else if (arg.startsWith("--grant=")) {
            options.grant = arg.slice("--grant=".length);
            if (!options.grant) throw new Error("--grant needs a grant id, as in --grant=g_9xq1…");
        } else
            throw new Error(
                `Unknown argument ${arg}. The flags are --grant <id>, --debug, --register <name> --scopes <list>, --request and --config <path>.`,
            );
    }
    if ("register" in options) {
        if (!options.register) throw new Error("--register needs the agent's name, as in --register \"Claude Code · Backlog\".");
        if (!options.scopes?.length) throw new Error("--register needs --scopes, as in --scopes tasks:read,devbook:read.");
        if (options.grant) throw new Error("--register makes a new grant, so it takes no --grant.");
    } else if ("scopes" in options) throw new Error("--scopes goes with --register.");
    return options;
}

// "a, b c" → ["a", "b", "c"]: commas or spaces between scopes.
function splitScopes(value) {
    return value
        .split(/[\s,]+/)
        .map((scope) => scope.trim())
        .filter(Boolean);
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
            if (reply?.error === "pending")
                return finish(
                    new Error(
                        `The Backlog token broker refused grant ${grant}: pending. The person has not allowed it yet; run --request to bring its request window forward.`,
                    ),
                );
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

// Sends `payload` as one JSON line over the socket `connect` opens and resolves to the reply
// object, refusals included: the caller reads `error`. Rejects on no pipe, a reply that is not
// JSON, or none in time — as requestToken does.
export function exchange(payload, connect, { timeoutMs = 5000 } = {}) {
    return new Promise((resolve, reject) => {
        const socket = connect();
        let received = "";
        let settled = false;
        const finish = (error, reply) => {
            if (settled) return;
            settled = true;
            clearTimeout(timer);
            socket.destroy();
            if (error) reject(error);
            else resolve(reply);
        };
        const timer = setTimeout(() => finish(new Error("The Backlog token broker did not answer in time.")), timeoutMs);

        socket.setEncoding("utf8");
        socket.on("connect", () => socket.write(JSON.stringify(payload) + "\n"));
        socket.on("data", (chunk) => {
            received += chunk;
            const newline = received.indexOf("\n");
            if (newline < 0) return;
            try {
                finish(null, JSON.parse(received.slice(0, newline)));
            } catch {
                finish(new Error("The Backlog token broker answered with something that is not JSON."));
            }
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

// Registers an agent (local ADR 0052 §5): resolves to { grant, request, state: "pending" }, or
// rejects with the broker's refusal and its sentence.
export async function registerAgent(name, scopes, connect, { folder = null, parentPid = null, timeoutMs } = {}) {
    const reply = await exchange({ register: { name, scopes, folder, parentPid } }, connect, { timeoutMs });
    if (typeof reply?.grant === "string" && reply.grant) return reply;
    throw new Error(`The Backlog token broker refused to register ${name}: ${reply?.error ?? "no grant in its reply"}.${reply?.message ? ` ${reply.message}` : ""}`);
}

// Brings the request window forward for `grant`'s waiting request: resolves to { opened }, or
// rejects with the refusal.
export async function openRequest(grant, connect, { timeoutMs } = {}) {
    const reply = await exchange({ request: grant }, connect, { timeoutMs });
    if (typeof reply?.opened === "boolean") return reply;
    throw new Error(`The Backlog token broker did not open a request for ${grant}: ${reply?.error ?? "no answer"}.${reply?.message ? ` ${reply.message}` : ""}`);
}

const GRANT_FLAG = /\s--grant(?:=|\s+)\S+/g;

// The grant id the `backlog` server's headersHelper names in an MCP config, or null.
export function grantFromConfig(text) {
    try {
        const helper = JSON.parse(text)?.mcpServers?.backlog?.headersHelper;
        return typeof helper === "string" ? (/\s--grant(?:=|\s+)(\S+)/.exec(helper)?.[1] ?? null) : null;
    } catch {
        return null;
    }
}

// The MCP config `text` (null for none) with the `backlog` server's headersHelper naming
// `grant`: its own helper command kept, any --grant it had replaced. A config with no
// `backlog` server gets one on the loopback listener's default address.
export function withGrant(text, grant, { debug = false } = {}) {
    const config = text ? JSON.parse(text) : {};
    if (typeof config !== "object" || config === null || Array.isArray(config)) throw new Error("The MCP config is not a JSON object.");
    config.mcpServers ??= {};
    const server = (config.mcpServers.backlog ??= { type: "http", url: "http://127.0.0.1:${BACKLOG_MCP_PORT:-5757}/mcp" });
    const base = typeof server.headersHelper === "string" && server.headersHelper.trim() ? server.headersHelper.replace(GRANT_FLAG, "").trim() : "node tools/mcp/backlog-mcp-headers.mjs";
    const debugFlag = debug && !/\s--debug\b/.test(` ${base}`) ? " --debug" : "";
    server.headersHelper = `${base} --grant ${grant}${debugFlag}`;
    return JSON.stringify(config, null, 2) + "\n";
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    let options;
    try {
        options = parseArgs(process.argv.slice(2));
    } catch (error) {
        process.stderr.write(`${error.message}\n`);
        process.exit(1);
    }

    if (options.register || options.request) {
        const name = pipeName(process.env, { debug: options.debug });
        const connect = () => net.connect(pipePath(name));
        const config = path.resolve(options.config ?? ".mcp.json");
        try {
            if (options.register) {
                // The config is read and checked before the grant exists, so one that cannot take
                // the grant never leaves a pending grant nobody can name.
                const text = existsSync(config) ? readFileSync(config, "utf8") : null;
                try {
                    withGrant(text, "g_check", { debug: options.debug });
                } catch (error) {
                    throw new Error(`The MCP config ${config} cannot take a grant: ${error.message}`);
                }
                const reply = await registerAgent(options.register, options.scopes, connect, { folder: process.cwd(), parentPid: process.ppid });
                writeFileSync(config, withGrant(text, reply.grant, { debug: options.debug }));
                process.stdout.write(
                    `Registered "${options.register}" as grant ${reply.grant}: pending. It gets no token until the person allows it in Backlog. Wrote the grant into ${config}.\n`,
                );
            }
            if (options.request) {
                const grant = options.grant ?? (existsSync(config) ? grantFromConfig(readFileSync(config, "utf8")) : null);
                if (!grant) throw new Error(`--request needs --grant <id>, or an MCP config whose backlog server names one (${config}).`);
                const reply = await openRequest(grant, connect);
                process.stdout.write(reply.opened ? "The request window is open in Backlog.\n" : "The request waits on Backlog's MCP server page; no window could be opened.\n");
            }
        } catch (error) {
            process.stderr.write(`${error.message} (pipe ${pipePath(name)})\n`);
            process.exit(1);
        }
    } else if (options.grant) {
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
