# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it. The Brain's
> requirements belong to the plan `+brain`: the roadmap item tagged `brain`, written
> with its [Plan Sigil](../roadmap/domain.md#plan-sigil). That plan builds local
> [ADR 0026](../../arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md)
> and local ADR 0027.

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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.The_brain_chip_leads_the_scope_behind_a_divider_in_band_two, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.With_no_repository_configured_the_scope_holds_the_brain_alone, unit:dotnet:Backlog.UI.Components.UnitTests.ScopeGlyphIconTests.The_brain_keeps_the_icon_contract, e2e:dotnet:Backlog.EndToEndTests.BrainTests.The_brain_chip_narrows_the_tasks_to_those_filed_against_no_repository]
```

The system SHALL render the Brain chip first in the repository scope, behind a divider, in [band 2](../../design/color-scheme.md#the-brains-accent), with no control that removes it.

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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.The_brain_alone_keeps_the_rows_no_repository_claims, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.A_repo_token_naming_nothing_configured_counts_as_no_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneBoardTests.Columns_by_priority_run_highest_first_and_by_repository_follow_the_configured_ones, unit:dotnet:Backlog.Desktop.UI.UnitTests.SessionsPaneTests.The_brain_keeps_the_sessions_of_no_configured_repository, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.TaskInsightsTests.The_brain_counts_the_tasks_that_name_no_repository, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.SessionInsightsTests.The_brain_keeps_the_rows_of_no_configured_repository, unit:dotnet:Backlog.Modules.Dashboard.UnitTests.RepositoryFocusTests.The_brain_is_kept_in_its_place_and_covers_the_work_filed_nowhere, e2e:dotnet:Backlog.EndToEndTests.BrainTests.The_brain_chip_narrows_the_tasks_to_those_filed_against_no_repository]
```

The system SHALL show only the tasks filed against no repository in Tasks, Board, Calendar, Dashboard and Sessions while Brain is the only chip pressed.

#### Scenario: Tasks

- **Given** one task filed against no repository and one filed against `backlog`
- **When** the reader presses Brain on the Tasks view
- **Then** only the task with no repository is listed

### Requirement: Pull requests read the scope without the Brain

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.PullRequestsPaneTests.The_brain_alone_lists_every_pull_request, unit:dotnet:Backlog.Desktop.UI.UnitTests.PullRequestsPaneTests.The_brain_beside_a_repository_lists_that_repositorys_pull_requests]
```

The system SHALL list every pull request while Brain is the only chip pressed, and the pressed repositories' pull requests while Brain is pressed beside them.

#### Scenario: Brain alone

- **Given** pull requests in `backlog` and in `tools`
- **When** the reader presses Brain and opens Pull requests
- **Then** both pull requests are listed

#### Scenario: Brain beside a repository

- **Given** pull requests in `backlog` and in `tools`
- **When** the reader presses Brain, Ctrl-presses `backlog` and opens Pull requests
- **Then** only the pull request in `backlog` is listed

### Requirement: Ctrl adds Brain to the pressed repositories

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.The_brain_chip_presses_as_a_repository_chip_does, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.The_brain_beside_a_repository_keeps_both_and_the_anchor_stays, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.The_brain_pressed_alone_replaces_the_repositories, e2e:dotnet:Backlog.EndToEndTests.BrainTests.The_brain_chip_narrows_the_tasks_to_those_filed_against_no_repository]
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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.The_brain_is_remembered_in_its_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_remembered_brain_comes_back_as_the_anchor, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksRepositoryScopeTests.A_remembered_brain_comes_back_in_its_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellNavigationStoreTests.The_brain_survives_a_restart_as_the_empty_value_in_its_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.ShellNavigationStoreTests.A_remembered_no_repository_only_state_migrates_to_the_brain, e2e:dotnet:Backlog.EndToEndTests.BrainTests.The_scope_survives_a_restart_with_the_brain_as_its_anchor]
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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Without_a_folder_the_chip_asks_to_be_set_up_and_still_scopes, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Without_a_folder_the_pane_on_the_brain_asks_for_one, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainFolderStateTests.With_no_folder_set_the_pane_asks_for_one_and_links_to_settings, e2e:dotnet:Backlog.EndToEndTests.BrainTests.First_use_asks_for_a_folder_and_create_conventions_scaffolds_it]
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
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests.Picking_an_empty_folder_leaves_it_empty, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.Picking_a_folder_keeps_its_path_in_the_app_settings_and_the_folder_empty, e2e:dotnet:Backlog.EndToEndTests.BrainTests.First_use_asks_for_a_folder_and_create_conventions_scaffolds_it]
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
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSetupTests.Create_conventions_scaffolds_an_empty_folder, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSetupTests.Create_conventions_never_overwrites_a_readme_already_there, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.Create_conventions_scaffolds_the_folder_and_then_goes_away, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.Create_conventions_leaves_the_persons_readme_unchanged, e2e:dotnet:Backlog.EndToEndTests.BrainTests.First_use_asks_for_a_folder_and_create_conventions_scaffolds_it]
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

### Requirement: A folder that cannot hold the Brain is refused

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/context.md#brain-folder]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests.A_folder_inside_a_repository_is_refused, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests.A_folder_inside_a_linked_worktree_is_refused, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests.A_relative_path_is_refused_in_words, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainSettingsStoreTests.A_file_is_not_a_folder, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.A_folder_inside_a_repository_is_refused_on_the_card]
```

The system SHALL refuse, in words on the Brain card, a Brain folder inside a repository's working tree, a relative path, or a file, and keep no folder for it.

#### Scenario: A folder inside a repository

- **Given** no Brain folder is set and `D:\repo` is a repository
- **When** the reader enters `D:\repo\notes` as the Brain folder
- **Then** the card says the folder must be outside every repository, and no Brain folder is set

#### Scenario: A relative path

- **Given** no Brain folder is set
- **When** the reader enters `notes\brain` as the Brain folder
- **Then** the card asks for a full path, and no Brain folder is set

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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Without_a_remembered_scope_the_devbook_option_offers_the_brain, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.Nothing_pressed_shows_the_brain_with_its_badge_path_and_declared_areas, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.The_pane_follows_the_anchor_and_the_switch_opens_on_its_side, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.A_repository_anchor_shows_that_repository_unchanged, e2e:dotnet:Backlog.EndToEndTests.BrainTests.The_pane_shows_the_declared_areas_and_the_journal_newest_first]
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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.Nothing_pressed_shows_the_brain_with_its_badge_path_and_declared_areas, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.Declared_areas_are_the_roots_in_their_declared_order, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.With_no_areas_declared_the_seven_para_areas_are_the_tabs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.No_declaration_gives_the_para_roots, e2e:dotnet:Backlog.EndToEndTests.BrainTests.The_pane_shows_the_declared_areas_and_the_journal_newest_first]
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

### Requirement: A dated area lists newest first

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.The_journal_tab_lists_and_opens_the_newest_entry_first, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.The_journal_sorts_newest_first_at_every_level_and_other_areas_by_name, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.An_area_conventions_marks_as_dated_sorts_newest_first, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.A_dated_area_is_the_journal_or_one_its_holds_line_calls_dated, e2e:dotnet:Backlog.EndToEndTests.BrainTests.The_pane_shows_the_declared_areas_and_the_journal_newest_first]
```

The system SHALL list the notes of a dated area — the `journal` folder, or an area whose `Holds` line says *dated* — newest first by name, at every level, and every other area by name.

#### Scenario: The journal

- **Given** a journal holding `2026-09-30.md`, `2026-10-01.md` and `2026-10-13.md`
- **When** the reader opens the Journal tab
- **Then** the menu lists `2026-10-13.md` first and the pane opens it

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

### Requirement: The Brain carries no repository chrome

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.The_repository_only_chrome_is_absent_and_its_services_are_not_asked, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.On_the_repository_the_same_harness_draws_the_repository_only_chrome]
```

The system SHALL leave branch updates, sync verdicts and scenario marks out of the pane while it shows the Brain.

#### Scenario: The same devbook content, Brain against repository

- **Given** a devbook with a scenario page and sync verdicts for its chapters
- **When** the pane shows it for a repository, and then shows the Brain
- **Then** the repository view draws the update action, the verdict badges and the scenario mark, and the Brain view draws none of them

