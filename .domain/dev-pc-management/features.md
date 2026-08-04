# Features: Dev PC Management

```meta
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: PC registration

```meta
status: draft
```

The desktop component registers itself with the cloud on startup, reporting name,
OS, IP (local + public), MAC, and status, with a heartbeat that tracks
online/sleeping/offline state and auto-deregisters after inactivity.

## Feature: Wake-on-LAN

```meta
status: draft
depends-on: [.domain/dev-pc-management/features.md#feature-pc-registration]
```

Send a magic packet to wake a registered sleeping/powered-off PC over the local
network or a cloud relay, verifying the wake by resumed heartbeat and queuing
requests when relay is needed.

## Feature: Remote desktop session

```meta
status: draft
depends-on: [.domain/dev-pc-management/features.md#feature-pc-registration]
```

Initiate a remote desktop session (RDP, VNC fallback) to any online PC via
cloud-brokered connection details, optionally tunneled through a relay for
machines behind NAT/firewall.

## Feature: Machine status dashboard

```meta
status: draft
related: [.domain/monitoring/features.md#feature-multi-layer-dashboards]
```

List all registered PCs with current state, last-seen and uptime, running
desktop-component version, and quick actions (wake, connect, optional shutdown).

## Feature: Configuration and tool version tracking

```meta
status: draft
related: [.domain/technology-stack/features.md#feature-technology-baseline-definition]
```

Report installed tool versions on startup and on demand, store version snapshots,
compare against the team baseline, and alert when a tool is outdated or
incompatible.

## Feature: Remote tool updates

```meta
status: draft
depends-on: [.domain/dev-pc-management/features.md#feature-configuration-and-tool-version-tracking]
```

Trigger single, targeted, or bulk tool updates from the dashboard or CLI, queue
them for offline PCs, report progress with rollback on failure, and optionally
require explicit confirmation.

## Feature: Copilot session tracking

```meta
status: draft
related: [.domain/monitoring/features.md#feature-multi-layer-dashboards]
```

Track active Copilot session IDs, URLs, and status per PC, link sessions to
GitHub issues or backlog items, alert on stalled sessions, and archive history
for audit.

## Feature: Security

```meta
status: draft
```

Require authentication for registration and connections, explicit authorization
for wake/connect/update, encrypted communication, and no storage of target-machine
credentials in the cloud.
