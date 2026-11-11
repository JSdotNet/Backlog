# Requirements

```meta
type: requirements
related: [.devbook/domain/brain/features.md]
```

> What the Brain's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it. They belong
> to the plan `+brain`: the roadmap item tagged `brain`, written with its
> [Plan Sigil](../roadmap/domain.md#plan-sigil). That plan builds local
> [ADR 0026](../../arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md)
> and local ADR 0027.

## Brain scope

```meta
type: requirements
related: [.devbook/domain/brain/features.md#brain-scope]
```

> The Brain chip in the header's repository scope (local ADR 0027).

### Requirement: The Brain chip leads the scope

```meta
type: requirement
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
related: [.devbook/domain/brain/features.md#brain-folder-setup]
```

> Choosing the Brain folder and scaffolding its convention (local ADR 0026).

### Requirement: The folder has no default

```meta
type: requirement
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
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSetupTests.Create_conventions_scaffolds_an_empty_folder, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSetupTests.Create_conventions_never_overwrites_a_readme_already_there, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.Create_conventions_scaffolds_the_folder_and_then_goes_away, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsBrainFolderTests.Create_conventions_leaves_the_persons_readme_unchanged, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainConventionTemplateTests.The_shipped_conventions_ignore_the_templates_folder, e2e:dotnet:Backlog.EndToEndTests.BrainTests.First_use_asks_for_a_folder_and_create_conventions_scaffolds_it]
```

The system SHALL write `README.md` marked `index: root`, `CONVENTIONS.md` with an `## Ignore` section listing `_templates`, and every area folder they declare on **Create conventions**, and leave every file already there unchanged.

#### Scenario: An empty folder

- **Given** an empty Brain folder
- **When** the reader presses Create conventions
- **Then** the folder holds `README.md` with `index: root`, `CONVENTIONS.md`, and the folders `_inbox`, `projects`, `areas`, `resources`, `people`, `journal` and `archive`
- **And** `CONVENTIONS.md` lists `_templates` under `## Ignore`

#### Scenario: A README already there

- **Given** a Brain folder whose `README.md` the person wrote
- **When** the reader presses Create conventions
- **Then** `CONVENTIONS.md` and the area folders are written and `README.md` is unchanged

### Requirement: The card says where the database is

```meta
type: requirement
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
related: [.devbook/domain/brain/context.md#brain-folder]
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
related: [.devbook/domain/brain/features.md#brain-mode-of-the-devbook-pane]
```

> The Devbook pane while it shows the Brain (local ADRs 0026 and 0027).

### Requirement: The pane follows the anchor and defaults to the Brain

```meta
type: requirement
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

### Requirement: The areas are the declared ones, then the discovered ones

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.Nothing_pressed_shows_the_brain_with_its_badge_path_and_declared_areas, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.Declared_areas_are_the_roots_in_their_declared_order, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.With_no_areas_declared_the_seven_para_areas_are_the_tabs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.No_declaration_gives_the_para_roots, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.A_folder_holding_a_note_is_a_tab_after_the_declared_ones, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSourceTests.The_areas_are_the_declared_ones_then_the_folders_holding_a_note, e2e:dotnet:Backlog.EndToEndTests.BrainTests.The_pane_shows_the_declared_areas_and_the_journal_newest_first]
```

The system SHALL show one tab per declared area in its order, or per PARA area when `CONVENTIONS.md` declares none, then one tab per other top folder holding a Markdown note, by folder name.

#### Scenario: Declared areas

- **Given** a `CONVENTIONS.md` declaring the areas *Work* (`work`) and *Home life* (`home`)
- **And** no other top folder holds a note
- **When** the pane shows the Brain
- **Then** it shows the tabs *Work* and *Home life* and no other

#### Scenario: No declaration

- **Given** a `CONVENTIONS.md` declaring no areas
- **And** no other top folder holds a note
- **When** the pane shows the Brain
- **Then** it shows the tabs of the PARA set

#### Scenario: A folder holding a note

- **Given** a `CONVENTIONS.md` declaring the area *Inbox* (`_inbox`)
- **And** `Meetings/` holds a note
- **When** the pane shows the Brain
- **Then** it shows the tabs *Inbox* and *Meetings*, in that order

#### Scenario: A folder holding no note

- **Given** a `CONVENTIONS.md` declaring the area *Inbox* (`_inbox`)
- **And** `Demo/` holds no Markdown file
- **When** the pane shows the Brain
- **Then** it shows the tab *Inbox* and no *Demo* tab

### Requirement: An ignored folder has no tab

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.Show_hidden_is_off_by_default_and_hides_the_hidden_folders, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.Without_show_hidden_the_hidden_folders_and_dot_entries_are_not_listed, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.An_ignored_folder_is_never_an_area_even_when_declared, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainFolderSourceTests.The_areas_are_the_declared_ones_then_the_folders_holding_a_note, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.An_ignored_folder_and_a_dot_folder_are_not_indexed]
```

The system SHALL show no tab for a folder `CONVENTIONS.md` lists under `## Ignore`, even when it holds a note or `## Areas` declares it.

#### Scenario: An ignored folder holding a note

- **Given** `Product/` holds a note
- **And** `CONVENTIONS.md` lists `Product` under `## Ignore`
- **When** the pane shows the Brain
- **Then** it shows no *Product* tab

#### Scenario: Ignored and declared

- **Given** `CONVENTIONS.md` declares the area *Templates* (`_templates`)
- **And** it lists `_templates` under `## Ignore`
- **When** the pane shows the Brain
- **Then** it shows no *Templates* tab

### Requirement: Show hidden brings hidden folders back

```meta
type: requirement
related: [.devbook/domain/brain/features.md#brain-mode-of-the-devbook-pane]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.Show_hidden_is_off_by_default_and_hides_the_hidden_folders, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.Show_hidden_adds_the_hidden_folders_as_tabs_after_the_rest_marked_hidden, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.Show_hidden_lists_an_area_s_dot_entries_in_the_menu_and_hides_them_again, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.A_pane_follows_the_device_s_show_hidden_choice, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.Without_show_hidden_the_hidden_folders_and_dot_entries_are_not_listed, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.Show_hidden_lists_the_hidden_folders_last_and_every_dot_entry, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainShowHiddenTests.Show_hidden_is_off_until_turned_on_and_survives_a_restart, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.BrainShowHiddenTests.The_port_announces_only_a_change_of_its_own_choice, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainConventionsTests.A_brain_s_hidden_folders_are_read_from_disk, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainDatabaseTests.An_ignored_folder_and_a_dot_folder_are_not_indexed]
```

