---
name: dispatch-sessions
description: "Run this machine's Manager session — the dispatcher of local ADR 0038 that turns each session request the Backlog app queues (a Start session click on an entry or a plan) into a Claude Desktop session in the request's clone folder, in its own worktree, with the app's fixed brief as its whole prompt, and hands each answer the app accepted for a waiting session back to that session (local ADR 0041). Waits on the app's long poll through a Monitor, delivers answers first, starts a request only while fewer than N dispatched sessions work and plan usage, CPU and memory leave room — as the app's check-in answer reads them against the limits it sends, measuring a figure itself only when the app cannot — and reports each start and delivery back. With the queue empty and room left, it also starts one fixer session, on a fixed brief, for each bot-opened pull request whose settled checks fail and that no session watches yet (local ADR 0038 §6). Started once as /loop /backlog-tools:dispatch-sessions in a pinned session; changes no file and is no flow. Invoke it only when the person's own /loop of it calls for it, never on your own initiative: /loop runs it through the Skill tool, so it stays model-invocable."
argument-hint: "[sessions=3] [usage=80] [cpu=80] [memory=4] [mend=<clone folders>|off]"
---

# Dispatch session requests

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

You dispatch and deliver, never run work or write a brief or an answer: each start is a click in the app or a bot's red pull request, each answer one it accepted (ADRs 0038, 0041).
Arguments [defaults]: `sessions` [3] working, `usage` [80] plan %, `cpu` [80] average % over 30 s, `memory` [4] free GB, `mend` [this session's git checkout]
clone folders, comma-separated, whose bot pull requests to mend, or `off`. Paths are from this folder. The six `*_session_request(s)` and `*_deliver(y|ies)` tools take
the Manager grant (local ADR 0058), which `backlog` does not send: in Bash, `node assets/manager-call.mjs <tool> '<JSON arguments>'` (PowerShell 5.1 strips inner quotes) prints the result or exits non-zero with why; the rest use `backlog`.

## Each turn

1. **Check in** per `../../resources/standing-session.md#check-in`, `sessionId` `${CLAUDE_SESSION_ID}`, `state`
   `holding` if a limit held the last queue, `heartbeatMinutes` step 9's last pace (30 at first), `sessions:` step 6's count,
   and the limits `maxSessions` `sessions`, `maxUsagePercent` `usage`, `maxCpuPercent` `cpu`, `minFreeMemoryGb` `memory`.
2. **Wait.** `node assets/wait-requests.mjs --name` prints your `dispatcher`. One Monitor, longest timeout, re-armed on expiry or
   `stopped`: `node assets/wait-requests.mjs --session ${CLAUDE_SESSION_ID}`, each wait a check-in; never loop on tools.
3. **Restart.** First unpaused turn only: mark this session per `../../resources/standing-session.md#mark`; `released` (`mark_session_request`, `mark_delivery`) each your `dispatcher` holds in either list's `state: claimed`.
4. **Deliver** each `list_deliveries` `state: pending` with a `desktopSessionId`, oldest first: `claim_delivery` (id, `dispatcher`),
   `claimed: false` → next; `send_message` to that id: "You asked: <question>. The answer from Backlog: <answer>", both verbatim.
   Sent or queued → `mark_delivery` `delivered`; the session no longer exists, or any other failure → `released`.
5. **Queue.** `list_session_requests` `state: requested`, in the order listed — the person's queue order, never reordered; empty
   → step 8; `paused: true` (every claim is refused until the person resumes it) → step 9. Any other Monitor line than `delivery`, `request`, `ready` → say it once, step 9.
6. **Capacity**, before each start: hold the queue on step 1's answer's `capacity` `hasRoom: false`, or once its `workingSessions`, plus starts
   since it and your running `CI fix` sessions (the app counts no fixer), reach `sessions`. Measure each figure it lacks (absent or `null`), or all with no `capacity`; first failing holds:
   - `workingSessions`: fewer than `sessions` rows of `list_sessions` (`linked: true`, `limit: 100`) with `startedBy` your id
     (`get_session` `self`) and `isRunning: true`, not counting a `desktopSessionId` `backlog`'s `list_sessions` reads `Waiting`;
   - `planUsagePercent`: every plan limit `get_usage` reports under `usage` percent (`not_applicable` passes);
   - `cpuPercent`, `freeMemoryGb`: `powershell -NoProfile -ExecutionPolicy Bypass -File assets/capacity.ps1 -MaxCpu <cpu> -MinFreeGb <memory>` prints `"fits":true`.
7. **Dispatch** the first listed: `claim_session_request` (id, `dispatcher`), `claimed: false` → next. `start_session`: `initiation: user_asked`, `context: fresh`,
   `cwd` its `cloneFolder`, `use_worktree: true`, `prompt` its `brief` verbatim, `wake_when_done: true`, `title` the entry's `read_item` title or plan's `+tag` (<60 chars),
   `background` "Started by dispatch-sessions for Backlog session request <id>." Failed → `released`. Started → `mark_session_request` `started` with its id, even past the lease;
   `marked: false`, another holds the claim → never release: say both sessions started for <id>, naming yours and the holder's, so the person stops one. → 6; none left → 8.
8. **Mend** (ADR 0038 §6): Write Claude Desktop's `list_sessions` (`include_archived`, `limit: 100`) rows' `title`, `prNumber`, `isArchived` to a scratch
   JSON file; per `mend` folder `node assets/red-bot-prs.mjs <folder> --sessions <file>`. Each line with a `brief`: step 6, then `start_session` as step 7, `cwd`
   the folder, `prompt` and `title` its `brief` and `title` verbatim, `background` "Started by dispatch-sessions to mend <repo>#<number>." Else say a `gave-up` or error once.
9. **Pace.** End the turn. `/loop` is only the fallback: 300 s if a limit held the queue, else 1800 s.

## Never

- Start or send what you did not claim or step 8 did not print, change a brief or an answer, merge a pull request, edit a file, or run a second Manager here: both would share one `dispatcher` name, which the claim tools trust.
