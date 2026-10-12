# QA report: task views in the desktop harness

Plan `task-views`, entry 10 (`qa-task-views`, `63274cdc-ac42-4558-a3ac-c6c62032e6cb`). Run on
2026-10-07 against `plan/task-views` at `ad1dc200`, which has entries 1 to 8 merged
(#1038 to #1045).

- **App:** `aspire start --isolated` for `D:/tv/63274cdc`, then `desktop-web-harness` at
  1600x1000, driven with Playwright in Chromium using real pointer events.
- **Monitoring:** the harness's structured logs and traces were checked after each scenario
  group. No error or warning entries and no failed traces were found.
- **Evidence:** every evidence path below is relative to
  `.qa-workspace/evidence/task-views-63274cdc/`. That folder is git-ignored by the
  repository's `.qa-workspace` convention, so the files exist only in the QA worktree.

**Result:** 17 passed, 2 failed, 1 not run.

## Scenarios

| # | Scenario | Result | Evidence |
|---|---|---|---|
| H1 | The header reads Inbox, [Tasks \| Board \| Calendar \| Roadmap], Devbook, [In progress \| Sessions \| Pull requests], Dashboard, Tools | pass | `screenshots/02-header-full-order.png` (`01-…` shows the header with the Inbox flag off and no repository scope) |
| H2 | Each view switches, and exactly one view option is selected | pass | `screenshots/03-…` to `07-view-*-with-inbox-devbook.png` |
| H3 | Inbox and Devbook open beside every view (Tasks, Board, Calendar, Roadmap, In progress) | pass | `screenshots/03-…` to `07-…`. Takeovers hide them: `08-…` to `11-…` |
| H4 | A restart reopens the last view (Board) | pass | `screenshots/12-restart-reopens-board.png`; `obj/local-development/shell-navigation.settings.json` read `"lastView": "Board"` before the restart |
| B1 | The Board's columns can be set by Status, Plan, Repository, or Priority, and each header shows its count, points, and how many are unestimated | pass | `screenshots/13-…` to `16-board-columns-by-*.png` |
| B2 | A Ready card dropped on Done is refused, with the reason shown | pass | `screenshots/17-board-ready-over-done-refusal-while-dragging.png` ("Ready can't move straight to Done — start it first"); `18-…` shows the card still in Ready |
| B3 | A Ready card dropped on In progress moves there and gets a Started date | pass | `screenshots/21-board-ready-over-in-progress-tall-viewport.png`, `22-board-in-progress-card-detail-started.png`; `data/db-after-board-drop.json` (`started_on: 2026-10-07`) |
| B3b | The same drop at 1600x1000, with the card low in a long Ready column | **fail**, filed as entry 14 | `screenshots/19-board-ready-over-in-progress-drop-to-start.png`, `20a-board-after-in-progress-drop.png` |
| B4 | Work badges link out | pass | `screenshots/23-board-card-work-badge.png`: `#924` links to `https://github.com/JSdotNet/Backlog/pull/924` with `target=_blank` |
| C1 | An overdue entry is red | pass | `screenshots/24-calendar-overview.png`: chip `task-calendar__chip--overdue`, ink `rgb(236,142,151)` |
| C2 | A done entry is struck through | pass | `screenshots/24-calendar-overview.png`: chip text `text-decoration: line-through` |
| C3 | Dragging a tray entry onto a day sets its due date | pass | `screenshots/25-calendar-tray-drag-over-day.png`, `26-calendar-tray-dropped-due-date-set.png`; the database then read `due_on: 2026-10-20` |
| C4 | Plan bars span weeks | pass | `screenshots/24-calendar-overview.png`: 34 bars, 32 of them carrying the continues-before or continues-after mark |
| C5 | Plan bars hide with "Show plans" | pass | `screenshots/27-calendar-show-plans-off.png`: 0 bars with the box unticked, 34 again with it ticked |
| C6 | Dragging a shelf plan onto a day gives it a window starting that day, and the window appears on the Roadmap | **fail**, filed as entry 13 | `screenshots/28-…` to `30-…` (dropped on 26 Oct, but stored as 7 to 9 Oct because one of its tasks had started; it does appear on the Roadmap). Control with nothing started: `31-…`, `32-…` (stored as 26 to 27 Oct). Data: `data/roadmap-plan-after-shelf-drop*.json` |
| R1 | The Roadmap shows only plans, on its graduated axis | pass | `screenshots/30-roadmap-shows-calendar-dropped-plan.png`: the axis runs days, then weeks (W43 to W45), then months; no task entries are drawn |
| P1 | An entry with a failing-check pull request sits under Needs you | not run | No open pull request in `JSdotNet/Backlog` has a failing check (`gh pr list` returned none). Making one would mean pushing a deliberately broken branch |
| P2 | An entry with a Stalled ("Quiet 30 min") linked session sits under Needs you, and its badge is gold | pass | `screenshots/35-in-progress-session-linked-under-entry.png`, `36-board-card-stalled-session-badge.png`, `37-tasks-row-stalled-session-badge.png` (`integration-link--session-stalled`, ink `rgb(242,193,78)`) |
| P3 | "Link to a task…" moves an unlinked session under its entry | pass | `screenshots/33-in-progress-view.png` (before: 7 unlinked), `34-in-progress-link-to-a-task-picker.png`, `35-…` (after: 6 unlinked, and the session sits under the entry) |
| K1 | Turning Colors off removes repository colour from every view and leaves status colours | pass | `screenshots/38-colors-A-*.png` (off) and `39-colors-B-*.png` (on) for Tasks, Board, Calendar, Roadmap, and In progress. With Colors off, no `repo-mark` classes are present. The computed status-badge and status-dot colours match in both states |

## Entries filed for failures

- `8f23f087-7051-4d28-aa47-bb20a92e7e45`, **13: Start a shelf plan's window on the day it is
  dropped even when its work has started** (C6).
- `f64b1c19-e2a4-4a11-9132-1d8cb57ba391`, **14: Let a Board drag reach a column that is
  scrolled out of view** (B3b).

## Observations not filed

None of these breaks a scenario above. They are left for the plan review (entry 11) to weigh.

- The Devbook option appears only while a repository scope chip is selected. The scope is not
  remembered, so after a restart the header shows no Devbook option until a chip is pressed
  again (`screenshots/12-restart-reopens-board.png`).
- The Started date is stored but is not shown anywhere in the detail panel
  (`screenshots/22-…`).
- A session linked through "Link to a task…" has no unlink control in the detail panel or in
  the In progress view.
- In progress shows "Couldn't read acme/web … acme/api" above the view. The harness registers
  those fixture repositories, and they do not exist on GitHub (`screenshots/33-…`).
- Environment only: `ui-storybook`'s first start failed with CS2012, because a parallel build
  held the DLL lock. Restarting `desktop-web-harness` from Aspire crashed MSBuild (MSB4166).
  A plain start brought both up again.

## What could not be proved

- **The real desktop (MAUI/WebView2) head.** Every drag ran in the web harness, in Chromium
  with real pointer events. The `desktop` resource was not started.
- **A failing-check or changes-requested pull request under Needs you** (P1). There was no such
  pull request to link.
- **A Running session badge.** Only a Stalled session was linked.

## Shared data

The harness's workspace (`%LOCALAPPDATA%\Backlog.Debug\settings.json`) points its task
database at
`D:\Repos\Backlog\.claude\worktrees\generated-knowledge-database-1509ba\.qa-workspace\backlog.db`.
Every worktree's harness writes to that database.

**Written:** only entries this run created, all titled `QA scratch …`.

- `+qa-task-views-scratch`: B, C, D, and E.
- `+qa-task-views-scratch-two`: F.
- A: a blank entry made by "+ New entry", with no plan tag.

**Restored:**

- All six scratch entries are archived.
- Both scratch roadmap items were deleted. The roadmap document then matched the snapshot
  taken before the run: `data/roadmap-plan-before.json` against
  `data/roadmap-plan-after-cleanup.json`.
- The session link lives on the archived entry B. It was not removed, because the app offers
  no unlink.
- Colors was off at the start of the run and is off again.
- The Inbox flag lives in this worktree's own `obj/local-development/feature.settings.json`.
  It was turned on for H3 and turned off again.
