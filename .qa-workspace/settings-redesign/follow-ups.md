# Settings redesign QA — follow-ups

Plan `settings-redesign`, entry 15 (`settings-qa`). The `backlog` MCP server was unreachable during
the run, so each failure is written here instead of as a Backlog entry. File each one under
`+settings-redesign`. Evidence paths are relative to the worktree root (`D:/sr/513287db`); the
screenshots are not committed.

Every item below was observed in the desktop web harness on `plan/settings-redesign` at `de5324a0`.

## F1 — The shell's bordered panel wraps each page in a second frame

- **Status:** known item, confirmed.
- **What failed:** The Settings shell draws a bordered, rounded panel. Inside it, each page's
  region draws a second bordered panel around its cards. On Features and AI providers that is
  three nested borders: shell, page region and card. At 390 px the nesting is four deep
  (shell, region, card, editor), and the account and repository editors are left about 200 px
  of content width.
- **Evidence:** `.qa-workspace/settings-redesign/02-page-features-1280.png`,
  `06-page-ai-1280.png`, `73-phone-page-repositories-390.png`,
  `84-phone-accounts-editor-390.png`.
- **Suggested fix:** Drop the border, radius and padding from the page region inside
  `SettingsFrame` (`settings-frame` content slot), so a page's cards sit directly on the shell
  panel. Alternatively, drop the shell panel's frame at the narrow breakpoint.

## F2 — Find a setting lists the AI usage cards while usage metrics are off (dead links)

- **Status:** known item, confirmed.
- **What failed:** With `AI usage metrics` off, searching `usage` lists *Claude usage API* and
  *GitHub usage API* under AI providers. Choosing *Claude usage API* opens
  `/settings/ai#card-claude-usage-api`, but the page draws no such card, so nothing is
  highlighted. With the feature on, the same link opens and highlights the card.
- **Evidence:** `20-find-usage-1280.png`, `21-find-usage-claude-deadlink-1280.png` (off);
  `61-find-github-usage-live-1280.png` (on).
- **Suggested fix:** `SettingsPages.BuiltIn` declares the AI page's card titles statically. Make
  the declared titles depend on `AppFeatures.UsageMetrics`, or pass `Find` a per-card predicate
  as well as `switchedOff`, so cards the page will not draw are not matched.

## F3 — The AI providers badge counts providers the page does not show

- **Status:** new.
- **What failed:** With `AI usage metrics` off, the page shows only Azure Foundry, and its
  headline reads "Azure Foundry reports usage." The menu badge still reads `2/3`, because
  `Settings.razor` `BadgeFor` counts Claude and GitHub whatever the feature's state. With the
  feature on, the badge (`2/3`) and the headline ("GitHub and Azure Foundry report usage;
  Claude is not set up.") agree.
- **Evidence:** `06-page-ai-1280.png` (off: badge `2/3`, one provider on the page);
  `60-ai-usage-metrics-on-1280.png` (on).
- **Suggested fix:** Count only the providers `AiSettingsPage` offers, in `BadgeFor` and
  `SettingsBadges.AiProviders`: Foundry alone (`1/1`) while usage metrics are off.

## F4 — GitHub account rows resize when the editor opens with fewer accounts than columns

- **Status:** known item, confirmed.
- **What failed:** With one account, its row is 844 px wide while the editor is closed. Opening
  the editor shrinks the row to 279 px, a single grid column, and the editor opens below it.
- **Evidence:** `51-accounts-added-1280.png` (open, 279 px); `52-accounts-editor-closed-1280.png`
  (closed, 844 px).
- **Suggested fix:** Give the account list one column template in both states. Either keep
  `repeat(auto-fill, minmax(…))` whether or not the editor is open, or keep the closed list
  full width and do not reflow it into columns when the editor opens.

## F5 — An account added by login reads "Signed in" when the GitHub CLI is not signed in as it

- **Status:** new.
- **What failed:** Adding the login `qa-settings-check` through *Add an account by login* gives
  a row that reads "Signed in · 0 repositories", and the headline "1 GitHub account signed in."
  The open editor says the opposite: "The GitHub CLI on this machine is not signed in as
  'qa-settings-check'."
- **Evidence:** `51-accounts-added-1280.png`.
- **Suggested fix:** Base the row status and the headline on the account's credential state, the
  same check the editor uses. For example "Not signed in" or "Needs sign-in", with the headline
  counting only accounts whose credential resolves.

## F6 — A gap of about 190 px under the login or title on account and repository cards below 720 px

- **Status:** known item, confirmed.
- **What failed:** At 390 px, the open repository editor leaves 188 px between the title
  (`JSdotNet/Backlog`) and its first control (*Rename*). The account editor's header is 247 px
  tall for two lines of text plus *Forget this account*.
- **Evidence:** `83-phone-repositories-editor-390.png`, `84-phone-accounts-editor-390.png`.
- **Suggested fix:** The `repo-card__header` keeps its desktop row layout (or a min-height) when
  its actions wrap. Below 720 px, stack the header with `align-items: start` and no fixed height.

## F7 — The phone list announces Features as the current page

- **Status:** known item, confirmed.
- **What failed:** At 390 px `/settings` shows the grouped list, yet the Features link carries
  `aria-current="page"`. The h1 above the list is Features' own headline ("12 of 19 features
  are on — 7 of them still unfinished."), not a heading for the list.
- **Evidence:** `70-phone-list-390.png`; accessibility tree: `Features 12/19` with
  `aria-current=page`.
- **Suggested fix:** While the narrow layout shows the list (no page chosen), mark no item
  current and title the frame "Settings".

## F8 — The h1 shows a focus outline after navigating

- **Status:** known item, confirmed.
- **What failed:** After every page change the shell moves focus to the h1 (`tabindex="-1"`).
  The browser draws its focus outline around the h1's full box, which overlaps the eyebrow. The
  outline is visible on every page load at both widths.
- **Evidence:** `40-repositories-1280.png`, `70-phone-list-390.png`.
- **Suggested fix:** Keep the programmatic focus for screen readers, but suppress the outline on
  the headline: `.settings-frame__headline:focus:not(:focus-visible) { outline: none; }`, or
  `outline: none` on that `tabindex=-1` heading.

## F9 — Missing space in the Repositories summary line

- **Status:** new.
- **What failed:** The summary reads "`JSdotNet/Backlog` is configured, and 9 more.Type
  `@backlog` on an entry…", with no space between the two sentences.
- **Evidence:** `40-repositories-1280.png`; source
  `src/App/Backlog.Desktop.UI/Settings/RepositoriesSettingsPage.razor` lines 114–117.
- **Suggested fix:** Add a space (or `@(" ")`) before the `<span>Type …</span>` sentence.