### Requirement: The header toggle names what the pane shows

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.With_nothing_pressed_the_devbook_option_names_the_brain, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.With_the_brain_as_the_anchor_the_devbook_option_names_the_brain, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.With_a_repository_as_the_anchor_the_devbook_option_names_the_devbook, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.In_a_mixed_scope_the_devbook_option_follows_the_switch]
```

The system SHALL label the header's Devbook toggle *Brain* while the pane shows the Brain, or would on opening, and *Devbook* otherwise.

#### Scenario: Nothing pressed, pane closed

- **Given** no chip is pressed and the Devbook pane is closed
- **When** the reader looks at the header
- **Then** the toggle reads *Brain* and its tooltip reads *Open the Brain*

#### Scenario: A repository anchor

- **Given** `backlog` is pressed
- **When** the reader looks at the header
- **Then** the toggle reads *Devbook*

#### Scenario: The switch flips the toggle

- **Given** Brain and `backlog` are pressed and the pane shows the Brain
- **When** the reader picks `backlog` on the pane's switch
- **Then** the toggle reads *Devbook*, and picking Brain again makes it read *Brain*

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

### Requirement: A remark on a Brain note is the Brain's

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainRemarkTests.A_remark_left_on_a_note_is_stored_under_the_brain_alias_by_the_notes_path, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainRemarkTests.A_remark_resolved_elsewhere_is_drawn_resolved_without_reopening_the_note, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainRemarkTests.A_repository_remark_on_the_same_path_is_not_the_notes, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.Listing_brain_remarks_answers_the_written_live_ones_on_the_brain]
```

The system SHALL let the reader leave a remark on a block of a Brain note and store it under the Brain's scope by the note's path from the Brain's root.

#### Scenario: A remark the agent can answer

- **Given** the pane shows the Brain note `projects/plan.md`
- **When** the reader leaves the remark *Which week?* on its second block
- **Then** the agent lists that remark for `projects/plan.md` on `#brain`, and resolving it there shows it resolved in the pane

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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Following_a_repo_link_opens_the_repository_chapter_and_keeps_the_scope, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Following_a_repo_link_to_the_anchor_from_the_switch_leaves_the_brain, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.Following_a_gold_chip_asks_for_the_repository_chapter, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.A_configured_alias_with_its_section_resolves, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainBacklinkTests.A_repo_link_reads_as_alias_path_and_slug, e2e:dotnet:Backlog.EndToEndTests.BrainTests.A_repo_link_opens_the_repository_chapter_and_the_brain_chip_brings_the_brain_back]
```

The system SHALL open the named repository's devbook at the named chapter when the reader follows a `repo:<alias>/<devbook path>#slug` link whose alias is configured.

#### Scenario: A configured alias

- **Given** a Brain note linking `repo:backlog/.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md`
- **When** the reader follows the gold chip
- **Then** the Devbook pane shows that chapter of `backlog`

### Requirement: The Brain chip brings the Brain back after a repo link

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Leaving_the_brain_scope_after_a_repo_link_brings_the_brain_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Pressing_the_brain_chip_after_a_repo_link_from_an_empty_scope_brings_the_brain_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_repo_link_left_behind_by_the_scope_does_not_come_back_with_the_brain, e2e:dotnet:Backlog.EndToEndTests.BrainTests.A_repo_link_opens_the_repository_chapter_and_the_brain_chip_brings_the_brain_back]
```

The system SHALL show the Brain in the Devbook pane again when the reader presses the Brain chip after following a repo link, and SHALL forget a followed link once the scope has moved away from the one it was followed under.

#### Scenario: Back from the repository chapter

- **Given** Brain is pressed and the reader followed a note's repo link into `backlog`
- **When** the reader presses the Brain chip
- **Then** the pane shows the Brain again

#### Scenario: Nothing was pressed

- **Given** no chip is pressed and the reader followed a note's repo link into `backlog`
- **When** the reader presses the Brain chip
- **Then** the pane shows the Brain

#### Scenario: The scope moved on

- **Given** the reader followed a repo link under Brain and then pressed another repository
- **When** the reader presses the Brain chip again
- **Then** the pane shows the Brain, not the repository the link opened

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

### Requirement: A backlink opens the note in the Brain

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.A_from_your_brain_item_is_a_link_that_opens_the_note_on_the_brain_side, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainLinkTests.A_host_that_cannot_switch_the_pane_lists_the_notes_as_text]
```

The system SHALL open a note listed in a From your Brain box on the Brain side of the pane when the reader follows it, and leave the header's scope and the task filter unchanged.

#### Scenario: Scope: backlog

- **Given** the scope is `backlog` alone and a chapter's From your Brain box lists `projects/plan.md`
- **When** the reader follows that note
- **Then** the pane shows the note on the Brain, and the scope is still `backlog`

## Bi-directional linking

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#bi-directional-linking]
```

> The requirements of linking Brain notes and tasks both ways through a note's
> `tasks` field (local ADR 0026 §3, ADR 0027 §6).

### Requirement: A task link is the task's id

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainTaskLinkTests.A_task_id_is_a_guid_in_one_spelling, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainTaskLinkTests.A_note_names_its_tasks_in_its_order_each_once_and_keeps_what_is_no_id, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainTaskLinkTests.The_reader_asks_the_brain_s_own_database, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainTaskLinkTests.A_brain_with_no_database_or_no_brain_links_no_task]
```

The system SHALL read each value of a Brain note's `tasks` field from the Brain's database as a task's stored id, a GUID compared case-insensitively and with or without braces.

#### Scenario: Two spellings of one task

- **Given** a Brain note whose `tasks` field holds a task's id in upper case and, in a later chapter, in lower case
- **When** the app reads the note's task links
- **Then** the note names that task once

### Requirement: A note lists the tasks it names

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainTaskLinkTests.A_note_lists_its_tasks_known_ones_followable_and_unknown_ones_dashed, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainTaskLinkTests.A_note_naming_no_task_draws_no_tasks_section, unit:dotnet:Backlog.UI.Components.UnitTests.BrainTaskLinkComponentTests.A_known_task_is_a_gold_chip_that_opens_it, unit:dotnet:Backlog.UI.Components.UnitTests.BrainTaskLinkComponentTests.An_unknown_task_is_a_dashed_chip_saying_so_with_nothing_to_press]
```

The system SHALL show under a Brain note's links into devbooks a Tasks section with one chip per task its `tasks` field names, opening the task when this backlog holds it and reading *not in this backlog* when it does not.

#### Scenario: A known and an unknown task

- **Given** a Brain note naming one task this backlog holds and one it does not
- **When** the pane shows the note
- **Then** the Tasks section shows the first by its title as a chip that opens it, and the second as a dashed chip reading not in this backlog, and no error is shown

### Requirement: A task row counts the notes naming it, for the owner only

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainTaskLinkTests.A_task_lists_the_notes_naming_it_in_title_order_each_once, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneBrainNotesTests.A_row_named_by_brain_notes_carries_a_teal_badge_counting_them, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneBrainNotesTests.With_no_brain_wired_no_row_carries_a_badge, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneBrainNotesTests.Pressing_a_one_note_badge_opens_that_note, unit:dotnet:Backlog.UI.Components.UnitTests.BrainTaskLinkComponentTests.A_badge_with_no_note_draws_nothing, unit:dotnet:Backlog.UI.Components.UnitTests.BrainTaskLinkComponentTests.A_badge_with_several_notes_lists_them_and_opens_the_one_pressed]
```

The system SHALL draw on a task row a teal ◆ n note badge counting the owner's Brain notes that name the task, read from the owner's own Brain, opening the note in the Devbook pane on the Brain side.

#### Scenario: Two notes name a task

- **Given** two of the owner's Brain notes name one task in their `tasks` field
- **When** the task list shows that task in a repository scope or the Brain scope
- **Then** its row carries ◆ 2 notes marked only you see this, and pressing it lists both notes to open

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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainCaptureTests.Capture_writes_a_note_into_inbox_and_opens_it, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainNoteWriterTests.A_capture_lands_in_inbox_under_the_para_default, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainNoteWriterTests.A_capture_lands_in_the_area_the_conventions_name_the_inbox, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainNoteWriterTests.A_taken_name_gets_the_next_free_one_and_nothing_is_overwritten, e2e:dotnet:Backlog.EndToEndTests.BrainTests.Capture_writes_a_note_into_the_inbox_area_and_typed_structure_stays_text]
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
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainNoteTextTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainCaptureTests.Typed_structure_is_written_as_text, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxBrainTargetTests.A_typed_fence_in_an_inbox_note_stays_text, e2e:dotnet:Backlog.EndToEndTests.BrainTests.Capture_writes_a_note_into_the_inbox_area_and_typed_structure_stays_text]
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
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.Reading_a_brain_note_answers_its_text, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.The_brain_scope_lists_the_brain_as_its_one_context, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.Listing_brain_remarks_answers_the_written_live_ones_on_the_brain, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.A_brain_remark_is_resolved_under_the_brain_scope, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.A_blank_repository_is_still_required_rather_than_read_as_the_brain]
```

The system SHALL answer the tools that list knowledge contexts, read a chapter, and list and resolve remarks for the Brain's reserved scope as they do for a repository alias.

#### Scenario: Reading a Brain note

- **Given** a Brain note in `projects`
- **When** an assistant reads it through the read-chapter tool with the Brain scope `#brain` and the path `projects/plan.md`
- **Then** the tool answers with the note's text

