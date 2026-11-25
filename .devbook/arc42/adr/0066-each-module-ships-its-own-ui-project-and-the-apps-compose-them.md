# ADR 0066: Each module ships its own UI project, and the apps under `src/App/` compose them

```meta
date: 2026-11-25
related: [".devbook/arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md", ".devbook/arc42/adr/0043-a-surface-can-open-in-its-own-window.md", ".devbook/arc42/adr/guidelines/0005-modular-monolith-structure.md", ".devbook/arc42/adr/guidelines/0011-centralized-frontend-styling-variables.md", ".devbook/arc42/05-building-block-view.md", ".devbook/arc42/09-architecture-decisions.md"]
c4: [backlog.desktop, backlog.mobile]
```

A module's screens live in its own `Backlog.Modules.<Name>.UI` Razor class library
under `src/Modules/<Name>/`. The apps under `src/App/` compose those libraries:
`Backlog.Desktop`, `Backlog.Mobile` and their `*.UI` shells. The development hosts
under `src/Harness/` serve the same components in a browser. The UI is not moved into
one app project under `src/App/`, which the first rule of org ADR 0009 reads as asking.

## Status

```meta
```

Proposed, 2026-11-25. Plan `app-defaults-rollout`, entry 37 (`backlog-deviation-adrs`).

A **local** decision. It records a deliberate difference from
[org ADR 0009: Frontend](https://github.com/JSdotNet/app-defaults/blob/main/defaults/arc42/adr/0009-frontend.md),
its placement rule only. The rest of that ADR already holds: Blazor is the option
(local ADR 0001), the harnesses are Aspire resources with `.WithBrowserLogs()`, the
tokens are CSS custom properties, and the storybook is the component catalog.

## Context

```meta
```

Org ADR 0009 says "the UI is its own app under `src/App/`, not part of a module's Api
project". Its aim is that UI code does not sit inside a server project. Backlog meets
that aim another way:

| Where | What | Count |
| --- | --- | --- |
| `src/Modules/<Name>/Backlog.Modules.<Name>.UI` | A module's screens, as a Razor class library beside the module and its `.Abstractions` | 14 |
| `src/Core/Backlog.UI.Components` | The shared components and the one token file | 1 |
| `src/App/Backlog.Desktop`, `Backlog.Mobile` and their `.UI` shells | The MAUI heads and shells that compose the module libraries | 4 |
| `src/Harness/*` | Browser hosts of the same components for development and E2E: the desktop and mobile harnesses and the storybook. The fourth project, the Foundry test service, has no UI | 3 |

No module has an Api project except `Sync`, and `Sync.UI` is a client library, not part
of `Sync.Api`. Local ADR 0043 has each module register the surfaces it shows, so a
module's UI ships with the module's boundary. The architecture tests guard that
boundary.

## Decision

```meta
```

**Keep.** A module's UI stays in its own `.UI` project under `src/Modules/`, the shared
components in `src/Core/Backlog.UI.Components`, the heads and shells in `src/App/`, and
the browser hosts in `src/Harness/`. Backlog reads org ADR 0009's placement rule as
met: no UI code is in an Api project, and the apps are under `src/App/`.

## Consequences

```meta
```

- A module change that touches its screens stays inside one folder.
- A new screen goes in its module's `.UI` project and registers its surface there
  (local ADR 0043). Only shell-wide chrome goes in a `src/App/` shell.
- An app-defaults review reads this record for the 0009 row.

## Rejected

```meta
```

- **Move every module's UI into one project under `src/App/` (M).** Moving 14 libraries
  into one app would undo the module-owned surfaces of local ADR 0043. The
  architecture tests would then have to police folders inside one project instead of
  references between projects.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | Proposed: keep module-owned UI libraries composed by the apps under `src/App/`. | Step 15 of plan `app-defaults-rollout` (PR #1967) listed the org ADR 0009 difference with no local decision. |
