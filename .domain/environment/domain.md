# Environment

```meta
type: domain
status: draft
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context; each chapter's
> `type` records which of those it is. An Aggregate's owned Entities, Value
> Objects, and Enums are chapters directly beneath it, typed `entity`,
> `value-object`, and `enum`. Value Objects/Enums shared across multiple
> aggregates get their own chapter at the end instead of being duplicated.

Environment owns the user's quick access to named environments: local harnesses,
development, staging, production, cloud dashboards, repository-hosted apps, or
other frequently used destinations. It stores launch preferences and shortcuts;
repository ownership and health remain with their supplier contexts.

## Environment Catalog

```meta
type: aggregate
status: draft
related: [.domain/repository-management/domain.md#repository-registry, .domain/monitoring/domain.md#progress-signal]
```

The personal catalog of launchable environments. Invariants: each Environment has
a stable name, type, target reference, and launch method; shortcuts can be pinned,
grouped, hidden, or reordered without changing the target environment; secrets are
never stored in the catalog, only pointers to the owning secret store or platform.

### Environment

```meta
type: entity
status: draft
```

A named destination the person wants quick access to, such as a local Aspire
dashboard, a staging web app, an Azure resource group, a repository preview, or a
production admin surface.

### Environment Shortcut

```meta
type: entity
status: draft
```

An owned quick-access entry for an Environment. It carries display name, group,
order, pinned/hidden state, and optional deep-link details.

### Launch Target

```meta
type: value-object
status: draft
```

The value needed to open an environment: URL, command, workspace path, dashboard
resource id, cloud resource id, or repository/environment id. Equality is by
target type and target value.

### Access Hint

```meta
type: value-object
status: draft
```

A non-secret pointer that helps the person reach the environment, such as the
credential source name, tenant label, VPN requirement, or required local profile.
It never contains the credential itself.

### Environment Type

```meta
type: enum
status: draft
```

Classifies the destination: `local`, `development`, `test`, `staging`,
`production`, `cloud`, `repository`, or `tooling`.

## Environment Shortcut Resolution

```meta
type: domain-service
status: draft
related: [.domain/environment/domain.md#environment-catalog, .domain/repository-management/domain.md#repository-registry, .domain/monitoring/domain.md#progress-signal]
```

Resolves an Environment Shortcut into the launch action the UI can present: open
a URL, run a command, focus a local workspace, or navigate to a cloud dashboard.
It is a service because it composes catalog data with supplier facts such as
repository path or environment health rather than belonging to one shortcut's
stored state. Invocation semantics: query/composition-oriented when a quick-access
view is shown or a shortcut is activated.

## EnvironmentShortcutUsed

```meta
type: domain-event
status: draft
related: [.domain/environment/domain.md#environment-catalog, .domain/productivity/domain.md#productivity-ledger]
```

Published when the person activates an Environment Shortcut.

### Payload

- `environment_id` - environment identifier.
- `shortcut_id` - activated shortcut identifier.
- `environment_type` - target category.
- `target_ref` - non-secret target reference.
- `used_at` - activation time.

### Consumers

- Productivity, which may count environment access as work-flow activity.

### Published language rules

- The event records a launch action only; it does not expose credentials or claim
  that the target environment was healthy.

## Shared Enums

```meta
type: shared-enums
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Environment currently has a single aggregate, so `Environment Type` is documented
under it. This chapter is reserved for future cross-aggregate enums.