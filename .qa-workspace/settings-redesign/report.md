# Settings redesign QA report

Plan `settings-redesign`, entry 15 (`settings-qa`, id `513287db-63d1-4428-9390-0f5479c77013`).

- **Build under test:** `plan/settings-redesign` at `de5324a0`, which holds all ten `after:`
  entries (PRs #1227, #1234, #1233, #1217, #1218, #1219, #1222, #1223, #1221, #1229).
- **Run:** `aspire start --isolated` from worktree `D:/sr/513287db`. `sync`,
  `azure-foundry-test`, `desktop-web-harness`, `mobile-web-harness` and `ui-storybook` were all
  `Running`, every resource path under the worktree. The desktop harness was served at a
  dynamic port, with a page title naming worktree `513287db`.
- **Driver:** Playwright (Chromium) at 1280×900 and 390×844.
- **Evidence:** screenshots in `.qa-workspace/settings-redesign/`. They are not committed, so
  the paths below are relative to the worktree root.
- **Board:** the board artifact `https://claude.ai/artifact/Ybc6FYhB6yhBGoyaAb5KQi` could not
  be read from this session ("artifact not found"). Pages were compared against the plan
  entries' stated sentences and the code's declared pages and cards, not against the boards.
  See *What could not be proved*.

## Pages covered

There are twelve pages in three groups:

- **Workspace:** Features, Storage & backup, Repositories, GitHub accounts.
- **AI:** AI providers, Claude Code, Model prices.
- **Product areas:** Dashboard, Working week, Inbox, Connectors, Devices.

## Checkpoints

| # | Pass criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Every page opens at `/settings/{page}`, Back returns to the previous page, `/settings` opens Features | **Pass** | `01-settings-root-1280.png`, `02`–`13-page-*-1280.png`. All 12 pages reached by menu click and by direct address, each with the menu item `aria-current=page`; three Back steps returned connectors → inbox → working-week |
| 2 | Each headline matches its sentence and changes with the setting behind it | **Pass** (backup unproved) | Features: 12 → 13 of 19 when C4 or Archify is turned on, and back (`30-features-toggle-*-1280.png`). Inbox routing rule added and removed: "No routing rules yet…" → "1 routing rule sorts what lands in the Inbox." → back (`33`, `34`). GitHub accounts: "No GitHub account is signed in yet." → "1 GitHub account signed in." → back (`51`, `53`). AI: headline follows usage metrics (`06`, `60`). Running a backup was not done (see below) |
| 3 | Badges show the right values and update; a switched-off module page shows `Off` and the turn-it-on note | **Fail** | Features `12/19` ↔ `13/19`, Repositories `10`, GitHub accounts `0` ↔ `1`, Inbox `Off` cleared when turned on and back again (`32-inbox-on-1280.png`). The Inbox and Devices pages show `Off` and the "turned off… Features page" note (`11`, `13`). **F3:** the AI providers badge reads `2/3` while the page offers only Azure Foundry (`06`) |
| 4 | Find a setting narrows by page and card title, opens and highlights a card, shows the no-match line, clears on Escape | **Fail** | Narrows to pages and cards (`20`). *Backup* opens `/settings/storage#card-backup` with the card ringed (`22`). The no-match line reads `No setting matches "zzqx".` (`23`). Escape empties the box and restores all 12 pages (`24`). **F2:** AI usage card results are dead links while usage metrics are off (`21`) |
| 5 | Features filter and tiles, AI tiles' last-read lines, Repositories inline editor, GitHub accounts inline editor | **Fail** | Filters: All 19, On 12, Off 7, Unfinished 12 (7 on), matching the tiles (`31-*`); tiles follow a toggle (12 → 13 → 12). Repositories: *Edit* opens one editor at a time, a pull-request-labels edit persisted across reload and was reverted, *Done* closes it (`41`, `42`). GitHub accounts: add by login, open, close and *Forget this account* all work (`51`–`53`). **F4:** rows resize 844 → 279 px when the editor opens. **F5:** the row reads "Signed in" while the editor says the CLI is not. AI last-read lines: unproved, since usage was never read on this device |
| 6 | At 390 px: grouped list, a page full width with back to the list, no horizontal page scroll | **Fail** | Grouped list with badges and chevrons (`70`). Each of the 12 pages full width with the menu hidden and *Back to all settings* shown; back returns to `/settings` with the list (`71`–`82`). `scrollWidth − clientWidth = 0` on all 12 pages (the Repositories table scrolls inside its own container). **F1:** four nested frames (`73`, `84`). **F6:** a 188 px gap in the repository editor and a 247 px account header (`83`, `84`). **F7:** the list marks Features current and carries its headline (`70`) |
| 7 | Keyboard only: every control reachable in order with a visible focus ring; no unlabeled control | **Pass** (with F8) | Tab sweep of all 12 pages at 1280 px: 17–53 stops a page, no positive `tabindex`, so the order follows the DOM (back, search, menu, page). Each stop has a ring. The search box rings its wrapper (`90`), time inputs draw a 2 px outline (`91-*`). Playwright `ariaSnapshot` of the frame on every page: no button, link, checkbox, textbox, combobox, searchbox or radio without an accessible name. **F8:** the h1 shows a focus outline after each navigation (`40`, `70`) |
| 8 | No error or warning in the Aspire logs during the run | **Pass** | Structured logs across all resources: 0 entries at `Error`. The only `Warning` entries are from startup, before the browser opened: `sync` ephemeral signing key (log 1) and `cosmos` readiness retries (logs 30, 31), both expected in local development. No trace with `hasError`. Browser console: 0 errors, 0 warnings |

**Total:** 4 of 8 criteria pass and 4 fail, giving 9 follow-ups in `follow-ups.md`. Six of them
are the known items, all confirmed. Three are new: F3, F5 and F9.

Also seen, not counted against a criterion: the Repositories summary line is missing a space,
"…and 9 more.Type @backlog…" (F9).

## Shared state touched and restored

Every switch was put back as it was found. The final Features badge read `12/19`, the same as
at the start.

- **Feature switches:** `Archify diagrams`, `C4 diagrams`, `Inbox pane` and `AI usage metrics`
  were each turned on for one checkpoint and turned off again.
- **Inbox routing rules:** one rule was added, then the box was emptied again.
- **Repository `acme/web`:** its pull-request labels were set to `qa-settings-check`, then
  cleared.
- **GitHub account `qa-settings-check`:** added twice (at 1280 px and at 390 px) and forgotten
  each time. No credential was entered.
- **Not touched:** no device was paired with the sync service.

## What could not be proved

- **Board comparison.** The board artifact could not be read from this session, so no page was
  compared with its board pixel for pixel or copy for copy.
- **Backup headline.** *Back up now* needs a backup repository, and a backup commits the
  database to GitHub. No backup was run, so the Storage headline moving off "Not backed up
  yet." is unproved.
- **AI tiles' last-read lines.** These appear only after a usage read, which calls the Claude
  and GitHub organization APIs. No read was triggered, so the tiles showed no read line.
- **Screen reader output.** The accessibility tree was inspected, but no screen reader was run.
