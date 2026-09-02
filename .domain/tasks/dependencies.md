# Tasks

```meta
type: dependencies
status: draft
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| GitHub (external) | ACL | REST call via the Projection policy | `.domain/tasks/domain.md#projection` | Multi-repo tasks project to one GitHub issue per target repo; status syncs bidirectionally through an adapter. |
| Copilot CLI (external) | ACL | Command/task projection via the Projection policy | `.domain/tasks/domain.md#projection` | Entries can project to one CLI task per target repo without taking a dependency on CLI task internals. |
| [Second Brain](../second-brain/domain.md#knowledge-note) | Partnership | Id-based cross-link and read-side embedding | `.domain/second-brain/domain.md#cross-linking` | Entries embed or deep-link Knowledge Note content for context; queries can span both contexts while each side keeps only foreign ids. |
| [Repository Management](../repository-management/domain.md#repository-registry) | Customer/Supplier (Tasks = customer) | Repo-registry lookup by opaque id, plus a registration trigger via the registry's own registration capability | `.domain/repository-management/naming.md#repository`, `.domain/repository-management/features.md#repository-registration` | `repo_ids` resolve to registered repos and their local clone paths; importing a plan that names an unregistered repository triggers registration rather than failing the import. |
| [Environment](../environment/domain.md#environment-catalog) | Customer/Supplier (Tasks = customer) | Shortcut lookup by opaque environment id | `.domain/environment/domain.md#environment-shortcut-resolution` | Work views can expose quick links to relevant environments without Tasks owning endpoint or launch semantics. |
| [Productivity](../productivity/domain.md#productivity-ledger) | OHS + Published Language (Tasks = supplier) | Publishes `AIWorkLogged` from tasks | `.domain/tasks/domain.md#aiworklogged` | AI-assisted activity on a task is available for productivity analysis without Productivity reading task internals. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-item) | Customer/Supplier (Tasks = customer) | Reads the roadmap tag vocabulary for the task's tag picker | `.domain/roadmap/naming.md#roadmap-tag` | The tag picker offers every Roadmap Item tag so a task can be filed against planned work using the plan's own slug. Tasks conforms to that vocabulary; it does not define roadmap tags and holds no roadmap state. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` | `.domain/inbox/domain.md#itemtriaged` | Relies on Tasks creating a draft task from a triaged item. |
| [Second Brain](../second-brain/domain.md#knowledge-note) | Partnership | Bi-directional link by id | `.domain/second-brain/domain.md#cross-linking` | Notes link to tasks and tasks link back; either can spawn the other without a shared aggregate. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-plan) | Partnership | Optional cross-link by foreign id; read-side gather by tag and by effort | `.domain/roadmap/naming.md#task-link` | Relies on a task id staying resolvable so a planned item can show real progress, and on tasks carrying its tag plus their registered `effort` so an item can gather and total the work behind it. The link is optional and may dangle: deleting a task must not corrupt a plan, and Tasks holds no roadmap state of its own. |
| [Monitoring](../monitoring/domain.md#progress-signal) | OHS + Published Language (Tasks = supplier) | Subscribes to status/projection events | `.domain/tasks/domain.md#statuschanged`, `.domain/tasks/domain.md#taskprojected`, `.domain/tasks/domain.md#taskcompleted`, `.domain/tasks/domain.md#occurrencespawned` | Relies on work-state and projection-state changes for progress signals and GitHub-sync comparison, and on occurrence provenance to read a recurring task as one series rather than as unrelated items. |
| [Productivity](../productivity/domain.md#productivity-ledger) | OHS + Published Language (Tasks = supplier) | Subscribes to `AIWorkLogged` | `.domain/tasks/domain.md#aiworklogged` | Relies on AI-assisted activity evidence linked to a task. |

## Notes

- Keep `repo_ids` as opaque identifiers so Tasks does not couple to Repository
  Management internals; only the Projection policy resolves them for GitHub/CLI.
- Import can trigger repository registration when a plan names a repository
  the registry does not yet have, but it does so through Repository
  Management's own registration capability rather than gaining new authority:
  Tasks remains the customer, and what a registered repository holds is
  still Repository Management's decision.
- GitHub issue sync is a two-way relationship — mismatch detection (backlog says
  done vs. issue still open) is owned jointly with Monitoring.
- The `ItemTriaged` payload is Inbox's published language; treat it as a stable
  contract, not an Inbox internal.
- Roadmap Planning is a `Partnership`, not a consumer of a projection. Tasks does
  not know what is planned, and Roadmap does not write status or priority — the
  relationship is one optional foreign id, held on the Roadmap side. See
  `.domain/context-map.md` for why the word *priority* means a different thing on
  each side of it.
- The Roadmap relationship now has two more threads, and both keep effort and tags
  owned here. Roadmap **reads** the tag vocabulary Tasks offers in its picker,
  and Roadmap **gathers** tasks by their tag and totals the `effort` those
  tasks registered — but Tasks registers the effort and files the tag, and
  Roadmap owns neither. A roadmap tag is a slug Tasks conforms to, not one it
  defines, which is why renaming a roadmap item's title must not change it.
- Task-to-task dependencies stay inside this context and add no cross-context
  relationship: `depends_on` holds Task ids only, and readiness is
  derived here rather than published. Roadmap Planning's dependencies are a
  separate graph over planned work, and the two are not the same edge seen twice.
- `OccurrenceSpawned` links a completed occurrence to its successor. Consumers
  treat the link as provenance, not ownership — each occurrence is a separate
  task with its own lifecycle, and a series is not one work item.
