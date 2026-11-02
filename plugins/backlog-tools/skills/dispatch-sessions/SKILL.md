---
name: dispatch-sessions
description: Run this machine's Manager session — the dispatcher of local ADR 0038 that turns each session request the Backlog app queues (a Start session click on an entry or a plan) into a Claude Desktop session in the request's clone folder, in its own worktree, with the app's fixed brief as its whole prompt, and hands each answer the app accepted for a waiting session back to that session (local ADR 0041). Waits on the app's long poll through a Monitor, delivers answers first, starts a request only while fewer than N dispatched sessions work and plan usage, CPU and memory leave room, and reports each start and delivery back. Started once as /loop /backlog-tools:dispatch-sessions in a pinned session; changes no file and is no flow.
argument-hint: "[sessions=3] [usage=80] [cpu=80] [memory=4]"
disable-model-invocation: true
---

# Dispatch session requests

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

You dispatch and deliver, never run work or write a brief or an answer: each start is a click in
the app, each answer one it accepted (ADRs 0038, 0041). Arguments [defaults]: `sessions` [3] working,
`usage` [80] plan %, `cpu` [80] average % over 30 s, `memory` [4] free GB. Paths are from this folder.

## Each turn

1. **Wait.** `node assets/wait-requests.mjs --name` prints your `dispatcher`. Keep one Monitor on
   `node assets/wait-requests.mjs` at its longest timeout, re-armed on expiry or `stopped`; never loop on tools.
2. **Restart.** First turn only: `list_session_requests` and `list_deliveries` `state: claimed`;
   release each your `dispatcher` holds (`mark_session_request`, `mark_delivery`).
3. **Deliver** each `list_deliveries` `state: pending` with a `desktopSessionId`, oldest first:
   `claim_delivery` (id, `dispatcher`), `claimed: false` → next; `send_message` to that id:
   "You asked: <question>. The answer from Backlog: <answer>", both verbatim. Sent or queued →
   `mark_delivery` `delivered`; the session no longer exists, or any other failure → `released`.
4. **Queue.** `list_session_requests` `state: requested`, oldest first; empty → step 7. A
   Monitor line other than `delivery`, `request` or `ready` → say it once, then step 7.
5. **Capacity**, before each start, cheapest first; the first that fails holds the queue:
   - fewer than `sessions` rows of `list_sessions` (`linked: true`, `limit: 100`) with `startedBy`
     your id (`get_session` `self`) and `isRunning: true`, not counting a row whose id is a
     `desktopSessionId` the `backlog` server's `list_sessions` reads `Waiting`;
   - every plan limit `get_usage` reports under `usage` percent (`not_applicable` passes);
   - `powershell -NoProfile -ExecutionPolicy Bypass -File assets/capacity.ps1 -MaxCpu <cpu>
     -MinFreeGb <memory>` prints `"fits":true`.
6. **Dispatch** the oldest: `claim_session_request` (id, `dispatcher`), `claimed: false` → next. `start_session`:
   `initiation: user_asked`, `context: fresh`, `cwd` its `cloneFolder`, `use_worktree: true`,
   `prompt` its `brief` verbatim, `wake_when_done: true`, `title` the entry's `read_item` title
   or the plan's `+tag` (under 60 characters), `background` "Started by dispatch-sessions for
   Backlog session request <id>." Started → `mark_session_request` `started` with its session id,
   else `released`; say what a `marked: false` answered. Back to step 5.
7. **Pace.** End the turn. `/loop` is only the fallback: 300 s if a limit held the queue, else 1800 s.

## Never

- Start or send what you did not claim, change a brief or an answer, edit a file, or run a second
  Manager here: both would share one `dispatcher` name, which the claim tools trust.
