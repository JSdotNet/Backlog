# Dependencies: Dev PC Management

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| [Technology Stack](../technology-stack/domain.md#aggregate-technology-registry) | `BaselineRequested` → `BaselineProvided` (sync) | Consumes the team tool baseline to compute per-machine compliance. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Emits `MachineStatusChanged` / `ComplianceUpdated` | Machine status, compliance, uptime, and session metrics feed dashboards. |
| [Backlog](../backlog/domain.md#aggregate-backlog-entry) | Id reference | Copilot sessions link to backlog items; compliance gaps can drive update tasks. |
| Native package managers (external) | Command execution on target machine (ACL) | Tool updates run via `dotnet tool update`, `npm update`, `git upgrade`, etc. |
| Cloud service (relay/broker) | Registration, WoL relay, connection brokering | Provides registry, wake relay, and connection details. |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| [Technology Stack](../technology-stack/domain.md#aggregate-technology-registry) | Consumes tool-version reports | Relies on machine tool inventories for portfolio adoption metrics. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | Subscribes to machine/compliance/session signals | Relies on infrastructure dashboard signals from this context. |

## Notes

- Tool baseline ownership lives in Technology Stack; Dev PC Management only
  consumes it and reports adoption back — avoid duplicating baseline authority.
- Package-manager execution is behind an anti-corruption layer per tool so
  external command semantics never leak into the `Machine` model.
- The desktop component is dual-role: agent (registers, reports, tracks sessions)
  and client (initiates connections) — an architecture concern, not a domain
  split.
