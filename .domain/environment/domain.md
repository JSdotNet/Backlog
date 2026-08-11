# Domain: Environment

```meta
status: draft
order: ["features.md", "model.md", "flow.md", "dependencies.md", "naming.md"]
```

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context.

Environment owns the user's quick access to named environments: local harnesses,
development, staging, production, cloud dashboards, repository-hosted apps, or
other frequently used destinations. It stores launch preferences and shortcuts;
repository ownership and health remain with their supplier contexts.

## Aggregate: Environment Catalog

```meta
status: draft
related: [.domain/repository-management/domain.md#aggregate-repository-registry, .domain/monitoring/domain.md#aggregate-progress-signal]
```

The personal catalog of launchable environments. Invariants: each Environment has
a stable name, type, target reference, and launch method; shortcuts can be pinned,
grouped, hidden, or reordered without changing the target environment; secrets are
never stored in the catalog, only pointers to the owning secret store or platform.

### Entities

#### Environment

A named destination the person wants quick access to, such as a local Aspire
dashboard, a staging web app, an Azure resource group, a repository preview, or a
production admin surface.

#### Environment Shortcut

An owned quick-access entry for an Environment. It carries display name, group,
order, pinned/hidden state, and optional deep-link details.

### Value Objects

#### Launch Target

The value needed to open an environment: URL, command, workspace path, dashboard
resource id, cloud resource id, or repository/environment id. Equality is by
target type and target value.

#### Access Hint

A non-secret pointer that helps the person reach the environment, such as the
credential source name, tenant label, VPN requirement, or required local profile.
It never contains the credential itself.

### Enums

#### Environment Type

Classifies the destination: `local`, `development`, `test`, `staging`,
`production`, `cloud`, `repository`, or `tooling`.

## Domain Service: Environment Shortcut Resolution

```meta
status: draft
related: [.domain/environment/domain.md#aggregate-environment-catalog, .domain/repository-management/domain.md#aggregate-repository-registry, .domain/monitoring/domain.md#aggregate-progress-signal]
```

Resolves an Environment Shortcut into the launch action the UI can present: open
a URL, run a command, focus a local workspace, or navigate to a cloud dashboard.
It is a service because it composes catalog data with supplier facts such as
repository path or environment health rather than belonging to one shortcut's
stored state. Invocation semantics: query/composition-oriented when a quick-access
view is shown or a shortcut is activated.

## Domain Event: EnvironmentShortcutUsed

```meta
status: draft
related: [.domain/environment/domain.md#aggregate-environment-catalog, .domain/productivity/domain.md#aggregate-productivity-ledger]
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
status: draft
```

> Enums used by more than one aggregate in this bounded context.

Environment currently has a single aggregate, so `Environment Type` is documented
under it. This chapter is reserved for future cross-aggregate enums.