# Backlog.EndToEndTests

Browser end-to-end tests that drive the Aspire-hosted harnesses with Playwright. They run
against the AppHost **this worktree already has running**, and reach it through the Aspire
CLI — no test project may reference the AppHost (`AspireAppModelTests`):

| Needs | From the CLI |
| --- | --- |
| resource URLs | `aspire describe <resource> --format Json` |
| take `sync` away and back | `aspire resource sync stop` / `start`, then `aspire wait` |
| the order the phone sent things in | `aspire otel spans mobile-web-harness` |
| no errors in `sync` | `aspire otel logs sync --severity Error` |

Every test **skips unless `BACKLOG_E2E=1`**, so the solution-wide `dotnet test` and CI
build this project and run nothing.

## Tests

`ConferenceDayTests` — the conference day the mobile Inbox slice was built for, one test
in seven named steps, the phone at 375 px:

1. The phone pairs with a code the desktop issued and reads *Synced*.
2. A quick capture shows at once, then on the desktop from channel *Mobile*.
3. With `sync` stopped, a talk note with tags, `@ada`, a JPEG and a PDF is sent and
   reads *waiting*. A second quick capture also waits.
4. With `sync` started, the phone sends both attachment `PUT`s, then the note's
   `POST /api/sync/inbox`, then the capture's, and reads *Synced* again.
5. The desktop shows the note with a thumbnail, a `handout.pdf` row, the tags and
   the speaker, and routes it to Tasks. The task carries the item's folder.
6. The phone Inbox drops the routed note. The task is absent from the phone's My Day
   until the desktop adds it there, and then it is present.
7. With `sync` stopped, a task added from My Day shows at once, marked waiting, and the
   phone reads *Offline*. With `sync` started, the marker clears and the desktop has the
   task in My Day.

## Running

Start this worktree's AppHost and wait for it to be healthy. The Cosmos emulator takes about
two minutes on a cold start, and the test waits up to ten minutes for it:

```powershell
aspire start --isolated --non-interactive --apphost src\Aspire\Backlog.Aspire.AppHost\Backlog.Aspire.AppHost.csproj
```

Playwright needs a browser. Install the one it ships with once per machine (the build writes
`playwright.ps1` beside the test assembly):

```powershell
pwsh tests\Backlog.EndToEndTests\bin\Debug\net10.0\playwright.ps1 install chromium
```

Or skip the download and use an installed browser with `BACKLOG_E2E_BROWSER_CHANNEL`
(`msedge`, `chrome`). Then run:

```powershell
$env:BACKLOG_E2E = '1'
dotnet test --project tests\Backlog.EndToEndTests
```

| Variable | Effect |
| --- | --- |
| `BACKLOG_E2E=1` | Runs the tests; without it they skip. |
| `BACKLOG_E2E_BROWSER_CHANNEL` | An installed browser instead of Playwright's Chromium. |
| `BACKLOG_E2E_HEADED=1` | Shows the browser. |
| `BACKLOG_E2E_EVIDENCE` | Where evidence goes; default `.qa-workspace/e2e/`. |

A run takes about two minutes once the AppHost is healthy.

## Evidence

Each run writes `.qa-workspace/e2e/conference-day/<yyyyMMdd-HHmmss>/`, which git ignores.
It holds:

- one **viewport** screenshot per checkpoint, numbered in order. There is no full-page
  capture: removing the scrollbar widens the container past the phone breakpoint;
- `run.log` — when `sync` went down and came back, the talk note's statuses, the calls
  the phone sent after the restart, and every desktop sync result;
- `sync-structured-logs.json` — `sync`'s structured logs at Information and above;
- `sync-errors.json` — its Error logs. The test fails on any error that appeared during
  the run.

## What a run changes

- **`sync`** is stopped and started twice; the finally block starts it again if a step
  fails. Restarting it in Development generates a new signing key. Every device fetches
  a fresh token, so the Warning logs about rejected tokens are expected.
- **`mobile-web-harness`** is reset to a phone that was never paired: it is stopped,
  its `obj/local-development` credential, outbox database and staged talk-note files are
  removed, and it is started again. These files belong to this worktree only.
- **`desktop-web-harness`** gets the *Sync* and *Inbox pane* features turned on (kept
  per worktree) and is registered with the sync service if it was not already.
- **The desktop workspace is shared.** Every worktree on the machine uses the one
  `Backlog.Debug` folder, so the captures, the routed task and the phone's task stay
  there. Each run stamps its own `MMdd-HHmmss` token on every title and asserts only on
  rows carrying it.

## Known behaviour it works around

- **Blazor Server prerenders.** A field filled, or a button clicked, before the circuit
  attaches is lost. `Interactive` repeats the action until the page shows it took.
- **The desktop's first sync after a `sync` restart fails.** It meets a 401, fetches
  a new token, and ends as *Sync failed: Unauthorized*. The following run pulls, so
  `DesktopHarness.SyncNowAsync` runs once more after a failure and logs both. The phone
  retries within the same flush.
- **The harness's outbox runs server-side**, so switching the browser offline does not
  cut it off. The test stops the `sync` resource instead, and raises the page's `online`
  event when it is back, which is what a real phone does on reconnect.