The system SHALL, while **Show hidden** is on, show a tab marked hidden per ignored folder and dot-folder after the others, list dot-entries in an area's menu, and leave search and the Brain database unchanged.

#### Scenario: Hidden folders shown

- **Given** `CONVENTIONS.md` declares the area *Inbox* (`_inbox`) and lists `_templates` under `## Ignore`
- **And** the Brain holds a `.obsidian/` folder
- **When** the reader turns **Show hidden** on
- **Then** the pane shows the tabs *Inbox*, *Obsidian* and *Templates*, in that order, the last two marked hidden

#### Scenario: Off by default

- **Given** the reader has never turned **Show hidden** on on this device
- **When** the pane shows the Brain
- **Then** it shows no tab for an ignored folder or a dot-folder

#### Scenario: Search stays the same

- **Given** `_templates/` holds the note `meeting.md` and `CONVENTIONS.md` lists `_templates` under `## Ignore`
- **And** **Show hidden** is on
- **When** the reader searches the Brain for words in `meeting.md`
- **Then** the results do not list `_templates/meeting.md`

### Requirement: A dated area lists newest first

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.The_journal_tab_lists_and_opens_the_newest_entry_first, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.The_journal_sorts_newest_first_at_every_level_and_other_areas_by_name, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.An_area_conventions_marks_as_dated_sorts_newest_first, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookMenuBrainTests.A_dated_area_is_the_journal_or_one_its_holds_line_calls_dated, e2e:dotnet:Backlog.EndToEndTests.BrainTests.The_pane_shows_the_declared_areas_and_the_journal_newest_first]
```

The system SHALL list the notes of a dated area (the `journal` folder, or an area whose `Holds` line says *dated*) newest first by name, at every level, and every other area by name.

#### Scenario: The journal

- **Given** a journal holding `2026-09-30.md`, `2026-10-01.md` and `2026-10-13.md`
- **When** the reader opens the Journal tab
- **Then** the menu lists `2026-10-13.md` first and the pane opens it

### Requirement: The switch leaves the task filter alone

```meta
type: requirement
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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.The_repository_only_chrome_is_absent_and_its_services_are_not_asked, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.On_the_repository_the_same_harness_draws_the_repository_only_chrome]
```

The system SHALL leave branch updates, sync verdicts and scenario marks out of the pane while it shows the Brain.

#### Scenario: The same devbook content, Brain against repository

- **Given** a devbook with a scenarios file holding a journey, and sync verdicts for its chapters
- **When** the pane shows it for a repository, and then shows the Brain
- **Then** the repository view draws the update action, the verdict badges and the scenario mark, and the Brain view draws none of them

### Requirement: The header toggle names what the pane shows

```meta
type: requirement
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
related: [.devbook/domain/brain/features.md#repo-links-and-backlinks]
```

> A Brain note's links into repository devbooks and the
> backlinks a repository chapter shows its owner (local ADR 0027).

### Requirement: A repo link opens the repository chapter

```meta
type: requirement
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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Leaving_the_brain_scope_after_a_repo_link_brings_the_brain_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Pressing_the_brain_chip_after_a_repo_link_from_an_empty_scope_brings_the_brain_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_repo_link_left_behind_by_the_scope_does_not_come_back_with_the_brain, e2e:dotnet:Backlog.EndToEndTests.BrainTests.A_repo_link_opens_the_repository_chapter_and_the_brain_chip_brings_the_brain_back]
```

The system SHALL show the Brain in the Devbook pane again when the reader presses its chip after a repo link, and forget that link once the scope leaves the one it was followed under.

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
related: [.devbook/domain/brain/features.md#bi-directional-linking]
```

> Linking Brain notes and tasks both ways through a note's
> `tasks` field (local ADR 0026 §3, ADR 0027 §6).

### Requirement: A task link is the task's id

```meta
type: requirement
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
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainTaskLinkTests.A_note_lists_its_tasks_known_ones_followable_and_unknown_ones_dashed, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainTaskLinkTests.A_note_naming_no_task_draws_no_tasks_section, unit:dotnet:Backlog.UI.Components.UnitTests.BrainTaskLinkComponentTests.A_known_task_is_a_gold_chip_that_opens_it, unit:dotnet:Backlog.UI.Components.UnitTests.BrainTaskLinkComponentTests.An_unknown_task_is_a_dashed_chip_saying_so_with_nothing_to_press]
```

The system SHALL show under a Brain note's repo links a Tasks section with one chip per task its `tasks` field names, opening a task this backlog holds and reading *not in this backlog* otherwise.

#### Scenario: A known and an unknown task

- **Given** a Brain note naming one task this backlog holds and one it does not
- **When** the pane shows the note
- **Then** the Tasks section shows the first by its title as a chip that opens it, and the second as a dashed chip reading not in this backlog, and no error is shown

### Requirement: A task row counts the notes naming it, for the owner only

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainTaskLinkTests.A_task_lists_the_notes_naming_it_in_title_order_each_once, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneBrainNotesTests.A_row_named_by_brain_notes_carries_a_teal_badge_counting_them, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneBrainNotesTests.With_no_brain_wired_no_row_carries_a_badge, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksPaneBrainNotesTests.Pressing_a_one_note_badge_opens_that_note, unit:dotnet:Backlog.UI.Components.UnitTests.BrainTaskLinkComponentTests.A_badge_with_no_note_draws_nothing, unit:dotnet:Backlog.UI.Components.UnitTests.BrainTaskLinkComponentTests.A_badge_with_several_notes_lists_them_and_opens_the_one_pressed]
```

The system SHALL draw on a task row a teal ◆ n note badge counting the notes in the owner's own Brain that name the task, opening the note on the Devbook pane's Brain side.

#### Scenario: Two notes name a task

- **Given** two of the owner's Brain notes name one task in their `tasks` field
- **When** the task list shows that task in a repository scope or the Brain scope
- **Then** its row carries ◆ 2 notes marked only you see this, and pressing it lists both notes to open

### Requirement: Create task links the task from its note

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.Create_task_files_a_draft_in_the_brain_scope_pointing_at_the_note_and_lists_it, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.Create_task_from_a_selected_line_takes_the_line_as_its_title, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.Create_task_on_a_note_naming_repositories_files_one_task_in_each, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainPickupTests.Create_task_takes_the_selected_line_and_says_the_note_lists_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainPickupTests.Create_task_with_nothing_selected_asks_for_the_note_itself]
```

