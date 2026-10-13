# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it. The plan
> `+brain` builds the Brain's requirements, and each gains its `tests` as it
> lands; one with no `tests` is not built yet.

## Brain scope

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#brain-scope]
```

> The requirements of the Brain chip in the header's repository scope (local
> ADR 0027).

### Requirement: The Brain chip leads the scope

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.The_brain_chip_leads_the_scope_behind_a_divider_in_band_two, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.With_no_repository_configured_the_scope_holds_the_brain_alone, unit:dotnet:Backlog.UI.Components.UnitTests.ScopeGlyphIconTests.The_brain_keeps_the_icon_contract]
```

The system SHALL render the Brain chip first in the repository scope, behind a divider, in band 2, with no control that removes it.

#### Scenario: Two repositories configured

- **Given** two repositories are configured
- **When** the desktop shell opens
- **Then** the scope shows the Brain chip, a divider, then the two repository chips, and the Brain chip offers no remove control

#### Scenario: No repository configured

- **Given** no repository is configured
- **When** the desktop shell opens
- **Then** the scope renders with the Brain chip alone

### Requirement: Brain alone narrows to the tasks with no repository

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.The_brain_alone_keeps_the_rows_no_repository_claims, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.A_repo_token_naming_nothing_configured_counts_as_no_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneBoardTests.Columns_by_priority_run_highest_first_and_by_repository_follow_the_configured_ones, unit:dotnet:Backlog.Desktop.UI.UnitTests.InProgressViewTests.The_brain_scope_narrows_the_view_like_the_task_list, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionsPaneTests.The_brain_keeps_the_sessions_of_no_configured_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.PullRequestsPaneTests.The_brain_keeps_the_pull_requests_of_tasks_filed_against_no_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.PullRequestsPaneTests.The_brain_alone_with_nothing_linked_says_pull_requests_belong_to_repositories, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.TaskInsightsTests.The_brain_counts_the_tasks_that_name_no_repository, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.SessionInsightsTests.The_brain_keeps_the_rows_of_no_configured_repository, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.RepositoryFocusTests.The_brain_is_kept_in_its_place_and_covers_the_work_filed_nowhere]
```

The system SHALL show only the tasks filed against no repository in Tasks, Board, Calendar, In progress, Dashboard, Sessions and Pull requests while Brain is the only chip pressed.

#### Scenario: Tasks

- **Given** one task filed against no repository and one filed against `backlog`
- **When** the reader presses Brain on the Tasks view
- **Then** only the task with no repository is listed

#### Scenario: Pull requests with nothing to show

- **Given** no pull request is linked to a task filed against no repository
- **When** the reader presses Brain and opens Pull requests
- **Then** the list is empty and its empty state says that pull requests belong to repositories

### Requirement: Ctrl adds Brain to the pressed repositories

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.The_brain_chip_presses_as_a_repository_chip_does, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.The_brain_beside_a_repository_keeps_both_and_the_anchor_stays, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.The_brain_pressed_alone_replaces_the_repositories]
```

The system SHALL add Brain to the chips already pressed on a Ctrl-press and press it alone on a plain press.

#### Scenario: Ctrl beside a repository

- **Given** `backlog` is pressed
- **When** the reader Ctrl-presses Brain
- **Then** both chips are pressed, `backlog` stays the anchor, and the tasks of `backlog` and the tasks with no repository are listed

#### Scenario: Plain press

- **Given** `backlog` is pressed
- **When** the reader presses Brain without a modifier
- **Then** Brain is the only chip pressed

### Requirement: The scope comes back with Brain in its place

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.The_brain_is_remembered_in_its_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_remembered_brain_comes_back_as_the_anchor, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.A_remembered_brain_comes_back_in_its_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellNavigationStoreTests.The_brain_survives_a_restart_as_the_empty_value_in_its_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellNavigationStoreTests.A_remembered_no_repository_only_state_migrates_to_the_brain]
```

The system SHALL reopen a remembered scope with Brain in the position it was stored in, the anchor when it was first, and drop a remembered alias that is no longer configured.

#### Scenario: Brain was the anchor

- **Given** the reader pressed Brain and then Ctrl-pressed `backlog`
- **When** the shell is opened afresh
- **Then** Brain and `backlog` are pressed, and Brain is the anchor

#### Scenario: A repository was removed

- **Given** the remembered scope is Brain and a repository no longer configured
- **When** the shell is opened afresh
- **Then** Brain alone is pressed

### Requirement: The No repo chip is gone

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.MyDayScopeTests.The_scope_group_holds_every_scope_and_the_bar_holds_no_areas]
```

