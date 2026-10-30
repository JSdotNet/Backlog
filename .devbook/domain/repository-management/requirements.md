# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/repository-management/features.md, .devbook/arc42/adr/0029-an-archived-repository-stays-in-the-registry.md, .devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

## Archiving a repository

```meta
type: requirements
status: draft
related: [.devbook/domain/repository-management/features.md#archiving-a-repository, .devbook/domain/repository-management/domain.md#archived-repository, .devbook/arc42/adr/0029-an-archived-repository-stays-in-the-registry.md]
```

> The requirements of an [archived repository](domain.md#archived-repository):
> what stops reading it as active work, what keeps working, and what Unarchive
> brings back (local ADR 0029). The rules the registry itself holds are
> [its invariants](domain.invariants.md#repository-registry).

### Requirement: Archive and Unarchive are offered on the repository's row

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.Archiving_a_repository_keeps_its_row_marks_it_archived_and_offers_Unarchive, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.The_last_active_repository_cannot_be_archived_and_says_one_must_stay_active]
```

The system SHALL offer Archive on the row of an active repository and Unarchive on the row of an archived one, in the repository list in Settings.

#### Scenario: Archiving a retired repository

- **Given** `backlog` and `budgetbeheer` are configured and both are active
- **When** the person opens the row of `budgetbeheer` and chooses Archive
- **Then** the row stays in the list, marked Archived, and offers Unarchive

#### Scenario: The last active repository

- **Given** `backlog` is the only active repository
- **When** the person opens the row of `backlog`
- **Then** Archive is unavailable, and the row says one repository must stay active

### Requirement: Archiving is suggested and never inferred

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.A_repository_GitHub_reports_archived_suggests_Archive_and_stays_active, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.A_repository_GitHub_cannot_find_says_so_and_suggests_Archive, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubRepositoryStateTests]
```

The system SHALL suggest Archive on a repository's row when GitHub reports the repository archived or cannot find it, and leave the repository active until the person chooses Archive.

#### Scenario: GitHub reports the repository archived

- **Given** `budgetbeheer` is active and GitHub reports it archived
- **When** the person opens the repository list
- **Then** the row of `budgetbeheer` suggests Archive, and the repository is still active

#### Scenario: GitHub cannot find the repository

- **Given** `budgetbeheer` is active and every GitHub read of it answers not found
- **When** the person opens the repository list
- **Then** the row of `budgetbeheer` says GitHub cannot find it and suggests Archive

### Requirement: GitHub reads skip an archived repository

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.PullRequestsPaneTests.An_archived_repository_is_not_read_and_names_no_failure, unit:dotnet:Backlog.Desktop.UI.UnitTests.WorkInProgressReaderTests.A_link_into_an_archived_repository_is_read_again_once_it_is_unarchived]
```

The system SHALL read nothing from GitHub for an archived repository in the Pull requests pane, the Goals view, and the dashboard's activity, and report no failure for it there.

#### Scenario: The Pull requests pane refreshes

- **Given** `budgetbeheer` is archived and GitHub cannot find it
- **When** the person refreshes the Pull requests pane
- **Then** no line reads "Couldn't read innovadis-dev/Budgetbeheer", and the pull requests of the active repositories are listed

#### Scenario: The Goals view refreshes

- **Given** `budgetbeheer` is archived, GitHub cannot find it, and a goal holds one of its pull requests
- **When** the Goals view refreshes
- **Then** it reads no pull requests of `budgetbeheer` and reports no failure for it

### Requirement: Pickers and the scope hide an archived repository

```meta
type: requirement
status: draft
related: [.devbook/domain/tasks/features.md#search-filter-and-organize, .devbook/domain/tasks/features.md#board-view]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.An_archived_repository_has_no_scope_chip_while_show_archived_is_off, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Turning_show_archived_on_offers_the_archived_repository_marked_and_remembers_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.TasksArchivedRepositoryTests]
```

The system SHALL leave an archived repository out of the repository scope, the Board's by-repository columns and every repository picker while show archived is off, and leave a task out of the task list and the Calendar while every repository it names is archived.

#### Scenario: The repository scope

- **Given** `backlog` is active, `budgetbeheer` is archived and show archived is off
- **When** the desktop shell opens
- **Then** the scope shows the Brain chip and `backlog`, and no chip for `budgetbeheer`

#### Scenario: Show archived is on

- **Given** `budgetbeheer` is archived
- **When** the person turns show archived on
- **Then** the scope, the Board's columns and the pickers offer `budgetbeheer`, marked Archived

#### Scenario: The Board by repository

- **Given** an open task filed against `budgetbeheer`, which is archived, and show archived is off
- **When** the person lays the Board out by repository
- **Then** no column is titled `budgetbeheer`, and the task is in no other column

#### Scenario: The task list and the Calendar

- **Given** a task with a due date filed only against `budgetbeheer`, which is archived, another filed against `budgetbeheer` and `backlog`, and show archived is off
- **When** the person opens the task list and the Calendar
- **Then** the first task is in neither, and the second is listed
- **And** once show archived is on, the first task is back in both

### Requirement: Show archived is remembered on this device

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.ShowArchivedRepositoriesTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.Show_archived_left_on_comes_back_on_with_a_fresh_shell]
```

The system SHALL remember whether show archived is on for this device, and not carry the choice to another device.

#### Scenario: A restart

- **Given** the person turned show archived on
- **When** the app is opened afresh on the same device
- **Then** show archived is still on

#### Scenario: A paired device

- **Given** the person turned show archived on, on one device
- **When** a paired device opens the app
- **Then** show archived there is as that device left it

### Requirement: A remembered scope drops an archived repository

```meta
type: requirement
status: draft
related: [.devbook/domain/tasks/features.md#search-filter-and-organize]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_remembered_scope_drops_a_repository_archived_since_while_show_archived_is_off, unit:dotnet:Backlog.Desktop.UI.UnitTests.HomeRepositoryScopeTests.A_remembered_scope_keeps_an_archived_repository_while_show_archived_is_on]
```

The system SHALL drop an archived repository from a remembered scope when the scope comes back while show archived is off.

#### Scenario: The repository was archived since

- **Given** the remembered scope is `backlog` and `budgetbeheer`, and `budgetbeheer` has been archived since
- **When** the shell is opened afresh with show archived off
- **Then** `backlog` alone is pressed

#### Scenario: Show archived is on

- **Given** the remembered scope is `backlog` and `budgetbeheer`, and `budgetbeheer` is archived
- **When** the shell is opened afresh with show archived on
- **Then** `backlog` and `budgetbeheer` are both pressed

### Requirement: An archived repository's linked tasks stop syncing

```meta
type: requirement
status: draft
related: [.devbook/domain/tasks/features.md#linked-tasks, .devbook/domain/tasks/domain.md#connected-target, .devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md]
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.LinkedTaskSyncWorkerTests.A_tick_skips_a_target_whose_repository_is_archived_until_it_is_unarchived, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.A_target_whose_repository_is_archived_is_paused_with_no_fetch_and_nothing_written]
```

The system SHALL not sync a [Connected Target](../tasks/domain.md#connected-target) while the repository it is filed under is archived, and leave the target's settings and its enabled state as they were.

#### Scenario: The sync interval passes

- **Given** an enabled Connected Target filed under `budgetbeheer`, which is archived
- **When** its sync interval passes
- **Then** no sync runs, and its linked tasks stay as they were

#### Scenario: The target's settings

- **Given** an enabled Connected Target filed under `budgetbeheer`
- **When** the person archives `budgetbeheer`
- **Then** the target is still enabled in its settings, and every other setting is unchanged

### Requirement: Archiving offers to move or drop the open tasks

```meta
type: requirement
status: draft
related: [.devbook/domain/tasks/domain.md#task-status]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.Archiving_asks_about_the_open_tasks_and_moving_them_files_them_under_the_other_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.The_open_tasks_can_only_move_to_another_active_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.Archiving_and_dropping_the_open_tasks_drops_them_and_archives, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.Keeping_the_open_tasks_archives_and_the_archived_row_offers_the_choice_later, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.A_refused_move_leaves_the_repository_active_and_says_why, unit:dotnet:Backlog.Modules.Tasks.UnitTests.RefileRepositoryTasksTests]
```

The system SHALL offer to move an archived repository's open tasks to another active repository or to drop them, when the repository is archived and afterwards from its archived row, and leave the repository active when the move or drop is refused.

#### Scenario: Moving the open tasks

- **Given** `budgetbeheer` has three open tasks and `backlog` is active
- **When** the person archives `budgetbeheer` and chooses to move its open tasks to `backlog`
- **Then** the three tasks are filed against `backlog`, and `budgetbeheer` is archived

#### Scenario: Dropping the open tasks

- **Given** `budgetbeheer` has three open tasks
- **When** the person archives `budgetbeheer` and chooses to drop its open tasks
- **Then** each of the three takes the `archived` [Task Status](../tasks/domain.md#task-status), stays filed against `budgetbeheer`, and no task is deleted

#### Scenario: Keeping the open tasks

- **Given** `budgetbeheer` has three open tasks
- **When** the person archives `budgetbeheer` and declines both
- **Then** the three tasks are unchanged, and the archived row offers the same choice later

#### Scenario: Only an active repository takes them

- **Given** `budgetbeheer` has open tasks, `backlog` is active and `old` is archived
- **When** the person archives `budgetbeheer` and chooses where to move its open tasks
- **Then** `backlog` is offered and `old` is not

#### Scenario: The tasks cannot be refiled

- **Given** `budgetbeheer` has open tasks
- **When** the person archives `budgetbeheer`, chooses to move or drop its open tasks, and the tasks cannot be refiled
- **Then** `budgetbeheer` stays active, and the dialog says the tasks could not be refiled

### Requirement: The roadmap keeps an archived repository's past in view

```meta
type: requirement
status: draft
related: [.devbook/domain/roadmap/features.md#reading-the-plan-by-repository]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewArchivedTests.Finished_work_in_the_drawn_window_draws_the_archived_band_marked_archived, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewArchivedTests.Finished_work_before_the_drawn_window_draws_no_band_until_the_window_reaches_back_to_it]
```

The system SHALL draw the band of an archived repository, greyed and tagged Archived with only its finished work, while the timeline's whole drawn window holds finished work of it. The rest of the plan sets that window, not where the person has scrolled.

#### Scenario: Finished work in the drawn window

- **Given** `budgetbeheer` is archived, has finished work last month, the drawn window reaches back to last month, and show archived is off
- **When** the person reads the roadmap
- **Then** a greyed band tagged Archived shows the finished work of `budgetbeheer`

#### Scenario: Finished work before the drawn window

- **Given** `budgetbeheer` is archived and all its finished work lies before the drawn window
- **When** the person reads the roadmap
- **Then** no band of `budgetbeheer` is drawn
- **And** once the rest of the plan widens the window back to that work, the band is drawn again

### Requirement: The roadmap hides an archived repository's open work

```meta
type: requirement
status: draft
related: [.devbook/domain/roadmap/features.md#reading-the-plan-by-repository]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewArchivedTests.Open_work_of_an_archived_repository_is_drawn_in_no_band_and_not_under_no_repository, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapPlanViewArchivedTests.Show_archived_draws_the_whole_band_with_its_open_work, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandArchivedTests.Show_archived_on_draws_the_whole_band_greyed_and_tagged_archived]
```

The system SHALL hide the open work that names only an archived repository while show archived is off, and never draw it in the No repository band.

#### Scenario: Show archived is off

- **Given** open work names only `budgetbeheer`, which is archived, and show archived is off
- **When** the person reads the roadmap
- **Then** the open work is drawn in no band, and the No repository band does not hold it

#### Scenario: Show archived is on

- **Given** open work names only `budgetbeheer`, which is archived
- **When** the person turns show archived on
- **Then** the band of `budgetbeheer` is drawn whole, greyed and tagged Archived, with its open work in it

### Requirement: Finished work of an archived repository still counts

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.RoadmapCompletedWorkTests.Archiving_a_repository_leaves_the_work_counted_over_a_past_range_unchanged
```

The system SHALL count the finished work of an archived repository toward history and pace, as it counted while the repository was active.

#### Scenario: Pace after archiving

- **Given** `budgetbeheer` finished work last month
- **When** the person archives `budgetbeheer`
- **Then** the history and the pace computed over last month are unchanged

### Requirement: An archived repository's devbook is read-only

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/features.md#area-selection-and-scope, .devbook/domain/devbook/features.md#the-devbook-on-the-repositorys-latest-version, .devbook/arc42/adr/0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md]
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.Arc42DevbookPanelTests.An_archived_repositorys_chapter_reads_with_no_edit_and_no_remark, unit:dotnet:Backlog.Desktop.UI.UnitTests.Arc42DevbookPanelTests.Unarchiving_gives_the_chapter_its_edit_and_its_remarks_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookUpdateServiceTests.An_archived_repositorys_clone_is_not_offered_an_update_until_it_is_unarchived, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookFolderSourceBranchTests.An_archived_repositorys_clone_reads_and_is_not_editable_until_it_is_unarchived]
```

The system SHALL open the devbook of an archived repository for reading only, with no edit, no remark and no offer to bring its clone up to date.

#### Scenario: A clone of an archived repository

- **Given** `budgetbeheer` is archived and has a local clone with a devbook
- **When** the person opens a chapter of its devbook
- **Then** the chapter reads, and no edit, remark or update control is offered

#### Scenario: Unarchived again

- **Given** `budgetbeheer` was archived and has a local clone
- **When** the person unarchives it and opens a chapter of its devbook
- **Then** the chapter is editable again, as a clone's is

### Requirement: No devbook snapshot is fetched for an archived repository

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookFolderSourceBranchTests.Preparing_an_archived_repositorys_snapshot_fetches_nothing, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookFolderSourceBranchTests.An_archived_repositorys_snapshot_is_served_as_it_is_and_nothing_is_fetched, unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.DevbookFolderSourceBranchTests.An_archived_repository_with_no_snapshot_says_it_is_archived_and_fetches_nothing]
```

The system SHALL fetch no devbook snapshot of an archived repository on its own.

#### Scenario: The app starts

- **Given** `budgetbeheer` is archived and has no local clone
- **When** the app starts and refreshes the devbook snapshots
- **Then** no snapshot of `budgetbeheer` is fetched, and its last snapshot stays readable

### Requirement: Remarks are not written into an archived devbook

```meta
type: requirement
status: draft
related: [.devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md]
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.ResolveAnnotationTests.A_note_on_an_archived_repository_is_refused, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.DevbookToolsTests.Annotations_of_an_archived_repository_still_list_and_say_it_is_archived]
```

The handle-remarks skill SHALL refuse to write an annotation into the devbook of an archived repository, and the MCP `resolve_annotation` tool SHALL refuse to resolve a remark on it.

#### Scenario: A remark waits on an archived devbook

- **Given** `budgetbeheer` is archived and a remark on one of its chapters is unresolved
- **When** the person asks the skill to handle the remarks of `budgetbeheer`
- **Then** the skill writes nothing and says the repository is archived

#### Scenario: An agent resolves a remark over MCP

- **Given** `budgetbeheer` is archived and a remark on one of its chapters is unresolved
- **When** an agent calls `resolve_annotation` for that remark
- **Then** the call is refused with `repository.archived`, and the remark stays unresolved
- **And** `list_annotations` still lists the remark and says the repository is archived

### Requirement: Subject state is not set on an archived devbook

```meta
type: requirement
status: draft
related: [.devbook/domain/devbook/domain.md#subject-state, .devbook/arc42/adr/0044-devbook-subject-state-lives-in-backlogs-own-store.md, .devbook/arc42/adr/0029-an-archived-repository-stays-in-the-registry.md]
tests: [unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.DecisionToolsTests.An_archived_repository_is_read_and_never_written, unit:dotnet:Backlog.Infrastructure.Mcp.UnitTests.DecisionToolsTests.An_archived_outcome_is_answered_as_repository_archived, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookReadOnlyBranchTests.A_subject_state_on_an_archived_repository_is_refused_and_nothing_is_kept, unit:dotnet:Backlog.Desktop.UI.UnitTests.DevbookPanelsReadOnlyBranchTests.The_arc42_panel_shows_a_debt_records_state_read_only_on_an_archived_repository]
```

The system SHALL show the workflow state of an archived repository's decision and debt records without offering to change it, and the MCP state tools SHALL refuse to set, approve or accept a subject of an archived repository.

#### Scenario: A debt record on an archived devbook

- **Given** `budgetbeheer` is archived, and one of its debt records was `planned` before the archive
- **When** the person opens that record
- **Then** it shows `planned`, and no state is offered to pick

#### Scenario: An agent sets a state over MCP

- **Given** `budgetbeheer` is archived, and one of its debt records is `planned`
- **When** an agent calls `devbook_set_state` to set it to `resolved`
- **Then** the call is refused with `repository.archived`, and the record stays `planned`
- **And** `devbook_get_state` still answers `planned`

### Requirement: Filing under an archived repository keeps it archived

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Tasks.UnitTests.ImportPlanTests.An_import_naming_an_archived_repository_files_under_it_leaves_it_archived_and_reports_it, unit:dotnet:Backlog.Desktop.UI.UnitTests.ImportPlanDialogRoadmapTests.An_import_filed_under_an_archived_repository_says_it_stays_archived]
```

The system SHALL file an imported or triaged item that names an archived repository under that repository, leave the repository archived, and say so in the import report.

#### Scenario: An import names the repository

- **Given** `budgetbeheer` is archived
- **When** the person imports a plan whose entries name `budgetbeheer`
- **Then** the entries are filed under `budgetbeheer`, it stays archived, and the report says it is archived

### Requirement: Unarchive restores everything

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsRepositoryArchiveTests.Unarchiving_brings_the_repository_back_and_offers_Archive_again, unit:dotnet:Backlog.Modules.Tasks.UnitTests.LinkedTasks.SyncLinkedTasksTests.Unarchiving_the_repository_resumes_the_sync, unit:dotnet:Backlog.Desktop.UI.UnitTests.RoadmapBandArchivedTests.Unarchiving_the_repository_brings_its_band_back_as_an_active_one, unit:dotnet:Backlog.Desktop.UI.UnitTests.Arc42DevbookPanelTests.Unarchiving_gives_the_chapter_its_edit_and_its_remarks_back, unit:dotnet:Backlog.Desktop.UI.UnitTests.WorkInProgressReaderTests.A_link_into_an_archived_repository_is_read_again_once_it_is_unarchived]
```

The system SHALL return an unarchived repository to every place it held while active, with the settings, tasks and linked-task targets it had when it was archived.

#### Scenario: Unarchiving

- **Given** `budgetbeheer` was archived with an enabled Connected Target and a chip in the remembered scope
- **When** the person unarchives it
- **Then** its chip, Board column, pickers, GitHub reads and roadmap band come back, and its Connected Target syncs again

### Requirement: Archived travels to a paired device

```meta
type: requirement
status: draft
related: [.devbook/arc42/adr/0021-github-accounts-and-repository-registry-ride-the-task-feed.md]
tests: unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubSettingsReplicationTests.An_archive_and_its_unarchive_on_one_device_reach_the_other
```

The system SHALL carry whether a repository is archived to every paired device with the repository registry.

#### Scenario: Archived on one device

- **Given** two paired devices both register `budgetbeheer`
- **When** the person archives `budgetbeheer` on the first
- **Then** after the next sync the second device reads `budgetbeheer` as archived

## Pull request status

```meta
type: requirements
status: draft
related: [.devbook/domain/repository-management/features.md#pull-request-status, .devbook/domain/progress/requirements.md#pull-request-status]
```

> The checks and approvals marks of the [pull request status](domain.md#pull-request-status)
> this context publishes. They moved here from Progress with the status itself (local
> [ADR 0042](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#2-one-published-contract-for-status-and-verdict)).
> What Progress promises about its own rows is in
> [its requirements](../progress/requirements.md#pull-request-status).

### Requirement: A pull request row shows its checks as passed over total

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.Every_check_passed_reads_passed_and_14_of_14, unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.Checks_still_running_read_running_and_9_of_14, unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.One_failed_check_reads_failing_even_while_others_run, unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusTests.A_running_or_failing_check_draws_its_own_glyph]
```

The system SHALL show on every open pull request row an icon and its checks as `passed/total`, in a running variant while a check runs and a failing variant once one fails.

#### Scenario: Every check passed

- **Given** #1341 has 14 checks, all passed
- **When** its row is drawn
- **Then** it shows the passed icon and `14/14`

#### Scenario: Checks still running

- **Given** #1358 has 14 checks, 9 passed and 5 running
- **When** its row is drawn
- **Then** it shows the running icon and `9/14`

#### Scenario: A failing check

- **Given** #1344 has 14 checks, 2 passed and one failed
- **When** its row is drawn
- **Then** it shows the failing icon and `2/14`

### Requirement: A pull request row shows approvals over required

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.One_of_two_approvals_is_short, unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusModelTests.No_required_total_shows_the_received_count_alone, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubClientLinkedPullRequestStatusTests.A_ruleset_pull_request_rule_gives_its_count, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubClientLinkedPullRequestStatusTests.The_larger_of_the_protection_rule_and_every_ruleset_rule_is_required, unit:dotnet:Backlog.Infrastructure.GitHub.UnitTests.GitHubClientLinkedPullRequestStatusTests.A_base_branch_with_no_rule_has_no_required_total]
```

The system SHALL show on every open pull request row an icon and its approvals as `approvals/required`, reading required as the larger of the base branch's protection rule and its rulesets' pull request rules, and SHALL read no required total for a base branch no rule asks an approval of.

#### Scenario: One of two approvals

- **Given** #1341 has one approval and its base branch `main` requires two
- **When** its row is drawn
- **Then** it shows the approvals icon and `1/2`

#### Scenario: A ruleset asks for the approval

- **Given** #1350's base branch has no protection rule and a ruleset whose pull request rule requires one approval
- **When** its row is drawn
- **Then** required is 1

#### Scenario: No rule asks for an approval

- **Given** #1358's base branch `main` has a ruleset that requires status checks and no review
- **When** its status is read
- **Then** it has no required total, and its approvals still count the ones received

### Requirement: Checks and approvals are named for screen readers only

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.UI.Components.UnitTests.PullRequestStatusTests.The_visible_text_holds_counts_and_a_screen_reader_hears_the_words]
```

The system SHALL name the checks and approvals marks with the words "checks" and "approvals" in their accessible labels and not in their visible text.

#### Scenario: Read by a screen reader

- **Given** #1341's row shows `14/14` and `1/2`
- **When** a screen reader reads the row
- **Then** it hears "14 of 14 checks passed" and "1 of 2 approvals", and the visible text holds neither word
