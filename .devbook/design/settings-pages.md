# Settings Pages

```meta
related: [".devbook/design/interaction-guidelines.md#auto-save-no-save-buttons", ".devbook/design/interaction-guidelines.md#empty-loading-and-error-states", ".devbook/design/accessibility.md#keyboard-navigation", ".devbook/design/typography-and-layout.md#border-radius-and-width", ".devbook/design/color-scheme.md#badge-and-chip-tones"]
```

> The Settings screen is one panel: a header that names the group and says the
> page's state in one sentence, a menu of three groups, and the page beside it.
> Every page has its own address, saves as you go, and declares the card titles
> the search reads. A module that registers a page follows this chapter, so its
> page reads as one of the screen's own.

This chapter records the pattern as it shipped. The shared parts live in
`src/Core/Backlog.UI.Components/Settings` (`SettingsFrame`, `SettingsHeadline`,
`SettingsMenuGroup`, `SettingsCard`, `SettingsSearch`). The screen that composes
them is `src/App/Backlog.Desktop.UI/Settings/Settings.razor`, with its page list in
`SettingsPages.cs` and its menu values in `SettingsBadges.cs`. A module brings a
page as a `SettingsSection` record (`src/Core/Backlog.SharedKernel/SettingsSection.cs`)
registered from its own `.UI` project, as `AddInboxSettings` and
`AddSessionsSettings` do.

## Panel and Header

```meta
related: [".devbook/design/typography-and-layout.md#shadows-and-elevation", ".devbook/design/accessibility.md#target-sizes-and-text"]
```

The screen is drawn inside `SettingsFrame`: the Dashboard's panel, so the two
full-page surfaces read as one family. The header holds, left to right, the way
back, the eyebrow over the headline, and the saved chip beside the search.

| Part | What it shows | Rule |
|---|---|---|
| Panel | `border-radius-lg`, `color-background-alt` mixed into `color-background`, `shadow-md` | A settings page MUST NOT draw a panel of its own inside the frame: the page's region in the content slot has no border, radius, padding or surface, so its cards sit directly on the panel. |
| Way back | An arrow, 44 × 44 px, to the tasks | Its accessible name MUST be said in words (`Back to your tasks`); the arrow is `aria-hidden`. |
| Eyebrow | `Settings · <group>`, from `SettingsPages.Eyebrow` | The group half MUST be the menu group's label, so the eyebrow and the menu never disagree. |
| Headline | The frame's `h1`, one sentence | See *Headline*. |
| Saved chip | A check and `Saved as you go` | Always drawn. It is the screen's statement that no save button exists. |
| Search slot | `SettingsSearch` | Drawn empty when nothing fills it, so the header keeps its shape. |

Rules:

- The page's own outline starts at `h2`. The frame owns the only `h1`.
- The header's right-hand cluster MUST wrap under the headline in a narrow
  panel rather than squeeze the sentence.

## Headline

```meta
```

The headline is one sentence that says the page's state, not its name: how many
features are on, which AI providers report usage, how many accounts this
machine can speak as. A page writes it by rendering `SettingsHeadline`, which
fills the `settings-headline` section outlet in the frame's `h1`.

| Case | The headline reads |
|---|---|
| The page renders a `SettingsHeadline` | That sentence. The last content added wins, so a page's sentence always replaces the title. |
| The page renders none | The page's `SettingsSection.Title`, which the frame writes first. |
| The page is open but hidden behind another | Nothing from that page: the `SettingsPageActive` cascade is false, and `SettingsHeadline` writes only while it is true. |

Rules:

- A page that has a state worth counting MUST write a `SettingsHeadline`. A
  page whose state is a list says how long the list is.
- The headline MUST be one sentence, computed from the store on each render, so
  it moves in the same render as the control that changed it.
- A page MUST NOT render a heading of its own that restates the headline.

## Saved as You Go

```meta
related: [".devbook/design/interaction-guidelines.md#auto-save-no-save-buttons", ".devbook/design/design-principles.md#no-save-buttons--auto-save-everywhere"]
```

Settings keep the product rule in `interaction-guidelines.md#auto-save-no-save-buttons`
with one sharpening: a typed field commits on **blur or Enter**, and a switch,
select or radio commits on change.

