# Technology Stack

```meta
type: naming
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Technology Registry

```meta
type: term
status: draft
aliases: [TechnologyRegistry]
related: [.domain/technology-stack/domain.md#technology-registry]
```

The single global registry that owns the approved technologies, baselines, and
deprecations for the portfolio.

## Technology

```meta
type: term
status: draft
aliases: [Technology, technology_id]
related: [.domain/technology-stack/domain.md#technology]
```

An individual language, runtime, framework, or tool tracked in the registry.

## Technology Baseline

```meta
type: term
status: draft
aliases: [TechnologyBaseline, TeamToolsBaseline, TechBaselines]
related: [.domain/technology-stack/domain.md#technology-baseline]
```

The canonical set of approved versions. Consumers hold a local copy under a
context-specific name: Dev PC Management calls it `TeamToolsBaseline`
(see `.domain/dev-pc-management/naming.md#team-tools-baseline`) and
Repository Management calls it `TechBaselines`
(see `.domain/repository-management/naming.md#tech-baselines`). This context
is the source of truth.

## Tech Status

```meta
type: term
status: draft
aliases: [TechStatus]
related: [.domain/technology-stack/domain.md#tech-status]
```

Adoption state of a technology (stable, beta, deprecated, unsupported); see
`flow.md` for the transitions.

## Deprecation Notice

```meta
type: term
status: draft
aliases: [DeprecationNotice]
related: [.domain/technology-stack/domain.md#deprecation-notice]
```

A recorded EOL date and replacement for a deprecated technology.
