// The dispatcher's host-neutral steps (resources/dispatch/assets/dispatcher-logic.mjs) against the
// fake session host and an in-memory stand-in for Backlog's Manager tools:
//   node --test plugins/backlog-tools/tests/dispatcher.test.mjs

import assert from "node:assert/strict";
import { test } from "node:test";

import {
    countSessions,
    deliverAnswers,
    dispatchQueue,
    dispatchRequest,
    mend,
    releaseHeld,
    requestBackground,
    room,
} from "../resources/dispatch/assets/dispatcher-logic.mjs";
import { fakeSessionHost } from "../resources/dispatch/assets/fake-session-host.mjs";
import { fixTitle } from "../resources/dispatch/assets/mend-logic.mjs";
import { OPERATIONS, deliveryMessage, portSession, sessionHost } from "../resources/dispatch/assets/session-host-port.mjs";

const DISPATCHER = "dispatch-sessions-pc";
const LIMITS = { sessions: 3, usage: 80, cpu: 80, memory: 4 };
const ROOMY = { hasRoom: true, workingSessions: 0, planUsagePercent: 10, cpuPercent: 10, freeMemoryGb: 16 };

// Backlog's six Manager tools, held in memory the way the app answers them.
function fakeBacklog({ requests = [], deliveries = [], paused = false, markRefusal = null } = {}) {
    const calls = [];
    const state = { requests: requests.map((r) => ({ state: "requested", claimedBy: null, ...r })), deliveries: deliveries.map((d) => ({ state: "pending", claimedBy: null, ...d })), paused };
    const tools = {
        list_session_requests: ({ state: wanted }) => ({ paused: state.paused, requests: state.requests.filter((r) => !wanted || r.state === wanted) }),
        claim_session_request: ({ id, dispatcher }) => {
            const request = state.requests.find((r) => r.id === id);
            if (state.paused) return { claimed: false, refusal: "The session queue is paused.", request };
            if (request.state !== "requested") return { claimed: false, refusal: "Taken.", request };
            Object.assign(request, { state: "claimed", claimedBy: dispatcher });
            return { claimed: true, request };
        },
        mark_session_request: ({ id, outcome, sessionId }) => {
            const request = state.requests.find((r) => r.id === id);
            if (markRefusal) return { marked: false, refusal: markRefusal, request: { ...request, claimedBy: "other", sessionId: "other-session" } };
            Object.assign(request, outcome === "started" ? { state: "started", sessionId } : { state: "requested", claimedBy: null });
            return { marked: true, request };
        },
        list_deliveries: ({ state: wanted }) => ({ deliveries: state.deliveries.filter((d) => !wanted || d.state === wanted) }),
        claim_delivery: ({ id, dispatcher }) => {
            const delivery = state.deliveries.find((d) => d.id === id);
            if (delivery.state !== "pending") return { claimed: false, refusal: "Held by another dispatcher.", delivery };
            Object.assign(delivery, { state: "claimed", claimedBy: dispatcher });
            return { claimed: true, delivery };
        },
        mark_delivery: ({ id, outcome }) => {
            const delivery = state.deliveries.find((d) => d.id === id);
            Object.assign(delivery, outcome === "delivered" ? { state: "delivered" } : { state: "pending", claimedBy: null });
            return { marked: true, delivery };
        },
    };
    return {
        calls,
        state,
        async call(tool, args) {
            calls.push({ tool, args });
            return tools[tool](args);
        },
    };
}

const request = (id, extra = {}) => ({ id, kind: "entry", repository: "JSdotNet/Backlog", cloneFolder: "D:/Repos/Backlog", brief: `/backlog-tools:run-plan-item entry \`${id}\`:`, ...extra });
const delivery = (id, session, extra = {}) => ({ id, sessionId: `transcript-${session}`, desktopSessionId: session, question: "Which database?", answer: "Postgres", ...extra });
const titleOf = (r) => `Entry ${r.id}`;
const operations = (host) => host.calls.map((c) => c.operation);