- A settings page MUST NOT draw a Save, Apply or Cancel button. Actions that do
  something other than store a value — Add account, Test connection, Remove
  repository — are allowed.
- A field whose value is refused says so in a `setting__status` line beside it
  (`role="status"`), with the input wearing `setting__input--error`. The value is
  not stored, and nothing else on the page changes.

## Menu Groups

```meta
related: [".devbook/design/interaction-guidelines.md#group-shape-says-cardinality"]
```

The menu lists every page under one of three groups, in this order
(`SettingsGroup`):

| Group | Label | Holds |
|---|---|---|
| `Workspace` | Workspace | What the whole app works with: Features, Storage & backup, Repositories, GitHub accounts. |
| `Ai` | AI | The providers the app reads usage and cost from, and the pages about pricing a run. |
| `ProductAreas` | Product areas | One page per area of the product that has settings of its own. |

A `SettingsSection` picks its group with `Group`; it defaults to `ProductAreas`.
Within a group the shell's own pages come first, in `SettingsPages.BuiltIn`'s
order, then the registered sections by ascending `Order`, ties broken by `Id`.

Rules:

- A page about one area of the product MUST be in `ProductAreas`. A page MUST
  be in `Ai` only when it configures a provider or the price of a run, and MUST
  NOT be in `Workspace`, which is the shell's.
- A module MUST publish its `Order` as a named constant beside its registration,
  with a comment naming the page it follows, so the next module can place its
  own without reading the screen.
- A group with no page on offer is not drawn.
- Every menu item is a link to the page's address, never a button. The page on
  screen carries `aria-current="page"` and the raised surface.
- The group label is a paragraph the list names through `aria-labelledby`, not
  a heading, so the menu does not interleave with the page's outline.

## Badges and the Off State

```meta
related: [".devbook/design/color-scheme.md#badge-and-chip-tones", ".devbook/design/typography-and-layout.md#type-scale"]
```

A menu item may carry a short value on the right of its name: a count or a
state, drawn in `color-text-secondary` at `font-size-xs` with tabular figures.
It is text, not a pill.

| Page | Badge | Source |
|---|---|---|
| Features | On over total, `9/19` | `SettingsBadges.Features` |
| AI providers | Providers set up over three, `2/3` | `SettingsBadges.AiProviders` |
| Repositories, GitHub accounts | The count, zero included | `SettingsBadges.Count` |
| Storage & backup | None | — |
| A registered page whose feature is off | `Off` | `SettingsBadges.Off`, drawn by the shell |
| Any other registered page | Its `ISettingsBadgeSource.Badge`, or none | The type `SettingsSection.BadgeSource` names, resolved from the container |

Rules:

- A badge MUST be a count, a fraction, or `Off`. It MUST NOT be a sentence; the
  headline is where a sentence goes.
- A count of zero is drawn as `0`: an empty list says so.
- A null or blank badge draws nothing, not an empty pill.
- A badge source MUST be registered as a singleton and MUST raise `Changed`
  whenever its value may have moved, from any thread. The menu follows a
  setting changed anywhere — this screen, another window, a sync.
- A registered page whose `FeatureKey` is off MUST stay in the menu with the
  `Off` badge. Opening it shows one note in place of its component — *<feature>
  is turned off. Turn it on on the Features page to use these settings.* — with
  the link to Features. The component is disposed, so turning the feature back
  on starts it afresh.
- `Off` wins over the page's own badge source.

## Card Titles and Search

```meta
related: [".devbook/design/interaction-guidelines.md#focus-and-selection", ".devbook/design/accessibility.md#keyboard-navigation"]
```

`SettingsSearch` narrows the menu to the pages whose title, or one of whose
**declared card titles**, holds what is typed, ignoring case. Only titles are
searched, never a field's label or what a page draws, so the search answers
without rendering a page.

- Every page MUST declare `CardTitles`: the titles of the `SettingsCard`s it
  draws, in the order it draws them. A change that adds, renames or drops a card
  MUST change that list in the same change, and a test MUST assert the declared
  list equals the rendered `.settings-card__title`s, as the Dashboard, Model
  prices and Claude Code page tests do.
