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

The system tools pane SHALL draw a catalog group with no entry needing action folded, saying all up to date, and a group with one open.

#### Scenario: Two groups

- **Given** a Baseline group that is up to date and a Team tools group with a missing application
- **When** the pane opens on Applications
- **Then** Baseline is folded and reads All up to date, and Team tools is open

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
