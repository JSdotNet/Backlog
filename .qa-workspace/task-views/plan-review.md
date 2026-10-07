# Plan review: task views

Plan `task-views`, entry 11 (`review-plan`, `7b23cc17-2a97-4af4-8394-f96c597698a2`). Run on
2026-10-07 against `plan/task-views` at `9b8910b6`, with entries 1 to 10 merged (#1038 to #1047).
This was an unattended `execute-plan` run.

**Method.** I read the five artboards of the design canvas
(https://claude.ai/artifact/PAE1X9m7f2BhvsUtQqakxi, version `1791401044-3c3c`) as source. I read
each entry with `read_item`, each pull request body with `gh pr view`, and the QA report
(`qa-report.md`, beside this file). Then I checked the open points against the code on
`plan/task-views` with grep.

**Result.** Entries 1 to 10 have all landed in `plan/task-views`. No step was dropped. The
leftovers are small: one documentation clean-up, two batches of code gaps, a list of design
decisions for the person, and a list of hand checks. I filed five entries (15 to 19). Entries 13
and 14, QA's two failures, are already being fixed, so I did not file them again.

## Artboards checked

| Artboard | Landed | Outstanding |
|---|---|---|
| Tasks — list and details | Header view switch and groups (1), work badges with review and session state (3, 4), "Sessions and pull requests" section with "Link a session or pull request…" and "Open in In progress" (8) | The detail panel has no `Started` row, although the app stamps `started_on` (16). The view options have no icons (18) |
| Board | Columns picker (Status, Plan, Repository, Priority) with counts and points, TaskCard, drag only by Status with the refusal reason, "+ New entry" per Status column (2, 5) | "Drop to start" is missing its second line, "Moves to In progress and stamps Started" (16). Space on a focused card also scrolls the column (16). Drag reach when a column is scrolled out of view is entry 14 |
| Calendar with plans | Month grid, month bar, today ring with the My Day count, overdue and done chips, "+N more", both trays, plan bars in lanes, milestones, "Show plans" (6, 7) | The Month/Week range and "Place tasks by" were scoped out by entry 6 and are listed for a decision (18). A Calendar drag has no edge autoscroll (16). The shelf drop with started work is entry 13 |
| Roadmap — plans, graduated axis | Main view, Planning row, graduated axis, plans only, shelf, milestones (1) | None |
| In progress — by task | Needs you, Moving and Not linked sections; both toggles; session and pull request cards with "None yet"; "Link to a task…" (8) | No unlink; the "No repository" scope is ignored; focus is lost after a link; a column shows "None yet" when its feature is off; a merged or closed pull request opens on a list that does not show it (17). The toggles are not remembered (18) |

The design's "Waiting for you" session state was left out on purpose: entry 4 maps the "needs
you" state to `Stalled` ("Quiet 30 min"). I did not count it as a gap.

## Entries checked

| # | Entry | PR | Done in `plan/task-views` | Left open (filed as) |
|---|---|---|---|---|
| 1 | Header view switch | #1040 | yes | Icons (18). Real-file migration not proved (19). Devbook follow-up done by entry 9 |
| 2 | TaskCard | #1039 | yes | Board-card design chapter (15). Space key (16). Screen reader (19) |
| 3 | Review state | #1038 | yes | Live `reviewDecision` read under a fine-grained token (19) |
| 4 | Badge states | #1041 | yes | Running badge not seen live (19) |
| 5 | Board | #1044 | yes | Slot second line (16). WebView2 drag (19). Drag reach is entry 14 |
| 6 | Calendar | #1042 | yes | Autoscroll (16). Week view and "Place tasks by" (18). WebView2 drag (19) |
| 7 | Calendar plans | #1043 | yes | Shelf drop with started work is entry 13. WebView2 drop (19) |
| 8 | In progress | #1045 | yes | Unlink, scope, focus, one feature off, merged pull requests (17). Toggles remembered (18) |
| 9 | Devbook capture | #1046 | yes | Three stale code comments (15). ADR 0022 still `proposed` (18) |
| 10 | QA | #1047 | yes, 17 passed / 2 failed / 1 not run | Failures are entries 13 and 14. P1 not run (19). Its observations went to 16, 17 and 18 |

On entries 1 to 10, the sub-item "Update Backlog's own knowledge docs / devbook once this prompt
lands" still reads open, and no tool can tick one. Entry 15 hands the person the list to tick.

## Entries filed

| # | Id | Type | Effort | Title |
|---|---|---|---|---|
| 15 | `522d485b-a61a-435c-9d46-79f5de74568d` | prompt | 2 | Clear the task views plan's documentation leftovers |
| 16 | `68e4a081-824f-4069-b128-cfc9ab8cac77` | prompt | 3 | Close the task views' small gaps against the design |
| 17 | `f3af23d5-4088-4c0f-98f2-82c0559ea9cb` | prompt | 5 | Round off the In progress view's linking and scope |
| 18 | `e35598d0-4db8-4199-8ead-eb27f3079ea8` | task | 1 | Decide the task views' deferred design pieces |
| 19 | `49a57378-f4bb-4429-b223-e6251fb1815e` | task | 2 | Check the task views by hand in the desktop app |

None of them carries `after:`. Entry 16's Calendar autoscroll uses the same file as entry 14, so
its text says to reuse entry 14's approach if that has landed, but it does not depend on it.

## Assumptions

- **Scoped out on purpose still counts.** Entry 6 scoped out the Calendar's Week range and "Place
  tasks by". The review still lists them, as decisions in entry 18, not as code work. Building
  them would go beyond what this plan agreed.
- **No new feature for "unlink".** The design draws no unlink control. Entry 17 adds one because
  QA's cleanup could not undo a link, and linking with no undo is a half-finished feature.
- **Pre-existing behaviour goes to a decision.** I did not check whether the Devbook option was
  always gated on a scope chip, so it is listed as a decision rather than a bug.
- **Efforts are my own estimates.** I sized them against the plan's existing entries; I did not
  run the `estimate` skill.
- **Nothing was run.** This review read the code, the pull requests and the canvas. The
  application was not started, because entry 10 already did the QA.