The system SHALL make, from a Brain note's **Create task**, a draft task titled by the selected line or the note's title, filed in the Brain scope or in each repository the note's `repos` names, carrying `#brain:<path>` in its Devbook references, and SHALL append its id to the note's `tasks`.

#### Scenario: A task from a selected line

- **Given** a Brain note with no `repos` and a line `- [ ] Buy boxes` selected in it
- **When** the reader presses Create task
- **Then** a draft task titled Buy boxes is filed in the Brain scope, it references the note, and the note's `tasks` lists its id

### Requirement: Plan this project names the plan in its note

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.Plan_this_project_drafts_from_the_note_and_its_links_imports_under_a_plan_entry_and_names_the_tag, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.A_plan_naming_a_repository_the_note_does_not_is_refused_before_import, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.A_note_naming_a_repository_the_workspace_does_not_have_makes_nothing, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.Plan_this_project_is_refused_on_a_note_outside_projects, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainPickupTests.Plan_this_project_is_offered_on_a_project_note_and_asks_for_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainPickupTests.A_note_outside_projects_offers_no_plan]
```

The system SHALL offer **Plan this project** on a note under the Brain's projects area only, draft the plan from the note and the Brain notes its `related` names, import it under a `plan` entry whose Knowledge Ref is the note, and append the plan's Roadmap Tag to the note's `roadmap`.

#### Scenario: A project note becomes a plan

- **Given** a note under `projects/` relating one resource note, and a configured plan drafter
- **When** the reader presses Plan this project
- **Then** the drafter is asked with both notes, the plan's roadmap item references the project note, and the note's `roadmap` names the plan's tag

### Requirement: A refused link keeps the work

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.A_note_whose_meta_block_does_not_parse_keeps_the_task_and_says_the_link_was_not_written, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.A_refused_roadmap_link_keeps_the_plan_and_says_so, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.Without_a_brain_or_a_backlog_both_actions_are_unavailable_with_a_reason, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainPickupTests.Without_a_drafter_only_plan_is_unavailable, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainPickupTests.A_link_the_note_refused_is_said_as_a_warning_and_the_work_stays, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainPickupTests.Each_action_that_cannot_run_stays_disabled_with_its_reason]
```

The system SHALL keep a task or plan made from a note whose top `meta` block it cannot parse, and say the link was not written and why; and SHALL show each pickup disabled with its reason while no Brain folder, or for a plan no drafter, is set.

#### Scenario: A note with a broken meta block

- **Given** a Brain note whose `tasks` list is not closed on its line
- **When** the reader presses Create task
- **Then** the task is in the backlog, the note is unchanged, and the pane says the link was not written into the note

## Atlas edges

```meta
type: requirements
related: [.devbook/domain/brain/features.md#atlas-edges]
```

> Drawing repo links in the Brain's atlas.

### Requirement: A resolved repo link is an edge to the repository chapter

```meta
type: requirement
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
```

The system SHALL draw a repo link this machine cannot resolve as a dashed edge.

#### Scenario: Another machine's repository

- **Given** a Brain note linking an alias not configured here
- **When** the reader opens the Brain's atlas
- **Then** the edge is dashed, its node is a hollow ring, and the node's chip reads *not on this machine*

## Lobes atlas

```meta
type: requirements
related: [.devbook/domain/brain/features.md#lobes-atlas, .devbook/domain/brain/context.md#lobes-atlas]
```

> The one atlas that draws the Brain and every devbook as
> brains. All of them hold while `atlas-lobes` is on.

### Requirement: One atlas maps the Brain and every registered devbook

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.With_the_lobes_flag_on_the_headers_atlas_takes_the_screen_and_its_close_returns_to_the_view, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_atlas_offers_no_scope_switch_and_draws_the_shells_scope, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.An_empty_shell_scope_draws_the_brain_and_every_registered_devbook_and_no_ghost, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.Each_scope_counts_its_regions_its_items_and_the_notes_linking_in, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_foot_shows_the_hint_with_the_zoom_level_and_the_marker_turns_with_the_camera, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_spaces_the_headline_from_the_title_as_the_design_does]
```

The system SHALL open one full-window atlas from the shell header that draws the Brain and the devbook of every registered repository as brains the reader rotates by dragging and zooms by scrolling.

#### Scenario: Opening from the header

- **Given** a Brain index, two registered repositories with devbooks, and nothing pressed in the header's repository scope
- **When** the reader opens the atlas from the shell header
- **Then** the atlas fills the window in the All layout, with the Brain and both devbooks drawn

#### Scenario: Rotating and zooming

- **Given** the atlas is open
- **When** the reader drags across it and then scrolls
- **Then** the drag rotates the brains and the scroll changes the zoom the atlas reports

#### Scenario: The headline

- **Given** the atlas is open in All
- **When** the reader reads its header
- **Then** the dash before the headline stands apart from the title, as in "ATLAS — Brain + 2 devbooks"

### Requirement: The map fills the atlas and its controls float over it

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_floats_every_part_over_a_full_bleed_map, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_parts_are_siblings_of_the_atlas_in_the_order_the_eye_meets_them, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_stacks_a_narrow_window_in_one_column_with_the_panel_in_a_sheet, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_panels_disclosure_names_the_panel_and_the_card_and_opens_the_sheet]
```

The system SHALL draw the map across the whole atlas in a window wider than 60rem, with the title, the left panel, the scope card, the top-right controls, the chips, the list of marks and the foot floating over it, and SHALL stack them in one column in a window 60rem wide or narrower, with the panel and the scope card folded behind one disclosure, the map in view and nothing overlapping.

#### Scenario: A wide window

- **Given** the atlas at 1440 × 900
- **When** the reader opens it
- **Then** the map fills the atlas, and the panel, the card, the controls, the chips, the list and the foot float over it