### Requirement: The capture tool writes into the inbox area

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.A_capture_lands_in_the_inbox_area_and_reads_back_as_written, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.A_second_capture_under_the_same_title_never_overwrites_the_first]
```

The system SHALL write a note sent to the capture tool into the Brain's inbox area through the writer the pane's Capture uses.

#### Scenario: An assistant captures

- **Given** a Brain folder is set
- **When** an assistant calls the capture tool with a title and a body
- **Then** a new note with that title and body is in the Brain's inbox area

## Rendered devbook documents

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#rendered-devbook-documents]
```

> The requirements of how a chapter's metadata is shown and written: the decision
> state a domain chapter carries, and the state another tool keeps on it.

### Requirement: Decision state is shown beside the status

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainDevbookContractSixteenPanelTests.The_nine_state_fields_are_never_rows_in_the_strip, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainDevbookContractSixteenPanelTests.Approval_and_acceptance_are_drawn_beside_the_status_as_who_and_when]
```

The system SHALL show a domain chapter's decision and review state as one line beside its status, saying who decided and when, and never as fields among the chapter's metadata.

#### Scenario: An accepted chapter

- **Given** a domain chapter approved by `jobsc` on 2026-09-02 and accepted by `ann` on 2026-09-20
- **When** the reader opens it in the domain panel
- **Then** the bar beside its status reads *approved by jobsc · 2026-09-02; accepted by ann · 2026-09-20*, and none of the approval or acceptance fields is listed with the chapter's metadata

### Requirement: Leaving a decision step takes its record with it

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookStatusWriteRulesTests.Moving_from_accepted_to_approved_deletes_the_acceptance_record_and_keeps_the_approval, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookStatusWriteRulesTests.Leaving_the_rungs_for_an_ordinary_status_deletes_all_six_record_fields, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookStatusWriteRulesTests.Leaving_the_rungs_for_the_resting_value_deletes_the_status_and_all_six_record_fields, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookStatusWriteRulesTests.Removing_the_status_of_an_approved_chapter_deletes_the_approval_record_with_it]
```

The system SHALL delete a domain chapter's approval or acceptance record in the same write that moves the chapter off that step.

#### Scenario: Back from accepted to approved

- **Given** a domain chapter that is accepted, with its approval and acceptance records
- **When** the reader sets its status to approved
- **Then** the chapter keeps its approval record and no longer carries an acceptance record

#### Scenario: Back to draft

- **Given** a domain chapter that is accepted, with its approval and acceptance records
- **When** the reader sets its status to draft
- **Then** the chapter carries neither record

### Requirement: Another tool's state is carried as written

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.MetadataReaderContractTests.An_extension_keeps_its_authors_casing_and_its_value_verbatim, unit:dotnet:Backlog.UI.Components.UnitTests.DevbookMetadataFindingsTests.Extension_keys_are_never_validated, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainDevbookContractSixteenPanelTests.An_extension_key_stays_a_row_and_is_marked_as_one]
```

The system SHALL show every `ext.` key of a chapter's metadata with its key and value exactly as written, and report no finding about it.

#### Scenario: A plugin's own key

- **Given** a chapter whose metadata holds `ext.archify.layer: core`
- **When** the reader opens it
- **Then** the chapter lists `ext.archify.layer` with the value `core`, marked as another tool's, and no finding is reported about it

## Remarks on a chapter

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#remarks-on-a-chapter]
```

> The requirements of where the read view shows a reader's remark once the chapter
> it was left on has changed.

### Requirement: A remark follows its passage

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/domain.md#remark]
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_follows_its_paragraph_when_the_chapter_grows_above_it, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_follows_its_paragraph_when_the_chapter_shrinks_above_it, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_survives_its_paragraph_being_rewrapped, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_repeated_passage_takes_the_copy_nearest_the_remembered_index, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_without_a_digest_is_anchored_by_index_exactly_as_before]
```

The read view SHALL show a remark beside the passage it was left on after the chapter has changed above that passage.

#### Scenario: Paragraphs are added above the remarked passage

- **Given** a remark on the second paragraph of a chapter
- **When** two paragraphs are added above it and the reader opens the chapter again
- **Then** the remark shows beside the same passage, now the fourth paragraph

### Requirement: A remark whose passage has gone shows at the end

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/domain.md#remark]
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_whose_paragraph_was_deleted_shows_at_the_end, unit:dotnet:Backlog.UI.Components.UnitTests.MarkdownCommentAnchorTests.A_remark_whose_paragraph_changed_wording_shows_at_the_end]
```

The read view SHALL show a remark whose passage has been deleted or reworded at the end of the chapter rather than drop it.

#### Scenario: The remarked paragraph is deleted

- **Given** a remark on a paragraph of a chapter
- **When** that paragraph is deleted and the reader opens the chapter again
- **Then** the remark shows at the end of the chapter

## Review notes in a chapter

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#review-notes-in-a-chapter]
```

> The requirements of the review notes a repository keeps in its chapters as
> `annotation` fences.

### Requirement: A review note never travels as the chapter's content

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/domain.md#review-note]
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.DevbookToolsTests.An_ordinary_read_leaves_out_only_the_meta_and_annotation_fences, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.PrivateFenceTests.An_annotation_fence_quoting_a_code_sample_is_cut_whole]
```

The system SHALL leave a chapter's review notes out of everything it hands on as the chapter's content: an assistant's ordinary read, the text it searches, and an excerpt.

#### Scenario: An assistant reads a chapter with an open question

- **Given** a chapter holding an open review note beside one of its paragraphs
- **When** an assistant reads the chapter without asking for its review state
- **Then** the answer holds the chapter's prose, diagrams and code listings, and no review note

## Sync verdicts beside a chapter

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#sync-verdicts-beside-a-chapter]
```

> The requirements of the [Sync Verdict](domain.md#sync-verdict) a chapter shows.

### Requirement: A sweep replaces only the verdicts of the chapters it checked

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.Sessions.UnitTests.DevbookSyncVerdictStoreTests.A_later_run_replaces_the_chapters_it_names_and_leaves_the_rest]
```

The system SHALL show beside a chapter the verdict of the latest sweep that checked it, and keep every other chapter's verdict when a sweep checks only some.

#### Scenario: A later sweep checks one of two chapters

- **Given** one sweep found `run-start` code-ahead and `run-finish` in conflict
- **When** a later sweep finds `run-start` aligned and says nothing about `run-finish`
- **Then** `run-start` shows aligned and `run-finish` still shows in conflict

### Requirement: A verdict keeps the sweep's word

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSyncVerdictBadgeTests.A_word_outside_the_sweeps_vocabulary_keeps_its_spelling_and_wears_the_unknown_tone]
```

The system SHALL show a verdict in the sweep's own word, and show a word outside the sweep's vocabulary as written, flagged as unknown.

#### Scenario: A word the app does not know

- **Given** a sweep recorded the verdict `drifting` for a chapter
- **When** the reader opens the chapter
- **Then** its badge reads `drifting` in the unknown tone, not the tone of a known verdict

## Choosing where the devbook is read from

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#choosing-where-the-devbook-is-read-from]
```

> The requirements of the Devbook pane's source field: the local clone or a branch,
> read from GitHub (local ADR 0008).

### Requirement: Typing narrows the sources

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_query_keeps_the_suggestions_containing_it_whatever_the_case, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_blank_query_is_no_filter, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.The_load_entry_survives_any_query_until_the_branches_are_in, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Typing_narrows_the_list_to_the_branches_that_match, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.A_click_in_the_untouched_field_lists_every_source]
```

The system SHALL suggest, in the source field, the sources whose names contain the typed text in any case, and every source while the field is blank or untouched.

#### Scenario: Narrowing to a branch

- **Given** the branches of `backlog` are loaded, among them `claude/devbook-branch-search` and `main`
- **When** the reader types `BRANCH-search` into the source field
- **Then** `claude/devbook-branch-search` is suggested and `main` is not

### Requirement: Branches load once per session, in the background

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.Opening_the_pane_loads_once_and_the_picker_and_tabs_ask_nothing_more, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.The_list_says_the_branches_are_loading_while_the_first_load_runs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.While_a_refresh_runs_the_field_keeps_naming_the_source_read, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.The_first_load_lists_compares_every_branch_and_checks_the_clone, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_second_load_answers_from_the_first_and_asks_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.Loads_asked_for_at_once_share_one, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.One_caller_giving_up_does_not_cancel_the_shared_load, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_refresh_loads_again, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_load_that_runs_out_of_time_stops_the_comparisons_it_started, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.The_load_entry_becomes_a_refresh_once_the_overview_is_in, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.No_load_entry_is_offered_while_the_overview_loads, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.Loading_branches_refreshes_the_overview, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.While_the_overview_loads_the_suggestions_say_so_and_the_entry_names_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.Loading_the_branches_does_not_change_what_is_current, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsDevbookFolderStatusTests.After_a_refresh_the_source_select_is_rebuilt_on_the_source_read, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchCatalogTests.Branches_past_the_first_page_are_listed, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchCatalogTests.Listing_stops_at_the_ceiling, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchCatalogTests.Branch_heads_are_paged_to_the_same_ceiling]
```

