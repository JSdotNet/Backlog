# Dev PC Management

```meta
type: dependencies
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Work](../work/domain.task.md#task) | Customer/Supplier (Dev PC Management = customer) | Id reference to work items | `.devbook/domain/work/domain.task.md#task` | Compliance gaps can drive update tasks. |
| Native package managers (external) | ACL | Command execution on target machine | `.devbook/domain/dev-pc-management/domain.md#remote-update` | Tool updates run via `dotnet tool update`, `npm update`, `git upgrade`, etc. |
| Cloud service (relay/broker) | ACL | Registration, WoL relay, connection brokering | `.devbook/domain/dev-pc-management/domain.md#remote-control` | Provides registry, wake relay, and connection details. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Insights](../insights/features.md#personal-productivity-dashboard) | OHS + Published Language (Dev PC Management = supplier) | Subscribes to machine/compliance signals; not built | `.devbook/domain/dev-pc-management/domain.md#machinestatuschanged`, `.devbook/domain/dev-pc-management/domain.md#complianceupdated` | Infrastructure dashboard signals from this context. |
| [Sessions](../sessions/domain.md#session-log) | Customer/Supplier (Dev PC Management = supplier) | Machine-name lookup | `.devbook/domain/dev-pc-management/domain.md#machine-registry` | The registry saying which registered machine an `Environment` corresponds to. |

- **Sessions:** This lookup is not built. A locally read session is stamped by the
  environment that read it, so nothing is asked of the registry yet. Sessions owns
  the session subject; see `.devbook/domain/sessions/dependencies.md`.

## Notes

- Dev PC Management owns the team tool baseline: its
  [tool catalog](features.md#copilot-tool-catalog) declares it, and no other
  context defines one (local ADR 0073 §5).
- Package-manager execution is behind an anti-corruption layer per tool, so
  external command semantics never leak into the `Machine` model.
- The desktop app is dual-role: an agent that registers and reports, and a client
  that initiates connections. That is an architecture concern, not a domain split.
- Which agent ran where is Sessions' [Session Log](../sessions/domain.md#session-log),
  for the reason [context.md](context.md) gives.
