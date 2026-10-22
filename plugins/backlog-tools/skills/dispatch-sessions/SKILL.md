---
name: dispatch-sessions
description: Run this machine's Manager session — the dispatcher of local ADR 0037 that turns each session request the Backlog app queues (a Start session click on an entry or a plan) into a Claude Desktop session in the request's clone folder, in its own worktree, with the app's fixed brief as its whole prompt. Waits on the app's long poll through a Monitor, starts a request only while fewer than N dispatched sessions work and plan usage, CPU and memory leave room, and reports each start back. Started once as /loop /backlog-tools:dispatch-sessions in a pinned session; changes no file and is no flow.
argument-hint: "[sessions=3] [usage=80] [cpu=80] [memory=4]"
disable-model-invocation: true
---

# Dispatch session requests

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

You dispatch; you never run work, write a brief, or take one from chat or entry text: every
start is a request a click in the app made (local ADR 0037). Arguments, defaults in brackets:
`sessions` [3] dispatched sessions working, `usage` [80] plan percent, `cpu` [80] average
percent over 30 s, `memory` [4] free GB. Paths below are from this skill's folder.

## Each turn

1. **Wait.** Your `dispatcher` name is what `node assets/wait-requests.mjs --name` prints.
   Unless a Monitor of yours runs `node assets/wait-requests.mjs`, start one at its longest
   timeout, re-armed on expiry or `stopped`. It is your only wait: never loop on the tools.
2. **Restart.** First turn only: `list_session_requests` `state: claimed`; release
   (`mark_session_request` `released`) each your `dispatcher` holds — a lost start reruns safely.
3. **Queue.** `list_session_requests` `state: requested`, oldest first; empty → step 6. A
   Monitor line other than `request` or `ready` → say it once, then step 6.
4. **Capacity**, before each start, cheapest first; the first that fails holds the queue:
   - fewer than `sessions` rows of `list_sessions` (`linked: true`, `limit: 100`) with
     `startedBy` your id (`get_session` `self`) and `isRunning: true`;
   - every plan limit `get_usage` reports under `usage` percent (`not_applicable` passes);
   - `powershell -NoProfile -ExecutionPolicy Bypass -File assets/capacity.ps1 -MaxCpu <cpu>
     -MinFreeGb <memory>` prints `"fits":true`.
5. **Dispatch** the oldest: `claim_session_request` (id, `dispatcher`); `claimed: false` → next.
   `start_session`: `initiation: user_asked`, `context: fresh`, `cwd` its `cloneFolder`,
   `use_worktree: true`, `prompt` its `brief` verbatim, `wake_when_done: true`, `title` the
   entry's `read_item` title or the plan's `+tag`, under 60 characters, and `background`
   "Started by dispatch-sessions for Backlog session request <id>; the prompt is the app's brief."
   Started → `mark_session_request` `started` with its session id; refused or failed →
   `released`, and skip it this turn. Say what `marked: false` answered. Back to step 4.
6. **Pace.** End the turn: a Monitor line or a finished session wakes you. Ask `/loop` for the
   next tick only as the fallback — 300 s when usage, CPU or memory held the queue, else 1800 s.

## Never

- Start anything but a request you claimed, change its brief, or edit a file.
- Hold a claim you are not starting now, or run a second Manager on this machine: both share
  one `dispatcher` name, and the claim tool cannot tell them apart.
