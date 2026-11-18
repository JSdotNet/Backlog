# QA: a backlog:// link re-shows the hidden main window

Plan `+tray-keep-alive`, Backlog entry `7072008f-7836-4b18-b74e-ec8d1abb4fe3`. Design: ADR 0043 §5.
Re-runs scenario 3b of `../tray-keep-alive/` (`03-re-show/b-backlog-link.log`, failed there) on the
fix. Run 2026-11-18 02:35–02:36 +01:00.

The Debug desktop head ran as the `desktop` resource of this worktree's `aspire start --isolated`
AppHost. Window state was read through Win32 (`IsWindowVisible`), and the main window was closed
with `WM_SYSCOMMAND SC_CLOSE`, the title bar's close. `backlog://` is registered to the installed
app, so the link was handed to the Debug build on its command line, the same
`BacklogLinkInstance` redirect a protocol activation takes.

| # | Scenario | Result | Evidence |
| --- | --- | --- | --- |
| 1 | Close hides, then a `backlog://window/goals` link: Goals opens, the main window is shown again, one process runs, the link's process exits 0 | Passed | `link-reshows-main.log` |
| 2 | Logs | Passed, one environmental error | The `desktop` resource logged only `The MCP server could not bind 127.0.0.1:5757` — the installed app holds that port, as in the earlier QA run. |
