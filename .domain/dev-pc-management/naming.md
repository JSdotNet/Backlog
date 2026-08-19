# Naming: Dev PC Management

```meta
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Term: Machine Registry

```meta
status: draft
aliases: [MachineRegistry]
related: [.domain/dev-pc-management/domain.md#aggregate-machine-registry]
```

The single global registry of developer machines in the fleet.

## Term: Machine

```meta
status: draft
aliases: [Machine, machine_id]
related: [.domain/dev-pc-management/domain.md#machine]
```

An individual developer PC. `machine_id` is the form other contexts use to
reference it (see Monitoring's `machine_status` signals).

## Term: Team Tools Baseline

```meta
status: draft
aliases: [TeamToolsBaseline]
related: [.domain/dev-pc-management/domain.md#team-tools-baseline, .domain/technology-stack/naming.md#term-technology-baseline]
```

This context's local copy of the Technology Stack `Technology Baseline`, used to
compute per-machine compliance without holding the foreign aggregate.

## Term: Machine Status

```meta
status: draft
aliases: [MachineStatus]
related: [.domain/dev-pc-management/domain.md#machine-status]
```

Runtime state of a machine (online, sleeping, offline); see `flow.md` for the
transitions.