test("the port has the five operations of ADR 0038, and refuses a host that lacks one", () => {
    assert.deepEqual(OPERATIONS, ["start", "sessionId", "list", "state", "deliver"]);
    assert.doesNotThrow(() => sessionHost(fakeSessionHost()));
    assert.throws(() => sessionHost({ start() {}, list() {} }), /does not implement sessionId, state, deliver/);
});

test("a host's rows read in the port's shape, Claude Desktop's names included", () => {
    assert.deepEqual(portSession({ sessionId: "local_1", title: "CI fix o/r#2", prNumber: 2, isArchived: true }), { id: "local_1", name: "CI fix o/r#2", prNumber: 2, archived: true });
    assert.deepEqual(portSession({ id: "s1", name: "x" }), { id: "s1", name: "x", prNumber: null, archived: false });
});

test("the fake host starts, names, lists, reads and delivers, and records each call", async () => {
    const host = fakeSessionHost({ sessions: [{ id: "s1", name: "Old", state: "idle" }] });
    const handle = await host.start({ cwd: "D:/r", brief: "b", title: "T" });
    const id = await host.sessionId(handle);
    assert.equal(await host.state(id), "working");
    assert.deepEqual((await host.list()).map((s) => s.name), ["Old", "T"]);
    assert.deepEqual(await host.deliver("s1", "hi"), { delivered: true });
    assert.equal(await host.state("s1"), "working", "a delivery starts a turn in an idle session");
    host.gone("s1");
    await assert.rejects(host.deliver("s1", "again"), /No session s1/);
    assert.deepEqual(operations(host), ["start", "sessionId", "state", "list", "deliver", "state", "deliver"]);
});

test("a restart gives back only the claims this dispatcher holds", async () => {
    const backlog = fakeBacklog({
        requests: [request("r1", { state: "claimed", claimedBy: DISPATCHER }), request("r2", { state: "claimed", claimedBy: "elsewhere" })],
        deliveries: [delivery("d1", "local_1", { state: "claimed", claimedBy: DISPATCHER })],
    });
    assert.deepEqual(await releaseHeld({ backlog, dispatcher: DISPATCHER }), ["r1", "d1"]);
    assert.equal(backlog.state.requests[1].state, "claimed");
});

test("an answer is claimed, delivered verbatim through the port, then marked delivered", async () => {
    const host = fakeSessionHost({ sessions: [{ id: "local_1", name: "Waiting one", state: "idle" }] });
    const backlog = fakeBacklog({ deliveries: [delivery("d1", "local_1"), delivery("d2", null)] });
    const outcomes = await deliverAnswers({ backlog, host, dispatcher: DISPATCHER, target: (d) => d.desktopSessionId });
    assert.deepEqual(outcomes, [{ id: "d1", outcome: "delivered", reason: null }]);
    assert.deepEqual(host.sessions[0].inbox, ["You asked: Which database?. The answer from Backlog: Postgres"]);
    assert.equal(deliveryMessage("Which database?", "Postgres"), host.sessions[0].inbox[0]);
    assert.deepEqual(backlog.calls.map((c) => c.tool), ["list_deliveries", "claim_delivery", "mark_delivery"], "a delivery with no session to reach is never claimed");
});

test("a delivery the host refuses, or whose session is gone, is released so it stays pending", async () => {
    const host = fakeSessionHost({ sessions: [{ id: "local_1", name: "a" }] });
    host.refuse.deliver = "The session does not take messages.";
    const backlog = fakeBacklog({ deliveries: [delivery("d1", "local_1"), delivery("d2", "local_gone")] });
    const outcomes = await deliverAnswers({ backlog, host, dispatcher: DISPATCHER, target: (d) => d.desktopSessionId });
    assert.deepEqual(outcomes.map((o) => o.outcome), ["released", "released"]);
    assert.ok(backlog.state.deliveries.every((d) => d.state === "pending"));
});

