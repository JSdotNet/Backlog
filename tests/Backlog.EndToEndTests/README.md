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

`ConferenceDayTests` — a conference day on the redesigned phone (Today, Inbox and Notes
tabs, the capture sheet), one test in seven named steps, the phone at 390 × 844:

1. The phone pairs with a code the desktop issued, reads *Synced* and opens on Today.
2. The capture sheet saves one thought as *Inbox*, one as *Note* and four as *Today*.
   The four show under Anytime today; the desktop Inbox has the first two from
   channel *Mobile*, and the desktop's My Day has the four.
3. On the desktop, one task gets two steps and a two-hour block holding the current
   time, another a block three hours later. The phone's Today shows the first on the
   Now card, with its first step next, and the second under Agenda.
4. A task ticked on Today and a step ticked on the task's page are ticked on the
   desktop; the other step is still open.
5. A task moved to tomorrow from its page leaves Today and the desktop's My Day.
6. A note written in the phone's Notes reaches the desktop Inbox. Edited there, the
   phone's Notes shows the edit; archived there, it leaves the phone's Notes.
7. With `sync` stopped, an Inbox capture with a JPEG, a plain one and a task for today
   wait, and the phone reads *Offline*. With `sync` started, the phone sends the
   attachment `PUT`, then the two `POST /api/sync/inbox`, reads *Synced* again, and
   the desktop has the photo, the capture and the task in My Day.

`BrainTests` — the Brain's core path on the desktop harness at 1440 × 900, one test per
requirement group, each listed under its requirement's `tests:` in
`.devbook/domain/devbook/requirements.md` and `.devbook/domain/inbox/requirements.md`:

| Test | Proves |
| --- | --- |
| `First_use_asks_for_a_folder_and_create_conventions_scaffolds_it` | With no folder the chip reads *Set up your Brain*, still scopes, and the pane asks for a folder; a typed folder stays empty; Create conventions writes `README.md` (`index: root`), `CONVENTIONS.md` and the seven PARA folders. |
| `The_brain_chip_narrows_the_tasks_to_those_filed_against_no_repository` | The chip leads the scope behind its divider; Brain alone and `backlog` alone list disjoint rows; Ctrl beside `backlog` keeps both with `backlog` the anchor; a plain press leaves Brain alone. |
| `The_pane_shows_the_declared_areas_and_the_journal_newest_first` | Nothing pressed shows the Brain with its badge and path; the tabs are the declared areas in order; the journal lists and opens its newest entry first; a `backlog` anchor shows `backlog`'s devbook. |
| `Capture_writes_a_note_into_the_inbox_area_and_typed_structure_stays_text` | Capture writes the note into `_inbox` with one `meta` block, opens it, and a typed fence stays text. |
| `To_brain_writes_an_inbox_item_as_a_note_in_the_inbox_area` | A desktop Inbox capture routed To Brain is a note in `_inbox` with its title. |
| `A_repo_link_opens_the_repository_chapter_and_the_brain_chip_brings_the_brain_back` | A gold `repo:backlog/…` chip opens ADR 0027 in `backlog`'s devbook with the scope still Brain; the Brain chip brings the Brain back. |
| `The_scope_survives_a_restart_with_the_brain_as_its_anchor` | Brain then Ctrl-`backlog` reopens after `desktop-web-harness` restarts with both pressed and Brain the anchor. |

`SettingsTests` — the Settings screen on the desktop harness, one test listed under the
`tests` of *Menu Groups*, *Card Titles and Search*, *Phone Width* and *Addresses* in
`.devbook/design/settings-pages.md`. At 1440 × 900 Repositories opens by its address,
Storage & backup by the menu, and Back returns to Repositories; Find a setting with
*backup* lists the Backup card as a link to `settings/storage#card-backup`, and following
it opens the page with the card in view and lit. At 390 × 844 `settings` is the grouped
list with the search above it (52 px rows, chevrons), a row opens its page alone with
*Back to all settings*, and that link returns to the list. It writes nothing, and runs in
the Brain tests' collection so their harness restart cannot cut it off.

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