The system SHALL offer no *No repo* chip on the Tasks filter bar.

#### Scenario: The filter bar

- **Given** the Tasks view is on screen
- **When** the reader looks at the filter bar
- **Then** no chip reads *No repo*, and narrowing to the tasks with no repository is done with the Brain chip

### Requirement: The chip asks for a folder until one is set

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Without_a_folder_the_chip_asks_to_be_set_up_and_still_scopes, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Without_a_folder_the_pane_on_the_brain_asks_for_one, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainFolderStateTests.With_no_folder_set_the_pane_asks_for_one_and_links_to_settings]
```

The system SHALL label the Brain chip **Set up your Brain** while no Brain folder is set, and still scope the tasks with it.

#### Scenario: First use

- **Given** no Brain folder is set
- **When** the reader presses the chip reading Set up your Brain
- **Then** only the tasks with no repository are listed, and the Devbook pane on the Brain asks for a folder

## Brain folder setup

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#brain-folder-setup]
```

> The requirements of choosing the Brain folder and scaffolding its convention
> (local ADR 0026).

### Requirement: The folder has no default

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests.A_fresh_install_has_no_brain_folder_and_writes_no_file, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.A_fresh_install_shows_the_brain_card_with_an_empty_field]
```

The system SHALL leave the Brain folder unset until the person picks one.

#### Scenario: A fresh install

- **Given** the app has never had a Brain folder
- **When** the reader opens Settings › Storage
- **Then** the Brain card's folder field is empty

### Requirement: The path stays out of the folder

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests.Picking_an_empty_folder_leaves_it_empty, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.Picking_a_folder_keeps_its_path_in_the_app_settings_and_the_folder_empty]
```

The system SHALL keep the Brain folder's path in the app's settings and write it into no file in the folder.

#### Scenario: Picking a folder

- **Given** an empty folder outside every repository
- **When** the reader picks it as the Brain folder
- **Then** the app's settings hold its path and the folder is still empty

### Requirement: Create conventions scaffolds without overwriting

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSetupTests.Create_conventions_scaffolds_an_empty_folder, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSetupTests.Create_conventions_never_overwrites_a_readme_already_there, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.Create_conventions_scaffolds_the_folder_and_then_goes_away, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.Create_conventions_leaves_the_persons_readme_unchanged]
```

The system SHALL write `README.md` marked `index: root`, `CONVENTIONS.md` and every area folder they declare on **Create conventions**, and leave every file already there unchanged.

#### Scenario: An empty folder

- **Given** an empty Brain folder
- **When** the reader presses Create conventions
- **Then** the folder holds `README.md` with `index: root`, `CONVENTIONS.md`, and the folders `_inbox`, `projects`, `areas`, `resources`, `people`, `journal` and `archive`

#### Scenario: A README already there

- **Given** a Brain folder whose `README.md` the person wrote
- **When** the reader presses Create conventions
- **Then** `CONVENTIONS.md` and the area folders are written and `README.md` is unchanged

### Requirement: The card says where the database is

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.A_onedrive_folder_is_said_to_be_synced_without_a_warning, unit:dotnet:Backlog.UI.Components.UnitTests.BrainFolderCardTests.A_synced_folder_is_information_not_a_warning]
```

The system SHALL state on the Brain card whether the folder is synced, as information, and that the Brain's search database is built in the app's storage and never synced.

#### Scenario: A OneDrive folder

- **Given** the Brain folder sits under OneDrive
- **When** the reader opens Settings › Storage
- **Then** the card says the folder is synced, without a warning, and says the search database lives in the app's storage

## Brain mode of the Devbook pane

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#brain-mode-of-the-devbook-pane]
```

> The requirements of the Devbook pane while it shows the Brain (local ADRs 0026
> and 0027).

### Requirement: The pane follows the anchor and defaults to the Brain

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Without_a_remembered_scope_the_devbook_option_offers_the_brain, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.Nothing_pressed_shows_the_brain_with_its_badge_path_and_declared_areas, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.The_pane_follows_the_anchor_and_the_switch_opens_on_its_side, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.A_repository_anchor_shows_that_repository_unchanged]
```