test("a delivery another dispatcher holds is left alone", async () => {
    const host = fakeSessionHost({ sessions: [{ id: "local_1", name: "a" }] });
    const backlog = fakeBacklog({ deliveries: [delivery("d1", "local_1")] });
    backlog.state.deliveries[0].state = "claimed";
    const list = backlog.call.bind(backlog);
    backlog.call = (tool, args) => (tool === "list_deliveries" ? { deliveries: [{ ...backlog.state.deliveries[0], state: "pending" }] } : list(tool, args));
    const outcomes = await deliverAnswers({ backlog, host, dispatcher: DISPATCHER, target: (d) => d.desktopSessionId });
    assert.equal(outcomes[0].outcome, "taken");
    assert.equal(host.calls.filter((c) => c.operation === "deliver").length, 0);
});

test("a request is claimed, started in its clone folder with the brief verbatim, and reported by the host's id", async () => {
    const host = fakeSessionHost();
    const backlog = fakeBacklog({ requests: [request("r1")] });
    const outcome = await dispatchRequest(backlog.state.requests[0], { backlog, host, dispatcher: DISPATCHER, title: "Entry r1" });
    assert.deepEqual(outcome, { id: "r1", outcome: "started", sessionId: "fake-1" });
    assert.deepEqual(host.calls[0], { operation: "start", args: { cwd: "D:/Repos/Backlog", brief: "/backlog-tools:run-plan-item entry `r1`:", title: "Entry r1", background: requestBackground("r1") } });
    assert.deepEqual(backlog.calls.at(-1), { tool: "mark_session_request", args: { id: "r1", outcome: "started", sessionId: "fake-1", dispatcher: DISPATCHER } });
});

test("a start that fails gives the claim back", async () => {
    const host = fakeSessionHost();
    host.refuse.start = "The folder is not trusted.";
    const backlog = fakeBacklog({ requests: [request("r1")] });
    const outcome = await dispatchRequest(backlog.state.requests[0], { backlog, host, dispatcher: DISPATCHER, title: "t" });
    assert.equal(outcome.outcome, "released");
    assert.equal(backlog.state.requests[0].state, "requested");
});

test("a late report refused because another dispatcher claimed it never releases, and names both sessions", async () => {
    const host = fakeSessionHost();
    const backlog = fakeBacklog({ requests: [request("r1")], markRefusal: "Another dispatcher holds the claim" });
    const outcome = await dispatchRequest(backlog.state.requests[0], { backlog, host, dispatcher: DISPATCHER, title: "t" });
    assert.equal(outcome.outcome, "double");
    assert.equal(outcome.sessionId, "fake-1");
    assert.equal(outcome.otherSession, "other-session");
    assert.ok(!backlog.calls.some((c) => c.args.outcome === "released"));
});

test("a request another dispatcher claimed first starts nothing", async () => {
    const host = fakeSessionHost();
    const backlog = fakeBacklog({ requests: [request("r1", { state: "claimed", claimedBy: "other" })] });
    const outcome = await dispatchRequest(request("r1"), { backlog, host, dispatcher: DISPATCHER, title: "t" });
    assert.equal(outcome.outcome, "taken");
    assert.equal(host.calls.length, 0);
});

test("the queue starts in the order listed and holds once sessions reach the limit", async () => {
    const host = fakeSessionHost();
    const backlog = fakeBacklog({ requests: [request("r2"), request("r1"), request("r3"), request("r4")] });
    const result = await dispatchQueue({ backlog, host, dispatcher: DISPATCHER, titleOf, checkIn: { ...ROOMY, workingSessions: 1 }, limits: LIMITS });
    assert.deepEqual(result.outcomes.map((o) => o.id), ["r2", "r1"]);
    assert.deepEqual(result.held, ["sessions"]);
    assert.deepEqual(host.calls.filter((c) => c.operation === "start").map((c) => c.args.title), ["Entry r2", "Entry r1"]);
});

test("a paused queue starts nothing and claims nothing", async () => {
    const host = fakeSessionHost();
    const backlog = fakeBacklog({ requests: [request("r1")], paused: true });
    const result = await dispatchQueue({ backlog, host, dispatcher: DISPATCHER, titleOf, checkIn: ROOMY, limits: LIMITS });
    assert.equal(result.paused, true);
    assert.deepEqual(backlog.calls.map((c) => c.tool), ["list_session_requests"]);
    assert.equal(host.calls.length, 0);
});

