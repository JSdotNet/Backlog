# GTD walkthrough in the desktop harness

Plan `+gtd-para`, entry 22 (`b4a315e1-559f-4bbf-a789-3adc10d01b41`), walked on 2026-11-11
against `desktop-web-harness` started by this worktree's `aspire start --isolated`, at
1440x900, with Playwright MCP. Form: a numbered screenshot sequence, one per checkpoint
(no trace or video tool was available).

Setup: the Brain folder was set to a fresh empty folder and **Create conventions** wrote
the PARA default areas (`_inbox`, `projects`, `someday`, `areas`, `resources`, `people`,
`journal`, `archive`). Settings → Features turned on **Inbox pane** and **Weekly review**.
Eight items were captured with the harness's own `/mcp` endpoint, `capture_to_inbox`, all
titled `GTD-1111b4 …`.

| # | Checkpoint | Outcome | Screenshot |
| --- | --- | --- | --- |
| 1 | Brain folder set, conventions written | Pass | `screenshots/01-brain-folder-set.png` |
| 2 | Capture over MCP lands in the Inbox | Pass: 8 items, channel Manual | `screenshots/02-mcp-captures-in-inbox.png` |
| 3 | Archive (a) | Pass: "Archived." | `screenshots/03-clarify-archive.png` |
| 4 | To Brain (b) → `resources` | Pass: picker lists the areas; note written to `resources/` | `screenshots/04-to-brain-destination-picker.png`, `05-to-brain-resources-routed.png` |
| 5 | Someday (s) | Pass: note in the Brain's `someday/` area | `screenshots/06-clarify-someday.png` |
| 6 | Defer (d) | Pass: deferred until 11 Dec 2026, Deferred count 1 → 2 | `screenshots/07-clarify-defer.png` |
| 7 | Done now (n) | Pass: task created Done, item routed | `screenshots/08-clarify-done-now.png` |
| 8 | Waiting for (w) | Pass: task waits on "Plumber Jansen", follow up 18/11/2026 | `screenshots/09-waiting-for-dialog.png`, `10-clarify-waiting-for.png`, `22-waiting-slice-and-waiting-on-group.png` |
| 9 | Move to backlog (r) with My Day | Pass: draft task in My Day | `screenshots/11-move-to-backlog-my-day-ticked.png`, `12-clarify-move-to-backlog-my-day.png`, `15-my-day-has-routed-task.png` |
| 10 | Create plan with a project note | Pass: 2 tasks under a fresh plan tag; `projects/` note carries `roadmap: [<tag>]` | `screenshots/13-create-plan-project-note-ticked.png`, `14-clarify-create-plan-with-project-note.png` |
| 11 | Next actions grouped by `#@pc` | Pass: an `@pc` group above "No context" | `screenshots/16-next-actions-grouped-by-context.png` |
| 12 | Brain note → Create task | Pass on disk: `tasks: [<id>]` written. **Defect**: the open note shows no Tasks chip until it is reopened (entry 25) | `screenshots/17-brain-note-create-task.png`, `19-brain-note-links-after-reload.png` |
| 13 | Brain note → Plan this project | Pass: plan of 2 tasks; `roadmap: [<tag>]` written beside `tasks` | `screenshots/18-brain-note-plan-this-project.png` |
| 14 | Finish the plan, archive its note | Pass: the toast's button moved the note to `archive/`. **Defect**: the offer toast lays its text out in narrow columns and times out before it can be read (entry 26) | `screenshots/20-plan-done-archive-offer-toast-cramped.png`, `21-plan-done-note-archived.png` |
| 15 | Weekly review takeover | Pass: Get clear, Get current (finished plan's note offered **Move to archive**, taken), Get creative (the someday note) | `screenshots/23-weekly-review-get-clear.png` to `26-weekly-review-get-creative.png` |

The Brain notes as the app left them are under `brain-notes/`: both project notes in
`archive/` with their `meta` links, the reference note in `resources/`, the someday note in
`someday/`.

Aspire logs, Warning and above across every resource for the whole walk
(`logs/01-warnings-and-errors.txt`): only the Cosmos emulator's boot readiness retries and
`sync`'s ephemeral signing key notice. No error was logged by `desktop-web-harness`; its
eight `capture_to_inbox` calls all completed with `IsError = False`.
