# Repository Management

```meta
index: root
type: context
related: [.devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md]
```

Repository Management maintains a [registry](domain.md#repository-registry) of
development repositories and tracks their health across the portfolio, so
outdated or at-risk repositories surface without manual investigation. Health
covers:

- package versions and dependencies;
- the technology stack;
- GitHub issues and pull requests;
- a computed health score.

It also owns the state of the registered repositories' pull requests:

- each one's [status](domain.md#pull-request-status);
- its [readiness verdict](domain.md#readiness-verdict);
- the stack it sits in;
- the [merge commands](domain.md#merge-command) that act on it.

A pull request is a fact
about a registered repository, and this context already decides which repositories
are read from GitHub, and with which account. Health counts the open pull requests
of a repository; readiness says what each one waits on. Both come from the same
reads. Local
[ADR 0042](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#1-repository-management-owns-pull-request-state)
widens the subject this way.

It consumes baselines from
[Technology Stack](../technology-stack/domain.md#technology-registry). The
registry is built; scanning and health scoring are planned.

Pull request state
is built in this context's module, `Backlog.Modules.RepositoryManagement`. Its
`.Abstractions` project holds the contract every reader uses. Beside it sit the
rules every screen shares: the readiness verdict, the stacks, which merge a pull
request is offered, and the label filter. The module holds the one cache and the
merge commands. The pull requests pane's markup and its status lines
stay in the desktop shell and read that contract.

No feature flag gates [archiving a repository](features.md#archiving-a-repository).
Archiving is a registry edit like a rename, so it is there wherever the repository list
is.

Outside it:

- what a baseline says and which versions are approved, which Technology Stack
  answers;
- the work items a finding turns into, and which task a session or a pull request
  belongs to, which [Tasks](../tasks/domain.md#task) answers;
- which session opened a pull request, which
  [Sessions](../sessions/context.md) answers;
- the goal a pull request counts toward, which
  [Progress](../progress/context.md) answers;
- the history of merged work the dashboard counts, which
  [Productivity](../productivity/features.md#personal-productivity-dashboard)
  reads on its own.

## Pull requests

```meta
type: feature-flag
key: pull-requests
related: [".devbook/domain/repository-management/features.md#pull-request-readiness", ".devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md"]
tests: [unit:dotnet:Backlog.Modules.RepositoryManagement.UnitTests.RepositoryManagementFeaturesTests.The_pull_requests_key_keeps_the_value_it_was_stored_under]
```

Turning the `pull-requests` flag on offers the pull requests pane in the shell, with its lanes, its detail
panel and its acts. Off, the pane is not offered, and a task's side panel names only
sessions under its linked work.

The flag is marked `DEV` in the feature catalog in `AppFeatures.cs`, the settings screen's
mark for a feature still under construction
([Feature Enablement](../../arc42/08-crosscutting-concepts.md#feature-enablement)).
A person's choice is kept in `features.json` beside the app's other per-device
choices. The key is declared as `RepositoryManagementFeatures.PullRequests` in
this context's `.Abstractions` project. Local
[ADR 0042](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#7-the-flag-pull-requests-moves-owner-and-keeps-its-key)
moved the declaration here from `SessionFeatures` and kept the key, so a stored
choice still applies. It is retired when the pull requests pane leaves `DEV`.

## Additional repositories

```meta
type: feature-flag
key: additional-repositories
default: on
related: [".devbook/domain/repository-management/features.md#repository-registry-configuration"]
```

Turning the `additional-repositories` flag on lets a person configure more than one repository and switch the
panes, and their devbooks, between them. It is released and on by default. It is
listed with the cross-cutting features, because every pane that reads a
repository follows it. The key is declared on `TasksFeatures`, the first context
to gate on it. The settings screen lists it from the feature catalog in
`AppFeatures.cs`. A person's choice is kept in `features.json` beside the app's
other per-device choices.
