# Environment

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Environment quick access

```meta
type: feature
status: draft
related: [.domain/environment/domain.md#environment-catalog]
```

Give the person fast access to the environments they care about without digging
through repositories, cloud portals, terminal history, or documentation.

### Pinned environments

```meta
type: sub-feature
status: draft
```

Pin important environments so they appear in predictable quick-access locations.

### Grouped shortcuts

```meta
type: sub-feature
status: draft
```

Group shortcuts by project, repository, customer, lifecycle stage, or custom label
while preserving each shortcut's target identity.

### Safe access hints

```meta
type: sub-feature
status: draft
```

Show non-secret access reminders such as tenant, VPN, profile, or credential-store
name without storing passwords or tokens.

## Environment-aware work context

```meta
type: feature
status: draft
related: [.domain/roadmap/features.md#reading-and-rescheduling-on-a-timeline]
```

Let backlog and roadmap views surface relevant environment shortcuts next to work
items so a person can jump from planned work to the right local, cloud, or project
environment quickly.