The system SHALL load a repository's branches, each compared with the default branch, and the local clone's currency once per app session in the background when the Devbook pane first opens on the repository, page by page up to 1000 branches, reuse that load for every later ask until the reader picks "Refresh branches", show "Loading branches…" in the list while a load runs, and never change the source being read or hold back the pane while it does.

#### Scenario: Reopening the picker asks nothing again

- **Given** the pane opened on `backlog` and its branches have loaded
- **When** the reader switches section tabs and opens the source field again
- **Then** GitHub is not asked for the branches again, and the field suggests what the first load found

#### Scenario: A repository with many branches

- **Given** `backlog` has 1200 branches on GitHub and the pane reads its clone
- **When** the pane opens on `backlog`
- **Then** the first 1000 branches are compared and the pane still reads the clone

#### Scenario: The first load is still running

- **Given** the pane has just opened on `backlog` and its branches have not loaded yet
- **When** the reader opens the source field
- **Then** the list shows "Loading branches…", and picking it changes nothing

### Requirement: Only branches that change the devbook are suggested

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.Only_branches_changing_the_devbook_are_offered, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.Once_the_overview_is_in_only_branches_changing_the_devbook_are_listed, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.The_configured_branch_is_listed_even_when_it_changes_no_devbook, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_listed_branch_left_out_by_the_filter_still_means_itself, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_branch_changes_the_sections_its_files_are_under_in_tab_order, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_legacy_root_folder_counts_as_its_section, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_folder_pointed_elsewhere_counts_as_its_section, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.Code_and_instruction_files_change_no_section, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_file_renamed_out_of_a_section_changed_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_cut_short_file_list_with_no_devbook_file_may_change_it]
```

The system SHALL suggest, among the branches, only those whose diff against the default branch touches a file in a devbook area other than the instructions, on either side of a rename, or whose file list GitHub cut short before any devbook file showed, marked as one that may change the devbook; the configured branch is suggested regardless, and any branch the load listed can still be typed by name.

#### Scenario: A code-only branch

- **Given** `fix/build` changes only files under `src/` and `claude/branch-picker` changes `.devbook/domain/`
- **When** the reader opens the source field
- **Then** `claude/branch-picker` is suggested and `fix/build` is not, and typing `fix/build` and pressing Enter switches to it

### Requirement: Each branch says what it changes and how far it is from the default branch

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.A_branch_row_shows_a_glyph_for_each_section_it_changes_and_its_counts, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_branch_row_carries_its_sections_and_counts, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.Nothing_loaded_means_no_row, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.Ahead_and_behind_come_from_the_comparison]
```

The system SHALL show, on each suggested branch, the glyph of every devbook area it changes in tab order with the area named in words for assistive technology, and its commits ahead of and behind the default branch, and show no counts on a row whose comparison nobody read.

#### Scenario: A branch changing two areas

- **Given** `claude/branch-picker` changes `.devbook/arc42/` and `.devbook/domain/`, is 3 commits ahead of `main` and 1 behind
- **When** the reader opens the source field
- **Then** its row shows the Architecture and Domain glyphs and `↑3 ↓1`, read out as "Changes Architecture", "Changes Domain", "3 ahead, 1 behind"

### Requirement: The clone says how far it is from its upstream

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.The_clone_row_shows_its_counts_against_its_upstream, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.The_clone_row_carries_its_counts_against_its_upstream, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_clone_git_could_not_place_has_no_counts, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_clone_with_no_upstream_has_no_counts, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_repository_with_no_clone_is_not_checked_for_one, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.Recording_the_clone_replaces_its_answer_and_keeps_the_branches, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_clone_recorded_during_a_load_is_not_overwritten_by_that_loads_older_look, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.Pulling_moves_the_clone_row_and_lists_no_branches_again]
```

The system SHALL show, on the local clone's row, its commits ahead of and behind the branch it tracks, follow the newest check or pull of the clone without listing the branches again, and show no counts when the clone tracks no upstream or git could not place it.

#### Scenario: A clone with no upstream

- **Given** the clone of `backlog` is on a branch that tracks nothing
- **When** the reader opens the source field
- **Then** the local clone row shows its name and no counts

### Requirement: A failed branch load leaves the known sources offered

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.A_refused_load_says_why_and_the_picker_keeps_working, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_refused_overview_keeps_the_default_the_clone_and_the_configured_branch, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_refused_refresh_keeps_the_list_in, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_listing_that_fails_comes_back_as_a_message, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_comparison_that_fails_leaves_that_branch_out_and_nothing_else, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_failed_load_is_not_retried_by_loading_again, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_clone_check_that_fails_leaves_the_clone_unknown, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchOverviewTests.A_refused_refresh_keeps_the_branches_the_last_load_listed, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_configured_branch_is_listed_even_when_nothing_was_loaded]
```

The system SHALL, when the branch listing is refused, say why beside the source field and keep offering the default branch, the local clone and the configured branch; leave out only a branch GitHub would not compare; not ask again until the reader refreshes; and, when a refresh is refused, keep the branches the last good load listed.

#### Scenario: Opening the pane offline

- **Given** the machine is offline and the pane has not loaded the branches of `backlog` this session
- **When** the pane opens on `backlog`
- **Then** the field says why the branches could not be read, still offers `main`, the local clone and the configured branch, and reopening the pane does not ask GitHub again

### Requirement: The version control is offered only for the clone

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/features.md#the-devbook-on-the-repositorys-latest-version]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.A_branch_shows_no_version_control, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchOverviewTests.A_clone_found_behind_offers_the_pull_without_a_check_first, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookUpdateServiceTests.Only_a_scope_reading_its_clone_has_a_clone_to_update, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookUpdateServiceTests.The_overviews_look_at_the_clone_is_the_state_a_check_would_give, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookUpdateServiceTests.Nothing_loaded_seeds_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookUpdateServiceTests.A_pull_leaves_the_overview_holding_the_clone_it_left]
```

The system SHALL show the version control only while the pane reads the local clone, start it from the clone's currency the session's branch load found, offering "Pull latest" at once for a clone found behind and announcing nothing, and pull only when the reader presses it.

#### Scenario: A clone found behind on opening

- **Given** the clone of `backlog` is 2 commits behind its upstream
- **When** the pane opens on `backlog` and the branch load lands
- **Then** the version control offers "Pull latest" without "Check now" pressed first, and nothing is pulled

#### Scenario: Reading a branch

- **Given** the pane reads the branch `main` of `backlog`
- **When** the reader looks under the area strip
- **Then** no version control is shown

### Requirement: A typed branch is checked before it is refused

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Typing_a_branch_before_the_list_is_loaded_fetches_it_and_switches, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Typing_a_branch_the_list_lacks_asks_github_and_switches, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Typing_an_unknown_branch_says_so_and_leaves_the_source_alone, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_branch_typed_in_another_case_means_the_branch_as_it_is_spelled, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchCatalogTests.A_branch_that_is_gone_resolves_to_null_rather_than_throwing]
```

The system SHALL ask GitHub for a branch the reader commits by name that the loaded list does not hold, switch to it when GitHub has it, and otherwise say there is no such branch and leave the source as it was.

#### Scenario: A branch newer than the list

- **Given** the branch list of `backlog` was loaded before `feature/x` was pushed
- **When** the reader types `feature/x` and presses Enter
- **Then** the pane reads `feature/x`

#### Scenario: A branch GitHub does not have

- **Given** `backlog` has no branch `nope`
- **When** the reader types `nope` and presses Enter
- **Then** the pane says no branch named "nope" is in the repository and keeps reading the source it read

### Requirement: Leaving the field mid-search switches nothing

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Leaving_the_field_mid_search_fetches_nothing_and_switches_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Leaving_an_emptied_field_switches_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Entering_an_emptied_field_switches_nothing_and_fetches_nothing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Leaving_the_field_on_a_named_source_switches_to_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Committing_the_source_already_read_says_nothing]
```

The system SHALL switch the source when the field is left only if its text names a source, and fetch nothing and switch nothing when it holds partial search text or nothing.

#### Scenario: Clicking away from a search

- **Given** the pane reads the clone of `backlog`
- **When** the reader types `devb` into the source field and clicks elsewhere
- **Then** nothing is fetched and the pane still reads the clone

### Requirement: Switching says what is read

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Nothing_is_said_about_the_source_until_somebody_switches_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Switching_to_a_branch_says_the_branch_is_being_read_as_a_snapshot, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.Switching_back_to_the_clone_says_the_clone_is_being_read, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneSourceStatusTests.A_refusal_on_a_branch_puts_the_branch_back_and_says_why_under_the_strip, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookSourceSelectionTests.A_repository_reading_a_branch_says_so_and_says_it_is_read_only]
```

