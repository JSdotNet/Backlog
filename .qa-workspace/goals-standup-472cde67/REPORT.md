# QA report: Goals and Stand-up re-validated with no flag (goals-standup 472cde67)

Plan entry `472cde67-c902-4247-aa1d-ce8be4221462` of `+goals-standup`. An unattended `execute-plan` run did
it on 2026-10-17 against `plan/goals-standup` at `a51edf31`. That commit holds entries 1–14 and
#1370–#1385, so the `goals` flag is gone.

## What was checked

- **The app.** The Aspire AppHost was started `--isolated` from `D:/gs/472cde67`. The first start
  failed because Docker Desktop was not running, so `cosmos` and `storage` could not start.
  `sync` also exited on a transient `CS2012` file lock. Docker Desktop was then launched and
  this AppHost was stopped and started again. After that, `sync`, `cosmos` (with its `backlog`
  database and containers), `desktop-web-harness`, `mobile-web-harness` and `ui-storybook` all
  reached Running and Healthy. No container was removed.
- **No flag.** Settings → Features lists 19 switches and none of them is `goals` (`screenshots/02`).
  Only `sync` was turned on, and it was turned off again at the end (`screenshots/06`).
- **A real pairing.** The phone was paired with a real code, against this run's own `sync`.
  - The phone was the mobile harness's own build, started by hand from `bin/` with
    `Harness__SeedStandup=false`. It used its own credential and outbox under
    `.qa-workspace/fresh-phone2/`, so it held no seeded stand-up (see Assumptions).
  - The desktop registered, generated a code, and the phone redeemed it.
- **The round trip.** Desktop ⇄ sync ⇄ phone ran live throughout.
- **The pull request.** No open pull request existed, so a scratch draft, **#1386**, was opened
  against `main` from `goals-standup/qa-scratch-472cde67`. It held one trivial commit and was never
  marked ready. Once the check was captured it was closed and its branch deleted with
  `gh pr close 1386 --delete-branch`.
- **Evidence.** The desktop and the phone were driven with the Playwright MCP. Evidence is a
  numbered **screenshot sequence**: there is no video and no trace.
- **Logs.** Aspire logs and traces were read for `desktop-web-harness` and `sync`, and the phone's
  own log was read too.

Evidence root (untracked, in the worktree `D:/gs/472cde67`):
`.qa-workspace/evidence/goals-standup-472cde67/`.

## Scenarios

