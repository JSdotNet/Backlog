# QA: tray keep-alive in the desktop build

Plan `+tray-keep-alive`, entry 3 (`qa-tray`). Design: ADR 0043 §5 ("Each window has its own
view state; closing the main window hides it to the tray"). Run 2026-11-18 00:15–00:24 +01:00.

The Debug desktop head ran as the `desktop` resource of this worktree's
`aspire start --isolated` AppHost. Each window's WebView2 was driven over CDP, window state
was read through Win32 (`IsWindowVisible`), and the main window was closed with
`WM_SYSCOMMAND SC_CLOSE`, the title bar's close. The MCP server was called over HTTP with
its bearer token.

| # | Scenario | Result | Evidence |
| --- | --- | --- | --- |
| 1 | Close hides | Passed | `01-close-hides/close-hides.log` |
| 2 | Pop-outs stay | Passed | `02-popouts-stay/popouts-stay.log`, `02-popouts-stay/second-hide-popout-stays.log` |
| 3 | Re-show | **Failed** (link) | `03-re-show/a-second-launch.log` passed, `a-main-reshown-titlebar.png`; `b-backlog-link.log` **failed**; `c-show-in-main-window.log` passed |
| 4 | `open_window` while hidden | Passed | `04-open-window/open-window.log` |
| 5 | One-time toast | Passed | `05-one-time-toast/01-first-hide-toast.png`, `02-second-hide-corner.png`, `second-hide.log` |
| 6 | Keep-in-tray off | Passed | `06-keep-in-tray-off/keep-in-tray-off.log`, `01-switch-off.png`, `02-switch-off-after-restart.png` |
| 7 | Start with Windows (Debug) | Passed | `07-start-with-windows/01-switches-debug-build.png` |
| 8 | Logs | Passed, one environmental error | `08-logs/aspire-and-app-log-errors.txt` |

## Defect

- **A `backlog://` link leaves the hidden main window hidden.** The link opens its pop-out
  (Goals), but the main window stays hidden. ADR 0043 §5 lists a link among the things
  that bring it back. Filed as Backlog entry `7072008f-7836-4b18-b74e-ec8d1abb4fe3` under
  `+tray-keep-alive`.

## How the environment was set up

- The installed MSIX app was running. It holds CDP port 9222 and MCP port 5757.
  - The Debug head got `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333`
    through the AppHost's environment.
  - The Debug head's first start logged
    `The MCP server could not bind 127.0.0.1:5757`, which is the one error in scenario 8.
    It was restarted with `mcpServer.port` 5799.
- `backlog://` is registered to the installed app. So the Debug build was handed the link on
  its command line. That is the same `BacklogLinkInstance` path a protocol activation
  redirects through.
- The shared `%LOCALAPPDATA%\Backlog.Debug\settings.json` was changed for the run and
  restored afterwards:
  - `tray.hintShown` was reset to false, so the first hide could show the toast.
  - The MCP port was set to 5799.
- Screenshots are limited to the app's own chrome, the two settings switches, and the toast
  corner. The repository is public, and the Debug workspace holds personal task data.

## Observations

- On the first Aspire start the main window came up hidden (`IsWindowVisible` false), with
  no hide requested by the code (`StartedAtSignIn` false). It did not happen on the two
  later starts. A plain second launch showed it. This is likely the process start's
  `SW_HIDE` from Aspire's launcher. It is not filed as a defect.
- The tray icon itself, the tray menu, Quit, and sign-in start were not driven. Plan entry 4
  checks them by hand in the installed app.
