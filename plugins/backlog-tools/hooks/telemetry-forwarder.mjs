#!/usr/bin/env node
// Forwards one Claude Code hook event to the Backlog desktop app, which attributes it to the
// delivery run the session is driving (Backlog's DeliveryRunTelemetry). The dashboards collect
// the same figures from hooks rather than tools; this is the forwarding half, and the app does
// the counting — it reads the transcript the event names, so nothing here does.
//
// Endpoint: POST http://127.0.0.1:<port>/telemetry, beside the app's /mcp, with a token for the
// "Telemetry" grant (mcpServer.grants.telemetry, local ADR 0052, Phase 1), asked for per event
// through the app's installed headers helper — see backlog-grant.mjs. BACKLOG_MCP_TOKEN is sent
// only when no Telemetry grant is recorded. With neither, nothing is posted.
//
// Must never fail or slow the tool call it reports on: every error exits 0, an app that is not
// running refuses the connection at once, and a hung one is abandoned after TIMEOUT_MS.

import { authorization, readEndpoint } from "./backlog-grant.mjs";

const TIMEOUT_MS = 2000;
// The share of TIMEOUT_MS the headers helper may take; the broker answers in milliseconds.
const HELPER_TIMEOUT_MS = 1500;

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

async function main() {
    const raw = await readStdin();
    const event = JSON.parse(raw);
    // The tool's output is the bulk of a PostToolUse payload and nothing the app records reads it.
    delete event.tool_response;

    // One deadline for the token and the post together, so the hook stays well inside its 5 s.
    const deadline = Date.now() + TIMEOUT_MS;
    const target = await readEndpoint("telemetry", "/telemetry");
    const { header } = await authorization(target, "Telemetry", { timeoutMs: HELPER_TIMEOUT_MS });
    // No token is a refusal waiting to happen: the route answers 401 to a call without one.
    if (!header) return;

    // A 401 or 403 — the grant revoked, or its scope switched off — is dropped like any other
    // failure: the session goes on, and the next event asks for a fresh token.
    await fetch(target.url, {
        method: "POST",
        headers: { "content-type": "application/json", authorization: header },
        body: JSON.stringify(event),
        signal: AbortSignal.timeout(Math.max(250, deadline - Date.now())),
    });
}

main()
    .catch(() => {})
    .finally(() => process.exit(0));