#### Scenario: Dragging on a control

- **Given** the atlas is open in a wide window
- **When** the reader drags or scrolls on the left panel, a chip or the list
- **Then** the camera does not move

#### Scenario: A phone

- **Given** the atlas at 390 × 844
- **When** the reader opens it
- **Then** the chips keep one sideways-scrolling row, the panel and the card sit behind one disclosure, the map is in view with its list under it, and nothing overlaps

### Requirement: The atlas draws the header's repository scope

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_atlas_offers_no_scope_switch_and_draws_the_shells_scope, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_new_shell_scope_hands_the_renderer_a_new_layout_which_resets_the_camera, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Several_in_view_draw_all_with_only_those_scopes, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.An_empty_shell_scope_draws_the_brain_and_every_registered_devbook_and_no_ghost, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_shells_one_member_opens_its_scope_and_one_with_no_devbook_says_so, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_shell_scope_change_lays_out_live_and_keeps_region_hides_labels_pause_and_tech, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_header_chip_press_lays_the_open_atlas_out_again, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.Opening_the_atlas_keeps_a_plural_scope_and_ctrl_on_a_chip_adds_to_it_while_it_has_the_screen, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesAtlasTests.InView_keeps_the_scopes_their_nodes_and_the_edges_between_them]
```

The system SHALL draw the members of the shell header's repository scope, one member as that scope alone, several or none pressed as All with only those scopes (the Brain in the middle when in view, each devbook a smaller brain around it), re-laying out while open when the scope changes, and say in words when nothing in scope has a devbook.

#### Scenario: All

- **Given** the Brain and backlog pressed in the header's repository scope, with two registered devbooks
- **When** the reader opens the atlas
- **Then** it draws All with only those two: the Brain in the middle and backlog's devbook as a smaller brain beside it

#### Scenario: One devbook

- **Given** the atlas is open in All
- **When** the reader presses backlog's header chip alone
- **Then** only that devbook is drawn, by region, without the atlas reopening

#### Scenario: Nothing to draw

- **Given** only a repository with no devbook pressed in the header's repository scope
- **When** the reader opens the atlas
- **Then** the atlas says none in scope has a devbook and draws no map

### Requirement: An unregistered alias is a ghost devbook

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.An_unregistered_alias_is_a_ghost_scope_holding_only_the_named_chapters_in_the_links_words, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Ghosts_is_a_toggle_that_draws_every_ghost_in_all_and_is_absent_without_one, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_ghost_row_steps_into_the_ghost_and_back_ghosts_off_or_a_shell_change_leaves_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesAtlasTests.In_all_each_tag_carries_its_count_and_a_ghosts_says_it_is_not_registered, unit:dotnet:Backlog.UI.Components.UnitTests.LobesAnatomyScriptTests.A_ghost_scope_makes_no_dust_and_its_outline_is_stroked_dashed_in_the_ghost_ink, unit:dotnet:Backlog.UI.Components.UnitTests.LobesAnatomyScriptTests.At_any_angle_the_outline_hugs_the_turned_shell]
```

The system SHALL draw a repository alias the Brain links to that is not registered on this machine as a dashed ghost devbook holding only the chapters the notes name, in the link's own words, drawn in All while the Ghosts toggle (offered while a ghost exists) is on, and its panel row steps into it alone.

#### Scenario: Another machine's repository

- **Given** Brain notes linking three chapters of an alias not registered here
- **When** the reader turns on Ghosts and presses the alias's row
- **Then** its brain alone is a dashed outline holding those three chapters, each named as its link wrote it

#### Scenario: Its tag in All

- **Given** a ghost devbook, a registered devbook, and Ghosts on
- **When** the reader opens the atlas in All
- **Then** the registered devbook's tag gives its name and its count, and the ghost's reads its alias and "· not registered"

### Requirement: A chapter of a devbook not indexed yet says the index is missing

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.A_chapter_of_a_devbook_not_indexed_yet_says_so_rather_than_that_it_is_no_chapter, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.A_chapter_an_indexed_devbook_does_not_hold_is_still_no_chapter_in_its_devbook, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_chapter_of_a_devbook_not_indexed_yet_says_so_on_its_chip_as_the_card_does]
```

The system SHALL give a chapter the notes name in a registered devbook whose index is not built yet a chip that reads *its devbook is not indexed yet — see its scope card*, and keep *not a chapter in its devbook* for a chapter an indexed devbook does not hold.

#### Scenario: A devbook not indexed yet

- **Given** a Brain note linking a chapter of a registered repository whose devbook index has not been built
- **When** the reader selects that chapter in the atlas
- **Then** it is drawn dashed, its chip reads *its devbook is not indexed yet — see its scope card*, and the scope's card says "Not indexed yet"

#### Scenario: A chapter an indexed devbook does not hold

- **Given** a Brain note linking a path that a registered, indexed devbook has no chapter for
- **When** the reader selects that chapter in the atlas
- **Then** it is drawn dashed and its chip reads *not a chapter in its devbook*

### Requirement: A Brain note's region comes from its PARA area

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.A_brain_note_is_placed_by_its_para_area, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.The_para_field_wins_over_the_folder, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.An_area_declared_beyond_para_is_parietal, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.Archive_is_the_dust_and_no_region, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.The_brain_stem_holds_conventions_and_the_area_roots_and_occipital_the_repository_hubs, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.The_para_attribute_wins_over_the_folder_for_a_note_and_its_chapters, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.A_para_area_in_a_folder_of_another_name_keeps_its_region]
```

The system SHALL place each Brain note in a region by its PARA area: Projects in Frontal; Areas, Resources and any area `CONVENTIONS.md` declares beyond PARA in Parietal; People in Temporal; the linked repository chapters in Occipital; Journal and the inbox in Cerebellum; area roots and `CONVENTIONS.md` in the Brain stem; and Archive as the faint outer dust.

#### Scenario: A project note

- **Given** a note in the Projects area
- **When** the reader opens the Brain scope
- **Then** the note is drawn in Frontal

#### Scenario: The para field wins

- **Given** a note in the Resources folder whose `para` field names Projects
- **When** the reader opens the Brain scope
- **Then** the note is drawn in Frontal, not Parietal

#### Scenario: A declared area beyond PARA

