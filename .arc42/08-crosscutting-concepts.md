# 08. Cross-cutting Concepts

```meta
status: active
```

Concepts that apply across multiple channels and domains and must be handled
uniformly. Shared data types define the vocabulary exchanged between them.

## Storage and Sync

```meta
status: active
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/06-runtime-view.md#state-sync-and-webhook-forwarding"]
```

- **Local-first, markdown canonical** — the desktop's markdown files are the single
  source of truth; JSON files hold derived indexes, metadata, and relationships.
- **Configurable repo paths** via a repo registry (`config/repos.json`).
- **Scope-portable dot-folder contract** — `.inbox/`, `.backlog/`, `.brain/` exist at
  workspace, repo, and project levels; shared tags/relationships live in the
  workspace-root `.tags/` (`tags.json`, `tag-graph.json`).
- **Optional cloud sync** for multi-device. Conflict resolution:
  **new items always create; edits are last-write-wins**.
- **Desktop works fully standalone**; the cloud connection is purely additive.

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
related: [".arc42/12-glossary.md"]
```

The vocabulary exchanged across all applications and domains:

| Type | Key fields |
|---|---|
| **InboxItem** | id, source, title, body_md, captured_at, received_at, status, tags[], source_metadata |
| **BacklogEntry** | id, repo_ids[], type, content_md, tags[], priority, sub_items[], github_issue_ids, cli_task_ids |
| **KnowledgeNote** | id, topic, project_refs[], body_md, tags[], linked_note_ids[], updated_at |
| **ProgressSignal** | item_id, repo_id, signal_type, detected_at, notes |
| **RoutingRule** | source patterns, tag patterns → repo mapping |
| **MachineRegistration** | machine_id, machine_name, os, ip, mac_address, status, last_heartbeat |
| **RepositoryRegistration** | repo_id, repo_name, local_path, default_branch, package_manifests[] |
| **TechBaseline** | tool_name, required_version, policy_level, rollout_status |

The cloud service persists only sync-oriented state derived from these types
(`SyncState`, `SyncPayload`, `WebhookEvents`, `GitHubWebhookConfig`,
`MachineRegistry`, `TeamConfig`) — never the canonical domain data itself.


