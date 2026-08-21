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

Define team-approved technologies (languages, frameworks, runtimes, tools) with
version constraints (min, recommended, optional target), stable/beta/deprecated
status, and per-team or per-project overrides on top of portfolio defaults.

## Deprecation and migration management

```meta
type: feature
status: draft
depends-on: [.domain/technology-stack/features.md#technology-baseline-definition]
```

Mark technologies deprecated with end-of-life dates and migration guidance,
generate migration advisories, enforce minimum versions with tracked legacy
exceptions, and recommend replacements.

## Portfolio-wide adoption tracking

```meta
type: feature
status: draft
related: [.domain/dev-pc-management/features.md#configuration-and-tool-version-tracking, .domain/repository-management/features.md#technology-stack-inventory]
```

Detect which technologies are in use across the portfolio, track adoption rates
and outliers, surface adoption velocity, and report technology sprawl.

## Technology recommendations

```meta
type: feature
status: draft
depends-on: [.domain/technology-stack/features.md#portfolio-wide-adoption-tracking]
related: [.domain/backlog/features.md#backlog-entry-creation]
```

Surface which tools are ready to upgrade, recommend new tools based on team
patterns, provide adoption decision context, and link to ADRs that justify
choices.

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
related: [.domain/monitoring/features.md#multi-repo-scanning]
```

Provide baselines consumed by Dev PC and Repository Management, feed adoption
data to Monitoring dashboards, link ADRs in Second Brain, and enable Backlog to
create tech-upgrade tasks from adoption gaps.