test("room follows the check-in answer, and counts fixers the app never counts", async () => {
    const host = fakeSessionHost({ sessions: [{ id: "f1", name: fixTitle("o/r", 1), state: "working" }, { id: "f2", name: fixTitle("o/r", 2), archived: true }] });
    assert.deepEqual(await room({ checkIn: { ...ROOMY, hasRoom: false, heldBy: ["usage"] }, limits: LIMITS, host }), { fits: false, heldBy: ["usage"] });
    assert.deepEqual(await room({ checkIn: { ...ROOMY, workingSessions: 1 }, limits: LIMITS, host }), { fits: true, heldBy: [] });
    assert.deepEqual(await room({ checkIn: { ...ROOMY, workingSessions: 1 }, startsSince: 1, limits: LIMITS, host }), { fits: false, heldBy: ["sessions"] });
    const read = host.calls.filter((c) => c.operation === "state").map((c) => c.args.id);
    assert.deepEqual([...new Set(read)], ["f1"], "the app counted the working sessions, so only the open fixer's state is read");
});

test("without a check-in figure, room measures it: working sessions through the port, waiting ones not counted", async () => {
    const host = fakeSessionHost({
        sessions: [
            { id: "a", name: "A", state: "working" },
            { id: "b", name: "B", state: "waiting" },
            { id: "c", name: "C", state: "working" },
            { id: "d", name: "D", state: "working" },
        ],
    });
    const own = (s) => s.id !== "d";
    assert.deepEqual(await countSessions({ host, own }), { working: 2, fixers: 0 });
    assert.equal((await room({ checkIn: null, limits: LIMITS, host, own })).fits, true);
    host.sessions[1].state = "working";
    assert.deepEqual(await room({ checkIn: null, limits: LIMITS, host, own }), { fits: false, heldBy: ["sessions"] });
});

test("without a check-in figure, room asks usage, then the machine, and the first failing holds", async () => {
    const host = fakeSessionHost();
    const asked = [];
    const measure = {
        usage: async () => (asked.push("usage"), [50, 85]),
        machine: async () => (asked.push("machine"), { fits: false, heldBy: ["memory"] }),
    };
    assert.deepEqual(await room({ checkIn: null, limits: LIMITS, host, measure }), { fits: false, heldBy: ["usage"] });
    assert.deepEqual(asked, ["usage"]);
    measure.usage = async () => [10];
    assert.deepEqual(await room({ checkIn: null, limits: LIMITS, host, measure }), { fits: false, heldBy: ["memory"] });
    assert.deepEqual(await room({ checkIn: ROOMY, limits: LIMITS, host, measure }), { fits: true, heldBy: [] }, "a full check-in measures nothing");
});

const check = (conclusion) => ({ __typename: "CheckRun", status: "COMPLETED", conclusion });
const botPr = (number, conclusion = "FAILURE") => ({ number, isDraft: false, isCrossRepository: false, author: { login: "app/dependabot" }, headRefOid: "abcdef1234", statusCheckRollup: [check(conclusion)] });