The system SHALL say under the area strip, after the reader switches the source, whether the pane now reads the local clone or a read-only branch snapshot.

#### Scenario: Switching to a branch

- **Given** the pane reads the clone of `backlog`
- **When** the reader switches the source to `main`
- **Then** the strip says `main` is read as a snapshot, and the chapters cannot be edited

## Reviewing what a branch changed

```meta
type: requirements
status: draft
related: [.devbook/domain/devbook/features.md#reviewing-what-a-branch-changed]
```

> The requirements of **Review changes** in the Devbook pane, read from GitHub and
> held in memory for the session.

### Requirement: Only a branch can be reviewed

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.The_toggle_is_offered_for_a_branch_and_not_for_the_local_clone, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_branch_source_can_be_reviewed_and_the_local_clone_cannot, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Switching_to_the_local_clone_closes_the_review, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Moving_to_another_repository_closes_the_review, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Closing_the_review_goes_back_to_the_chapters]
```

The system SHALL offer **Review changes** only while the pane reads a branch, and close the review when the source becomes the local clone or the pane moves to another repository.

#### Scenario: Back to the clone

- **Given** a review of the branch `feature/x` is open
- **When** the reader switches the source to the local clone
- **Then** the review closes, the chapters are shown, and Review changes is not offered

### Requirement: The review lists the branch's own commits

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_comparison_asks_for_the_three_dot_compare_of_the_two_refs, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_comparison_carries_the_merge_base_and_how_far_apart_the_two_are, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.The_branch_is_compared_with_the_default_branch_by_their_heads, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.Commits_are_listed_newest_first_with_short_sha_subject_and_age, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Opening_the_review_shows_the_branch_its_chapters_and_the_first_comparison]
```

The system SHALL list, in a branch review, the commits the branch holds that the repository's default branch does not, newest first with short SHA, subject, and age, beside an entry for all the branch's changes against the default branch.

#### Scenario: Opening a review

- **Given** the pane reads `feature/x`, two commits ahead of `main`
- **When** the reader presses Review changes
- **Then** the review lists All changes vs main and the two commits, newest first

### Requirement: A commit is compared with its first parent, the branch with its merge base

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_commit_is_compared_with_its_parent, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_commit_is_read_against_its_first_parent, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_root_commit_adds_every_chapter_and_lists_no_parent_tree, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.Each_chapter_counts_what_moved_between_the_merge_base_and_the_branch, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Choosing_a_commit_lists_the_chapters_that_commit_changed]
```

The system SHALL compare a chosen commit with its first parent, and the whole branch with its merge base on the default branch.

#### Scenario: Choosing a commit

- **Given** a review of `feature/x` is open
- **When** the reader chooses a commit that edited `domain/devbook/features.md`
- **Then** the chapters listed are the ones that commit changed against its first parent

### Requirement: Changed chapters are listed and other files counted

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.The_whole_branch_lists_its_devbook_chapters_and_counts_the_rest, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_folder_pointed_elsewhere_is_where_its_chapters_are_looked_for, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_chapter_renamed_out_of_the_devbook_reads_as_removed]
```

The system SHALL list the devbook chapters a chosen change touched and count every other file it touched as "+N other files not shown".

#### Scenario: Code beside a chapter

- **Given** a commit changed one devbook chapter and three source files
- **When** the reader chooses it
- **Then** one chapter is listed and the review reads +3 other files not shown

### Requirement: A chapter is compared section by section

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_chosen_chapter_is_compared_section_by_section, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.Choosing_a_chapter_compares_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_chapter_the_scope_does_not_hold_is_an_error]
```

The system SHALL show a chosen chapter's before and after compared section by section.

#### Scenario: Choosing a chapter

- **Given** a commit edited one section of `features.md`
- **When** the reader chooses `features.md` in the review
- **Then** the comparison shows that section changed and the others unchanged

### Requirement: A partial or refused answer is said

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_truncated_commit_list_says_how_many_are_shown, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_full_file_list_says_chapters_may_be_missing, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_cut_short_earlier_tree_says_a_chapter_may_read_as_new, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_comparison_with_more_commits_than_listed_says_it_is_truncated, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubBranchComparisonTests.A_full_file_list_says_it_may_be_cut_short, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_branch_that_is_gone_is_said_rather_than_thrown, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookBranchReviewTests.A_refused_comparison_is_said_rather_than_thrown, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBranchReviewTests.What_github_refused_is_said_in_place_of_the_review]
```

The system SHALL say in the review when GitHub listed only part of a change's commits or files, and show what GitHub refused in place of the review rather than failing.

#### Scenario: A long branch

- **Given** `feature/x` holds more commits than GitHub lists in one comparison
- **When** the reader opens its review
- **Then** the review says how many of the commits are shown

## Domain view by unit

```meta
type: requirements
status: draft
roadmap: [devbook-domain-view]
related: [.devbook/domain/devbook/features.md#domain-view-by-unit, .devbook/arc42/adr/0037-the-domain-view-reads-by-unit.md]
```

> The requirements of the Domain area read by unit, behind the
> [New domain view](context.md#new-domain-view) switch (local ADR 0037). The plan
> `+devbook-domain-view` builds them page by page; the Context map, a context's
> Overview and Neighbours, Domain, Features, Requirements, Model, Flow, the
> Glossary and the Brain and task links are built. The Demo button is not.

### Requirement: The flag off keeps today's Domain view

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneDomainViewFlagTests.With_the_switch_off_the_Domain_area_is_todays_panel, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.With_the_switch_off_a_split_context_is_todays_flat_list, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.With_the_switch_off_an_unsplit_context_is_todays_list, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneDomainViewFlagTests.Flipping_the_switch_relists_the_open_Domain_menu]
```

The system SHALL show the Domain area exactly as it reads today while the `devbook-domain-view` switch is off.

#### Scenario: A fresh install

- **Given** the `devbook-domain-view` switch has never been changed
- **When** the reader opens the Domain area of a repository's Devbook
- **Then** the Domain area reads as it did before the switch existed

### Requirement: The menu lists the files in reading order

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Lists_an_unsplit_context_as_its_kinds, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Lists_a_split_context_by_kind_in_reading_order]
```

The system SHALL list a bounded context's files in the devbook reading order, one row per file, each row titled by its kind.

#### Scenario: An unsplit context

- **Given** a context holding `context.md`, `domain.md`, `features.md`, `requirements.md`, `model.md` and `flow.md`
- **When** the reader opens the context in the Domain view
- **Then** the menu lists the context, Domain, Features, Requirements, Model and Flow rows in that order, and the Glossary is in the Domain page's chapter bar

### Requirement: A split file is a child of its kind's row

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Nests_each_split_file_under_its_kind_labelled_by_its_name_without_a_database, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Labels_a_split_row_by_its_heading_from_the_database, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.A_further_dot_nests_one_level_deeper, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.A_kind_with_no_base_file_is_a_row_that_only_opens_its_children]
```

The system SHALL list a file named `<kind>.<name>.md` under its kind's row, titled by the file's own heading, and nest each further dot one level deeper.

#### Scenario: A split domain

- **Given** a context holding `domain.md` and `domain.order.md`, whose heading is *Order*
- **When** the reader opens the context in the Domain view
- **Then** the Domain row holds a child row titled *Order*

#### Scenario: Two dots

- **Given** a context holding `domain.order.md` and `domain.order.line.md`, whose heading is *Order line*
- **When** the reader opens the context in the Domain view
- **Then** *Order line* is a child of the *Order* row

### Requirement: The rules are the Always true tab

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.An_aggregate_opens_on_about_with_always_true_beside_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.A_domain_file_s_rules_are_its_invariants_file_beside_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.A_split_page_reads_every_rule_of_its_own_invariants_file, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Draws_no_row_for_invariants_dependencies_demo_or_underscore_files]
```

The system SHALL show `domain.<name>.invariants.md` as the Always true tab of the `domain.<name>.md` page and give it no row of its own.

#### Scenario: An aggregate with its rules

- **Given** a context holding `domain.order.md` and `domain.order.invariants.md`
- **When** the reader opens *Order*
- **Then** the page offers the About and Always true tabs, Always true shows the rules, and the menu has no row for the invariants file