- **Given** a `CONVENTIONS.md` that declares an area `recipes`
- **When** the reader opens the Brain scope
- **Then** the notes in `recipes` are drawn in Parietal

#### Scenario: Archive

- **Given** a note in the Archive area
- **When** the reader opens the Brain scope
- **Then** the note is drawn in the outer dust and in no region

### Requirement: A devbook chapter's region comes from its folder

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesAnatomyScriptTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Technology_starts_hidden_in_a_devbooks_scope_with_its_count_still_in_the_panel, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_reader_shows_technology_with_its_chip_its_row_or_all, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Technology_starts_hidden_in_all_and_the_tech_toggle_beside_labels_reads_not_pressed, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_tech_toggle_shows_technology_in_all_and_leaves_each_scopes_own_chip_unchanged, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_tech_toggle_is_alls_alone, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_ghost_devbooks_named_technology_starts_hidden_in_its_scope_and_in_all_and_shows_with_its_chip_or_tech, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesAtlasTests.The_index_in_all_leaves_out_the_regions_hidden_in_all, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesAtlasTests.The_tech_regions_are_each_devbooks_parietal_ghosts_included_and_not_the_brains]
```

The system SHALL place each devbook chapter in a region by its folder: `arc42` in Frontal, `tech` in Parietal, `domain` in Temporal, `design` in Occipital, `ai` in Cerebellum, and each folder's convention root and the reading-order roots in the Brain stem, and SHALL start a devbook's Parietal region hidden in its scope until the reader turns it on with its chip, its panel row or All, and SHALL start every devbook's `tech/` chapters hidden in the All scope until the reader turns on that scope's Tech toggle, which leaves each devbook scope's own Parietal chip unchanged.

#### Scenario: Technology starts hidden

- **Given** a devbook with chapters under `tech/`
- **When** the reader opens that devbook's scope
- **Then** no `tech/` chapter is drawn on the map or listed beside it, its Parietal chip and panel row read not pressed, and the panel still gives Parietal its count

#### Scenario: The reader shows technology

- **Given** a devbook's scope with Parietal hidden
- **When** the reader presses the Parietal chip, its panel row, or All
- **Then** the devbook's `tech/` chapters are drawn in Parietal and listed beside the map

#### Scenario: Technology starts hidden in All

- **Given** registered devbooks with chapters under `tech/`
- **When** the reader opens the All scope
- **Then** no devbook's `tech/` chapter is drawn on the map or listed beside it, and the Tech toggle after Labels and Pause reads not pressed

#### Scenario: The reader shows technology in All

- **Given** the All scope with technology hidden
- **When** the reader presses the Tech toggle
- **Then** every devbook's `tech/` chapters are drawn in its Parietal region and listed beside the map, and each devbook scope's own Parietal chip is unchanged

#### Scenario: A decision record

- **Given** a devbook with a decision record under `arc42/adr/`
- **When** the reader opens that devbook's scope
- **Then** the record is drawn in Frontal

#### Scenario: A convention root

- **Given** a devbook whose `domain/` folder has a convention root
- **When** the reader opens that devbook's scope
- **Then** the root is drawn in the Brain stem, not Temporal

#### Scenario: Many roots

- **Given** a devbook with twenty-two convention and reading-order roots
- **When** the reader opens that devbook's scope at the default view
- **Then** its Brain stem draws the twenty-two as one cluster under the brain, inside the map

### Requirement: Brain and devbook marks keep their colours and bundles show the links

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesAtlasTests.A_brain_mark_wears_the_brains_tone_in_the_index_and_a_devbook_mark_stays_gold, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_component_stylesheet_paints_a_brain_mark_in_the_brains_accent]
```

The system SHALL draw Brain marks in `color-band-2` and devbook marks in gold, with a bundle from each Brain repository hub to the chapters the notes link, solid when the link resolves and dashed otherwise.

#### Scenario: Bundles in All

- **Given** Brain notes linking chapters of a registered devbook
- **When** the reader opens the All scope
- **Then** solid bundles run from the Brain's hub for that repository to those chapters

#### Scenario: The Brain hub in a devbook's scope

- **Given** Brain notes linking chapters of a registered devbook
- **When** the reader opens that devbook's scope
- **Then** a Brain hub floats beside the devbook with bundles to those same chapters

#### Scenario: An unresolved link

- **Given** a Brain note linking an alias not registered here
- **When** the reader opens the All scope
- **Then** the bundle from the Brain's hub for that alias is dashed

#### Scenario: The list beside the map

- **Given** the atlas in All
- **When** the reader reads the list beside the map
- **Then** each Brain note's mark is in `color-band-2` and each devbook chapter's is gold, as on the map

### Requirement: The panel and its controls act on the current scope

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.In_all_the_panel_lists_the_scopes_in_view_with_their_counts_and_a_row_asks_the_shell_for_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.In_a_scope_the_panel_lists_its_six_regions_with_what_each_holds_and_its_count, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.In_a_scope_the_top_right_has_labels_all_none_and_one_chip_per_region, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.In_all_there_are_no_chips_and_no_all_or_none, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.All_and_none_act_on_the_current_scope_only, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Labels_is_a_toggle_that_reports_its_state, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.None_in_a_scope_hides_its_archive_dust_and_its_rows_and_all_brings_them_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_show_all_tool_is_named_all_regions_and_starts_with_its_word, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Technology_starts_hidden_in_all_and_the_tech_toggle_beside_labels_reads_not_pressed, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Without_a_host_to_ask_alls_rows_are_a_plain_legend_and_its_card_offers_nothing]
```

The system SHALL list the scopes in view with their counts in the left panel in All, a row asking the header's scope for that scope alone, and the six regions with what each holds and its count in a single scope; offer Labels and Pause at the top right, with All, None and one chip per region in a single scope and none of them in All; apply the chips, Labels, All and None to the current scope only; hide a scope's Archive dust with its regions on None and show it again on All; and name the top right's All "All regions".

#### Scenario: None in one scope

- **Given** the atlas in the Brain scope with every region shown, and a devbook's regions all shown
- **When** the reader presses None and then presses that devbook's header chip
- **Then** the Brain's regions are hidden and the devbook's regions are all still shown

#### Scenario: The panel follows the scope

- **Given** the atlas in All
- **When** the reader presses the header's Brain chip alone
- **Then** the left panel, headed Anatomical regions, lists the six regions in place of the scopes, and the chips at the top right are the six regions'

#### Scenario: A row opens a scope

- **Given** the atlas in All
- **When** the reader presses a devbook's row in the left panel
- **Then** the header's scope becomes that devbook alone and the atlas shows its scope

#### Scenario: None hides the Archive

- **Given** the atlas in the Brain scope with a note in the Archive
- **When** the reader presses None, then All
- **Then** None leaves no mark on the map and no row in the list, the archived note among them, and All brings the note back

#### Scenario: No chips in All

- **Given** the atlas in All
- **When** the reader looks at the top right
- **Then** there is no chip and no All or None

### Requirement: The scope card offers one action for its scope

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_scope_card_says_when_the_scope_was_last_indexed_and_offers_its_action, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_close_button_and_the_card_raise_the_hosts_callbacks, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_scrolls_only_the_panels_rows_so_the_scope_card_stays_in_view, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Step_into_the_brain_asks_the_shell_for_the_brain, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_ghost_row_steps_into_the_ghost_and_back_ghosts_off_or_a_shell_change_leaves_it]
```

