# 08. Cross-cutting Concepts

```meta
status: active
```

Concepts that apply across multiple channels and domains and must be handled
uniformly. Shared data types define the vocabulary exchanged between them.

## Storage and Sync

```meta
status: active
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/06-runtime-view.md#state-sync-and-webhook-forwarding", ".arc42/06-runtime-view.md#copilot-app-session-capture", ".domain/capture/domain.md#domain-service-source-adapter"]
```

- **Local-first, one canonical local store** — the desktop's own store is the single
  source of truth. Tasks live in one SQLite database (`backlog.db`) under the
  workspace root, with a task's content held as markdown text; JSON files hold the
  workspace settings and feature flags. Markdown is the content of a task, not the
  storage format. See `.arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md`.
- **Configurable repo paths** via a repo registry (`config/repos.json`).
- **Scope-portable dot-folder contract** — `.inbox/`, `.backlog/`, `.brain/` exist at
  workspace, repo, and project levels; shared tags/relationships live in the
  workspace-root `.tags/` (`tags.json`, `tag-graph.json`).
- **Optional cloud sync** for multi-device. Conflict resolution:
  **new items always create; edits are last-write-wins**.
- **Desktop works fully standalone**; the cloud connection is purely additive.
- **Local credential handling includes Copilot sessions** — desktop workers and
  GitHub Copilot App session adapters both run on the same machine and pass local
  context (`session_id`, `worktree_path`, `branch`) without routing credentials
  through the optional Cloud Service.
- **Copilot capture vs. Copilot session tracking** — capture uses session context to
  create Inbox/Backlog/Knowledge items, while Dev PC Management tracking is a
  separate compliance/monitoring concern.

## Feature Enablement

```meta
status: proposed
related: [".arc42/04-solution-strategy.md", ".domain/second-brain/features.md#feature-repository-knowledge-areas", ".domain/dev-pc-management/features.md#sub-feature-copilot-tool-catalog"]
```

- **Optional capabilities are switchable per installation** — repository knowledge,
  the non-backlog knowledge areas, additional repositories, system tools, GitHub
  integration, feedback reporting, Copilot CLI, and AI assistance can each be turned
  on. Core backlog editing is always available and is never switchable.
- **Disabled is the default**; the stored setting records only what has been switched
  *on*, so a capability added later stays out of the way until it is deliberately
  chosen.
- **A disabled capability leaves no surface behind** — its entry points are absent
  rather than present-but-inert, and dependent settings disappear with it.
- **The switch is local to the installation**, kept with the other machine-local
  settings rather than inside the backlog folder, so it never travels with synced
  content.
- **Unreadable or unknown settings fall back to "everything disabled"** rather than
  failing startup, leaving only the always-available core.

> Not yet implemented: the current build defaults every optional capability to
> enabled and stores only the disabled ones. This section records the intended
> reversal, so treat it as the target state rather than a description of today's
> behavior.

## Tagging and Organization

```meta
status: active
```

- `#tags` embedded inside markdown, multiple per item.
- Project tags, cross-cutting tags, and PARA-inspired grouping (Projects, Areas,
  Resources, Archive).
- A tag index enables search across all domains.

## Authentication and Authorization

```meta
status: active
related: [".arc42/09-architecture-decisions.md"]
```

- **No account required** for personal use in standalone mode.
- **OAuth 2.0** for GitHub integration (issue sync, webhook registration).
- **Cloud connection uses device-based auth** — JWT device sessions, no user login.
- The current architecture assumes a single personal user and does not include team-oriented authorization roles.

For the cloud service specifically, the organization's identity, authorization, and
error-contract ADRs apply (see `.arc42/09-architecture-decisions.md`).

## Observability

```meta
status: active
related: [".arc42/09-architecture-decisions.md"]
```

Monitoring dashboards read telemetry signals from Application Insights (errors,
latency per project) alongside local queue/backlog health metrics. The cloud service
follows the organization's OpenTelemetry guidance.

## Shared Data Types

```meta
status: active
related: [".arc42/12-glossary.md", ".domain/inbox/domain.md#aggregate-inbox-item", ".domain/backlog/domain.md#aggregate-backlog-entry", ".domain/second-brain/domain.md#aggregate-knowledge-note", ".domain/monitoring/domain.md#aggregate-progress-signal", ".domain/dev-pc-management/domain.md#aggregate-machine-registry", ".domain/repository-management/domain.md#aggregate-repository-registry", ".domain/technology-stack/domain.md#aggregate-technology-registry"]
```

The vocabulary exchanged across all applications and domains is owned per
bounded context in `.domain` (aggregate shape, invariants, and lifecycle) — this
chapter only names which types cross container boundaries and are therefore an
architectural concern:

| Type | Owning aggregate |
|---|---|
| **InboxItem** | `.domain/inbox/domain.md#aggregate-inbox-item` |
| **TaskItem** (ubiquitous term: Task) | `.domain/backlog/domain.md#aggregate-backlog-entry` |
| **KnowledgeNote** | `.domain/second-brain/domain.md#aggregate-knowledge-note` |
| **ProgressSignal** | `.domain/monitoring/domain.md#aggregate-progress-signal` |
| **RoutingRule** | Not yet modeled in `.domain` — tracked in `.arc42/11-risks-and-technical-debt.md` |
| **MachineRegistration** | `.domain/dev-pc-management/domain.md#aggregate-machine-registry` |
| **RepositoryRegistration** | `.domain/repository-management/domain.md#aggregate-repository-registry` |
| **TechBaseline** | `.domain/technology-stack/domain.md#aggregate-technology-registry` |

The cloud service persists only sync-oriented state derived from these types
(`SyncState`, `SyncPayload`, `WebhookEvents`, `GitHubWebhookConfig`,
`MachineRegistry`, `TeamConfig`) — never the canonical domain data itself.


