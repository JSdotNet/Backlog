# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/dev-pc-management/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

## System tools pane

```meta
type: requirements
status: draft
related: [.devbook/domain/dev-pc-management/features.md#system-tools-pane]
```

> The requirements of the system tools pane's header and kind tabs. The feature
> chapter says what the pane offers; this says what it promises.

### Requirement: The host filter narrows every tab and every count

```meta
type: requirement
status: draft
```

The system tools pane SHALL show, under the Claude filter, only the entries for Claude Code or Claude Desktop, under the Copilot filter only the entries for Copilot, and entries for no host only under All hosts, with every tab count following the filter.

#### Scenario: Claude

- **Given** a catalog with plugins for Copilot, for Claude and for both, an MCP server for Claude Desktop, an application and a checklist item
- **When** the reader chooses the Claude filter
- **Then** the plugins for Claude and for both and the Claude Desktop server show, and the Copilot plugin, the application and the checklist item do not

#### Scenario: Counts follow the filter

- **Given** two plugins that are behind, one for Copilot and one for Claude
- **When** the reader chooses the Copilot filter
- **Then** the Plugins tab counts one entry needing action

### Requirement: Each kind tab counts the entries that need action

```meta
type: requirement
status: draft
```

The system tools pane SHALL mark each kind tab with the number of its entries that are behind, missing or not confirmed, in the error tone when one is missing, the warning tone when one is behind and the neutral tone otherwise, and with no mark when none is.

#### Scenario: A missing server

- **Given** one MCP server that is missing and one that is behind
- **When** the pane opens
- **Then** the MCP servers tab reads two in the error tone

#### Scenario: Nothing outstanding

- **Given** applications that are all up to date
- **When** the pane opens
- **Then** the Applications tab carries no count

### Requirement: Entries confirmed by hand are the checklist

```meta
type: requirement
status: draft
```

The system tools pane SHALL list the applications confirmed by hand under the Checklist tab and every other application under the Applications tab.

#### Scenario: Split

- **Given** an installed application and an item nothing can check
- **When** the reader opens each tab
- **Then** the application is under Applications only and the item is under Checklist only

### Requirement: A group with nothing outstanding starts folded

```meta
type: requirement
status: draft
```

The system tools pane SHALL draw a catalog group with no entry needing action folded, saying all up to date, and a group with one open, its fold naming its title, how many entries of the tab's kind it holds and, when any do, how many need action.

#### Scenario: Two groups

- **Given** a Baseline group that is up to date and a Team tools group with a missing application
- **When** the pane opens on Applications
- **Then** Baseline is folded and reads All up to date with no count needing action, and Team tools is open and its fold says one needs action

### Requirement: The headline counts each kind of outstanding in order

```meta
type: requirement
status: draft
```

The system tools pane SHALL, when the tab on show has more than one kind of outstanding entry, say how many are behind, missing and to confirm, each on its own and in that order, and SHALL keep the "n of m are behind" sentence when only one kind is outstanding.

#### Scenario: Mixed Overview

- **Given** three plugins behind, two applications missing and one unconfirmed checklist item
- **When** the pane opens on the Overview
- **Then** the headline reads "Three updates waiting, two tools missing and one item to confirm"

#### Scenario: Kind tab

- **Given** two plugins behind and one missing
- **When** the reader opens Plugins
- **Then** the headline reads "Two plugins behind and one missing"

### Requirement: An open group shows its work before what matches

```meta
type: requirement
status: draft
```

Under All, the system tools pane SHALL list an open group's entries needing action first, hold its matching entries behind one control that says how many, show them after the work once pressed and keep them shown across a re-check; under Needs action it SHALL hold nothing back.

#### Scenario: Reveal

- **Given** a Plugins group with two plugins behind and three that match
- **When** the reader opens Plugins
- **Then** the two behind show with "3 more up to date — show", and pressing it adds the three under them

### Requirement: The tab and the host filter are remembered on this machine

```meta
type: requirement
status: draft
```

The system tools pane SHALL reopen on the tab and the host filter it was last left on, on the same machine.

#### Scenario: Reopen

- **Given** the reader left the pane on the Checklist tab with the Copilot filter
- **When** the pane is opened again on this machine
- **Then** it opens on the Checklist tab with the Copilot filter

## Overview tab

```meta
type: requirements
status: draft
related: [.devbook/domain/dev-pc-management/features.md#overview-tab]
```

> The requirements of the system tools pane's Overview tab.

### Requirement: Overview is where a new machine opens

```meta
type: requirement
status: draft
```

The system tools pane SHALL open on the Overview tab on a machine that has no remembered tab.

#### Scenario: First open