The system SHALL show the Brain in the Devbook pane when nothing is pressed or the scope's anchor is Brain, and the anchor repository's devbook otherwise.

#### Scenario: Nothing pressed

- **Given** no chip is pressed and a Brain folder is set
- **When** the reader opens the Devbook pane
- **Then** the pane shows the Brain with its Brain badge

#### Scenario: A repository anchor

- **Given** `backlog` is pressed
- **When** the reader opens the Devbook pane
- **Then** the pane shows the devbook of `backlog`

### Requirement: The areas are the declared ones

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.Nothing_pressed_shows_the_brain_with_its_badge_path_and_declared_areas, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.Declared_areas_are_the_roots_in_their_declared_order, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.With_no_areas_declared_the_seven_para_areas_are_the_tabs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.No_declaration_gives_the_para_roots]
```

The system SHALL show one tab per area `CONVENTIONS.md` declares, in its order, and the seven PARA areas when it declares none.

#### Scenario: Declared areas

- **Given** a `CONVENTIONS.md` declaring the areas *Work* (`work`) and *Home life* (`home`)
- **When** the pane shows the Brain
- **Then** it shows the tabs *Work* and *Home life* and no other

#### Scenario: No declaration

- **Given** a `CONVENTIONS.md` declaring no areas
- **When** the pane shows the Brain
- **Then** it shows the tabs of the PARA set

### Requirement: The switch leaves the task filter alone

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.The_switch_flips_the_pane_and_leaves_the_scope_and_the_rows_alone, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainSwitchTests.Pressing_the_repository_side_asks_the_shell_for_the_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainSwitchTests.On_the_repository_side_the_switch_stays_and_the_brain_chrome_goes]
```

The system SHALL flip the pane between the Brain and the repository with the `Brain | <repo>` switch without changing which tasks are listed.

#### Scenario: Brain and a repository pressed

- **Given** Brain and `backlog` are pressed and the pane shows the Brain
- **When** the reader picks `backlog` on the pane's switch
- **Then** the pane shows the devbook of `backlog` and the task list is unchanged

### Requirement: A Brain note is always editable

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainNoteEditTests.Editing_a_note_writes_the_file_in_the_brain_folder, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainNoteEditTests.A_brain_moved_to_another_folder_reads_and_writes_the_note_there]
```

The system SHALL let the reader edit any Brain note in the pane and save it to its file in the Brain folder.

#### Scenario: Editing a note

- **Given** the pane shows a note in the Brain's `projects` area
- **When** the reader changes its body
- **Then** the note's file in the folder holds the new body

### Requirement: A conflict copy warns and changes nothing

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainConflictTests.A_conflict_copy_is_named_beside_its_original_and_neither_file_changes, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainConflictTests.No_pair_draws_no_warning, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSourceTests.Listing_conflict_copies_changes_neither_file]
```

The system SHALL warn in the pane when the Brain folder holds a conflict copy `<name>-<MACHINE>.md` beside `<name>.md`, and change neither file.

#### Scenario: OneDrive kept both

- **Given** the Brain folder holds `plan.md` and `plan-LAPTOP.md`
- **When** the pane shows the Brain
- **Then** a warning names both files and both are unchanged

### Requirement: Nothing derived lands in the folder

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.Nothing_derived_lands_in_the_brain_folder, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.The_brain_s_database_is_keyed_by_its_folder_and_found_from_the_folder, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderWatchTests.A_rebuilt_brain_database_tells_open_panels]
```

The system SHALL build the Brain's database in the app's storage under the folder's absolute path and write no database file or sidecar into the Brain folder.

#### Scenario: After a build

- **Given** a Brain folder holding notes
- **When** the app has built the Brain's database
- **Then** the folder holds no SQLite file and no sidecar, and the database is in the app's storage

## Repo links and backlinks

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#repo-links-and-backlinks]
```

> The requirements of a Brain note's links into repository devbooks and the
> backlinks a repository chapter shows its owner (local ADR 0027).

