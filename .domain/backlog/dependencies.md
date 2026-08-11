# Dependencies: Backlog Management

```meta
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| GitHub (external) | ACL | REST call via the Projection policy | `.domain/backlog/domain.md#domain-service-projection` | Multi-repo entries project to one GitHub issue per target repo; status syncs bidirectionally through an adapter. |
| Copilot CLI (external) | ACL | Command/task projection via the Projection policy | `.domain/backlog/domain.md#domain-service-projection` | Entries can project to one CLI task per target repo without taking a dependency on CLI task internals. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Partnership | Id-based cross-link and read-side embedding | `.domain/second-brain/domain.md#domain-service-cross-linking` | Entries embed or deep-link Knowledge Note content for context; queries can span both contexts while each side keeps only foreign ids. |
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Customer/Supplier (Backlog = customer) | Repo-registry lookup by opaque id | `.domain/repository-management/naming.md#term-repository` | `repo_ids` resolve to registered repos and their local clone paths. |`r`n| [Environment](../environment/domain.md#aggregate-environment-catalog) | Customer/Supplier (Backlog = customer) | Shortcut lookup by opaque environment id | `.domain/environment/domain.md#domain-service-environment-shortcut-resolution` | Roadmap and work views can expose quick links to relevant environments without Backlog owning endpoint or launch semantics. |`r`n| [Productivity](../productivity/domain.md#aggregate-productivity-ledger) | OHS + Published Language (Backlog = supplier) | Publishes `AIWorkLogged` from entries | `.domain/backlog/domain.md#domain-event-aiworklogged` | AI-assisted activity on an entry is available for productivity analysis without Productivity reading entry internals. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` | `.domain/inbox/domain.md#domain-event-itemtriaged` | Relies on Backlog creating a draft entry from a triaged item. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Partnership | Bi-directional link by id | `.domain/second-brain/domain.md#domain-service-cross-linking` | Notes link to entries and entries link back; either can spawn the other without a shared aggregate. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | OHS + Published Language (Backlog = supplier) | Subscribes to status/projection events | `.domain/backlog/domain.md#domain-event-statuschanged`, `.domain/backlog/domain.md#domain-event-entryprojected`, `.domain/backlog/domain.md#domain-event-entrycompleted` | Relies on work-state and projection-state changes for progress signals and GitHub-sync comparison. |`r`n| [Productivity](../productivity/domain.md#aggregate-productivity-ledger) | OHS + Published Language (Backlog = supplier) | Subscribes to `AIWorkLogged` | `.domain/backlog/domain.md#domain-event-aiworklogged` | Relies on AI-assisted activity evidence linked to a backlog item. |

## Notes

- Keep `repo_ids` as opaque identifiers so Backlog does not couple to Repository
  Management internals; only the Projection policy resolves them for GitHub/CLI.
- GitHub issue sync is a two-way relationship — mismatch detection (backlog says
  done vs. issue still open) is owned jointly with Monitoring.
- The `ItemTriaged` payload is Inbox's published language; treat it as a stable
  contract, not an Inbox internal.
