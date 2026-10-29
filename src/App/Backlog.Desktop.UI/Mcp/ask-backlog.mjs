#!/usr/bin/env node
// Relays a Claude session's question to Backlog and its answer back (local ADR 0041): the hook
// script the desktop app ships, serves at GET /hooks/ask-backlog.mjs on its MCP listener and
// writes to <app data>\hooks\ask-backlog.mjs on every start, so the installed hook is always the
// running app's own.
//
// One script, five hook events, chosen by the first argument:
//
//   pre     PreToolUse / AskUserQuestion  ask (kind dialog). Route session: exit, the dialog opens.
//                                         Route backlog: hold on get_answer until answered, closed
//                                         or the dialog wait runs out; answered prints allow with
//                                         updatedInput {questions, answers}; out of time closes
//                                         the question as timed-out.
//   post    PostToolUse / AskUserQuestion close answered-in-session, with the dialog's answers.
//   stop    Stop                          the last assistant message, when it ends in a question:
//                                         ask (kind prose) with it as one free-text question.
//   prompt  UserPromptSubmit              close the session's open prose questions
//                                         answered-in-session, the prompt as the answer.
//   end     SessionEnd                    close every open question of the session session-ended.
//
// The MCP tools are called as JSON-RPC tools/call over HTTP on the app's /mcp, with the bearer
// token. Port and token come from BACKLOG_MCP_PORT / BACKLOG_MCP_TOKEN, else from the app's
// settings.json — the one beside this script's own folder first, then %LOCALAPPDATA%\Backlog's —
// the way telemetry-forwarder.mjs finds them.
//
// There is no tool that lists a session's questions, so the ids this script was given are kept
// in a small file per session under state\ beside it; prompt, post and end close from that.
//
// Must never hold a session up on Backlog: every error exits 0 with no output, so the dialog
// opens and the turn goes on.

import { mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const DEFAULT_PORT = 5757;
const STDIN_TIMEOUT_MS = 2000;
// One call that is not held: ask, close.
const CALL_TIMEOUT_MS = 5000;
// get_answer holds up to this long, under the server's 55-second cap.
const ANSWER_WAIT_SECONDS = 50;
// How long to wait before asking again while Backlog does not answer. Overridable only so the
// tests need not sit through it.
const RETRY_MS = Number(process.env.ASK_BACKLOG_RETRY_MS) || 15_000;

const here = path.dirname(fileURLToPath(import.meta.url));

function readStdin() {
    return new Promise((resolve) => {
        let data = "";
        process.stdin.setEncoding("utf8");
        process.stdin.on("data", (chunk) => (data += chunk));
        const done = () => {
            // Let go of stdin, so nothing but the calls still to come keeps the process alive.
            process.stdin.pause();
            resolve(data);
        };
        process.stdin.on("end", done);
        // A hook invoked with no stdin must not hang the session.
        setTimeout(done, STDIN_TIMEOUT_MS).unref();
    });
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// .NET's LocalApplicationData, then the Backlog folder: where the installed app keeps settings.json.
function installedSettingsPath() {
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
    // The app writes this script to <app data>\hooks, so its own settings.json is one folder up:
    // that is what makes a debug head's Backlog.Debug hook talk to the debug head.
    for (const settings of [path.join(here, "..", "settings.json"), installedSettingsPath()]) {
        if (port && token) break;
        try {
            const mcp = JSON.parse(await readFile(settings, "utf8")).mcpServer || {};
            port ||= Number(mcp.port) || null;
            token ||= mcp.token || null;
        } catch {
            // No settings there: try the next, or the defaults.
        }
    }
    return { url: `http://127.0.0.1:${port || DEFAULT_PORT}/mcp`, token };
}

// What callTool answers when Backlog did answer, and said no: a tool error, a JSON-RPC error, a
// 4xx. Waiting on it changes nothing, unlike Backlog not answering at all (null).
const REFUSED = Object.freeze({ refused: true });

// One tools/call. The tool's payload; REFUSED when Backlog refused it; null when Backlog did not
// answer - closed, restarting, timed out - or answered something that does not parse.
async function callTool(name, args, timeoutMs = CALL_TIMEOUT_MS) {
    try {
        const { url, token } = await endpoint();
        const headers = {
            "content-type": "application/json",
            accept: "application/json, text/event-stream",
        };
        if (token) headers.authorization = `Bearer ${token}`;

        const body = JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name, arguments: args } });
        const reply = await httpPost(url, headers, body, timeoutMs);
        if (!reply) return null;
        if (reply.status >= 400 && reply.status < 500) return REFUSED;
        if (reply.status < 200 || reply.status >= 300) return null;

        const message = parseRpc(reply.text);
        if (message?.error) return REFUSED;
        const result = message?.result;
        if (!result) return null;
        if (result.isError) return REFUSED;
        if (result.structuredContent && typeof result.structuredContent === "object") return result.structuredContent;

        const text = (result.content || []).find((part) => part?.type === "text")?.text;
        return text ? JSON.parse(text) : null;
    } catch {
        return null;
    }
}

