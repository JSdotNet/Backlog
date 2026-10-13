# Repository Management

```meta
status: draft
index: root
type: context
```

Repository Management maintains a [registry](domain.md#repository-registry) of
development repositories and tracks their health across the portfolio, so
outdated or at-risk repositories surface without manual investigation. Health
covers:

- package versions and dependencies;
- the technology stack;
- GitHub issues and pull requests;
- a computed health score.

It consumes baselines from
[Technology Stack](../technology-stack/domain.md#technology-registry). The
registry is built; scanning and health scoring are planned.

Outside it: what a baseline says and which versions are approved, which
Technology Stack answers, and the work items a finding turns into, which
[Tasks](../tasks/domain.md#task) answers.

## Additional repositories

```meta
status: draft
type: feature-flag
key: additional-repositories
default: on
related: [".devbook/domain/repository-management/features.md#repository-registry-configuration"]
```

Turning it on lets a person configure more than one repository and switch the
panes, and their devbooks, between them. It is released and on by default. It is
listed with the cross-cutting features, because every pane that reads a
repository follows it. The key is declared on `TasksFeatures`, the first context
to gate on it. The settings screen lists it from the feature catalog in
`AppFeatures.cs`. A person's choice is kept in `features.json` beside the app's
other per-device choices.
