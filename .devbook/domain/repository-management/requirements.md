# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/repository-management/features.md, .devbook/arc42/adr/0029-an-archived-repository-stays-in-the-registry.md]
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
```

The system SHALL read nothing from GitHub for an archived repository in the Pull requests pane, the In progress view, the dashboard's activity and the sprint views, and report no failure for it there.

#### Scenario: The Pull requests pane refreshes

- **Given** `budgetbeheer` is archived and GitHub cannot find it
- **When** the person refreshes the Pull requests pane
- **Then** no line reads "Couldn't read innovadis-dev/Budgetbeheer", and the pull requests of the active repositories are listed

#### Scenario: The In progress view refreshes

- **Given** `budgetbeheer` is archived and GitHub cannot find it
- **When** the In progress view refreshes
- **Then** it reports no failure for `budgetbeheer`

#### Scenario: A sprint series holds the repository

- **Given** `budgetbeheer` is archived and belongs to a sprint series
- **When** the sprint view opens
- **Then** it reads no pull requests of `budgetbeheer` and reports no failure for it

### Requirement: Pickers and the scope hide an archived repository

```meta
type: requirement
status: draft
related: [.devbook/domain/tasks/features.md#search-filter-and-organize, .devbook/domain/tasks/features.md#board-view]
```

The system SHALL leave an archived repository out of the repository scope, the Board's by-repository columns and every repository picker while show archived is off.

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

### Requirement: Show archived is remembered on this device

```meta
type: requirement
status: draft
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
```

The system SHALL offer to move an archived repository's open tasks to another repository or to drop them, when the repository is archived and afterwards from its archived row.

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

### Requirement: The roadmap keeps an archived repository's past in view

```meta
type: requirement
status: draft
related: [.devbook/domain/roadmap/features.md#reading-the-plan-by-repository]
```

The system SHALL draw the band of an archived repository, greyed and tagged Archived with only its finished work, while the timeline's visible range holds finished work of it.

#### Scenario: Finished work in range

- **Given** `budgetbeheer` is archived, has finished work last month and show archived is off
- **When** the person reads the roadmap over last month
- **Then** a greyed band tagged Archived shows the finished work of `budgetbeheer`

#### Scenario: Scrolled past it

- **Given** `budgetbeheer` is archived and all its finished work lies before the visible range
- **When** the person reads the roadmap
- **Then** no band of `budgetbeheer` is drawn, and scrolling back to its finished work draws it again

### Requirement: The roadmap hides an archived repository's open work

```meta
type: requirement
status: draft
related: [.devbook/domain/roadmap/features.md#reading-the-plan-by-repository]
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
```

The handle-remarks skill SHALL refuse to write an annotation into the devbook of an archived repository.

#### Scenario: A remark waits on an archived devbook

- **Given** `budgetbeheer` is archived and a remark on one of its chapters is unresolved
- **When** the person asks the skill to handle the remarks of `budgetbeheer`
- **Then** the skill writes nothing and says the repository is archived

### Requirement: Filing under an archived repository keeps it archived

```meta
type: requirement
status: draft
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
```

The system SHALL carry whether a repository is archived to every paired device with the repository registry.

#### Scenario: Archived on one device

- **Given** two paired devices both register `budgetbeheer`
- **When** the person archives `budgetbeheer` on the first
- **Then** after the next sync the second device reads `budgetbeheer` as archived