// One POST, answering its status and body, or null when no whole reply came. node:http with no agent
// rather than fetch: fetch keeps its connection alive past the call, and a hook that exits
// while undici is still closing it aborts on Windows (a libuv assertion, exit 0xC0000409)
// instead of exiting 0.
function httpPost(url, headers, body, timeoutMs) {
    return new Promise((resolve) => {
        const request = http.request(
            url,
            { method: "POST", agent: false, headers: { ...headers, "content-length": Buffer.byteLength(body) } },
            (response) => {
                let text = "";
                response.setEncoding("utf8");
                response.on("data", (chunk) => (text += chunk));
                response.on("end", () => resolve({ status: response.statusCode, text }));
                response.on("error", () => resolve(null));
                // A reply cut off by the timeout below closes without ending; resolving twice
                // is harmless, so whichever comes first wins.
                response.on("close", () => resolve(null));
            },
        );
        request.setTimeout(timeoutMs, () => request.destroy());
        request.on("error", () => resolve(null));
        request.on("close", () => resolve(null));
        request.end(body);
    });
}

// The JSON-RPC response, whether the server answered plain JSON or one server-sent event.
function parseRpc(body) {
    const trimmed = body.trim();
    if (trimmed.startsWith("{")) return JSON.parse(trimmed);

    let found = null;
    for (const line of trimmed.split(/\r?\n/)) {
        if (!line.startsWith("data:")) continue;
        const message = JSON.parse(line.slice(5).trim());
        if (message && (message.result || message.error)) found = message;
    }
    return found;
}

// The ids this script was given for a session, so the hooks that come after can close them.
function stateFile(sessionId) {
    const folder = process.env.ASK_BACKLOG_STATE_DIR || path.join(here, "state");
    return path.join(folder, `${encodeURIComponent(sessionId)}.json`);
}

async function readState(sessionId) {
    try {
        const state = JSON.parse(await readFile(stateFile(sessionId), "utf8"));
        return Array.isArray(state.questions) ? state.questions : [];
    } catch {
        return [];
    }
}

async function writeState(sessionId, questions) {
    const file = stateFile(sessionId);
    if (questions.length === 0) {
        await rm(file, { force: true });
        return;
    }
    await mkdir(path.dirname(file), { recursive: true });
    // Through a temporary file, so a hook reading meanwhile never meets half a file.
    const temporary = `${file}.${process.pid}.tmp`;
    await writeFile(temporary, JSON.stringify({ questions }), "utf8");
    await rename(temporary, file);
}

async function remember(sessionId, entry) {
    const questions = (await readState(sessionId)).filter((held) => held.id !== entry.id);
    questions.push(entry);
    await writeState(sessionId, questions);
}

async function forget(sessionId, ids) {
    const gone = new Set(ids);
    await writeState(sessionId, (await readState(sessionId)).filter((held) => !gone.has(held.id)));
}

function print(output) {
    process.stdout.write(JSON.stringify(output));
}

// PreToolUse on AskUserQuestion.
async function pre(input) {
    const sessionId = input.session_id;
    const toolUseId = input.tool_use_id;
    const questions = input.tool_input?.questions;
    if (!sessionId || !toolUseId || !Array.isArray(questions)) return;

    const asked = await callTool("ask", { sessionId, toolUseId, questions, kind: "dialog" });
    if (!asked?.id || asked === REFUSED) return;

    await remember(sessionId, { id: asked.id, kind: "dialog", toolUseId });
    // At the keyboard: the dialog opens, and PostToolUse closes the question with what was picked.
    if (asked.route !== "backlog") return;

    const deadline = Date.now() + (Number(asked.waitSeconds) || 0) * 1000;
    while (Date.now() < deadline) {
        const remaining = Math.ceil((deadline - Date.now()) / 1000);
        const waitSeconds = Math.max(1, Math.min(ANSWER_WAIT_SECONDS, remaining));
        const answer = await callTool(
            "get_answer",
            { id: asked.id, sessionId, waitSeconds },
            (waitSeconds + 10) * 1000,
        );

        // Refused - the relay switched off, the question gone: waiting would only hold the
        // dialog shut, so it opens.
        if (answer === REFUSED) {
            await forget(sessionId, [asked.id]);
            return;
        }

        if (!answer) {
            // Backlog is closed or restarting: keep waiting for it, inside the same deadline.
            await sleep(Math.min(RETRY_MS, Math.max(0, deadline - Date.now())));
            continue;
        }

        if (answer.status === "answered") {
            await forget(sessionId, [asked.id]);
            print({
                hookSpecificOutput: {
                    hookEventName: "PreToolUse",
                    permissionDecision: "allow",
                    updatedInput: { questions, answers: answer.answers || {} },
                },
            });
            return;
        }

        // Closed elsewhere: nothing to wait for, so the dialog opens.
        if (answer.status !== "open") {
            await forget(sessionId, [asked.id]);
            return;
        }
    }

    await callTool("close", { id: asked.id, sessionId, reason: "timed-out" });
    await forget(sessionId, [asked.id]);
}

