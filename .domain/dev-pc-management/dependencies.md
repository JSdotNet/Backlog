# Dev PC Management

```meta
type: dependencies
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Technology Stack](../technology-stack/domain.md#technology-registry) | Customer/Supplier (Dev PC Management = customer) | `BaselineRequested` -> `BaselineProvided` sync contract | `.domain/technology-stack/domain.md#deprecation-management` | Consumes the team tool baseline to compute per-machine compliance. |
| [Monitoring](../monitoring/domain.md#progress-signal) | OHS + Published Language (Dev PC Management = supplier) | Emits `MachineStatusChanged` / `ComplianceUpdated` | `.domain/dev-pc-management/domain.md#machinestatuschanged`, `.domain/dev-pc-management/domain.md#complianceupdated` | Machine status, compliance, and uptime feed dashboards. |
| [Backlog](../backlog/domain.md#backlog-entry) | Customer/Supplier (Dev PC Management = customer) | Id reference to work items | `.domain/backlog/naming.md#backlog-entry` | Compliance gaps can drive update tasks. |
| Native package managers (external) | ACL | Command execution on target machine | `.domain/dev-pc-management/domain.md#remote-update` | Tool updates run via `dotnet tool update`, `npm update`, `git upgrade`, etc. |
| Cloud service (relay/broker) | ACL | Registration, WoL relay, connection brokering | `.domain/dev-pc-management/domain.md#remote-control` | Provides registry, wake relay, and connection details. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Technology Stack](../technology-stack/domain.md#technology-registry) | Customer/Supplier (Technology Stack = customer) | Consumes tool-version reports | `.domain/dev-pc-management/domain.md#machine-registry` | Relies on machine tool inventories for portfolio adoption metrics. |
| [Monitoring](../monitoring/domain.md#progress-signal) | OHS + Published Language (Dev PC Management = supplier) | Subscribes to machine/compliance signals | `.domain/dev-pc-management/domain.md#machinestatuschanged`, `.domain/dev-pc-management/domain.md#complianceupdated` | Relies on infrastructure dashboard signals from this context. |
| [Sessions](../sessions/domain.md#session-log) | Customer/Supplier (Dev PC Management = supplier) | Machine-name lookup | `.domain/dev-pc-management/domain.md#machine-registry` | Relies on the registry to say which registered machine an `Environment` corresponds to. Not built: a locally read session is stamped by the environment that read it, so nothing is asked of the registry yet. This context took the session subject from here — see `.domain/sessions/dependencies.md`. |

## Notes

- Tool baseline ownership lives in Technology Stack; Dev PC Management only
  consumes it and reports adoption back — avoid duplicating baseline authority.
- Package-manager execution is behind an anti-corruption layer per tool so
  external command semantics never leak into the `Machine` model.
- The desktop component is dual-role: agent (registers, reports) and client
  (initiates connections) — an architecture concern, not a domain split.
- Sessions are not this context's subject. The `Copilot Session Tracking` service,
  the `Active Session` / `Session Record` value objects and the Copilot-session
  feature moved to [Sessions](../sessions/domain.md#session-log) when a
  second agent arrived; nothing here models them any more, and a Machine holds no
  session state.