### Requirement: A rule is its claim and its proof

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.Each_rule_is_its_claim_marked_by_its_proof, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.Rules_group_by_the_concept_their_enforced_at_names_falling_back_to_the_aggregate, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRulesTests.Rules_under_a_group_heading_read_as_that_group_and_link_what_it_is_about, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.The_aggregate_s_always_true_tab_lists_its_rules_by_concept_with_their_marks, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.A_written_group_is_a_card_headed_by_its_words_with_what_it_is_about_beside_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.The_tab_counts_the_rules_and_each_mark, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.An_open_rule_says_nobody_has_settled_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.Where_each_rule_is_enforced_shows_only_when_asked, unit:dotnet:Backlog.UI.Components.UnitTests.DomainAlwaysTrueTests.A_shared_grouping_s_rules_show_on_the_base_page]
```

The system SHALL show each rule on Always true as its heading, marked ✓ when its chapter names a test, ○ when it names none and ? when its `Enforced at:` line is `open`, with the rules and each mark counted, in a card per group the invariants chapter writes — headed by its words, with what its `related` names beside them — and, for a rule outside every such group, under the domain concept its `Enforced at:` line names or else under the aggregate, with its first sentence and where it is enforced hidden until the reader asks for them.

#### Scenario: Rules in the groups the chapter writes

- **Given** an *Order* invariants chapter holding a `### How an order moves` heading of `type: invariants`, whose `related` names *Order* and the order lifecycle, with two rules beneath it, one of them `open`
- **When** the reader opens Always true
- **Then** the tab reads *Always true · 2*, the bar counts one rule proved and one open, and both rules sit in one card headed *How an order moves* with *Order lifecycle* linked beside it and *Order* not linked
- **And** the open rule says it is not built until someone decides

#### Scenario: Rules by concept

- **Given** an *Order* aggregate owning *Order Line*, and an invariants chapter holding a rule enforced at `Place()` and one enforced at `OrderLine.SetQuantity()`
- **When** the reader opens Always true
- **Then** the first rule is under *Order* and the second under *Order Line*, and neither shows where it is enforced

#### Scenario: Where each is enforced

- **Given** Always true open on *Order*
- **When** the reader presses *Show where each is enforced*
- **Then** each rule shows its first sentence and its `Enforced at:` line, and pressing it again hides them

#### Scenario: Shared rules on the base page

- **Given** a `domain.md` with a `## Shared Value Objects` grouping holding *Money*, and an invariants chapter about *Money*
- **When** the reader opens the Domain row
- **Then** the chapter bar offers *Shared Value Objects*, and it shows *Money*'s rules

### Requirement: A demo is a button on its page

```meta
type: requirement
status: draft
```

The system SHALL offer a `*.demo.html` file as a Demo button on the page it belongs to and give it no row of its own.

#### Scenario: A page with a demo

- **Given** a context holding `features.md` and `features.demo.html`
- **When** the reader opens Features
- **Then** the page offers a Demo button, and the menu has no row for the demo

### Requirement: Dependencies are the Neighbours tab

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextPageTests.Neighbours_reads_dependencies_md_into_listens_to_listen_to_and_hand_in_hand, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextPageTests.A_context_without_dependencies_md_reads_its_own_Dependencies_chapter, unit:dotnet:Backlog.UI.Components.UnitTests.DomainNeighboursTests, unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextViewTests.The_board_groups_the_neighbours_and_dashes_an_external_system, unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextViewTests.The_page_opens_with_its_title_and_tags_each_card]
```

The system SHALL show a context's `dependencies.md` as the Neighbours tab of the context's page, each relation worded by the first sentence of its table's "Why" or "What it relies on" column cut to a short phrase, the whole sentence on hover, and give the file no row of its own. A neighbour stands by the way information goes: the ones that supply the context on the left as what it listens to, the ones it supplies on the right as who listens to it, read from the pattern's role mark — *(Inbox = supplier)*, *(Tasks = customer)* — and, where a row has none, from its table, outbound on the left and inbound on the right. Its Partnership neighbours stand below as hand in hand, and on neither side. A neighbour that is not one of the domain's contexts is a dashed card, and outside systems on one side that share a contract are one card. No card names the relationship's pattern. A context that keeps its tables in a `## Dependencies` chapter of `context.md` reads the same.

#### Scenario: Each neighbour stands where its information goes

- **Given** a context whose `dependencies.md` lists Inbox as an inbound dependent with *(Inbox = supplier)*, Productivity as an outbound dependency with *(Tasks = supplier)*, and Roadmap Planning both as an outbound Customer/Supplier and as an inbound Partnership
- **When** the reader opens the context's Neighbours tab
- **Then** Inbox stands on the left, Productivity on the right, and Roadmap Planning only under hand in hand

#### Scenario: A context with neighbours

- **Given** a context whose `dependencies.md` lists Tasks with the Why *Notes link to and embed tasks*
- **When** the reader opens the context's Neighbours tab
- **Then** Tasks is listed with *Notes link to and embed tasks*, and the menu has no row for `dependencies.md`

#### Scenario: A partner and an outside system

- **Given** a context whose `dependencies.md` lists Devbook as a Partnership and *GitHub (external)* as an outbound ACL
- **When** the reader opens the context's Neighbours tab
- **Then** Devbook stands under hand in hand, GitHub is a dashed card on the left, and neither card says *Partnership* or *ACL*

### Requirement: Overview is the context's boundary

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextPageTests.A_context_row_opens_on_Overview_with_the_boundary_counts_proof_and_flags, unit:dotnet:Backlog.UI.Components.UnitTests.DomainContextOverviewTests]
```

The system SHALL open a context's row on an Overview tab that shows the prose above the first chapter of its `context.md`, its chapters counted per kind, how many of its requirements and always-true rules name a test, its chapters per status, and its feature flags and settings.

#### Scenario: A context's Overview

- **Given** a context whose `context.md` holds the feature flag `github-integration` and whose `requirements.md` holds two requirements, one with `tests`
- **When** the reader opens the context
- **Then** Overview is the open tab, it counts two Requirements, says one of two name a test, and lists `github-integration` among the flags and settings

### Requirement: Underscore files stay hidden

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuDomainUnitsTests.Draws_no_row_for_invariants_dependencies_demo_or_underscore_files]
```

The system SHALL list no file or folder whose name starts with `_`.

#### Scenario: Generated renders

- **Given** a context holding an `_archify` folder
- **When** the reader opens the context in the Domain view
- **Then** no row names `_archify` or a file inside it

### Requirement: An unsplit domain picks its units in the page

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.An_unsplit_file_offers_its_units_in_a_chapter_bar_led_by_their_kind, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.A_split_file_opens_on_its_one_unit_without_a_bar, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitsTests.The_chapter_bar_offers_aggregates_services_and_the_glossary_in_file_order]
```

The system SHALL offer an unsplit `domain.md`'s aggregates, domain services and Glossary as a chapter bar inside the Domain page.

#### Scenario: One domain file

- **Given** a context whose only domain file is `domain.md`, holding the aggregates *Knowledge Note* and *Remark* and the domain service *Cross-Linking*
- **When** the reader opens Domain
- **Then** the page's chapter bar offers *Knowledge Note*, *Remark*, *Cross-Linking* and Glossary, and picking *Remark* shows that aggregate

### Requirement: Wording is the chapters' own

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_summary_is_the_features_first_sentence, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.About_is_a_summary_of_the_chapter_not_its_prose, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitSummaryTests.A_summary_leaves_a_closing_list_of_fields_to_model]
```

The system SHALL title each row and page with the heading as written, and summarize a unit with its chapter's first sentence, read as far as the colon when it closes on a list of fields.

#### Scenario: A summary

- **Given** a feature titled *Knowledge capture* whose first sentence is *Store notes as markdown.*
- **When** the reader opens Features
- **Then** the feature is listed as *Knowledge capture* with the summary *Store notes as markdown.*

### Requirement: Technical detail is on Model only

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.About_reads_cards_values_and_events_in_words_and_no_field, unit:dotnet:Backlog.UI.Components.UnitTests.DomainUnitPageTests.A_service_page_is_its_summary_and_never_how_it_is_invoked, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.Fields_groups_each_class_and_carries_the_notes_and_how_the_services_run]
```

The system SHALL show field types, invocation semantics and event payloads on the Model page only.

#### Scenario: An aggregate's About tab

- **Given** an aggregate whose `model.md` lists its fields with their types
- **When** the reader opens the aggregate's About tab
- **Then** the tab names no field type, and the Model page's Fields tab does

#### Scenario: A domain service's page

- **Given** a domain service whose chapter says it is called synchronously by the use case that completes the task
- **When** the reader opens the service in the chapter bar
- **Then** the page shows its first sentence and what it is held to, and says nothing of how it is invoked

### Requirement: Model draws the aggregate's boundary

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.The_page_opens_on_Diagram_with_Fields_and_Events_beside_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.An_arranged_fence_draws_the_band_its_parts_references_and_services, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.A_fence_that_does_not_parse_is_drawn_with_mermaid, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelArrangementTests.The_root_sits_in_its_band_with_its_composed_parts_and_their_multiplicity, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewModelPageTests.The_Model_row_opens_on_Diagram_with_the_aggregate_in_its_band]
```