- **Given** a machine that has never been on a tab of the pane
- **When** the pane opens
- **Then** the Overview tab is on show

### Requirement: The headline figures count the entries this machine wants

```meta
type: requirement
status: draft
```

The Overview SHALL show how many entries match the catalog out of the entries this machine wants and whose version was looked up, with the matching, behind, missing and unconfirmed shares said in words, the entries behind split by host, the missing entries split into those installable from here and those installed by hand, and the count of cached versions nothing loads.

#### Scenario: Mixed machine

- **Given** three matching entries, a plugin behind on Claude, an application behind, an application missing that is installed by hand, an unconfirmed checklist item and a switched-off plugin
- **When** the pane opens
- **Then** Matches the catalog reads 3 of 7, and Updates reads 2 with Claude 1 and Machine 1

### Requirement: The Updates split adds up to the Updates figure

```meta
type: requirement
status: draft
```

The Overview SHALL count every entry behind on exactly one line of the Updates split — under the one host it is for or the one host that reports it behind, under Both when it is for both hosts and both or neither report it, under Machine when it has no host — so the lines always sum to the Updates figure, under every host filter.

#### Scenario: Behind on both hosts

- **Given** a plugin for both hosts behind on both, a plugin for both hosts behind on Copilot only, a Claude plugin behind and an application behind
- **When** the pane opens
- **Then** Updates reads 4 with Copilot 1, Claude 1, Both 1 and Machine 1

#### Scenario: Claude filter

- **Given** the same machine
- **When** the reader chooses the Claude filter
- **Then** the plugin behind on Copilot only still counts once, under Copilot, and the lines sum to the Updates figure

### Requirement: Needs you lists every action in a fixed order

```meta
type: requirement
status: draft
```

The Overview SHALL list one card per entry needing action, updates first, then missing entries, then unconfirmed checklist items, then the stale cache, each carrying exactly one control — Update, Install, a link to the download page for an entry installed by hand, Done here for a checklist item, or Clear for the stale cache, and SHALL say under the detail of a card whose step failed in the last Update all why it failed, until the next run starts.

#### Scenario: Order

- **Given** an unconfirmed checklist item, an application missing that is installed by hand, a missing plugin, a plugin behind and a stale cached version, in that catalog order
- **When** the pane opens
- **Then** Needs you reads Update, Install by hand, Install, Confirm, Clear the stale cache, in that order

#### Scenario: Nothing to do

- **Given** a catalog whose every entry matches
- **When** the pane opens
- **Then** Needs you says everything matches the catalog and lists nothing

#### Scenario: A failed step

- **Given** an Update all whose step for one plugin failed with "network unreachable"
- **When** the run ends
- **Then** that plugin's card reads "Update all failed here: network unreachable" and still carries one control

### Requirement: The host filter narrows every Overview figure

```meta
type: requirement
status: draft
```

The Overview SHALL count only the entries the host filter shows, in every tile, in Needs you and in every kind bar.

#### Scenario: Copilot

- **Given** a mixed machine whose only Copilot entry is a matching plugin
- **When** the reader chooses the Copilot filter
- **Then** Matches the catalog reads 1 of 1 and Needs you says everything matches the catalog

### Requirement: Last run shows the last finished run

```meta
type: requirement
status: draft
```

The Overview SHALL show this machine's last finished run — its outcome, kind, when it finished, how many of its steps succeeded, how long it took and the last lines of its transcript, with the whole transcript a click away — and SHALL show no Last run card when there is none.

#### Scenario: Partly failed Update all

- **Given** an Update all that updated one of two tools in 72 seconds
- **When** the pane opens
- **Then** Last run reads Partly failed, Update all, 1 of 2 steps succeeded and 1 min 12 s

#### Scenario: No run yet

- **Given** a machine that has never finished a run
- **When** the pane opens
- **Then** there is no Last run card

### Requirement: The strip's transcript link opens the last run

```meta
type: requirement
status: draft
```

The tab strip SHALL offer "Last run transcript" whenever this machine has a last finished run, opening that run's whole transcript as the Last run card does, and while the pane has command output but no last run, SHALL open that output instead.

#### Scenario: After a restart

- **Given** a machine with a last finished run, and an app just restarted with no command run since
- **When** the reader presses Last run transcript
- **Then** that run's whole transcript opens

### Requirement: The footer names where the setup comes from

```meta
type: requirement
status: draft
```

The Overview SHALL name at its foot the catalog this machine reads and, where the host names one, the file this machine's own choices are kept in, and SHALL offer Add tool and Import there only where the host can write the catalog.

#### Scenario: Read-only host

- **Given** a host that only reads the catalog
- **When** the pane opens
- **Then** the footer names the catalog and offers neither Add tool nor Import