- A page MUST declare only the cards it currently draws. A card a feature
  leaves undrawn is not declared while that feature is off, so the search never
  lists it and its `#card-<slug>` address names nothing: with usage metrics off
  the AI providers page declares neither *Claude usage API* nor *GitHub usage
  API*, and with the AI assistant off it does not declare *Azure Foundry AI*
  (`SettingsPages.BuiltInAsDrawn`).
- A switched-off page is matched on its title alone: it draws only the note, so
  none of its cards is there to reveal.
- While the search has text, a matched page lists the matched cards under its
  menu link, each a link to `settings/<page>#card-<slug>`. Following one opens
  the page, scrolls the card into view and lights its border in
  `color-border-focus` for a moment, with no fade under reduced motion.
- A search that keeps no page says `No setting matches "<text>".` in the menu's
  place, as a status.
- The search filters on every keystroke; Escape clears it.

## Settings Card

```meta
related: [".devbook/design/typography-and-layout.md#border-radius-and-width", ".devbook/design/accessibility.md#screen-reader--announcements"]
```

A page is a column of `SettingsCard`s, laid out in the `setting-columns` grid,
which wraps cards into columns of at least 22 rem and caps each at 640 px.

| Part | Rule |
|---|---|
| Title | `h2` by default, `settings-card__title`. It names the card's region, so a screen reader's landmark list reads as the page's list of cards. It MUST be one of the page's declared `CardTitles`. |
| Description | One or two sentences under the title saying what the card is for. |
| Action | One control for the whole card, in the header's corner: Add, Test connection, a count. |
| Body | The controls. |
| Surface | `color-background-alt`, `border-radius-md`, a hairline `color-border`. |

Rules:

- A settings page MUST draw its sections as `SettingsCard`, not `Card` and not
  a hand-rolled `section`: the card's title is what the search reveals.
- A card's title MUST name the thing configured (`Claude usage API`,
  `Where the backlog is stored`), never an instruction.
- A card SHOULD hold one subject. Two vendors, two credentials or two failure
  modes are two cards, each with its own status line.

## Figure Tiles

```meta
related: [".devbook/design/typography-and-layout.md#type-scale"]
```

A page whose state is a few figures shows them first, as a row of the shared
`MetricTile` before any card: Features' three tiles (Turned on, Unfinished and
on, Always available), one tile per AI provider, and the Backup card's figures
in a `MetricGrid`.

- A figure tile MUST be the shared `MetricTile`; a page MUST NOT draw a figure in
  a card's prose.
- Each tile carries a label, the figure, and at most one footnote or trend line.
  The figure MUST be named by its label, so nothing is carried by colour.
- Tiles repeat what the headline counts at a glance; they MUST read the same
  store, so the headline and the tiles never disagree.

## Lists That Expand in Place

```meta
related: [".devbook/design/interaction-guidelines.md#action-density-and-overflow", ".devbook/design/accessibility.md#keyboard-navigation"]
```

A setting that is a list of things each with its own settings — repositories,
GitHub accounts — shows one row per thing and opens that thing's editor in place,
under its row.

| List | Row | Opens with |
|---|---|---|
| Repositories | A `DataTable` row: alias with its colour mark, folder, devbook, account | An `Edit` button in the row, which reads `Done` while open |
| GitHub accounts | The whole row is a button: avatar initial, login, a meta line | The row itself |

Rules:

- The row MUST say what each thing is without opening it: where it lives, what
  it reads, which account it uses.
- At most one row is open at a time; opening one closes the other.
- The opening control carries `aria-expanded`, and `aria-controls` naming the
  editor while it is open.
- The editor opens directly under its row, spanning the row's full width, as a
  `repo-card` with its own title and its destructive action in the header: the
  one that keeps everything first (Rename), the one that keeps nothing last
  (Remove repository, Forget this account).
- Opening or closing the editor MUST NOT resize a row. A list laid out as a
  grid keeps one column template in both states: the GitHub accounts list is
  `repeat(auto-fill, …)`, never `auto-fit`, so a lone account is one track wide
  whether its card is closed or open.
- A table that is too wide scrolls sideways inside its own frame; the page never
  scrolls sideways.
- A list with nothing in it says so in a sentence, and what happens meanwhile,
  not with an empty table: *No account yet. Every call goes out as whichever
  account this machine is signed in as.*

## Phone Width

```meta
related: [".devbook/design/typography-and-layout.md#layout-grid-and-breakpoints", ".devbook/design/accessibility.md#target-sizes-and-text"]
```

