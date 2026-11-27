// The Claude Desktop adapter of the session-host port (skills/dispatch-sessions/assets/claude-desktop-host.mjs):
// each port operation is the Claude Desktop call dispatch-sessions made before the port existed,
// argument for argument, so its dispatching behaves as it did.
//   node --test plugins/backlog-desktop/tests/claude-desktop-host.test.mjs

import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import { claudeDesktopHost, sendAccepted, sendMessageArguments, startSessionArguments, startedId } from "../skills/dispatch-sessions/assets/claude-desktop-host.mjs";
import { deliverAnswers, dispatchRequest, requestBackground } from "../skills/dispatch-sessions/assets/dispatcher-logic.mjs";
import { OPERATIONS } from "../skills/dispatch-sessions/assets/session-host-port.mjs";

const SKILL = fs.readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "skills", "dispatch-sessions", "SKILL.md"), "utf8");

// A stand-in for the tools a Claude Desktop turn calls, recording each.
function desktop(answers = {}) {
    const calls = [];
    const invoke = async (tool, args) => {
        calls.push({ tool, args });
        const answer = answers[tool];
        return typeof answer === "function" ? answer(args) : answer;
    };
    return { calls, invoke };
}

test("Start is the start_session call step 7 makes: fresh, its own worktree, the brief verbatim, woken when done", () => {
    const args = startSessionArguments({ cwd: "D:/Repos/Backlog", brief: "/backlog-tools:run-plan-item entry `x`:", title: "6 - Put", background: requestBackground("r1") });
    assert.deepEqual(args, {
        initiation: "user_asked",
        context: "fresh",
        cwd: "D:/Repos/Backlog",
        use_worktree: true,
        prompt: "/backlog-tools:run-plan-item entry `x`:",
        wake_when_done: true,
        title: "6 - Put",
        background: "Started by dispatch-sessions for Backlog session request r1.",
    });
    assert.equal(startSessionArguments({}, { initiation: "own_initiative" }).initiation, "own_initiative");
    for (const name of ["initiation: user_asked", "context: fresh", "use_worktree: true", "wake_when_done: true", "Started by dispatch-sessions for Backlog session request <id>."]) {
        assert.ok(SKILL.includes(name), `SKILL.md no longer says ${name}`);
    }
});

test("Deliver is send_message to the desktop id, and only delivered or queued counts as sent", () => {
    assert.deepEqual(sendMessageArguments("local_1", "m"), { session_id: "local_1", message: "m" });
    assert.equal(sendAccepted("Sent. (delivery: delivered; message_id: m1)"), true);
    assert.equal(sendAccepted("Waiting. (delivery: queued; message_id: m1)"), true);
    assert.equal(sendAccepted("No such session. (delivery: failed; message_id: m1)"), false);
    assert.equal(startedId({ sessionId: "local_9" }), "local_9");
    assert.equal(startedId("Started local_ab12-cd34 in its own worktree."), "local_ab12-cd34");
});

test("the adapter is a whole port, and the skill names each operation", () => {
    const host = claudeDesktopHost(desktop().invoke);
    for (const operation of OPERATIONS) assert.equal(typeof host[operation], "function");
    for (const name of ["Start", "Report id", "List", "Read state", "Deliver", "start_session", "send_message"]) {
        assert.ok(SKILL.includes(name), `SKILL.md never names ${name}`);
    }
});

test("a request dispatched through the adapter calls start_session, then reports the desktop id", async () => {
    const tools = desktop({ start_session: { sessionId: "local_42" } });
    const reported = [];
    const backlog = {
        async call(tool, args) {
            reported.push({ tool, args });
            return tool === "claim_session_request" ? { claimed: true } : { marked: true };
        },
    };
    const request = { id: "r1", cloneFolder: "D:/Repos/Backlog", brief: "/backlog-tools:run-plan-item entry `x`:" };
    const outcome = await dispatchRequest(request, { backlog, host: claudeDesktopHost(tools.invoke), dispatcher: "d", title: "6 - Put" });
    assert.deepEqual(outcome, { id: "r1", outcome: "started", sessionId: "local_42" });
    assert.deepEqual(tools.calls, [{ tool: "start_session", args: startSessionArguments({ cwd: "D:/Repos/Backlog", brief: request.brief, title: "6 - Put", background: requestBackground("r1") }) }]);
    assert.deepEqual(reported.at(-1).args, { id: "r1", outcome: "started", sessionId: "local_42", dispatcher: "d" });
});

test("an answer delivered through the adapter is one send_message, and a refused one is released", async () => {
    let answer = "(delivery: delivered; message_id: m1)";
    const tools = desktop({ send_message: () => answer });
    const marks = [];
    const backlog = {
        async call(tool, args) {
            if (tool === "list_deliveries") return { deliveries: [{ id: "q1", desktopSessionId: "local_1", question: "Which database?", answer: "Postgres" }] };
            if (tool === "claim_delivery") return { claimed: true };
            marks.push(args.outcome);
            return { marked: true };
        },
    };
    const run = () => deliverAnswers({ backlog, host: claudeDesktopHost(tools.invoke), dispatcher: "d", target: (d) => d.desktopSessionId });
    await run();
    answer = "(delivery: failed; message_id: m2)";
    await run();
    assert.deepEqual(marks, ["delivered", "released"]);
    assert.deepEqual(tools.calls[0], { tool: "send_message", args: { session_id: "local_1", message: "You asked: Which database?. The answer from Backlog: Postgres" } });
});

test("List and Read state come from Claude Desktop's list_sessions and backlog's Waiting", async () => {
    const tools = desktop({
        list_sessions: [{ sessionId: "local_1", title: "A", isRunning: true }, { sessionId: "local_2", title: "B", isRunning: true, isArchived: true, prNumber: 7 }],
        "backlog:list_sessions": { sessions: [{ desktopSessionId: "local_3", state: "Waiting" }] },
    });
    const host = claudeDesktopHost(tools.invoke);
    const rows = await host.list();
    assert.deepEqual(rows.map((r) => [r.id, r.name, r.archived, r.prNumber]), [["local_1", "A", false, null], ["local_2", "B", true, 7]]);
    assert.equal(await host.state("local_1"), "working");
    assert.equal(await host.state("local_3"), "waiting");
    assert.equal(await host.state("local_9"), "idle");
});
