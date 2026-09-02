# Repository Management

```meta
type: naming
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their
> aliases. Each term links to where it is modeled (`related`); the surface
> names it is also known by are recorded in the `aliases` metadata field so a
> synonym can always be resolved back to one canonical concept.

## Repository Registry

```meta
type: term
status: draft
aliases: [RepositoryRegistry]
related: [.domain/repository-management/domain.md#repository-registry]
```

The single global registry of repositories across the portfolio.

## Repository

```meta
type: term
status: draft
aliases: [Repository, repo_id, repo_ids]
related: [.domain/repository-management/domain.md#repository]
```

An individual code repository. `repo_id` aligns with the Tasks context's `repo_ids` and
the workspace `repos.json`, so a repository resolves consistently across
contexts.

## Tech Baselines

```meta
type: term
status: draft
aliases: [TechBaselines]
related: [.domain/repository-management/domain.md#tech-baselines, .domain/technology-stack/naming.md#technology-baseline]
```

This context's local copy of the Technology Stack `Technology Baseline`, used to
validate repositories without holding the foreign aggregate.

## Health Score

```meta
type: term
status: draft
aliases: [health_score, HealthDetails, HealthScoreSnapshot]
related: [.domain/repository-management/domain.md#health-details]
```

The 0-100 composite score for a repository, broken down in `HealthDetails` and
tracked over time as `HealthScoreSnapshot`; see `flow.md` for how it is computed.

## Package Manifest

```meta
type: term
status: draft
aliases: [PackageManifest]
related: [.domain/repository-management/domain.md#package-manifest]
```

A dependency manifest file in a repository, identified by `(repo_id, file_path)`.
