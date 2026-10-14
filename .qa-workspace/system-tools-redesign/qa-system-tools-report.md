# QA: the redesigned System tools pane

Plan `system-tools-redesign`, item `qa-system-tools` (entry `8e39fcfe-3636-4de8-931f-d28201c06b6a`).
Run on 2026-10-13 in `desktop-web-harness`, started with `aspire start --isolated` from worktree
`D:/str/8e39fcfe`. The System tools feature was already on in the shared harness settings. The
pane read the sandbox catalog that the shared debug workspace points at. The pane's own status
line names that path.

Screenshots sit beside this file. The prefix `1440-` or `390-` is the viewport width. The prefix
`after-fix-` marks a shot taken after the fixes below.

## Defects found and fixed

| # | Defect | Fix | Re-check |
|---|---|---|---|
| 1 | At 390 px a catalog group's fold label stayed on one line. It ran past its card, and the card scrolled sideways (`390-applications-group-labels-before.png`). | `app.css`: the group fold and its heading give up their min-width, and the label wraps. | `390-group-unfolded.png`; the overflow scan finds no sideways scroller on any tab or filter (36 combinations). |
| 2 | At 390 px a stacked row's actions (Install, Disable, Remove) measured 364 px in a 259 px row. Remove was only reachable by scrolling the card. | `app.css`: in the inventory's stacked layout (below 46rem) the action slots become a wrapping row. The 6rem and 4rem reserved widths stay as minimums, so a row that fits still lines up. | The same scan at 390, 600 and 1440 px (54 combinations): no sideways scroller and no overlapping controls (`after-fix-390-applications-all.png`, `after-fix-600-applications-all.png`). |
| 3 | At 390 px a Needs you card on the Overview had a note containing `HKCU:\Software\Classes\...`. It widened the card and scrolled the whole `.tools-panel` sideways. | `app.css`: `.tools-overview-need__body` breaks long words anywhere. | `after-fix-390-overview-all.png`, `after-fix-390-overview-long-note-wraps.png`; the scan is clean. |
| 4 | Escape in any of the pane's dialogs closed the dialog **and** the whole System tools pane. Affected: Add tool, Import, and the last run's transcript. | `Modal.razor`: the dialog stops Blazor keydown propagation to its ancestors. Regression test: `ModalTests.A_key_pressed_in_the_dialog_does_not_reach_the_host_behind_it`. | All three dialogs close on Escape and leave the pane open. The next Escape closes the pane (`1440-escape-add-dialog-open.png`, `1440-escape-after-dialog-closed.png`). |

## Checks

| Check | Result | Evidence |
|---|---|---|
| Overview with outstanding items, 1440 and 390 | Pass | `1440-overview-all.png`, `after-fix-390-overview-all.png` |
| Overview with everything matching | **Not driven.** The catalog-only harness always adds sample rows that are behind or missing. Covered by bUnit `ToolsPaneOverviewTests.Nothing_needing_action_says_everything_matches_the_catalog`. | — |
| Every kind tab × host filter (All, Claude, Copilot), 1440 and 390 | Pass. Empty kinds show "No … here". | `{1440,390}-{tab}-{host}.png`, `after-fix-*` |
| Folded and unfolded group | Pass. `aria-expanded` flips and the table is hidden when folded. | `{1440,390}-group-folded.png`, `-unfolded.png` |
| Running Update all | Partly. The harness refuses within about 5 ms, so the running state was captured as a DOM snapshot at the moment the queue rendered, then re-rendered with the app's CSS. It shows the Updating header, 47 queued steps, both buttons disabled, and the polite live transcript. | `{1440,390}-update-all-running-dom-snapshot.png`, `-running-attempt.png`, `-after.png` |
| Failed step | **Not driven.** The harness refuses before any step runs. Covered by bUnit `ToolsPaneRunStatesTests.A_failed_step_returns_to_its_row_with_the_error_and_keeps_its_transcript`. A whole-run failure was driven: Last run reads "Failed · No tool steps". | `1440-last-run-before-restart.png` |
| No-catalog and empty-catalog states | **Not driven.** Reaching them means moving the sandbox catalog, which lives in a folder shared with other worktrees, and that write was refused. Covered by bUnit `No_catalog_offers_two_option_cards_and_one_at_a_time`, `An_empty_catalog_offers_adding_or_importing_as_cards` and the read-only variants. | — |
| No horizontal page scroll at 390 | Pass after fixes 1–3. `document.scrollingElement.scrollWidth` is 390 on every tab and filter, and nothing inside the pane scrolls sideways. The exceptions are the tab strip and the run tail, which scroll on purpose. | the scan and the `after-fix-390-*` shots |
| Tab strip scrolls on one row | Pass. One row (all tabs share one top). `.tools-tabs__strip` scrolls (`overflow-x: auto`, 882 px in 295 px). Arrow-key focus scrolls the focused tab into view. | `390-keyboard-last-tab-scrolled-into-view.png` |
| Keyboard reaches every control in order | Pass. The Tab sequence equals DOM order: host filter, Check for updates, Update all, Close, the selected tab, the tab panel, Needs action/All, filter field, Add tool, Import, the group toggle, then the row controls. The arrow keys move the host radio group and the tab strip. | the sequence is recorded in the PR body |
| Remembered tab and host filter survive a reload | Pass: Plugins with Claude, then MCP servers with Copilot, both restored. | `reload-1-before.png`, `reload-2-after.png`, `reload-3-after-mcp-copilot.png` |
| Last run survives a harness restart | Pass. "Failed · Update all · Today 21:17" is identical after the `desktop-web-harness` rebuild and restart. | `1440-last-run-before-restart.png`, `1440-last-run-after-restart.png` |

## Aspire logs and traces

Searched the structured logs on every resource for `error`, `exception`, `fail` and `Warning`:
0 entries. Traces with errors on `desktop-web-harness`: 0. Browser console: 1 error, the expected
WebSocket 1006 when the harness was deliberately restarted.

## Observations left open

- During a run both header buttons read "Updating…". The idle Check for updates button reads
  "Checking..." with three dots.
- A group label reads "22 row(s)". The wording is pinned by `ToolsPaneTests`.
- With the Claude or Copilot filter, Applications and Checklist are empty, by design. Rows with
  no host show only under All hosts.
- The tab strip's "Last run transcript →" button shows only while this session ran commands. The
  Overview's Last run card still opens the transcript.