### Requirement: A repo link opens the repository chapter

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Following_a_repo_link_opens_the_repository_chapter_and_keeps_the_scope, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Following_a_repo_link_to_the_anchor_from_the_switch_leaves_the_brain, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.Following_a_gold_chip_asks_for_the_repository_chapter, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.A_configured_alias_with_its_section_resolves, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainBacklinkTests.A_repo_link_reads_as_alias_path_and_slug]
```

The system SHALL open the named repository's devbook at the named chapter when the reader follows a `repo:<alias>/<devbook path>#slug` link whose alias is configured.

#### Scenario: A configured alias

- **Given** a Brain note linking `repo:backlog/.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md`
- **When** the reader follows the gold chip
- **Then** the Devbook pane shows that chapter of `backlog`

### Requirement: An unknown alias is not an error

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.An_alias_this_machine_has_not_got_is_unresolved_and_says_so, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.A_note_draws_its_repo_links_as_chips_gold_when_resolved_and_dashed_when_not]
```

The system SHALL render a repo link whose alias is not configured on this machine as a dashed chip reading *not on this machine*.

#### Scenario: Another machine's repository

- **Given** a Brain note linking `repo:client-app/.devbook/domain/context-map.md`, and no repository `client-app` here
- **When** the pane shows the note
- **Then** the link is a dashed chip reading not on this machine, and no error is shown

### Requirement: Backlinks reach only the owner and write nothing

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainBacklinkTests.A_repository_chapter_lists_every_brain_note_linking_to_it_once, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainBacklinkTests.Reading_backlinks_writes_nothing_into_the_brain, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainBacklinkTests.The_reader_asks_the_brain_s_own_database, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.A_repository_chapter_shows_the_brain_notes_linking_to_it_to_its_owner, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.A_chapter_nothing_links_to_draws_no_box_and_the_brain_draws_none_either]
```

The system SHALL list under a repository chapter the owner's Brain notes that link to it, marked *Only you see this*, and write nothing into the repository.

#### Scenario: A linked chapter

- **Given** two Brain notes link to a chapter of `backlog`
- **When** the reader opens that chapter
- **Then** a From your Brain box lists both notes with Only you see this, and the repository's working tree is unchanged

## Unified search

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#unified-search]
```

> The requirements of one search across the Brain and every configured
> repository's devbook.

### Requirement: Results are grouped by source, Brain first

```meta
type: requirement
status: draft
```

The system SHALL group search results by the devbook they came from and put the Brain's group first.

#### Scenario: Everywhere

- **Given** a word that occurs in a Brain note and in a chapter of `backlog`
- **When** the reader searches for it with Everywhere
- **Then** the Brain group comes first and the `backlog` group after it

### Requirement: A scope chip narrows to one source

```meta
type: requirement
status: draft
```

The system SHALL search only the Brain under the Brain chip and only one repository's devbook under that repository's chip.

#### Scenario: Brain only

- **Given** a word that occurs in a Brain note and in a chapter of `backlog`
- **When** the reader searches for it with the Brain chip
- **Then** only the Brain note is listed

### Requirement: A source with no database is named

```meta
type: requirement
status: draft
```

The system SHALL name a source whose database has not been built as not searchable, beside the groups of the sources that answered, rather than leave it out.

#### Scenario: A repository with no index

- **Given** a configured repository whose devbook database has not been built
- **When** the reader searches with Everywhere
- **Then** that repository is listed as not searchable yet, with the reason, after the groups that answered

### Requirement: A result opens in its own devbook

```meta
type: requirement
status: draft
```

The system SHALL open a result in the devbook it came from, moving the pane to that devbook when it shows another, and SHALL leave the header's scope and the task filter unchanged.

#### Scenario: A repository chapter found on the Brain

- **Given** the pane shows the Brain and a search lists a chapter of `backlog`
- **When** the reader opens that result
- **Then** the pane shows the `backlog` devbook on that chapter, and the header's scope is unchanged

## Atlas edges

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#atlas-edges]
```

> The requirements of drawing repo links in the Brain's atlas.

### Requirement: A resolved repo link is an edge to the repository chapter

```meta
type: requirement
status: draft
```

The system SHALL draw each resolved repo link in the Brain's atlas as an edge to a node for the repository chapter, labelled with the repository alias.

