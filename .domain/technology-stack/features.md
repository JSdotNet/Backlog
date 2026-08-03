# Features: Technology Stack

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: Technology baseline definition

```meta
status: draft
depends-on: []
related: []
issue: null
```

Define team-approved technologies (languages, frameworks, runtimes, tools) with
version constraints (min, recommended, optional target), stable/beta/deprecated
status, and per-team or per-project overrides on top of portfolio defaults.

## Feature: Deprecation and migration management

```meta
status: draft
depends-on: [.domain/technology-stack/features.md#feature-technology-baseline-definition]
related: []
issue: null
```

Mark technologies deprecated with end-of-life dates and migration guidance,
generate migration advisories, enforce minimum versions with tracked legacy
exceptions, and recommend replacements.

## Feature: Portfolio-wide adoption tracking

```meta
status: draft
depends-on: []
related: [.domain/dev-pc-management/features.md#feature-configuration-and-tool-version-tracking, .domain/repository-management/features.md#feature-technology-stack-inventory]
issue: null
```

Detect which technologies are in use across the portfolio, track adoption rates
and outliers, surface adoption velocity, and report technology sprawl.

## Feature: Technology recommendations

```meta
status: draft
depends-on: [.domain/technology-stack/features.md#feature-portfolio-wide-adoption-tracking]
related: [.domain/backlog/features.md#feature-backlog-entry-creation]
issue: null
```

Surface which tools are ready to upgrade, recommend new tools based on team
patterns, provide adoption decision context, and link to ADRs that justify
choices.

## Feature: Technology versioning standards

```meta
status: draft
depends-on: []
related: []
issue: null
```

Define semantic-versioning expectations per tool, map version aliases (latest,
LTS, stable) to concrete versions, and track compatibility matrices between
technologies.

## Feature: Cross-domain integration

```meta
status: draft
depends-on: []
related: [.domain/monitoring/features.md#feature-multi-repo-scanning]
issue: null
```

Provide baselines consumed by Dev PC and Repository Management, feed adoption
data to Monitoring dashboards, link ADRs in Second Brain, and enable Backlog to
create tech-upgrade tasks from adoption gaps.