| # | Check | Result | Note | Evidence |
|---|---|---|---|---|
| S1 | Create a goal | PASS | "QA472 goal", due 27 Nov, reads 0/0 · On track | `sequence/s1/02`–`03` |
| S1 | Attach a plan from the Goals view search | PASS | `qa472-plan` → 0/2. A plan already on another goal opens the shared goal picker, which says "Attaching moves this plan … from QA12 goal". That attach was cancelled | `sequence/s1/04`–`06` |
| S1 | Attach a task from the search | PASS | "QA472 loose task" → 0/3 | `sequence/s1/07` |
| S1 | Attach from the task panel | PASS | Shared picker. The panel then reads GOAL QA472 goal. **"Sessions and pull requests" is back** in the task panel | `sequence/s1/08`–`10` |
| S1 | Attach from the roadmap item | PASS | "QA472 road" → 0/5, and the plan counts its `#qa472-road` entry (0/1) | `sequence/s1/11`–`14` |
| S1 | Attach from the PR detail head | PASS | #1386 → 0/6 | `sequence/s1/15`–`16` |
| S1 | Each item counts once | PASS | Attaching a task its attached plan already holds keeps 0/6 | `sequence/s1/17` |
| S1 | Detach, edit and delete on the Goals view (#1381) | PASS | Detach (×) removes a task. A title edit reaches the phone. Delete asks first, and then the goal is gone from the desktop and, after sync, from the phone | `sequence/s1/18`–`23` |
| S2 | Day / Week / 2 weeks / Month | PASS | 16 Oct → 19 Oct; 12–16 Oct → 19–23 Oct; 28 Sep–11 Oct → 12–25 Oct; 1–30 Sep → 1–31 Oct, the same as entry 12. The first 2 weeks opened the **Two-week start** dialog (#1382), defaulting to 12 Oct, which was kept. Finished and Planned carry their counts, and Planned carries the points tag ("11 / 3 pts") | `sequence/s2/01`–`04`, `03b` |
| S3 | Block a task → a New impediment | PASS | "QA472 block task · New · Not reported yet · 0 days · QA472 goal" | `sequence/s3/02`–`03` |
| S3 | Desktop: Reported → Escalated → Being handled → Resolved | PASS | The picker offers only the allowed moves: New/Reported/Resolved, then Reported/Escalated/Being handled/Resolved, then Escalated/Being handled/Resolved. Resolve clears the row | `sequence/s3/04`–`06`, `11` |
| S3 | Phone: change state, seen on the desktop | PASS | A second impediment ("QA472 loose task") went New → Reported on the phone. The desktop then reads Reported · 0 days | `sequence/s3/09`–`10` |
| S3 | Desktop Resolve, seen on the phone | PASS | The phone reads "Resolved 17 Oct" and keeps the check-again (21 Oct) and the note | `sequence/s3/12`–`13` |
| S4 | Open PR: checks and approvals marks | PASS | #1386 showed the same marks on the PR pane row, the goal card and the phone goal page: a running mark (1/5, then 3/9, 9/10), then a green passed mark (10/10), and the approvals mark 0. The screen-reader text is "0 approvals, none required", which is right: `main` has only a required-status-checks rule. No visible "checks" or "approvals" words | `sequence/s4/01`–`07`, `c3/04` |
| S5 | Copy copies the blocks as plain text | PASS | `writeText` was intercepted: one plain string with CRLF line ends. It holds Goals, Finished, Planned and Impediments | `sequence/s5/01`, `logs/01-s5-copied-clipboard-text.txt` |
| C1 | A phone note shows on the desktop row | PASS (#1372) | The phone set the note and the check-again date. After sync the desktop row reads "check Wed · QA472 goal", then the note, then "Edit note" | `sequence/c1/01`–`02` |
| C1 | A desktop note edit reaches the phone | PASS | The desktop edited the note through "Edit note". After sync the phone's note field holds the new text | `sequence/c1/03`–`05` |
| C2 | Being handled, with nobody named, reads no "Not reported yet" | PASS (#1371) | The desktop row reads "0 days · QA472 goal". Copy reads "QA472 block task · Being handled · 0 days · QA472 goal". The phone row reads "0 days · QA472 goal". Reported reads "0 days". Only New reads "Not reported yet" | `sequence/s3/06c`, `s3/07`, `logs/01` |
| C3 | A `#tag` entry counts on a goal, on the desktop and on the phone | PASS (#1373) | "QA472 hashtag entry" carries `#qa472-road`. The roadmap item gathers it "by tag". On the desktop its plan reads 0/1, then 1/1 once the entry is done, and the goal goes to 1/6. After sync the phone goal page reads Qa472 road 1/1 and the goal 1/6 | `sequence/s1/14`, `c3/01`–`04` |
| C4 | Open PR shows the checks and approvals marks | PASS | See S4 | `sequence/s4/` |
| C5 | A fresh phone shows "No stand-up yet" until a desktop with a goal syncs | PASS (#1374) | The phone was paired before the desktop's first push. It reads "No stand-up yet — A desktop sends the stand-up once it has a goal and has synced", and its tab bar holds no Stand-up tab. One Refresh after the desktop synced, the stand-up arrived, and the Stand-up tab appeared with its target icon (#1376) | `sequence/c5/03`–`04` |
| — | Ask AI on the Goals view (#1380) | PASS (offered only) | The header's Ask AI opens "Ask about this content — Answers from the Goals content". No question was sent | `screenshots/04` |
| F1 | A roadmap item planned since the page loaded is offered by the Goals view's attach search | **FAIL** | `qa472-stale` was planned on the Roadmap. The Goals view's search does not list it, even after a wait, until a full reload → entry `dff2eaf7` | `sequence/f1/01`–`03` |

## Failures written up as new entries of `+goals-standup`

| Entry | Defect | Severity |
|---|---|---|
| `dff2eaf7-e84b-4f2b-ab8d-148aaf55e9de` | Offer a roadmap item in the Goals view's attach search as soon as it is planned. `GoalsCandidateSource` reads `TasksDesktopState.RoadmapTags`, which only a reload refreshes | Low |

Entry 12's four defects (`376b2a93`, `3dda5231`, `5b6cce3c`, `fdf64d94`) did not recur.

## Other findings (not written up)

- **The first fresh-phone attempt lost the race.** The desktop pushes within seconds of registering.
  A phone paired in that window saw the stand-up straight away. A second fresh phone, paired about
  0.4 s after registering, caught the "No stand-up yet" state (`sequence/c5/02`, `03`).
- **The phone title-cases a plan tag.** The desktop reads `qa472-plan`, and the phone goal page reads
  "Qa472 plan". This looks deliberate.
- **The checks mark counts skipped as passed.** "10 of 10 checks passed" while GitHub shows six
  passes and three skips. This matches the storybook's description of the mark.
- **Weekend completions** are again outside every Day and Week window, as entry 12 noted.

## Logs

- **desktop-web-harness:** no exceptions in the console log.
- **sync:** no exceptions.
  - One Warning: a session sync with no bearer token, in the instant between "Forget this
    device" and registering again.
  - The error-flagged traces are 404 read-before-create lookups and that 401.
- **The fresh phone's own log:** no errors.
- **Excerpt:** `logs/02-aspire-logs-and-traces.txt`.

## State changed and restored

- **Restored:**
  - `sync`: off → on → **off**.
  - The iteration is back on Week, and the PR pane is still on Open / Mine.
  - The two tasks blocked for S3 are unblocked again.
- **Scratch PR:** #1386 is **closed** and its branch `goals-standup/qa-scratch-472cde67` is deleted.
- **Deleted on purpose (the #1381 check):** the goal "QA472 goal".
- **Left in place in the shared debug workspace (`%LOCALAPPDATA%\Backlog.Debug`):**
  - **Tasks:** "QA472 hashtag entry" (done, `#qa472-road`), "QA472 plan task one" and "QA472 plan
    task two" (`+qa472-plan`), "QA472 loose task", "QA472 panel task" and "QA472 block task".
  - **Roadmap items:** "QA472 plan", "QA472 road" and "QA472 stale".
  - **Impediments:** two resolved ones.
  - **The two-week start** is set to 12 Oct 2026.
  - Plan entry `087ce80e` covers clearing QA data like this.
- **Other worktrees' data** (QA goal A/B, QA e23ff6ba, QA12, QA ddd65d4c) was not touched. The one
  move the shared picker offered from QA12 goal was cancelled.
- **The desktop harness's registration.** It was registered, forgotten, and registered again. It
  now pairs with a second owner on this run's own Cosmos. The credentials sit in this worktree's
  `obj/local-development`.
- **The phone credentials and outboxes** sit in `.qa-workspace/fresh-phone/` and `fresh-phone2/`.

## What could not be proved

- **The Aspire-run `mobile-web-harness`.** It was not used for the phone checks: its development
  config always seeds a stand-up, so it can never show "No stand-up yet".
- **The copied text pasted into Teams or Outlook.** `writeText` was intercepted; the system
  clipboard was not read.
- **An approvals mark that requires approvals** (such as 1/2). `main` requires none, so only the
  "0, none required" form was seen live.
- **The Ask AI answer for the Goals content.** No question was sent.

## Assumptions

- **The fresh phone.** The phone checks used the mobile harness's own build, run by hand with
  `Harness__SeedStandup=false` and its own credential and outbox paths. It pointed at this run's
  `sync`. The Aspire-run harness seeds a stand-up in Development, which would have masked "No
  stand-up yet".
- **Docker Desktop.** Docker Desktop was launched because it was not running. This worktree's
  AppHost was then stopped and started again. No other worktree's containers were touched, and no
  stale container had to be removed.
- **The two-week start.** The dialog's default of 12 Oct was saved, which keeps entry 12's windows.
- **Where screenshots were taken.** The Playwright MCP may write only under the session's own
  checkout, so screenshots were taken there and copied into this worktree's evidence folder.
- **The gates.** The repository declares an `approval` gate before create-pr, set to block when
  unattended. This draft pull request stands in for it, and the run is parked, not self-approved.

An unattended `execute-plan` run opened this pull request.
