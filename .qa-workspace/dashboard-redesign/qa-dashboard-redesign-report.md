# Dashboard redesign — harness validation

Plan `dashboard-redesign`, entry 13 `qa-dashboard-redesign` (Backlog id
`a40b699c-4d01-4c64-888c-e7c798cf834b`). Run unattended by `execute-plan` on 2026-10-12 against
`origin/plan/dashboard-redesign` at `5746c7c0`, in `desktop-web-harness` started with
`aspire start --isolated` for this worktree, at 1440×1000 and 390×844, through the Playwright MCP
browser. The data is the shared debug workspace's real data, as `run-backlog` describes.

Evidence is a numbered screenshot sequence (no tracing or video in this Playwright server). It is kept
local under `.qa-workspace/evidence/dashboard-redesign-a40b699c/` and is untracked by the
repository's convention: `screenshots/NN-*.png`, and the Playwright page snapshots and browser console
logs under `playwright/`.

## Checks

| # | Check | Result | Evidence (`screenshots/`) |
|---|---|---|---|
| 1 | Opens on Overview | pass | `01-opens-on-overview.png` |
| 2 | Every tab shows its parts, no unexplained unavailable state | pass | `01`, `02-productivity-12w.png`, `03-tasks-roadmap-behind-orange.png`, `05-sessions-12w.png`, `06-devbook-no-verdicts.png`, `08-cost-no-budget.png` |
| 3 | Window toggle changes the figures and deltas | pass | `13-overview-4w-window.png`, `14-sessions-4w-delta.png` |
| 4 | Budget in Dashboard settings → Cost tab, "Will pass budget" chip, Needs-you item | pass | `09-settings-budget-50.png`, `10-overview-needs-you-budget.png`, `11-needs-you-opens-cost-will-pass-budget.png` |
| 5 | Each Needs-you link opens its tab | pass (Tasks, Cost); Devbook and Productivity items not present in the data | `11`, `12-needs-you-opens-tasks.png` |
| 6 | Roadmap timeline shows Behind items in orange | **fail → fixed** | before: `03`, `04-timeline-closeup.png`; after: `21-timeline-behind-outlined-after-fix.png`, `22-tasks-roadmap-behind-orange-after-fix.png` |
| 7 | Devbook filter and Show all work | partial: the direction filter toggles; Show all could not be exercised | `06`, `07-devbook-filter-pull.png` |
| 8 | "Read as brief" opens the brief, its link returns to Overview | pass | `15-read-as-brief.png`, `16-brief-returns-to-overview.png` |
| 9 | Closing and reopening lands on Overview | pass | `17-dashboard-closed-from-cost.png`, `18-reopened-lands-on-overview.png` |
| 10 | Phone width: Overview side column stacks, timeline scrolls inside its card | pass | `19-overview-side-stacked-390w.png`, `20-timeline-scrolls-in-card-390w.png` |

### What each check saw

1. The dashboard opened with the Overview tab selected (`aria-selected=true`).
2. Every tab rendered its parts. The empty or partial states each carried their reason:
   "First review arrived in —" because no pull request in the window was reviewed; the Devbook
   tab's "No sweep has verified a unit here yet" because no sync verdicts are recorded
   (`~/.claude/backlog/sync-verdicts.json` is absent); "or more" counts because some
   repositories' drift issues could not be read; Spend by model has no token columns because
   Azure Foundry reports billing meters, as the part's note says.
3. 12 → 4 weeks: pull requests merged 865 → 504, agent-active time 19.9d → 11.1d, sessions
   1,922 → 1,206, and the delta "up 33.4% vs previous 4 weeks" appeared on agent-active time.
   The pull request tile shows no delta in either window because its count is a floor ("or
   more"), which the part withholds by design. Back to 12 weeks restored 1,922 sessions.
4. Azure AI Foundry budget set to 50 (projection 64.51 EUR). The Cost card read "23.28 EUR of
   50.00 EUR" with "Will pass budget", the cumulative chart drew "Budget 50.00 EUR", the
   Overview spend tile read "Azure Foundry will pass budget", and Needs you listed "Azure AI
   Foundry is projected past its budget — crosses its 50.00 EUR budget on 25 Oct". The budget is
   stored per worktree (`src/Harness/Backlog.Desktop.WebHarness/obj/local-development/spend-budgets.settings.json`).
5. "Open Cost →" selected and focused the Cost tab; "Open Tasks →" (QA parts AC2, Behind) the
   Tasks tab.
6. Before the fix, QA parts AC2 (Behind, 0 / 8 pts) was drawn only as its remaining-work wash —
   `color-band-4` at 28% — which reads brown on the dark theme and is hard to tell from a Planned
   bar; QA keepup plan A (Overdue, 10 / 18) read orange only through its done fill. Fixed: a
   behind or overdue bar is now outlined in its own tone. After the fix, both late bars carry
   the outline and the other nine report `box-shadow: none`.
7. The direction filter (All / Code → chapter / Chapter → code) switches its pressed state. With
   no unit verdicts in the data there are no units to filter and no aligned units, so "Show all"
   does not render.
8. "Read as brief →" opened the week 41 brief (Shipping, Plan, Sessions, Cost, Carrying into
   next week); "Open overview" returned to the Overview tab.
9. Closed with ✕ while on Cost, reopened with the Dashboard button: Overview selected. A full
   page reload with the dashboard remembered also reopened on Overview.
10. At 390×844 `.dashboard-overview__side` sits under `.dashboard-overview__main` at the same x
    (grid columns `none`), and the page does not scroll sideways (`scrollWidth` 390).
    `.dashboard-timeline__scroll` scrolls (`scrollWidth` 760 in a 264px box) with the names
    sticky.

## Fix

`src/App/Backlog.Desktop.UI/wwwroot/app.css`: `.dashboard-timeline__bar--behind` adds
`box-shadow: inset 0 0 0 calc(var(--border-width) * 2) var(--dashboard-timeline-tone)`.
The modifier is applied to Behind and Overdue outlooks (`RoadmapPart.razor`), so every late bar is
orange whatever share of it is done; the fill still paints over the outline where work is done.

## Aspire

All resources of this AppHost ran healthy. One error: on first start `sync` exited with
`CS2012 Cannot open ...Backlog.SharedKernel.dll for writing` — parallel project builds racing on a
fresh worktree — and came up after `aspire resource sync start`. No error or warning in the
structured or console logs of `desktop-web-harness` during the checks. The browser console showed
no error from this harness (earlier `ERR_CONNECTION_REFUSED` entries belong to another session's
port 63833 in the shared browser).

## Observed outside the checks

- GitHub reads: drift issues first failed with `gh: Validation Failed (HTTP 422)` for some
  repositories, then with `gh: API rate limit exceeded`. The dashboard said so in place each time.
- Phone width: the tab strip wraps into a five-row stack, leaving about 90px for the tab body
  inside the takeover.
- Phone width, Tasks tab: the weekly bar charts (`metric-bars`) do not shrink below 13 × 23px
  columns and push `.dashboard-panel__content` 54px wider than its box, so the tab body scrolls
  sideways.
- Sessions: limit hits read 52 over 12 weeks and 0 over 4 weeks.