// The answers the dialog returned, keyed by question text, out of whatever shape the tool result
// arrives in.
function dialogAnswers(input) {
    let response = input.tool_response;
    if (typeof response === "string") {
        try {
            response = JSON.parse(response);
        } catch {
            response = null;
        }
    }
    const answers = response?.answers ?? input.tool_input?.answers;
    return answers && typeof answers === "object" ? answers : null;
}

// PostToolUse on AskUserQuestion.
async function post(input) {
    const sessionId = input.session_id;
    if (!sessionId) return;

    const held = (await readState(sessionId)).filter(
        (question) => question.kind === "dialog" && question.toolUseId === input.tool_use_id,
    );
    if (held.length === 0) return;

    const answers = dialogAnswers(input) || {};
    for (const question of held) {
        await callTool("close", { id: question.id, sessionId, reason: "answered-in-session", answers });
    }
    await forget(sessionId, held.map((question) => question.id));
}

// The text of the last assistant message in a Claude Code transcript (JSON Lines).
async function lastAssistantText(transcriptPath) {
    const lines = (await readFile(transcriptPath, "utf8")).split(/\r?\n/);
    for (let i = lines.length - 1; i >= 0; i--) {
        if (!lines[i].trim()) continue;
        let entry;
        try {
            entry = JSON.parse(lines[i]);
        } catch {
            continue;
        }
        if (entry.type !== "assistant") continue;

        const content = entry.message?.content;
        const text =
            typeof content === "string"
                ? content
                : (content || [])
                      .filter((part) => part?.type === "text" && typeof part.text === "string")
                      .map((part) => part.text)
                      .join("\n");
        // A tool-use-only turn has no text: the message that ended the turn is further up.
        if (text.trim()) return text.trim();
    }
    return null;
}

// Whether the last sentence of a message asks something: it ends in a question mark once
// trailing emphasis, quotes and closing brackets are set aside.
function endsInQuestion(text) {
    return /\?$/.test(text.trim().replace(/[\s*_`"'”’)\]]+$/u, ""));
}

// Stop.
async function stop(input) {
    const sessionId = input.session_id;
    if (!sessionId) return;

    const message =
        typeof input.last_assistant_message === "string" && input.last_assistant_message.trim()
            ? input.last_assistant_message.trim()
            : input.transcript_path
              ? await lastAssistantText(input.transcript_path)
              : null;
    if (!message || !endsInQuestion(message)) return;

    const asked = await callTool("ask", { sessionId, questions: [{ question: message }], kind: "prose" });
    if (asked?.id && asked !== REFUSED) await remember(sessionId, { id: asked.id, kind: "prose", text: message });
}

// UserPromptSubmit.
async function prompt(input) {
    const sessionId = input.session_id;
    if (!sessionId) return;

    const held = (await readState(sessionId)).filter((question) => question.kind === "prose");
    if (held.length === 0) return;

    const text = typeof input.prompt === "string" ? input.prompt : "";
    for (const question of held) {
        // close keys the answers by question text, which stop kept beside the id.
        const answers = question.text ? { [question.text]: text } : {};
        await callTool("close", { id: question.id, sessionId, reason: "answered-in-session", answers });
    }
    await forget(sessionId, held.map((question) => question.id));
}

// SessionEnd.
async function end(input) {
    const sessionId = input.session_id;
    if (!sessionId) return;

    const held = await readState(sessionId);
    for (const question of held) {
        await callTool("close", { id: question.id, sessionId, reason: "session-ended" });
    }
    await writeState(sessionId, []);
}

const modes = { pre, post, stop, prompt, end };

async function main() {
    // Claude may close the pipe before a reply is written: that is no reason to fail the hook.
    process.stdout.on("error", () => process.exit(0));

    const mode = modes[process.argv[2]];
    if (!mode) return;

    const input = JSON.parse(await readStdin());
    await mode(input);
}

main()
    .catch(() => {})
    // Exit 0 whatever happened, once anything printed has reached the pipe.
    .finally(() => process.stdout.write("", () => process.exit(0)));
