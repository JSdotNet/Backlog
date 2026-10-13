# Dev PC Management

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## PC registration

```meta
type: feature
status: draft
```

The desktop component registers itself with the cloud on startup, reporting name,
OS, IP (local + public), MAC, and status, with a heartbeat that tracks
online/sleeping/offline state and auto-deregisters after inactivity.

## Wake-on-LAN

```meta
type: feature
status: draft
depends-on: [.devbook/domain/dev-pc-management/features.md#pc-registration]
```

Send a magic packet to wake a registered sleeping/powered-off PC over the local
network or a cloud relay, verifying the wake by resumed heartbeat and queuing
requests when relay is needed.

## Remote desktop session

```meta
type: feature
status: draft
depends-on: [.devbook/domain/dev-pc-management/features.md#pc-registration]
```

Initiate a remote desktop session (RDP, VNC fallback) to any online PC via
cloud-brokered connection details, optionally tunneled through a relay for
machines behind NAT/firewall.

## Machine status dashboard

```meta
type: feature
status: draft
related: [.devbook/domain/monitoring/features.md#multi-layer-dashboards]
```

List all registered PCs with current state, last-seen and uptime, running
desktop-component version, and quick actions (wake, connect, optional shutdown).

## Configuration and tool version tracking

```meta
type: feature
status: draft
related: [.devbook/domain/technology-stack/features.md#technology-baseline-definition]
```

Report installed tool versions on startup and on demand, store version snapshots,
compare against the team baseline, and alert when a tool is outdated or
incompatible.

### Copilot tool catalog

```meta
type: sub-feature
status: draft
related: [.devbook/domain/technology-stack/features.md#technology-baseline-definition]
```

Track the AI tooling a development machine is expected to run — Copilot plugins
and MCP servers — as a catalog the repository declares, layered with per-machine
choices. The catalog states which tools belong to the working setup; the machine
layer records what this particular PC has enabled and which version is actually
installed. Each tool reports its installed version against the available one, so
"behind", "up to date", and "not installed" are distinguishable rather than
lumped together.

### Catalog authoring

```meta
type: sub-feature
status: draft
feature-flag: .devbook/domain/dev-pc-management/context.md#system-tools
depends-on: [.devbook/domain/dev-pc-management/features.md#copilot-tool-catalog]
```

Author the catalog itself from the machine, rather than treating it as something only a
person editing a file can maintain: create it where none exists, naming the resolved path
before writing it, add or remove a Copilot plugin or MCP server entry, and import a JSON
file or pasted text to replace the whole catalog in one step, keeping the prior catalog as
a backup and rejecting invalid input without touching what is already declared. Removing
an entry also clears any per-machine override recorded against it, so what the catalog
declares and what a machine has enabled cannot drift into a stale combination.

### System tools pane

```meta
type: sub-feature
status: draft
feature-flag: .devbook/domain/dev-pc-management/context.md#system-tools
depends-on: [.devbook/domain/dev-pc-management/features.md#copilot-tool-catalog]
related: [.devbook/domain/dev-pc-management/requirements.md#system-tools-pane]
```

Read the catalog against this machine one kind at a time. The pane leads with a
sentence saying what is outstanding on the tab on show — how many of its entries
are behind, missing or not yet confirmed, or that everything matches the catalog —
and when the catalog was last checked. Its tabs are an Overview, then Plugins,
Marketplaces, MCP servers, Applications and a Checklist, which holds the
applications nothing can check and somebody confirms by hand; each kind tab
carries a count of the entries needing action. A host filter narrows every tab
and every count to the entries for Claude (Claude Code or Claude Desktop) or for
Copilot, and entries for no host at all — applications and checklist items —
show only under all hosts. Within a tab, a toolbar narrows to the entries that
need action or by name, and each catalog group is a card that starts folded when
nothing in it is outstanding. The tab and the host filter are remembered on this
machine, never synced, so the pane reopens where it was left.

## Remote tool updates

```meta
type: feature
status: draft
depends-on: [.devbook/domain/dev-pc-management/features.md#configuration-and-tool-version-tracking]
```

Trigger single, targeted, or bulk tool updates from the dashboard or CLI, queue
them for offline PCs, report progress with rollback on failure, and optionally
require explicit confirmation.

### Local tool enablement and updates

```meta
type: sub-feature
status: draft
feature-flag: .devbook/domain/dev-pc-management/context.md#system-tools
depends-on: [.devbook/domain/dev-pc-management/features.md#copilot-tool-catalog]
```

Act on the machine the person is sitting at: re-check the catalog for newer
versions, update one tool or every updatable tool at once, and switch an
individual tool on or off for this machine without changing what the catalog
declares for everyone else. Each action reports back whether it succeeded, and
tool management is a capability that can be switched off wholesale on machines
where it does not apply.

### Last run record

```meta
type: sub-feature
status: draft
feature-flag: .devbook/domain/dev-pc-management/context.md#system-tools
depends-on: [.devbook/domain/dev-pc-management/features.md#local-tool-enablement-and-updates]
related: [.devbook/domain/dev-pc-management/domain.md#last-tool-run]
```

Keep the last Update all, Update, Install or Clear stale cache this machine
finished, so what it did is still there after the app restarts: which kind of run
it was, when it started and finished, whether it succeeded, failed or partly
failed, each tool it touched with the version it came from, the version it was
going to, how that tool's step ended and how long it took, and the whole
transcript. Only the most recent run is kept, per machine and beside that
machine's own tool choices; a new run replaces it once it finishes, and a machine
that has never finished one has none.

## Security

```meta
type: feature
status: draft
```

Require authentication for registration and connections, explicit authorization
for wake/connect/update, encrypted communication, and no storage of target-machine
credentials in the cloud.
