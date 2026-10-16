# Technology Stack

```meta
type: features
status: draft
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Technology baseline definition

```meta
type: feature
status: draft
```

Define team-approved technologies (languages, frameworks, runtimes, tools). Each
carries version requirements (minimum, recommended) and a stable, beta, or
deprecated status. Teams and projects can override the portfolio defaults.

## Deprecation and migration management

```meta
type: feature
status: draft
depends-on: [.devbook/domain/technology-stack/features.md#technology-baseline-definition]
```

Mark technologies deprecated with end-of-life dates and migration guidance, and
announce each one as a [Deprecation Notice](domain.md#deprecation-notice).
Enforce minimum versions, track each legacy exception as a
[Migration Approval](domain.md#migration-approval), and recommend replacements.

## Portfolio-wide adoption tracking

```meta
type: feature
status: draft
related: [.devbook/domain/dev-pc-management/features.md#configuration-and-tool-version-tracking, .devbook/domain/repository-management/features.md#technology-stack-inventory]
```

Detect which technologies are in use across the portfolio, track adoption rates
and outliers, surface adoption velocity, and report
[Technology Sprawl](domain.md#technology-sprawl).

## Technology recommendations

```meta
type: feature
status: draft
depends-on: [.devbook/domain/technology-stack/features.md#portfolio-wide-adoption-tracking]
related: [.devbook/domain/tasks/features.md#task-creation]
```

Surface which tools are ready to upgrade, and recommend new tools based on what
the portfolio already adopts. Give adoption decisions their context, and link to
the ADRs that justify them.

## Technology versioning standards

```meta
type: feature
status: draft
```

Define semantic-versioning expectations per tool, map version aliases (latest,
LTS, stable) to concrete versions, and track compatibility matrices between
technologies.

## Cross-domain integration

```meta
type: feature
status: draft
related: [.devbook/domain/monitoring/features.md#multi-repo-scanning]
```

Provide baselines to the contexts [the context chapter](context.md#technology-stack)
names. Feed adoption data to Monitoring dashboards, link ADRs in Devbook, and let
Tasks create tech-upgrade tasks from adoption gaps.