The system SHALL show a scope card naming the scope with its counts, when its index was last built, and its action: Open in the pane for the Brain, Open devbook for a devbook, Register repository with Back to all scopes for a ghost devbook, and Step into the Brain for All, which asks the header's scope for the Brain.

#### Scenario: A devbook

- **Given** the atlas in a registered devbook's scope
- **When** the reader presses Open devbook on its card
- **Then** the pane shows that repository's devbook and the atlas closes

#### Scenario: A ghost devbook

- **Given** the atlas in a ghost devbook's scope
- **When** the reader reads its card
- **Then** the card offers Register repository and Back to all scopes

#### Scenario: When it was indexed

- **Given** the Brain's index was built four minutes ago
- **When** the reader opens the Brain scope
- **Then** its card says "Indexed 4 minutes ago"

#### Scenario: A short window

- **Given** the atlas at 1440 by 900 in the scope of a devbook not indexed yet, whose card explains why
- **When** the reader reads its card
- **Then** the card and its action are in view without scrolling the panel; the rows above it scroll instead

### Requirement: Without a Brain index the atlas offers to build one

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.With_no_brain_index_the_empty_state_offers_to_build_it_and_a_way_back_instead_of_a_map, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Build_the_index_now_builds_it_and_the_map_takes_the_empty_states_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_build_that_fails_or_finds_nothing_says_why_and_keeps_the_offer, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_host_that_cannot_build_the_index_offers_only_the_way_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.While_the_index_builds_the_button_is_busy_says_so_and_a_second_press_starts_no_second_build, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.An_atlas_opened_on_a_scope_over_the_empty_state_opens_there_once_the_index_is_built, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesTests.The_lobes_source_builds_the_brains_index_through_the_refresher, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_empty_states_header_names_the_brain_beside_the_title]
```

The system SHALL, with no Brain index yet, name the Brain beside the atlas's title, say it is not indexed and offer Build the index now and Back to notes instead of an empty map.

#### Scenario: A Brain never indexed

- **Given** a Brain folder whose index has not been built
- **When** the reader opens the atlas
- **Then** its header reads "Atlas" with the Brain's badge beside it, it says the Brain is not indexed yet and offers Build the index now and Back to notes, and draws no map

#### Scenario: Building it

- **Given** the atlas shows that the Brain is not indexed yet
- **When** the reader presses Build the index now
- **Then** the index is built and the map takes the empty state's place

### Requirement: The foot shows the zoom and the orientation

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_foot_shows_the_hint_with_the_zoom_level_and_the_marker_turns_with_the_camera]
```

The system SHALL show under the map the drag and scroll hint with the zoom level, and an orientation marker that turns with the camera.

#### Scenario: Zooming in

- **Given** the atlas at its default view
- **When** the reader scrolls to zoom in to 150%
- **Then** the hint ends with 150% and the orientation marker has turned with the camera

### Requirement: The keyboard browses the list without opening details

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_keyboard_arrows_through_the_list_without_opening_the_sheet_and_enter_opens_it, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasIndexTests.The_arrows_and_the_ends_move_the_active_row_and_leave_the_selection, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasIndexTests.Enter_or_space_selects_the_row_the_keyboard_is_on, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasIndexTests.Components_js_refuses_the_index_keys_default_at_the_keydown_and_lets_them_bubble, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasTests.Arrowing_the_index_spotlights_the_row_on_the_picture_and_enter_selects_it, unit:dotnet:Backlog.UI.Components.UnitTests.GraphAtlasTests.Clearing_the_selection_leaves_the_tab_stop_on_the_row_and_clears_the_spotlight]
```

The system SHALL keep the marks list one tab stop: arrows, Home and End move focus and spotlight between rows without opening details, Enter or Space opens them, and Escape closes them, refocusing the row.

#### Scenario: Arrowing down the list

- **Given** the atlas in the Brain scope with no mark selected
- **When** the reader tabs into the list beside the map and presses the down arrow
- **Then** the focus and the map's spotlight move from the first mark to the second, and no details are open

#### Scenario: Opening and closing a mark's details

- **Given** the reader has arrowed to a mark in the list
- **When** the reader presses Enter, then Escape
- **Then** Enter opens that mark's details, and Escape closes them with the focus back on its row

### Requirement: Every atlas control is a full-size button

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Every_control_of_the_atlas_is_a_real_button, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Every_control_of_the_empty_state_is_a_real_button, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.The_stylesheet_gives_every_atlas_control_a_44px_target_and_a_focus_ring]
```

The system SHALL make every control of the atlas a real button at least 44 px square with a visible focus ring, and every toggle report `aria-pressed`.

#### Scenario: A region chip

- **Given** the atlas in the Brain scope
- **When** the reader tabs to a region chip
- **Then** the chip shows a focus ring, is at least 44 px square, and reports whether it is pressed

