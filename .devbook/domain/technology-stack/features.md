# Technology Stack

```meta
type: features
```

## Technology baseline definition

```meta
type: feature
```

Define team-approved technologies (languages, frameworks, runtimes, tools). Each
carries minimum and recommended version requirements and a status: stable, beta
or deprecated. Teams and projects can override the portfolio defaults.

## Deprecation and migration management

```meta
type: feature
depends-on: [.devbook/domain/technology-stack/features.md#technology-baseline-definition]
```

Mark technologies deprecated with end-of-life dates and migration guidance, and
announce each one as a [Deprecation Notice](domain.md#deprecation-notice).
Enforce minimum versions, track each legacy exception as a
[Migration Approval](domain.md#migration-approval), and recommend replacements.

## Portfolio-wide adoption tracking

```meta
type: feature
related: [.devbook/domain/dev-pc-management/features.md#configuration-and-tool-version-tracking, .devbook/domain/repository-management/features.md#technology-stack-inventory]
```

Across the portfolio, detect which technologies are in use, track adoption rates and outliers, surface adoption velocity, and report [Technology Sprawl](domain.md#technology-sprawl).

## Technology recommendations

```meta
type: feature
depends-on: [.devbook/domain/technology-stack/features.md#portfolio-wide-adoption-tracking]
related: [.devbook/domain/tasks/features.md#task-creation]
```

Surface which tools are ready to upgrade, and recommend new tools based on what
the portfolio already adopts. Give adoption decisions their context, and link to
the ADRs that justify them.

## Technology versioning standards

```meta
type: feature
```

Define semantic-versioning expectations per tool, and track compatibility matrices
between technologies. Map the version aliases latest, LTS and stable to concrete
versions.

## Cross-domain integration

```meta
type: feature
related: [.devbook/domain/monitoring/features.md#multi-repo-scanning]
```

Provide baselines to the contexts [the context chapter](context.md#technology-stack)
names. Feed adoption data to Monitoring dashboards, link ADRs in Devbook, and let
Tasks create tech-upgrade tasks from adoption gaps.