The menu and the page sit side by side while the panel has room for both. Below
that — `50.25rem`, the menu's `13.75rem` basis, the `spacing-lg` gap and the
page's `35rem` basis — the screen shows one of them, and the address decides
which. The width is the frame's own, asked through the `settings-frame` container
query, never the window's: Settings beside an open pane is as narrow as on a
phone.

| Address | Below `50.25rem` the screen shows |
|---|---|
| `settings` | The grouped list: each group's pages in one rounded card on `color-background-alt`, rows divided by a hairline, 52 px rows with the badge and a chevron, and the search on a row of its own above the groups. No row is marked current. |
| `settings/<page>` | The page alone at full width, no menu and no search. The way back goes to `settings`, named `Back to all settings`, in place of the way back to the tasks. |

Rules:

- A module page MUST lay out within the page column at any width down to the
  phone's, with no fixed width; use `setting-columns` and let it wrap.
- The switch is CSS only: both ways back are in the markup and the stylesheet
  shows one, so a hidden link is out of the tab order too.
- A matched card under a row is a 44 px target of its own.

## Addresses

```meta
related: [".devbook/design/interaction-guidelines.md#shell-header"]
```

Every page has an address, `settings/{page}`, where `{page}` is the
`SettingsSection.Id`. Choosing a page navigates, so Back returns to the page
before and any part of the app can link to a page.

| Address | Opens |
|---|---|
| `settings` | Features beside the menu; the grouped list at phone width |
| `settings/<page>` | That page |
| `settings/<page>#card-<slug>` | That page with the named card brought into view. The slug is the card title in lower case, every run of other characters one hyphen (`SettingsPages.CardFragment`). |
| `settings?page=<page>` | That page; the older link form, still honoured |
| An id nothing offers | Features |

Rules:

- An `Id` MUST be stable and lower-case, and MUST NOT take one of the shell's own
  ids — `features`, `ai`, `storage`, `accounts`, `repositories` — which win a
  collision and leave the registered page out.
- A link to a page MUST be built with `SettingsPages.Href` or the address above,
  never by query string.
- A page opened once stays alive, hidden, while another is on screen, so a
  half-typed field survives a move. It is not redrawn while hidden.

## Registering a Page

```meta
```

The checklist a module's page meets, each item from a chapter above:

1. A `SettingsSection` from the module's `.UI` registration extension, with a
   stable lower-case `Id`, a `Title`, a published `Order` constant, a `Group`, and
   the `FeatureKey` the page belongs to when it has one.
2. `CardTitles` equal to the cards the page draws, and a test asserting it.
3. A `SettingsHeadline` sentence when the page has a state worth counting.
4. Cards as `SettingsCard` in `setting-columns`; figures as `MetricTile`; a
   list of things as rows that expand in place.
5. No save button; fields commit on blur or Enter.
6. A `BadgeSource` registered as a singleton when the menu should show a count.

## Materialization

```meta
```

| Rule | Materialized in |
|---|---|
| Panel, header, headline outlet, saved chip, phone-width switch | `SettingsFrame.razor`, `SettingsHeadline.razor`, the `settings-frame` rules in `components.css`; the unframed page region is `.settings-frame__content > .setting` in the app's `app.css` |
| Menu groups, links, badges, matched cards | `SettingsMenuGroup.razor`, `SettingsMenuItem.cs`, `SettingsPages.Menu` |
| Search over declared titles, card fragments | `SettingsSearch.razor`, `SettingsPages.Find`, `SettingsPages.CardFragment`, `backlogRevealSettingsCard` in `components.js` |
| Off state, badge values | `Settings.razor`, `SettingsBadges.cs` |
| Card | `SettingsCard.razor`; `setting-columns` in the desktop `app.css` |
| Expand in place | `RepositoriesSettingsPage.razor`, `GitHubAccountsSettingsPage.razor` |

The storybook shows the frame, the menu and the card on its *Settings frame*,
*Settings menu* and *Settings card* pages, and each links back to this chapter
from its **Design rules** block: the frame to *Panel and Header* and *Headline*,
the menu to *Menu Groups*, *Badges and the Off State* and *Phone Width*, the
card to *Settings Card* and *Figure Tiles*.