test("the mend starts one fixer per red bot pull request no session watches, through the port", async () => {
    const host = fakeSessionHost({ sessions: [{ id: "w", name: "someone", prNumber: 11 }, { id: "f", name: fixTitle("o/r", 12) }] });
    const readPullRequests = async () => ({ repository: "o/r", pullRequests: [botPr(10), botPr(11), botPr(12), botPr(13, "SUCCESS")] });
    const result = await mend({ host, folders: ["D:/o/r"], readPullRequests, checkIn: ROOMY, limits: { ...LIMITS, sessions: 5 } });
    assert.deepEqual(result.outcomes.map((o) => [o.number, o.outcome]), [[10, "started"], [11, "watched"], [12, "fixing"]]);
    const start = host.calls.find((c) => c.operation === "start");
    assert.equal(start.args.cwd, "D:/o/r");
    assert.equal(start.args.title, "CI fix o/r#10");
    assert.match(start.args.brief, /o\/r#10/);
    assert.match(start.args.brief, /abcdef1/);
});

test("the mend holds when room runs out, and says a folder it cannot read", async () => {
    const host = fakeSessionHost();
    const readPullRequests = async (folder) => {
        if (folder === "bad") throw new Error("gh: not a repository");
        return { repository: "o/r", pullRequests: [botPr(1), botPr(2)] };
    };
    const result = await mend({ host, folders: ["bad", "D:/o/r"], readPullRequests, checkIn: ROOMY, limits: { ...LIMITS, sessions: 2 } });
    assert.deepEqual(result.outcomes.map((o) => o.outcome), ["error", "started", "started"]);
    const held = await mend({ host: fakeSessionHost(), folders: ["D:/o/r"], readPullRequests, checkIn: ROOMY, limits: { ...LIMITS, sessions: 1 } });
    assert.deepEqual(held.outcomes.map((o) => o.outcome), ["started"]);
    assert.deepEqual(held.held, ["sessions"], "the working fixer it started counts once against the limit");
});

test("without a check-in figure, a session started this turn counts once, so the limit is reached exactly", async () => {
    const host = fakeSessionHost();
    const backlog = fakeBacklog({ requests: [request("r1"), request("r2"), request("r3"), request("r4")] });
    const result = await dispatchQueue({ backlog, host, dispatcher: DISPATCHER, titleOf, checkIn: null, limits: LIMITS });
    assert.deepEqual(result.outcomes.map((o) => o.outcome), ["started", "started", "started"]);
    assert.deepEqual(result.held, ["sessions"]);
});

test("a started session whose id the host cannot say keeps its claim, so no second one starts", async () => {
    const host = fakeSessionHost();
    host.sessionId = async () => {
        throw new Error("no id in the answer");
    };
    const backlog = fakeBacklog({ requests: [request("r1")] });
    const outcome = await dispatchRequest(backlog.state.requests[0], { backlog, host, dispatcher: DISPATCHER, title: "t" });
    assert.equal(outcome.outcome, "unreported");
    assert.equal(backlog.state.requests[0].state, "claimed");
    assert.ok(!backlog.calls.some((c) => c.tool === "mark_session_request"));
});

test("a report refused for any other reason than another holder is not a double start", async () => {
    const host = fakeSessionHost();
    const backlog = fakeBacklog({ requests: [request("r1")] });
    const call = backlog.call.bind(backlog);
    backlog.call = async (tool, args) => (tool === "mark_session_request" ? { marked: false, refusal: "The request was cancelled.", request: { id: "r1", claimedBy: null } } : call(tool, args));
    const outcome = await dispatchRequest(backlog.state.requests[0], { backlog, host, dispatcher: DISPATCHER, title: "t" });
    assert.equal(outcome.outcome, "refused");
});

test("an idle fixer waiting at Personal Validation holds no slot, and own() sees the host's own fields", async () => {
    const host = fakeSessionHost({
        sessions: [
            { id: "f1", name: fixTitle("o/r", 1), state: "idle" },
            { id: "a", name: "A", state: "working", startedBy: "me" },
            { id: "b", name: "B", state: "working", startedBy: "someone else" },
        ],
    });
    host.list = async () => host.sessions.map(({ id, name, prNumber, archived, startedBy }) => ({ id, name, prNumber, archived, startedBy }));
    assert.deepEqual(await countSessions({ host, own: (s) => s.startedBy === "me" || s.name.startsWith("CI fix ") }), { working: 1, fixers: 0 });
});

test("an adapter answer that is not delivered: true is released, never marked delivered", async () => {
    const host = fakeSessionHost({ sessions: [{ id: "local_1", name: "a" }] });
    host.deliver = async () => undefined;
    const backlog = fakeBacklog({ deliveries: [delivery("d1", "local_1")] });
    const outcomes = await deliverAnswers({ backlog, host, dispatcher: DISPATCHER, target: (d) => d.desktopSessionId });
    assert.equal(outcomes[0].outcome, "released");
});
