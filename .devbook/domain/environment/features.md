# Environment

```meta
type: features
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Environment quick access

```meta
type: feature
related: [.devbook/domain/environment/domain.md#environment-catalog]
```

Give the person fast access to the environments they care about, without digging
through any of these:

- repositories;
- cloud portals;
- terminal history;
- documentation.

### Pinned environments

```meta
type: sub-feature
```

Pin important environments so they appear in predictable quick-access locations.

### Grouped shortcuts

```meta
type: sub-feature
```

Group shortcuts while preserving each shortcut's target identity. A group can be
by:

- project;
- repository;
- customer;
- lifecycle stage;
- custom label.

### Safe access hints

```meta
type: sub-feature
```

Show non-secret access reminders without storing passwords or tokens. A reminder
can name:

- a tenant;
- a VPN;
- a profile;
- a credential store.

## Environment-aware work context

```meta
type: feature
related: [.devbook/domain/roadmap/features.md#reading-and-rescheduling-on-a-timeline]
```

Let backlog and roadmap views surface relevant environment shortcuts next to work
items so a person can jump from planned work to the right local, cloud, or project
environment quickly.