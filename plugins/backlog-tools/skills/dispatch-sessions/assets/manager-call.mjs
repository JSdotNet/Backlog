#!/usr/bin/env node
// One Backlog MCP tool call under the "Manager" grant (local ADR 0052 §4):
//
//   node manager-call.mjs <tool> [<arguments as JSON>]
//
// The session-request and answer-delivery tools — list_session_requests, claim_session_request,
// mark_session_request, list_deliveries, claim_delivery, mark_delivery — require
// sessions:dispatch, which only the Manager grant holds, never the "Backlog delivery" grant the
// `backlog` MCP server registration sends. So the Manager calls them through this script rather
// than that server: a JSON-RPC tools/call to the app's /mcp with a fresh token for
// mcpServer.grants.manager from the app's installed headers helper (../../../hooks/backlog-grant.mjs).
// No other session gets the Manager's tools in its tool list.
//
// Prints the tool's result as JSON and exits 0; a refusal — 401 or 403, a tool error — or no
// token prints the reason on stderr and exits 1; an app that does not answer exits 2.

import { authorization, readEndpoint } from "../../../hooks/backlog-grant.mjs";

const CALL_TIMEOUT_MS = 30_000;

// The JSON-RPC response, whether the server answered plain JSON or server-sent events.
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

// What a tools/call answered: { result } for the tool's payload, else { refused } with the reason.
function outcomeOf(status, body) {
    if (status === 401 || status === 403) {
        return { refused: `Backlog refused the Manager grant (${status}): it is revoked, still pending, or lacks sessions:dispatch.` };
    }
    if (status < 200 || status >= 300) return { refused: `Backlog answered ${status}.` };
    let message;
    try {
        message = parseRpc(body);
    } catch {
        return { refused: "Backlog answered something that is not JSON-RPC." };
    }
    if (message?.error) return { refused: message.error.message ?? JSON.stringify(message.error) };
    const result = message?.result;
    if (!result) return { refused: "Backlog answered no result." };
    const text = (result.content || []).find((part) => part?.type === "text")?.text;
    if (result.isError) return { refused: text || "The tool reported an error." };
    if (result.structuredContent && typeof result.structuredContent === "object") return { result: result.structuredContent };
    try {
        return { result: text ? JSON.parse(text) : null };
    } catch {
        return { result: text };
    }
}

async function main([tool, json]) {
    if (!tool) {
        process.stderr.write("Usage: node manager-call.mjs <tool> [<arguments as JSON>]\n");
        return 1;
    }
    let args;
    try {
        args = json ? JSON.parse(json) : {};
    } catch {
        process.stderr.write(`The arguments are not JSON: ${json}\n`);
        return 1;
    }

    const target = await readEndpoint("manager", "/mcp");
    const { header, error } = await authorization(target, "Manager");
    if (!header) {
        process.stderr.write(`${error}\n`);
        return 1;
    }

    let status;
    let body;
    try {
        const response = await fetch(target.url, {
            method: "POST",
            headers: { "content-type": "application/json", accept: "application/json, text/event-stream", authorization: header },
            body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name: tool, arguments: args } }),
            signal: AbortSignal.timeout(CALL_TIMEOUT_MS),
        });
        status = response.status;
        body = await response.text();
    } catch (failure) {
        process.stderr.write(`Backlog did not answer at ${target.url}: ${failure?.message ?? failure}\n`);
        return 2;
    }

    const outcome = outcomeOf(status, body);
    if ("refused" in outcome) {
        process.stderr.write(`${tool}: ${outcome.refused}\n`);
        return 1;
    }
    process.stdout.write(`${JSON.stringify(outcome.result)}\n`);
    return 0;
}

main(process.argv.slice(2))
    .catch((failure) => {
        process.stderr.write(`${failure?.message ?? failure}\n`);
        return 2;
    })
    .then((code) => process.exit(code));
