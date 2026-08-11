# Features: Environment

```meta
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Environment quick access

```meta
status: draft
related: [.domain/environment/domain.md#aggregate-environment-catalog]
```

Give the person fast access to the environments they care about without digging
through repositories, cloud portals, terminal history, or documentation.

### Sub-feature: Pinned environments

```meta
status: draft
```

Pin important environments so they appear in predictable quick-access locations.

### Sub-feature: Grouped shortcuts

```meta
status: draft
```

Group shortcuts by project, repository, customer, lifecycle stage, or custom label
while preserving each shortcut's target identity.

### Sub-feature: Safe access hints

```meta
status: draft
```

Show non-secret access reminders such as tenant, VPN, profile, or credential-store
name without storing passwords or tokens.

## Feature: Environment-aware work context

```meta
status: draft
related: [.domain/backlog/features.md#feature-roadmap-planning]
```

Let backlog and roadmap views surface relevant environment shortcuts next to work
items so a person can jump from planned work to the right local, cloud, or project
environment quickly.