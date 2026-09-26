# Repository Management

```meta
status: draft
index: root
type: context
```

Repository Management maintains a registry of development repositories and tracks
their health across the portfolio — package versions, dependencies, technology
stack, GitHub issues and PRs, and a computed health score — so outdated or
at-risk repos surface without manual investigation. It consumes baselines from
[Technology Stack](../technology-stack/domain.md#technology-registry).

## additional-repositories

```meta
status: draft
type: feature-flag
key: additional-repositories
related: [".devbook/domain/repository-management/features.md#repository-registry-configuration"]
```

Decided at release, from configuration. Name the switch in business language, and say who owns the rollout, what turning it on changes, and when the flag is retired.