The system SHALL open a `model.md` or `model.<name>.md` row on a Diagram tab, beside Fields and Events, that draws each aggregate root inside its consistency boundary with its composed parts and their multiplicity, places the references by identity outside the boundary and the domain services below it, and draws the diagram with mermaid when it cannot be read that way.

#### Scenario: An aggregate with parts and references

- **Given** a `model.md` whose `classDiagram` composes *SubItem* into the aggregate root *Task* as `"0..*"` and draws a dashed line from *Task* to *Task*
- **When** the reader opens Model
- **Then** Diagram is open, *SubItem* sits inside *Task*'s boundary marked `0..*`, and the dashed reference sits outside it

#### Scenario: A diagram the view cannot read

- **Given** a `model.md` whose `classDiagram` names no aggregate root
- **When** the reader opens Model
- **Then** Diagram draws the fence with mermaid

### Requirement: Fields groups each class's attributes by kind

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelFieldsTests.Attributes_are_grouped_by_kind_in_the_tab_order, unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.Fields_groups_each_class_and_carries_the_notes_and_how_the_services_run]
```

The system SHALL list on Model's Fields tab each class of the `classDiagram` that holds state, with its attributes grouped by what their types say — dates and times, ids pointing elsewhere, owned parts, vocabularies and other values, in that order — the model file's relationship notes beneath, and each domain-service chapter's invocation semantics under *Domain services · how they run*.

#### Scenario: A task's fields

- **Given** a `model.md` whose aggregate root *Task* has `+Date due_on`, `+List~Id~ depends_on`, `+TaskStatus status` for an enumeration *TaskStatus*, and `+String title`, and composes *SubItem* as `"0..*"`
- **When** the reader opens Model and picks Fields
- **Then** *due_on* is under Dates and times, *depends_on* under Ids pointing elsewhere, *SubItem* under Owned parts marked `0..*`, *status* under Vocabularies with *TaskStatus*'s values, and *title* under Other values

#### Scenario: How a service runs

- **Given** a `domain-service` chapter *Occurrence Spawning* whose prose says *Invocation semantics: called synchronously by the use case that completes the task.*
- **When** the reader opens Model and picks Fields
- **Then** *Domain services · how they run* names *Occurrence Spawning* with *Called synchronously by the use case that completes the task.*

### Requirement: Events reads each event as a contract

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainModelViewTests.Events_reads_each_event_as_a_contract, unit:dotnet:Backlog.UI.Components.UnitTests.DomainChapterContractsTests.A_domain_event_reads_as_a_contract, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewModelPageTests.Fields_and_Events_read_the_model_notes_and_the_contexts_domain_chapters]
```

The system SHALL list on Model's Events tab each `domain-event` chapter of the context with what raises it, its consumers, its payload and its published-language rules as the chapter writes them, and mark it *Emitted* only when the chapter that raises it says it emits or publishes it and no chapter says it is not emitted yet, and *Documented* otherwise.

#### Scenario: A documented event

- **Given** a `domain-event` chapter *StatusChanged* whose `related` names the aggregate *Task*, with *Payload* and *Consumers* sub-chapters, in a context whose chapters say it publishes no domain events yet
- **When** the reader opens Model and picks Events
- **Then** *StatusChanged* is raised by *Task*, shows its payload and consumers, and is marked *Documented*

### Requirement: Features stand in columns by what they build on

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.Features_stand_in_columns_by_what_they_build_on_in_the_context, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_deprecated_feature_goes_to_the_end_of_its_column_and_a_proposed_one_says_so, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_card_counts_its_sub_features_and_names_its_flag_or_setting, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_card_counts_its_requirements_and_how_many_are_proved_from_their_tests, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.A_split_file_reads_its_one_feature_with_the_depth_the_base_file_gives_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Cards_stand_in_columns_with_their_counts_flag_and_proof, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_proposed_card_is_dashed_and_a_deprecated_one_struck_through_last_in_its_column, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFeaturesPageTests.The_Features_row_opens_the_board_in_columns_with_each_cards_proof, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFeaturesPageTests.A_split_features_file_reads_its_one_feature_in_the_column_the_base_file_gives_it]
```

The system SHALL draw the features of a `features.md` or `features.<name>.md` row in columns by how many of the context's features each builds on through `depends-on`, each card showing its sub-feature count, its flag or setting, and its requirement count with how many a linked test proves, a proposed feature dashed and a deprecated one struck through at the end of its column.

#### Scenario: Three levels

- **Given** a `features.md` whose *Checkout* depends on *Ordering*, whose *Quotes* depends on *Checkout*, and whose deprecated *Fax orders* depends on nothing
- **When** the reader opens Features
- **Then** *Ordering* and *Fax orders* stand in the first column with *Fax orders* last and struck through, *Checkout* in the second, and *Quotes* in the third

#### Scenario: A card's proof

- **Given** a feature *Checkout* behind the flag *New checkout*, with two sub-features, whose requirements group holds two requirements of which one names a test
- **When** the reader opens Features
- **Then** *Checkout*'s card says 2 sub-features, Flag · New checkout, and 2 requirements · 1 proved

### Requirement: Picking a feature shows its detail

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeaturesTests.The_detail_names_what_it_builds_on_and_the_model_chapters_its_related_names, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Picking_a_card_opens_its_detail_and_reports_the_pick, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Following_a_chapter_in_the_detail_hands_its_reference_to_the_host, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFeaturesPageTests.A_features_requirements_link_opens_Requirements_on_that_features_group]
```

The system SHALL show, for the feature the reader picks on Features, what it builds on, its flag or setting, the model chapters its `related` names, its sub-features and its requirement count, each a link to the chapter it names.

#### Scenario: Picking Checkout

- **Given** a feature *Checkout* that depends on *Ordering* and whose `related` names the aggregate *Order*
- **When** the reader picks *Checkout* on Features
- **Then** the detail names *Ordering* under Builds on and *Order* under Model

### Requirement: Requirements read per feature with their proof

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_requirements_file_reads_one_group_per_feature_with_its_tally, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_requirement_reads_its_shall_sentence_and_whether_a_test_proves_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_scenario_reads_as_given_when_then_rows_with_an_and_clause_split_into_bullets, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.An_and_row_joins_the_row_above_it, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_clause_is_not_split_inside_a_code_span_or_emphasis, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_split_requirements_file_reads_its_one_feature, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.Requirements_written_straight_under_the_title_are_one_group, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Requirements_read_per_feature_with_the_shall_sentence_and_proof, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_scenario_reads_as_rows_with_an_and_clause_as_bullets, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.Picking_another_feature_shows_its_requirements]
```

The system SHALL show on a `requirements.md` or `requirements.<name>.md` row, per feature, each requirement with its SHALL sentence set apart and whether a test its `tests` field names proves it, and each scenario as Given, When and Then rows, a row joining clauses with ", and" read as one bullet per clause.

#### Scenario: A proved requirement

- **Given** a requirement whose `tests` names one test and whose scenario reads *Then the payment is refused, and the order stays paid*
- **When** the reader opens Requirements
- **Then** the requirement is marked Test linked, and its Then row holds the bullets *the payment is refused* and *the order stays paid*

### Requirement: Requirements read as the design lays them out

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.The_page_opens_on_its_summary_and_sets_the_shall_word_apart, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.A_long_feature_opens_two_requirements_and_lists_the_rest_as_rows, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_shall_sentence_splits_around_its_first_shall_outside_code]
```

The system SHALL open the Requirements page on a card saying what a requirement is with how many the file holds and how many a test proves, and a dashed What is always true card per unit the open feature's requirements rest on; list the features By feature · tested, each with its proved and total count and a bar; and draw each requirement as a card holding its title, a *Test linked* or dashed *No test linked* mark, its SHALL sentence with the word SHALL set apart, the tasks referencing it, and each scenario's GIVEN, WHEN and THEN rows on the card's foot. A feature with more than three requirements opens its first two and lists the rest as rows that each open in place, four of them before an *and n more* row.

#### Scenario: A feature with seven requirements

- **Given** a feature whose requirements file holds seven requirements
- **When** the reader opens it on Requirements
- **Then** the first two read as cards, four more as rows reading Show, and a last row reads *and 1 more in* the feature
- **And** pressing a row opens that requirement in place