### Requirement: Selecting a mark opens its details and follows it into the Devbook

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_brain_note_offers_to_open_in_the_pane, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_resolved_chapter_opens_its_devbook_and_a_dashed_one_says_why_it_cannot, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.A_devbook_chapter_no_note_links_to_still_opens_in_its_devbook, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_chapter_followed_out_of_the_atlas_opens_the_devbook_in_its_place, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.A_heading_chapter_followed_out_of_the_atlas_opens_the_devbook_at_that_heading, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneChapterNavigationTests.A_request_with_an_anchor_into_an_arc42_chapter_brings_the_heading_into_view, unit:dotnet:Backlog.UI.Components.UnitTests.BacklogRevealHeadingScriptTests]
```

The system SHALL open a mark's details when the reader selects it on the map or in the list beside it, and following a Brain note, a gold chip or a devbook's own chapter out of them SHALL give the Devbook the screen in the atlas's place, at the heading the chapter names when it is a heading inside a file.

#### Scenario: Selecting a mark

- **Given** the atlas is open in the All scope
- **When** the reader selects a devbook chapter's mark in the list beside the map
- **Then** its details open, offering to open the chapter in its devbook

#### Scenario: A dashed chip

- **Given** a chapter a note links to in a repository not registered here
- **When** the reader selects its mark
- **Then** its details show the dashed chip with the reason it cannot be followed, and nothing to press

#### Scenario: A chapter that is a heading inside a file

- **Given** the atlas shows *Quality Goals*, a heading in `.devbook/arc42/01-introduction-and-goals.md`
- **When** the reader selects its mark and opens it
- **Then** the atlas closes, the Devbook opens that file, and the reading is scrolled to *Quality Goals*, whether the chapter was the devbook's own or a note's gold chip

### Requirement: The pane's Atlas button follows the flag

```meta
type: requirement
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneAtlasTests.With_the_lobes_flag_on_the_atlas_button_opens_the_lobes_atlas_at_the_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneAtlasTests.With_the_lobes_flag_off_the_atlas_button_opens_todays_atlas, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneAtlasTests.On_the_brain_the_atlas_button_opens_the_lobes_atlas_at_the_brain, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeWorkspaceSurfaceTests.The_devbooks_atlas_button_opens_the_atlas_on_the_shells_scope_and_the_way_back_returns_to_the_devbook]
```

The system SHALL open the Lobes atlas on the header's repository scope from the Devbook pane's Atlas button while `atlas-lobes` is on, and today's atlas otherwise, leaving the Technology section to its own views.

#### Scenario: On a repository

- **Given** `atlas-lobes` is on, the header's scope is `backlog` alone, and the pane shows `backlog`
- **When** the reader presses Atlas
- **Then** the Lobes atlas opens in the `backlog` scope

#### Scenario: Flag off

- **Given** `atlas-lobes` is off and the pane shows the Brain
- **When** the reader presses Atlas
- **Then** today's atlas of the Brain opens

#### Scenario: The Technology section

- **Given** `atlas-lobes` is on
- **When** the reader opens the Technology section
- **Then** it shows its own views, and no atlas draws the stack

### Requirement: A drag turns the brains like a globe in the hand

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.A_fling_slows_and_comes_to_rest_within_a_second_and_a_half, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.One_long_frame_moves_the_camera_as_far_as_two_short_ones]
```

The system SHALL turn the near face of a brain with the pointer on both axes while the reader drags, and carry a brisk drag on after release until it comes to rest.

#### Scenario: Dragging across

- **Given** the brains are at rest
- **When** the reader drags to the right across the map
- **Then** the near face of the brains moves to the right with the pointer

#### Scenario: A fling

- **Given** the atlas is open
- **When** the reader drags quickly across it and lets go while moving
- **Then** the brains keep turning the same way and come to rest within about a second and a half

#### Scenario: A click is no fling

- **Given** the brains are at rest
- **When** the reader presses and releases on a mark without moving the pointer
- **Then** the camera stays where it was and the mark's details open

#### Scenario: Catching a spin

- **Given** the brains are still turning after a fling
- **When** the reader presses on a mark
- **Then** the brains stop where they are and no details open

### Requirement: A scroll zooms toward the pointer

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.A_zoom_keeps_the_point_under_the_cursor_where_it_is, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.The_pan_is_held_at_the_centre_until_zoomed_in_and_never_past_the_edge, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.An_easing_closes_on_its_target_and_then_stops_there]
```

The system SHALL zoom toward the point under the pointer when the reader scrolls, easing to each step and staying between half and 2.4 times.

#### Scenario: Zooming at the pointer

- **Given** the atlas is open
- **When** the reader scrolls in with the pointer on a mark away from the centre
- **Then** the zoom eases to its new level and the mark stays under the pointer

#### Scenario: At the limit

- **Given** the atlas is zoomed in to 2.4 times
- **When** the reader scrolls in again
- **Then** the zoom stays at 2.4 times and the foot still reads 240%

### Requirement: A new layout eases the camera home

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.The_scope_curve_starts_at_rest_ends_at_rest_and_never_turns_back, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.Easing_home_takes_the_short_way_round_however_far_the_idle_turn_wound]
```

The system SHALL ease the camera from the last view to the default view, the short way round, when the header's scope or the Ghosts toggle changes what the atlas draws.

#### Scenario: Switching scope

- **Given** the reader has turned the brains in All
- **When** the reader presses a devbook's header chip alone
- **Then** the camera turns through the views in between and settles on the default view within a second

#### Scenario: After a long idle turn

- **Given** the brains have turned on their own for several minutes
- **When** the reader changes the header's scope
- **Then** the camera turns less than half a turn on its way to the default view

### Requirement: A region or Labels toggle leaves the camera as it was

```meta
type: requirement
```

The system SHALL keep the camera's view and its motion when the reader shows or hides a region or turns Labels on or off.

#### Scenario: A toggle mid-turn

- **Given** the brains are turning
- **When** the reader hides a region or turns Labels off
- **Then** the brains keep turning from where they were, with no jump

### Requirement: The brains turn on their own only while nobody uses them

```meta
type: requirement
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.The_idle_turn_waits_its_delay_then_ramps_in_over_transition_slow, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.The_idle_turn_is_slow_enough_to_take_about_a_hundred_seconds_a_turn, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.Resuming_after_a_pause_waits_the_delay_from_the_resume_then_eases_in, unit:dotnet:Backlog.UI.Components.UnitTests.LobesCameraScriptTests.Any_other_hand_over_keeps_the_last_input_it_remembers, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Pause_is_a_toggle_that_holds_the_idle_turn, unit:dotnet:Backlog.Desktop.UI.UnitTests.LobesAtlasTakeoverTests.Pausing_changes_nothing_but_the_idle_turn, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookLobesAtlasTests.The_idle_turn_is_on_unless_paused_and_passes_through]
```

