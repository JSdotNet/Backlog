# Settings redesign plan review

Plan `settings-redesign`, entry 16 (`review-plan`, id `69c3d9d9-35c3-4086-bf96-58bf3dde6643`).

- **Reviewed:** `plan/settings-redesign` at `97de62a9`. It holds entries 1–15, merged as PRs #1176, #1211,
  #1227, #1234, #1233, #1217, #1173, #1218, #1219, #1222, #1223, #1221, #1229, #1236 and #1237.
- **Sources:** each PR body's *Review findings left open*, *What could not be proved* and *Assumptions*;
  the QA step's `report.md` and `follow-ups.md` (F1–F9); and the code on the base, read to confirm each
  item below.
- **Design canvas:** `https://claude.ai/artifact/Ybc6FYhB6yhBGoyaAb5KQi` returned "artifact not found"
  from this session, as it did to every earlier entry. No entry, the QA step included, compared a page
  with its board. That comparison is still open (R7).
- **Backlog:** the `backlog` MCP server was unreachable (ECONNREFUSED), so no entry could be filed. The
  items below are ready to import under `+settings-redesign`. F1–F9 in `follow-ups.md` still need
  filing as well; nothing here repeats them.

## Verdict

**Outstanding items remain.** All 15 entries landed, and every Settings page has its headline, cards,
declared card titles and menu place. What is still open:

- the 9 QA follow-ups (F1–F9), unchanged on the base;
- 10 more items below (R1–R10): two defects the QA did not reach (R1, R2), a design-link gap (R3), one
  dropped piece of the Features board (R4), one half-built contract (R5), a stale parallel PR (R6), the
  checks no entry could make (R7, R8), the open copy and behaviour decisions (R9), and a test gap (R10).

## Per entry

| # | Entry | PR | What landed | What is left |
|---|---|---|---|---|
| 1 | Split Settings into one component per page (`split-settings-pages`) | #1176 | Five page components, `SettingsScreenContext`, `SettingsPageBase` | Open PR #1147 still moves Repositories into the Tasks module (R6) |
| 2 | Grouped Settings shell (`settings-shell`) | #1211 | `SettingsFrame`, three menu groups, `/settings/{page}` addresses, `SettingsCard`, `SettingsHeadline` | F1, F8; the e2e harness still clicks tabs (R1) |
| 3 | Phone-width grouped list (`settings-narrow-width`) | #1227 | Container query at `50.25rem`, grouped list, back to the list | F7, plus the eyebrow over the list (see F7 note) |
| 4 | Find a setting (`settings-search`) | #1234 | Header search, card links, card reveal, phone row | F2; module card titles are not checked for duplicates (R10) |
| 5 | Menu badges, switched-off pages (`settings-menu-badges`) | #1233 | Built-in badges, `Off` note, `ISettingsBadgeSource` | F3; no module supplies a badge (R5) |
| 6 | Features board (`features-page`) | #1217 | Headline, three tiles, filter chips, section cards | The row switch was not built (R4) |
| 7 | Record each usage read (`ai-usage-last-read`) | #1173 | `IUsageReadStatus`, `usage-reads.json`, Claude and GitHub recording | Azure Foundry records no read (R2); real-provider reads unproved (R8) |
| 8 | AI providers page (`ai-providers-page`) | #1218 | Headline, provider tiles with read lines, two-column cards, Foundry warning | F2, F3; copy decisions (R9) |
| 9 | Storage & backup (`storage-page`) | #1219 | Backup headline, two cards, figures, Browse, `DailyCopies` | Browse on the Windows head unproved (R8); daily-copies decision (R9) |
| 10 | Repositories table (`repositories-table`) | #1222 | `DataTable` with an inline editor, `HasRowDetail` | F6, F9 |
| 11 | GitHub accounts list (`github-accounts-list`) | #1223 | Headline, account grid, inline editor | F4, F5, F6 |
| 12 | Module pages A (`module-pages-a`) | #1221 | Dashboard, Working week, Claude Code, Model prices in cards with headlines | F1, F8 |
| 13 | Module pages B (`module-pages-b`) | #1229 | Inbox, Devices, Connectors in cards with headlines | F1, F8; paired Devices state unproved (R8) |
| 14 | Settings design guideline (`settings-design-guideline`) | #1236 | `.devbook/design/settings-pages.md`, README and `ui-components.md` links | The storybook pages do not link the chapter (R3); no rule linked to a test (R10) |
| 15 | Settings QA (`settings-qa`) | #1237 | `report.md`, `follow-ups.md` (F1–F9) | F1–F9 to file and fix; board comparison not made (R7) |

## Outstanding items

Each is written as a Backlog entry to import under `+settings-redesign`. "Evidence" names what this
review read on `97de62a9`.

### R1 — The end-to-end desktop harness still opens Settings by its old tabs

