# Dependencies: Environment

```meta
status: draft
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Use explicit DDD relationship semantics, integration
> mechanism details, and contract references.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Customer/Supplier (Environment = customer) | Repository and workspace lookup by opaque id | `.domain/repository-management/naming.md#term-repository` | Environment shortcuts can point at repository-local workspaces without copying repository ownership data. |
| [Monitoring & Dashboard](../monitoring/domain.md#aggregate-progress-signal) | Customer/Supplier (Environment = customer) | Reads health and availability signals | `.domain/monitoring/domain.md#aggregate-progress-signal` | Quick-access views can show whether an environment appears healthy before launch. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Backlog Management](../backlog/domain.md#aggregate-backlog-entry) | Customer/Supplier (Environment = supplier) | Shortcut lookup by environment id | `.domain/environment/domain.md#domain-service-environment-shortcut-resolution` | Backlog and roadmap views can show launchable environments near relevant work items. |
| [Dev PC Management](../dev-pc-management/domain.md#aggregate-machine-registry) | Customer/Supplier (Environment = supplier) | Shortcut lookup for local tools and machine resources | `.domain/environment/domain.md#domain-service-environment-shortcut-resolution` | Operator support views can open the right machine, local service, or tool environment quickly. |
| [Productivity](../productivity/domain.md#aggregate-productivity-ledger) | OHS + Published Language (Environment = supplier) | Subscribes to `EnvironmentShortcutUsed` | `.domain/environment/domain.md#domain-event-environmentshortcutused` | Productivity may count environment access as work-flow activity. |

## Notes

- Environment does not store credentials; access hints point to the source of
  credentials or required preconditions only.
- Repository identity and environment health remain with their supplier contexts.