### Requirement: A feature's rules are a link to Always true

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_sentence_explaining_invariants_gives_way_to_the_units_always_true_link, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_group_with_no_invariants_explained_or_aggregate_named_links_no_unit, unit:dotnet:Backlog.UI.Components.UnitTests.DomainRequirementsTests.A_sentence_naming_rules_no_unit_here_holds_stays, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.The_always_true_link_replaces_the_invariants_sentence_and_tells_the_host, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFeatureBoardTests.An_aggregate_can_be_opened_on_always_true, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFeaturesPageTests.Requirements_read_as_rows_and_the_Always_true_link_opens_the_aggregates_tab]
```

The system SHALL show, in place of a requirements group's sentence explaining invariants, a link that opens the aggregate the group, its requirements, its feature or that sentence names on the aggregate's Always true tab, and keep the sentence where no aggregate of the context matches.

#### Scenario: The rules a task keeps

- **Given** a requirements group whose text says *The rules a task keeps are its invariants*, linking `domain.invariants.md#task`
- **When** the reader opens Requirements and presses Always true for Task
- **Then** the sentence is not shown, and the Domain page opens on *Task* with Always true picked

### Requirement: The context map reads as a map and as a list

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.ContextMapTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewContextMapPageTests]
```

The system SHALL offer the context map as a Map tab that draws the contexts and their relations and a List tab that names the contexts in reading order. Map draws every bounded context of the map's Mermaid fence as a card on an ellipse, ordered to cross as few lines as it can, and one line per pair of contexts the fence joins, with a head at both ends when the pair points both ways. A line between two core contexts is amber and any other grey, and a line the fence draws `-.->` is dashed. Each card says whether its context is core or supporting and how many lines touch it. Pointing at or focusing a card fades every line that does not touch it. A line's tooltip and accessible name are the "Why" of the matching row in the source context's dependency tables, or the other context's when the source keeps none. Picking a card opens that context on its Neighbours tab. List names the core contexts and then the supporting ones, each with its "Why" from the Subdomain landscape table, then the published-languages table and the strategic rules. No pattern name shows on either tab.

#### Scenario: Switching to the list

- **Given** a domain folder with three bounded contexts
- **When** the reader opens Context map and picks List
- **Then** the three contexts are listed in reading order

#### Scenario: Reading a line and zooming to a context

- **Given** a context map whose fence draws `Inbox -->|OHS + Published Language| Tasks`, and an Inbox `dependencies.md` whose row for Tasks says why *A triaged item becomes a draft task*
- **When** the reader points at the Inbox card, then picks the Tasks card
- **Then** every line that does not touch Inbox fades, the Inbox–Tasks line reads *A triaged item becomes a draft task* and never *OHS*, and Tasks opens on its Neighbours tab

### Requirement: Flow draws a lifecycle as one row of states

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.Forward_arcs_are_drawn_above_the_row_the_ways_back_below_and_the_restore_loop_outermost, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.The_flow_page_draws_the_row_with_the_prose_beneath_and_mermaid_for_the_rest, unit:dotnet:Backlog.UI.Components.UnitTests.DomainFlowsTests.A_diagram_the_row_cannot_hold_is_left_to_mermaid, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewFlowPageTests.The_Flow_row_draws_the_lifecycle_as_one_row_and_keeps_every_other_diagram]
```

The system SHALL draw each `stateDiagram-v2` of a `flow.md` or `flow.<name>.md` row as one row of states, with the forward transitions above the row, the ways back below it, the way from the last state back to the first as a loop round the outside, and the chapter's prose beneath, and SHALL render every other Mermaid diagram of the file as it renders elsewhere.

#### Scenario: A lifecycle with a restore

- **Given** a `flow.md` chapter whose `stateDiagram-v2` runs *Draft* to *Ready* to *Done* and back from *Done* to *Draft* as *Restored*
- **When** the reader opens Flow
- **Then** *Draft*, *Ready* and *Done* stand in one row, *Restored* loops round the outside below it, and the chapter's prose follows the row

#### Scenario: A sequence diagram beside it

- **Given** the same `flow.md` holds a chapter with a `sequenceDiagram`
- **When** the reader opens Flow
- **Then** that chapter's diagram renders as Mermaid, as it does on every other page

### Requirement: The Glossary lists terms and model chapters A to Z

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.Lists_every_term_and_model_chapter_of_the_context_a_to_z, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.Each_row_shows_its_kind_its_aliases_as_code_chips_and_its_first_sentence, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.A_deprecated_term_is_struck_through_and_says_so_in_words, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.A_proposed_term_carries_a_proposed_badge, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.The_filter_matches_a_heading_or_an_alias, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.The_glossary_opens_on_its_lead_and_a_labelled_find_box_hinting_at_aliases, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.The_show_pills_count_and_narrow_to_model_terms_or_borrowed, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.A_term_naming_its_own_heading_in_another_context_is_borrowed, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.A_long_list_shows_fifteen_and_the_rest_behind_more, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.Terms_that_name_each_other_are_easily_confused_as_one_card_and_a_one_way_pointer_is_not, unit:dotnet:Backlog.UI.Components.UnitTests.DomainGlossaryTests.Reached_from_the_domain_chapter_bar_it_lists_the_split_files_too]
```

The system SHALL offer, from the Domain page's chapter bar, a Glossary that lists every term chapter under `## Ubiquitous Language` and every model chapter of the context's domain files A to Z, each with its kind, a proposed or deprecated badge, its aliases as code chips and its first sentence; that filters by heading or alias and by one choice of All, Model, Terms or Borrowed, each with its count; that strikes a deprecated chapter through; that marks as borrowed, linking there, a term whose `related` names its own heading in another context; and that sets beside the list, as one Easily confused card per group, the term chapters that name each other in `related`.

#### Scenario: Filtering by an alias

- **Given** a term *Customer* with the alias `Client`
- **When** the reader types `client` into the Glossary's filter
- **Then** *Customer* is listed

#### Scenario: Two terms that name each other

- **Given** a term *Basket* whose `related` names *Draft Order*, and *Draft Order*'s names *Basket*
- **When** the reader opens the Glossary
- **Then** Easily confused sets *Basket* beside *Draft Order* in one card, each with its first sentence

#### Scenario: A borrowed word

- **Given** a term *Roadmap Tag* whose `related` names `.devbook/domain/roadmap/domain.md#roadmap-tag`
- **When** the reader presses Borrowed
- **Then** *Roadmap Tag* is listed, reading *borrowed from roadmap*, and following that opens the roadmap context's chapter

### Requirement: A unit, feature or term shows the Brain notes linking to it

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/requirements.md#requirement-backlinks-reach-only-the-owner-and-write-nothing]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_unit_lists_the_notes_linking_to_it_or_to_a_chapter_inside_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_glossary_term_carries_its_notes_badge_and_box_and_a_term_with_none_carries_neither, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_model_part_counts_the_notes_linking_to_its_chapter, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.Links_to_a_whole_file_name_no_chapter_and_a_note_is_listed_once_per_chapter, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainBacklinkTests.A_repository_s_links_read_per_heading_once_per_note_and_heading]
```

The system SHALL show under each unit, feature and glossary term of the Domain view the From your Brain box for the owner's Brain notes linking to that chapter or to a chapter inside it, and on each model-diagram part and glossary term a teal **◆ n note** badge counting them, and draw neither where no note links.

#### Scenario: Notes on an aggregate and its entity

- **Given** a Brain note *Plan* linking to the aggregate *Task* and a note *Steps idea* linking to its entity *Sub Item*
- **When** the reader opens the Domain page on *Task*
- **Then** the box under *Task* lists *Plan* and *Steps idea*, and on Model *SubItem*'s part reads ◆ 1 note

#### Scenario: A term no note links to

- **Given** a Brain note linking to the term *Inbox* and none to the term *Today*
- **When** the reader opens the Glossary
- **Then** *Inbox* carries ◆ 1 note and its box, and *Today* carries neither

### Requirement: A feature or requirement shows the tasks naming it

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/features.md#bi-directional-linking]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_feature_shows_the_tasks_naming_it_gold_or_dashed_and_the_notes_linking_to_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.A_requirement_shows_the_tasks_naming_it_and_nothing_on_the_view_is_teal_but_the_Brains, unit:dotnet:Backlog.Desktop.UI.UnitTests.DomainViewBrainTaskLinksTests.Links_to_a_whole_file_name_no_chapter_and_a_note_is_listed_once_per_chapter]
```

The system SHALL show on a feature's detail and on each requirement the tasks whose Devbook references name that chapter as task chips, gold when this backlog holds the task and dashed, reading *not in this backlog*, when it does not, and pressing a gold chip SHALL open the task.

#### Scenario: Two tasks on Capture

- **Given** a task this backlog holds and a task id it does not hold, both naming the feature *Capture* in their Devbook references
- **When** the reader picks *Capture* on Features
- **Then** the detail shows the held task as a gold chip reading its title and the other as a dashed chip reading *not in this backlog*, and pressing the gold chip opens the task