- **Type:** defect. **Belongs to:** entry 2 (`settings-shell`).
- **Why:** `#1211` replaced the tab strip with menu links, but `DesktopHarness.OpenSettingsAsync`
  still looks for `AriaRole.Tab` with an exact name and waits for `aria-selected="true"`. No such
  element exists any more, so `EnableSyncAndInboxAsync` and `IssuePairingCodeAsync` time out. That
  breaks `ConferenceDayTests` the next time it runs. It is skipped unless its environment is present,
  so no suite caught it. #1233 noticed it and left it open; no QA follow-up covers it.
- **Evidence:** `tests/Backlog.EndToEndTests/DesktopHarness.cs` lines 40–48, called at lines 55, 73
  and 127. Since #1233 the badge is part of each link's accessible name (`Features 12/19`), so an
  exact-name link lookup would fail too.
- **Suggested fix:** go to `settings/<page>` by address (`settings/features`, `settings/devices`)
  and wait for the menu link with `aria-current="page"`, instead of clicking a named control.
  Then run `ConferenceDayTests` once against the harness.

### R2 — Azure Foundry never records a usage read, so its tile never shows a read line

- **Type:** feature gap. **Belongs to:** entry 7 (`ai-usage-last-read`), drawn by entry 8.
- **Why:** entry 7 records reads in `ClaudeSpendSource` and `CopilotSpendSource` only. The AI
  providers page draws a read line for Azure Foundry once something records under
  `SpendProvider.AzureFoundry` (#1218, Assumptions). `AzureFoundrySpendSource` exists, but it takes
  no `IUsageReadRecorder`, so the Foundry tile shows no read line, and no failure either.
- **Evidence:** `IUsageReadRecorder` is used only by `ClaudeSpendSource.cs`, `CopilotSpendSource.cs`
  and the store. `src/Infrastructure/Backlog.Infrastructure.AzureFoundry/Dashboard/AzureFoundrySpendSource.cs`
  records nothing. `UsageSource` has no Foundry case.
- **Suggested fix:** add a Foundry `UsageSource`, and have `AzureFoundrySpendSource` record success
  or failure the same way as the other two, with tests that match `CopilotSpendSourceReadTests`. If
  Foundry should never show a read line, say so in the tile instead and close this item.

### R3 — The Settings storybook pages do not link the Settings design chapter

- **Type:** documentation (harness). **Belongs to:** entry 14 (`settings-design-guideline`).
- **Why:** #1236 wrote `.devbook/design/settings-pages.md`. Its own *Materialization* section says
  the storybook should link back to it from a **Design rules** block. The *Settings frame*,
  *Settings menu* and *Settings card* pages each have a Design rules block, but it names only
  `interaction-guidelines.md`, `typography-and-layout.md` and `accessibility.md`. Nothing in `src/`
  or `tests/` names `settings-pages.md`.
- **Evidence:** `src/Harness/Backlog.UI.Storybook/Components/Pages/SettingsFramePage.razor` lines
  118–121, `SettingsMenuPage.razor` lines 40–41, `SettingsCardPage.razor` lines 57–59.
- **Suggested fix:** add `DesignGuideline` rows to `settings-pages.md#panel-and-header` and
  `#headline` (frame), `#menu-groups`, `#badges-and-the-off-state` and `#phone-width` (menu), and
  `#settings-card` and `#figure-tiles` (card). Then update the chapter's *Materialization* section.

### R4 — The Features board's row switch was dropped

- **Type:** feature gap, needs a decision. **Belongs to:** entry 6 (`features-page`).
- **Why:** the Features board asked for a 44×26 switch on each row, if `Toggle` already supported
  that shape. #1217 kept the native checkbox, reading the switch shape as unsupported. `Toggle` does
  have a switch shape, a button with `role="switch"`, so the condition in the brief was met.
- **Evidence:** `src/Core/Backlog.UI.Components/Inputs/Toggle.razor` lines 3 and 33; #1217
  *What could not be proved*, third bullet.
- **Suggested fix:** decide whether the rows take the switch shape. If they do, move the Features
  rows to it, and update `FeaturesBoardTests` and the e2e harness. `EnableSyncAndInboxAsync` finds
  features by `AriaRole.Checkbox` and would need `AriaRole.Switch`, so do this together with R1.

### R5 — No module supplies a menu badge

- **Type:** feature gap, check against the canvas. **Belongs to:** entry 5 (`settings-menu-badges`).
- **Why:** entry 5 added `ISettingsBadgeSource` and `SettingsSection.BadgeSource` so that a module
  can put a value on its menu item. No module registers one. The contract is proved only with a test
  double (#1233). The module pages already count things in their headlines (Inbox rules, connected
  task sources, model prices) but show no badge unless they are `Off`.
- **Evidence:** `ISettingsBadgeSource` appears only in `SettingsSection.cs` and its own file. No
  `*SettingsRegistration.cs` sets `BadgeSource`.
- **Suggested fix:** check the canvas's menu. Where it shows a module badge, give that module a
  badge source (Inbox: rules, Connectors: connected sources, Model prices: prices, Devices: paired
  or not), registered as a singleton. If the canvas shows none, remove the contract or leave it
  documented as unused.

### R6 — Open PR #1147 conflicts with the redesigned Repositories page

- **Type:** housekeeping. **Belongs to:** entry 1 (`split-settings-pages`).
- **Why:** #1176 noted that issue-sweep PR #1147 moves the Repositories page into
  `Backlog.Modules.Tasks.UI`, and that whichever lands second needs a rebase. #1147 is still open.
  Since then #1211 regrouped the menu, and #1222 rebuilt the page as a table. #1147 now rewrites a
  page that no longer exists in that shape.
- **Evidence:** `gh pr view 1147`: OPEN, "refactor(settings): move the Repositories page into the
  Tasks module".
- **Suggested fix:** close #1147 as superseded, or redo the move on top of the redesigned page once
  the plan is merged to `main`.

### R7 — Compare every page with its board

- **Type:** test (by a person). **Belongs to:** entry 15 (`settings-qa`).
- **Why:** the canvas could not be read by any entry. Every PR built from the entry text and asked
  for a comparison by eye (#1211, #1217, #1218, #1219, #1222, #1221, #1237). Spacing, tile
  typography, chip and menu styling, and the place of `Add organization` were never compared.
  #1237's screenshots at 1280 and 390 px are the starting point.
- **Suggested fix:** open the canvas and compare each of the 12 pages, wide and at phone width.
  File each difference as its own entry.

### R8 — Check Settings in the Windows desktop app and in the light theme

- **Type:** test (by a person). **Belongs to:** entry 15 (`settings-qa`).
- **Why:** every entry and the QA drove only the web harness in the dark theme. These behaviours
  depend on the head, or were never seen in a running app:
  - **Browse** on Storage is drawn only where a folder picker exists, which is the Windows head
    (#1219).
  - Smooth scroll and the card highlight in WebView2 (#1234).
  - Back and the in-app back link (#1211).
  - The Brain set-up link to `settings/storage#brain-folder` (#1211).
  - The `Off` note link colour, fixed after its QA (#1233).
  - The paired Devices cards (#1229).
  - The AI tiles' read lines after a real Claude and GitHub read. This includes whether the recorder
    reaches the spend sources in the real heads and writes `usage-reads.json` (#1173, #1218, #1237).
  - The Storage headline after a real backup (#1237).
- **Suggested fix:** one pass in the installed Windows app, in light and dark themes, through the
  list above.

### R9 — Settle the copy and behaviour decisions the entries left to the owner

- **Type:** decision. **Belongs to:** entries 8 and 9.
- **Why:** each was built one way and flagged for a decision:
  1. **Foundry wording.** "Azure Foundry reports usage" in the AI headline, where "is set up" may be
     truer (#1218).
  2. **GitHub organizations.** The GitHub tile's "organizations" counts GitHub identities, so two
     accounts in one organization read `2 organizations` (#1218).
  3. **Daily copies.** An existing install counts from 1 and ignores the repository's earlier
     backup history (#1219).
  4. **Folder commit.** The Storage folder field commits on Enter only, not on blur (#1219). The
     `SettingsCard` storybook story says every settings field commits "on blur or Enter".
- **Suggested fix:** decide each one. Change the copy or behaviour where the decision differs. For
  item 4, fix either the field or the storybook sentence so they agree.

### R10 — Small checks the entries left behind

- **Type:** test. **Belongs to:** entries 4 and 14.
- **Why:**
  - `Every_built_in_card_has_its_own_fragment_on_its_page` guards duplicate card titles on built-in
    pages only. Two module cards with one title would share a `#card-` fragment (#1234).
  - The rules in `settings-pages.md` link to no test. `devbook-design.md` asks for a design
    requirement to be proved end to end (#1236). There is no e2e test of the Settings screen at all.
- **Suggested fix:** extend the fragment test over every registered `SettingsSection`. Add one e2e
  scenario (open a page by menu and by address, Back, Find a setting to a card, phone-width list)
  once R1 has fixed the harness, and link it from the chapter's rules.

## Amendments to the QA follow-ups

- **F7:** the eyebrow over the phone-width list also reads `Settings · Workspace`, which is the
  Features page's group (#1227). Fix it together with the title.
- **F3:** the AI badge can also dip by one during a GitHub connection re-check, because
  `CheckConnectionAsync` clears `Connection` first (#1233). Fix it together with the count.
- **F2** is the same as #1234's first open finding. File them as one entry.

## Seen and left as decided

These were flagged in the PRs and accepted there. None is an entry:

- Pages stay alive and hidden in the DOM. Switching off a feature drops that page's unsaved drafts
  (#1176).
- The headline size has a non-token `vw` middle value (#1211).
- `.settings-card__action` renders an empty wrapper on a host with no price store (#1221).
- The Repositories page resolves devbook folders on every render, with no cache (#1222).
- The usage-metrics note is a non-list child of `role="list"`; this predates the plan (#1217).
- Two repositories under one alias share an `Edit <alias>` label (#1222).

## Not in this plan's scope

`plan/settings-redesign` has no pull request into `main` yet. Merging it is the plan's own last
step, not an entry.
