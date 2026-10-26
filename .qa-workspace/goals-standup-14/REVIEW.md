# Review of plan `+goals-standup`: what is missing

Plan entry 14, `review-plan` (`f545edea-9843-4230-aee9-c7f6710fb3de`). An unattended `execute-plan` run did this review on 2026-10-17, against `plan/goals-standup` at `1cb6b2b5` (after #1374).

## What was reviewed

- **The design:** https://claude.ai/artifact/7SNYvNcLEPTQnxwMjWDQN3. All four artboards were read: Desktop, phone Main, Goal and Impediment.
- **The decision and the domain model:** local ADR 0035, and `.devbook/domain/progress/` (context, domain, invariants, features, requirements). The Sync and design chapters were read where they touch Progress.
- **The pull request bodies**, #1359–#1374. For each one: its "What could not be proved", "Assumptions" and "Review findings left open".
- **The 19 plan entries and their closing comments**, from `get_plan_items +goals-standup`.
- **The code on the plan branch**, checked with `git grep` for each claim. The checks covered:
  - sprint leftovers;
  - storybook pages added and removed;
  - the attach control on all three places, and the status line on every PR row;
  - the Goal port against the UI;
  - Ask AI sources;
  - the phone pages and the tab bar.

## Combined build and tests

#1371–#1374 merged without a combined run, so the whole plan branch was built and tested once here.

| Check | Result |
|---|---|
| `dotnet build Backlog.sln` | 0 errors, 2 warnings. Both are NU1902 for MessagePack 2.5.302 on the AppHost, which was already on the base. |
| `dotnet test Backlog.sln` | **17,511 total: 17,496 passed, 0 failed, 15 skipped**, across 66 test assemblies. The 15 skipped are the browser end-to-end tests, which need `BACKLOG_E2E`. |
| `node .devbook/_tools/devbook-meta/build.mjs --check` | exit 0 |
| `node tools/devbook/check-metadata.mjs` | 0 blocking, 475 advisory |

## Every entry done?

Each of entries 1–13 and the three QA follow-ups has a merged PR on the plan branch. The table below covers what each scope asked for and did not fully get.

| Area | Landed | Left over (finding) |
|---|---|---|
| Phone pages (11) | Stand-up, Goal and Impediment pages; harness seed; `StandupPage` story; shared status line swapped in | No target icon on the tab (F6). A refused later queued move goes undetected (F7). The real round trip has passed live only once (F8). |
| Attach buttons (8) | Task panel, roadmap editor, plan shelf, PR detail head, work-link cards; `GoalPickerPage` | The Goals view keeps its own duplicate attach parts (F4). No detach control (F2). |
| Impediment states and history (3) | Forward-only states, dated history with who, check-again, note, blocked → New; MCP tools; phone stepper | The desktop has no page showing history or holds-back, no manual raise, no task link/unlink, and does not ask who (T6). No two-desktop sync test (F9). |
| PR checks and approvals (5) | Required count from protection rule or ruleset; status line on Goals, stand-up, phone, PR pane, Sessions pane | Not on work-link cards or the task panel's linked work. The pane's list read never fills `Required` (F5). |
| Sprints retired (9) | Model, UI, storybook pages, `progress` flag and brief block are gone; `sprint-series` is dropped on read | `progress_series` is kept, because ADRs 0006/0014 forbid the drop (T1). Stale sprint and In progress traces remain in code (F11) and in the chapters (F1). |
| Storybook pages | Added `GoalPickerPage`, `GoalsViewPage`, `PullRequestStatusPage`, `StandupPage`. Removed `SprintViewPage`, `SprintPlanningPage`, `SprintSettingsPage`, `InProgressPage` | `IntegrationsWorkPage` still shows "Open in In progress" (F11). The design page map lists none of the new pages (F1). |
| Goals view (7) | Header, switch, Copy, goal cards, No goal, stand-up aside | Goal edit and delete, and detach (F2). Goals heading count, planned effort in points, and the two-week start prompt (F3). R7 lists Finished flat (T2). No Ask AI source (F10). |
| Devbook | ADR 0035 and the Progress chapters track most changes | Several chapters are still behind or ahead of the code (F1). The ADR is still `proposed` and the chapters `draft`; sign-off is entry 15. |

## Findings and the entries written for them

All the new entries are `after:f545edea-9843-4230-aee9-c7f6710fb3de` in `+goals-standup`.

### Prompt entries: work an AI session can do

| # | Entry | Finding |
|---|---|---|
| F1 | `4082a90d-219c-4588-be7e-9c5c8e75ec25`: Bring the Progress and Sync chapters level with what landed | See the list below this table. |
| F2 | `ddd65d4c-9294-4375-8826-04c48b1bdf84`: Edit, delete and detach on the desktop Goals view | `IGoals.EditAsync`, `DeleteAsync` and `DetachAsync` exist, but no screen calls them. `features.md#setting-a-goal` promises them. QA goals cannot be removed. |
| F3 | `b16c86a3-33a2-4338-a38e-64e63c2f24d2`: Show the stand-up counts and planned points on the desktop, and ask for the two-week start | The Goals heading has no count (R6). The desktop shows no planned item's effort, though the phone does. Choosing 2 weeks never asks for the start day. |
| F4 | `a69b104e-8ae6-4973-927c-aa781c7771c8`: Use the shared goal picker in the Goals view | #1366 and #1367 duplicated the attach menu, the move confirm and the New goal dialog. `GoalsKeys` uses string keys, where #1366 has `GoalSubject`. |
| F5 | `3f0a21ad-e35d-43e8-bc11-49dbed5a2a21`: Show the pull request status line on work links, and read required approvals on the pane | `WorkLinks` and the task panel's linked work draw no `PullRequestStatus`. The pane's slim read leaves `Required` null. |
| F6 | `3676cde4-1e45-4203-8eee-f06247b4c9b7`: Give the phone's Stand-up tab its target icon | `TabBar` is text only. Entry 11 asked for the target icon. |
| F7 | `0dc56ad4-0ae5-4187-8685-b048f0bb532b`: Detect a desktop refusal of a later queued phone move | The partial-chain rule never sees a refused second move. Two lose-an-edit edge cases from #1365 have no test. |
| F8 | `472cde67-c902-4247-aa1d-ce8be4221462`: Re-validate Goals and Stand-up end to end now the flag is gone | #1371–#1374 were never driven live end to end. The phone ↔ desktop round trip has passed once. Open-PR marks were never seen live (S4 partial). |
| F9 | `f235e33a-bb17-4e92-a59a-f6ee3393ce81`: Prove impediments sync between two desktops through the sync service | `GoalSyncBetweenDevicesTests` has no impediment twin. #1363 proved impediments against a stubbed service only. |
| F10 | `1b515b53-c84c-44b0-bca0-ee0c9d1deecd`: Offer Ask AI on the Goals view | `Home.razor:1150` maps the Goals view to `goals`, but no `IAiContentSource` has that key, so Ask AI is not offered there. |
| F11 | `6734732c-4fed-44f1-ac48-157083f5ca56`: Remove what the In progress view and sprints left behind in code | `WorkLinksPanel.OnOpenInProgress` and its storybook use remain, along with In progress doc comments and the sprint comment in `StorybookIndex.cs:187`. |

The chapters F1 covers:

- `sync/context.md:98` still lists "sprint series".
- `progress/features.md:228` says "behind the same goals switch".
- The Goal and Goal Assignment invariants have no `tests:`.
- `domain.md#goal-assignment` does not describe the opt-in move.
- `requirements.md:250` refuses an attach that features.md and the code move on yes.
- Sync promises an impediment delete that does not exist.
- The design page map is missing the Progress pages.
- The archify context-map label still says "open sprint".

### Task entries: decisions only you can make

| # | Entry | Decision |
|---|---|---|
| T1 | `613949ea-504f-4a27-abcb-6c9795c3b4ea`: Decide whether to drop the progress_series table | Keep the table unread, or write an ADR that allows the one-way drop. |
| T2 | `9d8bb2a4-bd3f-49b8-8cd8-e9e49a4399a6`: Decide how Finished reads on the desktop stand-up (R7) | Add goal headings to the aside, or reword R7. |
| T3 | `b6a6ac52-69fd-4baa-8e6b-0a55404a537d`: Archive the moot "Withdraw the phone Stand-up when goals is switched off" | `376b2a93` is moot since #1374. |
| T4 | `6cdb0ced-f449-4d37-84bf-a3ad757ba123`: Decide where the iteration choice lives | The chapter says `scope: user`. The desktop keeps the choice per device, and the phone keeps it per visit. |
| T5 | `6110d2f1-ee1f-4cea-8208-5dcd4fc535dd`: Decide how goal counting treats its edge cases | Archived and draft entries, a PR counted in two goals, offline double attach, No goal finished by merged PRs, status against count, the shelf count against the goal count. |
| T6 | `3b0ebdf4-9972-44e3-ad55-e115c520dc07`: Decide on a desktop impediment page and the design details left out | The desktop impediment page, a manual raise and task linking, asking who, the impediment's PR reference, "your review", HTML Copy. |
| T7 | `ca20bbaa-3792-4f17-872c-406283307bd5`: Resolve the design-chapter conflicts the phone pages raised | Five conflicts #1368 listed, including the spark contrast below 3:1. |
| T8 | `087ce80e-96aa-4e75-916f-2f83ed131ce6`: Clear the QA goals and impediments from the shared debug workspace | QA goals and impediments in the shared workspace, a leftover pairing, and queued outbox edits. |

### Not written up as entries, and why

- **Known leftovers already reported, now confirmed.** Everything in the orchestrator's list is covered by F1–F11 and T1–T7.
- **`progress/context.md:42-43` no longer describes sprint code.** #1374's devbook commit rewrote it. Only `sync/context.md:98` is still stale.
- **Blocked tasks that predate the upgrade raise their impediments only at the next task write** (#1363, #1370). This is how `BlockedTaskPolicy` is defined to run, and no chapter promises otherwise.
- **The task side panel's "Sessions and pull requests" section came back** (#1374). That is a question for #1374's own Personal Validation, which entry 15's sign-off covers.
- **A Release `-warnaserror` build with the NuGet audit on fails** on NU1902 (MessagePack 2.5.302 in the AppHost). This predates the plan and is outside its scope; every PR in the plan built with `-p:NuGetAudit=false`.
- **ADR 0035 is still `proposed`, and the Progress chapters are still `draft`.** Accepting them belongs to entry 15, "Sign off the Goals and stand-up plan".

## What could not be proved

- **The running app.** This review started no application and drove nothing in a browser. Each finding comes from the chapters, the PR bodies and `git grep` over the plan branch. F8 is the entry that drives it live.
- **The MAUI heads at runtime.** `dotnet build Backlog.sln` built them, but none was launched.
- **The design canvas.** It was read as its four artboard sources. Nothing in it marks what was meant as illustration rather than scope, so the artboard details that did not land are raised as decisions (T6), not as defects.

## Assumptions

- **A1. One entry per decision.** Work an AI session can do became a `prompt` entry. Anything that needs a person's choice became a `task` entry, even where a likely answer exists.
- **A2. F1 aligns with the code.** For the attach rule, F1 tells the session to align `requirements.md` with `features.md` and the code, which name the other goal, ask, and move on yes. Both of those already agree, so the requirement is the outlier. If you want a refusal instead, revise F1 before it runs.
- **A3. No duplicates.** No new entry duplicates one the plan holds. The four QA follow-ups (`376b2a93`, `3dda5231`, `5b6cce3c`, `fdf64d94`) are landed, apart from the moot `376b2a93`, which T3 archives.
- **A4. Personal Validation is parked.** The `create-pr` gate (`unattended: block`) is parked with `approval: pending`. This draft stands in for both gates.

Opened by an unattended `execute-plan` run (plan `+goals-standup`, entry `f545edea-9843-4230-aee9-c7f6710fb3de`).