The system SHALL turn the brains slowly on their own only after four seconds without input, stop the turn at once when the reader presses, scrolls or rests the pointer on a mark, and offer a Pause toggle that holds the turn while the atlas is open until the reader presses it again.

#### Scenario: Left alone

- **Given** the atlas is open with the brains at rest
- **When** the reader leaves the map untouched for four seconds
- **Then** the brains start turning slowly, about one turn in a hundred seconds

#### Scenario: Coming back

- **Given** the brains are turning on their own
- **When** the reader presses or scrolls on the map, or rests the pointer on a mark
- **Then** the turn stops at once, and starts again only after four more seconds without input

#### Scenario: Pausing

- **Given** the brains are turning on their own
- **When** the reader presses Pause
- **Then** the brains stop, Pause reads pressed, and they stay still until Pause is pressed again

#### Scenario: Resuming

- **Given** the reader has paused the brains
- **When** the reader presses Pause again
- **Then** the brains wait four seconds without input, then ease into the turn

### Requirement: Reduced motion holds the atlas still

```meta
type: requirement
```

The system SHALL, under reduced motion, turn the brains only while a drag lasts, land a scroll and a new layout at once, and never turn the brains on their own.

#### Scenario: Reduced motion

- **Given** reduced motion is on
- **When** the reader drags quickly and lets go, then scrolls, then changes the header's scope
- **Then** the drag turns the brains only until the release, and the zoom and the default view arrive at once

#### Scenario: Waiting under reduced motion

- **Given** reduced motion is on and the atlas is open
- **When** the reader leaves the map untouched for longer than four seconds
- **Then** the brains do not move

## Capture into the Brain

```meta
type: requirements
related: [.devbook/domain/brain/features.md#capture-into-the-brain]
```

> Writing a new note into the Brain, from the pane and from
> Inbox triage.

### Requirement: Capture writes a note into the inbox area

```meta
type: requirement
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
related: [.devbook/domain/brain/features.md#mcp-brain-scope]
```

> The knowledge tools the app serves over MCP, on the Brain.

### Requirement: The knowledge tools take the Brain scope

```meta
type: requirement
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
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.A_capture_lands_in_the_inbox_area_and_reads_back_as_written, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainScopeToolsTests.A_second_capture_under_the_same_title_never_overwrites_the_first]
```

The system SHALL write a note sent to the capture tool into the Brain's inbox area through the writer the pane's Capture uses.

#### Scenario: An assistant captures

- **Given** a Brain folder is set
- **When** an assistant calls the capture tool with a title and a body
- **Then** a new note with that title and body is in the Brain's inbox area


## PARA default gains someday

```meta
type: requirements
related: [.devbook/domain/brain/features.md#para-organization, .devbook/domain/brain/domain.md#para-category, .devbook/domain/brain/domain.md#knowledge-note, .devbook/arc42/adr/0054-clarify-outcomes-follow-gtd-and-each-item-has-one-home.md]
```

> Local ADR 0054 decides these. Only the first is built.

### Requirement: The PARA default holds someday after projects

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainNoteDestinationsTests.The_para_default_set_has_eight_areas_with_someday_after_projects, unit:dotnet:Backlog.Infrastructure.Devbook.UnitTests.BrainNoteDestinationsTests.A_brain_declaring_its_own_areas_gets_no_someday, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPaneBrainModeTests.With_no_areas_declared_the_eight_para_areas_are_the_tabs]
```

The system SHALL show, for a Brain whose `CONVENTIONS.md` declares no areas, the area `someday` directly after `projects`, and SHALL add no `someday` area to a Brain that declares its own.

#### Scenario: No declaration

- **Given** a `CONVENTIONS.md` declaring no areas
- **And** no other top folder holds a note
- **When** the pane shows the Brain
- **Then** it shows the tabs `_inbox`, `projects`, `someday`, `areas`, `resources`, `people`, `journal` and `archive`, in that order

#### Scenario: Declared areas

- **Given** a `CONVENTIONS.md` declaring the areas *Work* (`work`) and *Home life* (`home`)
- **And** no other top folder holds a note
- **When** the pane shows the Brain
- **Then** it shows the tabs *Work* and *Home life* and no *Someday* tab

### Requirement: The app never writes a review date

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainNoteLinkWriterTests.A_link_written_beside_a_review_date_never_touches_it]
```

The system SHALL read a note's optional `review` date and SHALL never add, change or remove it, through capture, To Brain or the link writer.

#### Scenario: Linking a note with a review date

- **Given** a note whose top `meta` block says `review: 2026-12-01`
- **When** the app appends a task id to the note's `tasks` field
- **Then** the block still says `review: 2026-12-01`, byte for byte

### Requirement: A note's review date comes due

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainReviewDateReaderTests.The_notes_whose_review_date_has_come_arrive_oldest_first, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainReviewDateReaderTests.An_unreadable_date_is_a_warning_and_the_rest_still_answer, unit:dotnet:Backlog.Infrastructure.Brain.UnitTests.BrainReviewDateReaderTests.A_later_day_brings_the_future_review_in_and_a_headings_review_never, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.BrainReviewToolTests.It_answers_the_due_notes_and_the_warnings_for_today]
```

The system SHALL keep the `review` value of a note's top `meta` block in the Brain's database as written, and SHALL answer the notes whose date is on or before a given day, oldest first, to the app and through `list_brain_reviews`. A value that is not a `YYYY-MM-DD` date SHALL be a warning beside that answer, never an error.

#### Scenario: A review date that has come

- **Given** a note whose top `meta` block says `review: 2026-11-01`
- **When** the app asks for the notes due on 2026-11-11
- **Then** the note is in the answer, with its date

#### Scenario: A review value that is no date

- **Given** a note whose top `meta` block says `review: next week`
- **When** the app asks for the notes due on any day
- **Then** the note is listed as a warning and left out of the due notes
- **And** the build and the other notes' answer go on as before
