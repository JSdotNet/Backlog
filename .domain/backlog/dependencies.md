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
| [Repository Management](../repository-management/domain.md#aggregate-repository-registry) | Customer/Supplier (Backlog = customer) | Repo-registry lookup by opaque id | `.domain/repository-management/naming.md#term-repository` | `repo_ids` resolve to registered repos and their local clone paths. |
| [Environment](../environment/domain.md#aggregate-environment-catalog) | Customer/Supplier (Backlog = customer) | Shortcut lookup by opaque environment id | `.domain/environment/domain.md#domain-service-environment-shortcut-resolution` | Work views can expose quick links to relevant environments without Backlog owning endpoint or launch semantics. |
| [Productivity](../productivity/domain.md#aggregate-productivity-ledger) | OHS + Published Language (Backlog = supplier) | Publishes `AIWorkLogged` from entries | `.domain/backlog/domain.md#domain-event-aiworklogged` | AI-assisted activity on an entry is available for productivity analysis without Productivity reading entry internals. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-item) | Customer/Supplier (Backlog = customer) | Reads the roadmap tag vocabulary for the entry's tag picker | `.domain/roadmap/naming.md#term-roadmap-tag` | The tag picker offers every Roadmap Item tag so an entry can be filed against planned work using the plan's own slug. Backlog conforms to that vocabulary; it does not define roadmap tags and holds no roadmap state. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#aggregate-inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` | `.domain/inbox/domain.md#domain-event-itemtriaged` | Relies on Backlog creating a draft entry from a triaged item. |
| [Second Brain](../second-brain/domain.md#aggregate-knowledge-note) | Partnership | Bi-directional link by id | `.domain/second-brain/domain.md#domain-service-cross-linking` | Notes link to entries and entries link back; either can spawn the other without a shared aggregate. |
| [Roadmap Planning](../roadmap/domain.md#aggregate-roadmap-plan) | Partnership | Optional cross-link by foreign id; read-side gather by tag and by effort | `.domain/roadmap/naming.md#term-backlog-entry-link` | Relies on an entry id staying resolvable so a planned item can show real progress, and on entries carrying its tag plus their registered `effort` so an item can gather and total the work behind it. The link is optional and may dangle: deleting an entry must not corrupt a plan, and Backlog holds no roadmap state of its own. |
| [Monitoring](../monitoring/domain.md#aggregate-progress-signal) | OHS + Published Language (Backlog = supplier) | Subscribes to status/projection events | `.domain/backlog/domain.md#domain-event-statuschanged`, `.domain/backlog/domain.md#domain-event-entryprojected`, `.domain/backlog/domain.md#domain-event-entrycompleted`, `.domain/backlog/domain.md#domain-event-occurrencespawned` | Relies on work-state and projection-state changes for progress signals and GitHub-sync comparison, and on occurrence provenance to read a recurring entry as one series rather than as unrelated items. |
| [Productivity](../productivity/domain.md#aggregate-productivity-ledger) | OHS + Published Language (Backlog = supplier) | Subscribes to `AIWorkLogged` | `.domain/backlog/domain.md#domain-event-aiworklogged` | Relies on AI-assisted activity evidence linked to a backlog item. |

## Notes

- Keep `repo_ids` as opaque identifiers so Backlog does not couple to Repository
  Management internals; only the Projection policy resolves them for GitHub/CLI.
- GitHub issue sync is a two-way relationship — mismatch detection (backlog says
  done vs. issue still open) is owned jointly with Monitoring.
- The `ItemTriaged` payload is Inbox's published language; treat it as a stable
  contract, not an Inbox internal.
- Roadmap Planning is a `Partnership`, not a consumer of a projection. Backlog does
  not know what is planned, and Roadmap does not write status or priority — the
  relationship is one optional foreign id, held on the Roadmap side. See
  `.domain/context-map.md` for why the word *priority* means a different thing on
  each side of it.
- The Roadmap relationship now has two more threads, and both keep effort and tags
  owned here. Roadmap **reads** the tag vocabulary Backlog offers in its picker,
  and Roadmap **gathers** entries by their tag and totals the `effort` those
  entries registered — but Backlog registers the effort and files the tag, and
  Roadmap owns neither. A roadmap tag is a slug Backlog conforms to, not one it
  defines, which is why renaming a roadmap item's title must not change it.
- Entry-to-entry dependencies stay inside this context and add no cross-context
  relationship: `depends_on` holds Backlog Entry ids only, and readiness is
  derived here rather than published. Roadmap Planning's dependencies are a
  separate graph over planned work, and the two are not the same edge seen twice.
- `OccurrenceSpawned` links a completed occurrence to its successor. Consumers
  treat the link as provenance, not ownership — each occurrence is a separate
  entry with its own lifecycle, and a series is not one work item.