A run takes about two minutes once the AppHost is healthy; `BrainTests` alone about
one and a half, and needs only `desktop-web-harness`:

```powershell
dotnet test --project tests\Backlog.EndToEndTests -- --filter-class Backlog.EndToEndTests.BrainTests
```

## Evidence

Each run writes `.qa-workspace/e2e/conference-day/<yyyyMMdd-HHmmss>/`, each Brain test
`.qa-workspace/e2e/brain-<scenario>/<yyyyMMdd-HHmmss>/` and the Settings test
`.qa-workspace/e2e/settings/<yyyyMMdd-HHmmss>/` with its screenshots; git ignores them.
It holds:

- one **viewport** screenshot per checkpoint, numbered in order. There is no full-page
  capture: removing the scrollbar widens the container past the phone breakpoint;
- `run.log` — the agenda blocks set and the phone's clock, when `sync` went down and
  came back, the calls the phone sent after the restart, and every desktop sync result;
- `sync-structured-logs.json` — `sync`'s structured logs at Information and above;
- `sync-errors.json` — its Error logs. The test fails on any error that appeared during
  the run.

## What a run changes

- **`sync`** is stopped and started once; the finally block starts it again if a step
  fails while it is down, and leaves a running `sync` alone — starting a running
  resource restarts it, and every device then has to recover from the new key. Restarting it in Development generates a new signing key. Every device fetches
  a fresh token, so the Warning logs about rejected tokens are expected.
- **`mobile-web-harness`** is reset to a phone that was never paired: it is stopped,
  its `obj/local-development` credential, outbox database and staged talk-note files are
  removed, and it is started again. These files belong to this worktree only.
- **`desktop-web-harness`** gets the *Sync* and *Inbox pane* features turned on (kept
  per worktree) and is registered with the sync service if it was not already.
- **The desktop workspace is shared.** Every worktree on the machine uses the one
  `Backlog.Debug` folder, so the captures, the tasks and the archived note stay there.
  Each run stamps its own `MMdd-HHmmss` token on every title and asserts only on rows
  carrying it. The Tasks view's filters are the workspace's too, so the test sets My Day,
  *All* and the scope chips itself rather than trusting what the last session left.
- **Agenda times are taken off again.** The Now card goes to the open timed task whose
  block holds the time and starts first, so a block left in the shared My Day would beat
  the next run's. The test starts its block as early as it can (the first quarter hour
  after now less 110 minutes) and, pass or fail, clears both agenda times at the end.

- **The Brain folder is never the person's.** Each `BrainTests` test remembers the folder
  Settings › Storage holds — the harness keeps it per worktree, in
  `obj/local-development/brain.settings.json` — points it at a new folder under
  `%TEMP%\backlog-e2e-brain\`, outside every repository, and types the remembered folder
  back in afterwards, pass or fail. The temporary folder is then deleted; nothing is
  written into the remembered one. The Brain's search database for each temporary folder
  stays in the app's storage (`devbook-cache`), as for any folder the Brain has used.
- **`BrainTests` turn on the *Inbox pane*** (kept per worktree), leave a routed
  `Standup <token>` item in the shared Inbox, and leave the remembered scope as the last
  test set it. `The_scope_survives_a_restart_with_the_brain_as_its_anchor` stops and
  starts `desktop-web-harness` once. The class runs in a collection of its own with
  parallelization off, so no other test drives the harness meanwhile.

## Known behaviour it works around

- **Blazor Server prerenders.** A field filled, or a button clicked, before the circuit
  attaches is lost. `Interactive` repeats the action until the page shows it took.
- **The harness's outbox runs server-side**, so switching the browser offline does not
  cut it off. The test stops the `sync` resource instead, and raises the page's `online`
  event when it is back, which is what a real phone does on reconnect.
