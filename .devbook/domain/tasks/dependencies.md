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
| GitHub (external) | ACL | REST call via the Projection policy | `.devbook/domain/tasks/domain.md#projection` | Multi-repo tasks project to one GitHub issue per target repo; status syncs bidirectionally through an adapter. |
| Copilot CLI (external) | ACL | Command/task projection via the Projection policy | `.devbook/domain/tasks/domain.md#projection` | Entries can project to one CLI task per target repo without taking a dependency on CLI task internals. |
| [Devbook](../devbook/domain.md#knowledge-note) | Partnership | Id-based cross-link and read-side embedding | `.devbook/domain/devbook/domain.md#cross-linking` | Entries embed or deep-link Knowledge Note content for context; queries can span both contexts while each side keeps only foreign ids. |
| [Repository Management](../repository-management/domain.md#repository-registry) | Customer/Supplier (Tasks = customer) | Repo-registry lookup by opaque id, plus a registration trigger via the registry's own registration capability | `.devbook/domain/repository-management/domain.md#repository`, `.devbook/domain/repository-management/features.md#repository-registration` | `repo_ids` resolve to registered repos and their local clone paths; importing a plan that names an unregistered repository triggers registration rather than failing the import. |
| [Environment](../environment/domain.md#environment-catalog) | Customer/Supplier (Tasks = customer) | Shortcut lookup by opaque environment id | `.devbook/domain/environment/domain.md#environment-shortcut-resolution` | Work views can expose quick links to relevant environments without Tasks owning endpoint or launch semantics. |
| [Productivity](../productivity/domain.md#productivity-ledger) | OHS + Published Language (Tasks = supplier) | Publishes `AIWorkLogged` from tasks | `.devbook/domain/tasks/domain.md#aiworklogged` | AI-assisted activity on a task is available for productivity analysis without Productivity reading task internals. |
| [Sessions](../sessions/domain.md#session-state) | Customer/Supplier (Tasks = customer) | Port in Tasks' abstractions (`ILinkedSessionStates`), answered by an infrastructure adapter over `IAgentSessionSource` | `.devbook/domain/sessions/domain.md#session-state` | A task's work badge colours a linked session by its state — stalled highlighted as the state that needs the person ("Quiet 30 min"), running neutral, finished muted. A finished session also says whether the Claude desktop app still holds it: Open in muted ink ("finished, still open") or Archived in quiet ink ("finished, archived"), read from the desktop app's records as `LinkedSession(State, Archived)`; a session with no desktop record draws as before. Tasks keeps only the session id it was linked with; the state and the archived fact are read on load and on every reload, never stored, and a session the record no longer holds draws with no state. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-item) | Customer/Supplier (Tasks = customer) | Reads the roadmap tag vocabulary for the task's tag picker | `.devbook/domain/roadmap/domain.md#roadmap-tag` | The tag picker offers every Roadmap Item tag so a task can be filed against planned work using the plan's own slug. Tasks conforms to that vocabulary; it does not define roadmap tags and holds no roadmap state. |
| [Sync](../sync/context.md) | OHS + Published Language (Sync = supplier) | The desktop's `TaskSyncWorker` pushes and pulls the task feed through the sync service; `TaskReplicaMerge` in `Backlog.Infrastructure.Sync` applies each pulled document to the local store | `TaskChange` and `TaskPayload` in `Backlog.Modules.Sync.Abstractions`; `.devbook/domain/sync/domain.md#change-precedence`; local ADR 0005 | A task reaches the person's other devices as one whole document, and a push never moves it backwards. Sync stores the document without reading it; what a task is, and how a pulled copy merges into the local store, stay with Tasks ([Multi-device sync](features.md#multi-device-sync)). |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` | `.devbook/domain/inbox/domain.md#itemtriaged` | Relies on Tasks creating a draft task from a triaged item. |
| [Devbook](../devbook/domain.md#knowledge-note) | Partnership | Bi-directional link by id | `.devbook/domain/devbook/domain.md#cross-linking` | Notes link to tasks and tasks link back; either can spawn the other without a shared aggregate. |
| [Roadmap Planning](../roadmap/domain.md#roadmap-plan) | Partnership | Optional cross-link by foreign id; read-side gather by tag and by effort | `.devbook/domain/roadmap/domain.md#task-link` | Relies on a task id staying resolvable so a planned item can show real progress, and on tasks carrying its tag plus their registered `effort` so an item can gather and total the work behind it. The link is optional and may dangle: deleting a task must not corrupt a plan, and Tasks holds no roadmap state of its own. |
| [Monitoring](../monitoring/domain.md#progress-signal) | OHS + Published Language (Tasks = supplier) | Subscribes to status/projection events. Intended: Tasks emits none of them yet | `.devbook/domain/tasks/domain.md#statuschanged`, `.devbook/domain/tasks/domain.md#taskprojected`, `.devbook/domain/tasks/domain.md#taskcompleted`, `.devbook/domain/tasks/domain.md#occurrencespawned` | Relies on work-state and projection-state changes for progress signals and GitHub-sync comparison, and on occurrence provenance to read a recurring task as one series rather than as unrelated items. |
| [Productivity](../productivity/domain.md#productivity-ledger) | OHS + Published Language (Tasks = supplier) | Subscribes to `AIWorkLogged` | `.devbook/domain/tasks/domain.md#aiworklogged` | Relies on AI-assisted activity evidence linked to a task. |
| [Sessions](../sessions/context.md) | Customer/Supplier (Tasks = supplier) | Calls a capability Tasks publishes in `Backlog.Modules.Tasks.Abstractions`, from `SessionValidationAnswers` in `Backlog.Infrastructure.Sessions`, in process | `.devbook/domain/tasks/features.md#filing-a-follow-up-after-an-entry`, `.devbook/domain/tasks/requirements.md#filing-a-follow-up-after-an-entry` | Relies on Tasks filing exactly one `ready` prompt after the gated entry per notification id, in the gated entry's plan and repository, so an answer handed over twice files one follow-up. **Not built.** |

## Notes

- Keep `repo_ids` as opaque identifiers so Tasks does not couple to Repository
  Management internals; only the Projection policy resolves them for GitHub/CLI.
- Import can trigger repository registration when a plan names a repository
  the registry does not yet have. It does so through Repository Management's own
  registration capability rather than gaining new authority. Tasks remains the
  customer, and what a registered repository holds is still Repository
  Management's decision.
- GitHub issue sync is a two-way relationship. Mismatch detection, such as the
  backlog saying done while the issue is still open, is owned jointly with
  Monitoring.
- The `ItemTriaged` payload is Inbox's published language; treat it as a stable
  contract, not an Inbox internal.
- The Roadmap relationship has three threads, and Tasks owns the state in each:
  - **Partnership by task id.** A Roadmap Item may name a task by one optional
    foreign id, held on the Roadmap side. Roadmap never writes status or
    priority.
  - **Tag vocabulary.** Tasks reads the roadmap tags for its picker, as the
    outbound Customer/Supplier row says. A roadmap tag is a slug Tasks conforms
    to, not one it defines, which is why renaming a roadmap item's title must not
    change it. Tasks reads them again whenever the plan changes, so an item just
    planned is offered — in the picker and in the Goals view's attach search —
    without a reload.
  - **Gathering.** Roadmap gathers tasks by their tag and totals the `effort`
    they registered. Tasks registers the effort and files the tag.
- Roadmap is not a consumer of a projection, and Tasks does not know what is
  planned. See `.devbook/domain/context-map.md` for why the word *priority*
  means a different thing on each side of the relationship.
- Task-to-task dependencies stay inside this context and add no cross-context
  relationship. `depends_on` holds Task ids only, and readiness is derived here
  rather than published. Roadmap Planning's dependencies are a separate graph
  over planned work, not the same edge seen twice.
- `OccurrenceSpawned` links a completed occurrence to its successor. Consumers
  treat the link as provenance, not ownership: each occurrence is a separate
  task with its own lifecycle, and a series is not one work item.