#### Scenario: One link

- **Given** a Brain note with a resolved repo link into `backlog`
- **When** the reader opens the Brain's atlas
- **Then** an edge labelled `backlog` runs from the note to a node for that chapter, in a group named `backlog`

#### Scenario: Two spellings of one chapter

- **Given** two Brain notes linking one chapter as `repo:backlog/.devbook/arc42/x.md` and `repo:Backlog/.arc42/x.md`
- **When** the reader opens the Brain's atlas
- **Then** one node stands for the chapter, with an edge from each note

#### Scenario: Following the chapter

- **Given** the Brain's atlas with a resolved repository node selected
- **When** the reader presses its chip
- **Then** the pane shows that repository's devbook at the chapter and the atlas closes

### Requirement: An unresolved repo link is drawn dashed

```meta
type: requirement
status: draft
```

The system SHALL draw a repo link this machine cannot resolve as a dashed edge.

#### Scenario: Another machine's repository

- **Given** a Brain note linking an alias not configured here
- **When** the reader opens the Brain's atlas
- **Then** the edge is dashed, its node is a hollow ring, and the node's chip reads *not on this machine*

## Capture into the Brain

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#capture-into-the-brain]
```

> The requirements of writing a new note into the Brain, from the pane and from
> Inbox triage.

### Requirement: Capture writes a note into the inbox area

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainCaptureTests.Capture_writes_a_note_into_inbox_and_opens_it, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainNoteWriterTests.A_capture_lands_in_inbox_under_the_para_default, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainNoteWriterTests.A_capture_lands_in_the_area_the_conventions_name_the_inbox, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainNoteWriterTests.A_taken_name_gets_the_next_free_one_and_nothing_is_overwritten]
```

The system SHALL write a capture's title and body as a new note with a `meta` block in the Brain's inbox area.

#### Scenario: The PARA default

- **Given** a Brain under the PARA default
- **When** the reader captures the title "Ideas for the sync" with a body
- **Then** a new note in `_inbox` carries that title, that body and a `meta` block

### Requirement: Typed text never becomes structure

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainNoteTextTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainCaptureTests.Typed_structure_is_written_as_text, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBrainTargetTests.A_typed_fence_in_an_inbox_note_stays_text]
```

The system SHALL write a capture's text so that no heading, fence or `meta` block typed into it becomes the note's structure.

#### Scenario: A typed fence

- **Given** a capture body holding a line of three backticks followed by `meta`
- **When** the reader captures it
- **Then** the note has one `meta` block, its own, and the typed line reads back as text

### Requirement: To Brain writes a note and nothing on the feed

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBrainTargetTests.An_inbox_note_becomes_a_brain_note_with_its_title_and_body, unit:dotnet:Backlog.Modules.Inbox.UnitTests.RouteToBrainTests.An_inbox_note_keeps_its_title_and_body_and_stays_on_the_phone]
```

The system SHALL write an Inbox item routed **To Brain** as a note in the Brain's inbox area, keeping its title and body, and put no `note` document for it on the task feed.

#### Scenario: An Inbox note

- **Given** an Inbox note titled "Standup"
- **When** the reader routes it To Brain
- **Then** the Brain's inbox area holds a note titled "Standup" with the same body, and the item is routed

## MCP Brain scope

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#mcp-brain-scope]
```

> The requirements of the knowledge tools the app serves over MCP, on the Brain.

### Requirement: The knowledge tools take the Brain scope

```meta
type: requirement
status: draft
```

The system SHALL answer the tools that list knowledge contexts, read a chapter, and list and resolve remarks for the Brain's reserved scope as they do for a repository alias.

#### Scenario: Reading a Brain note

- **Given** a Brain note in `projects`
- **When** an assistant reads it through the read-chapter tool with the Brain scope
- **Then** the tool answers with the note's text

### Requirement: The capture tool writes into the inbox area

```meta
type: requirement
status: draft
```

The system SHALL write a note sent to the capture tool into the Brain's inbox area through the writer the pane's Capture uses.

#### Scenario: An assistant captures

- **Given** a Brain folder is set
- **When** an assistant calls the capture tool with a title and a body
- **Then** a new note with that title and body is in the Brain's inbox area
