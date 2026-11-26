# ADR 0074: Infrastructure is organised by technology, never by bounded context

```meta
date: 2026-11-27
related: [".devbook/arc42/adr/0073-the-bounded-contexts-are-redrawn.md", ".devbook/arc42/adr/0064-data-access-stays-in-infrastructure-adapters-without-ef-core-or-an-outbox.md", ".devbook/arc42/adr/0067-each-module-ships-its-own-ui-project-and-the-apps-compose-them.md", ".devbook/arc42/adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md", ".devbook/arc42/adr/guidelines/0005-modular-monolith-structure.md", ".devbook/arc42/adr/guidelines/0014-persistence-and-repository-boundaries.md", ".devbook/arc42/05-building-block-view.md", ".devbook/arc42/09-architecture-decisions.md"]
c4: [backlog.desktop]
c4-touches: [backlog.desktop.devbook, backlog.desktop.brain, backlog.desktop.sync, backlog.desktop.mcp, backlog.desktop.work, backlog.desktop.insights, backlog.desktop.devPc, backlog.mobile]
```

A project under `src/Infrastructure/` is named for a technology and knows no bounded
context: it references no module project, and no project, folder or namespace in it is
named after one. The code that binds a context's port to a technology moves to that
context's own `Backlog.Modules.<Context>.Adapters` project, and the rules that leaked into
infrastructure move into the context's module. Stored values keep their names.

## Status

```meta
```

Proposed, 2026-11-27. A follow-up to local ADR 0073 on `plan/boundary-redesign`: it uses
0073's contexts and names, and changes none of its sections.

A **local** decision. Every bare ADR number below means the local one. It amends local
ADR 0064 §1 and the adapter deviation that inherited ADRs 0005 and 0014 record under
*How Backlog applies it*.

## Context

```meta
```

Counts are from `src/` and `tests/` on `plan/boundary-redesign` at `7fb25c9`.

**Five infrastructure projects are a context, not a technology.** `Backlog.Infrastructure.Brain`,
`.Capture`, `.DevPc`, `.Devbook` and `.Sessions` are named after a context, and
`.Sync` after the Sync module. Four of them back a context that has no module
implementation project (Brain, DevPc, Devbook, Sessions), so the infrastructure project
is where that context's logic lives. Inherited ADR 0005 already lists those modules as
a gap.

**Rules leaked into infrastructure.** Examples:

| Project | Rule it holds |
| --- | --- |
| Sessions | `ValidationQuestions` (the approve/revise question), `SessionAlertPublisher` (ADR 0051 alerts), `QuestionRelayEscalation`, `CapacityReader` |
| Devbook | `DevbookWriteGuard` (what is editable), `RemarkNotificationStore` (when a remark alert is raised and ended) |
| Brain | `BrainPickup` (a note becomes work), `InboxBrainTarget` routing |
| DevPc | the tool catalog, refresh and last-run policy inside the 4,632-line `DevToolService` |
| FileSystem | `SessionRequestCompletion`, entry-text composition in `InboxBacklogTarget`, `RoadmapItemRollupBuilder`, `RoadmapActualHours` |
| Sync | `TaskReplicaMerge`, `AnnotationMergeOutcome`, `KnowledgeEventMergeOutcome` |

**Technology projects are organised by context inside.** `FileSystem` has eight
context folders (Calendar, DevPc, Events, Goals, Inbox, Insights, Roadmap, Tasks) and a
`Workspace/` folder of about 51 per-context stores. `Sqlite` has six context folders,
`Cosmos` six, `Sync` five, and `GitHub`, `Claude` and `AzureFoundry` an `Insights/`
folder each. `Mcp` keeps one tool file per context. Most FileSystem folders are joins
between two contexts, each with its own `*CrossContextAdapterRegistration`.

**The architecture tests allow it.** `InfrastructureGraphTests` caps an infrastructure
project at four module references and lists six exceptions with reasons: FileSystem 12,
Sync 9, Mcp 9, Sqlite 7, GitHub 6, Brain 5. Its FileSystem reason says that splitting
`Workspace/` by module would bring it under the cap. Other tests pin context folders
inside infrastructure: `FileSystem.Events`, `FileSystem.Calendar`, `Sqlite.Events`, and
`Devbook/Chapters`, `Instructions` and `Documents`.

**Infrastructure projects also reference each other across contexts.** FileSystem
references GitHub, Devbook and Sqlite, and Brain references Devbook and FileSystem.
`BrainCorpusProfile` derives from Devbook's `DevbookCorpusProfile`. As a result, the
boundary between Brain and Devbook that ADR 0073 §9 drew for the UI is crossed again
one layer down.

## Decision

```meta
```

### 1. Infrastructure knows technology only

```meta
```

A project under `src/Infrastructure/` is named `Backlog.Infrastructure.<Technology>`,
for a storage engine, a protocol, an external service or a file format. Its folders are
named for technical concerns, such as connections, bootstrapping, clients, documents,
transport or parsing. Inside it:

- **It references** `Backlog.SharedKernel` and other technology projects only.
- **It holds only context-free code:** clients, connections and pooling, the bootstrap
  runner, file-store primitives, and readers that return the technology's own model. A
  Claude transcript, an RSS item, a schema.org event and a Cosmos document envelope are
  that kind of model.
- **It implements no module port.** A port is answered one layer up (§2).

A technology one context alone uses gets no project of its own: its code sits in that
context's adapters until a second context needs it. Roslyn for the devbook is one case.

### 2. Each context owns its adapters

```meta
```

Each module that binds a port to a technology gets one
`Backlog.Modules.<Context>.Adapters` project beside its module, `.Abstractions` and
`.UI`. Its folders are named by technology (`Sqlite/`, `FileSystem/`, `GitHub/`,
`Mcp/`, `Sync/`). It holds:

- **The outbound adapters** that answer the module's ports: repositories, stores,
  readers and sources, written over the technology projects.
- **Its cross-context joins.** A join that answers this module's port over another
  module's published surface lives here and references that module's `.Abstractions`
  only. The context that declared the port owns the join; the supplier never does.
- **Its MCP tool group** (an inbound adapter). The module ships its tools the way it
  ships its screens (ADR 0067), and the channel composes them (ADR 0073 §6).
  `Backlog.Infrastructure.Mcp` keeps the host, the transport and the scope enforcement.
- **Its persisted mappings and its bootstrap DDL.** Each module's tables are then
  declared in the module that owns them, which inherited ADR 0014 already requires.
- **One registration extension** the composition roots call (inherited ADR 0005).

A module with two deployables gets one adapters project per deployable. Sync is the
only one: `Backlog.Modules.Sync.Adapters` is the client on the desktop and the phone,
and `Backlog.Modules.Sync.Api.Adapters` holds the Cosmos replica stores of the sync
service.

### 3. Rules go back to their module

```meta
```

A rule an adapter decides moves into the module implementation project of the context
it belongs to. That covers which question to ask, when an alert is raised or ended, how
two replicas merge, what becomes a task, and which tool needs a refresh. Brain, DevPc,
Devbook and Sessions each gain `Backlog.Modules.<Context>`, which closes the gap
inherited ADR 0005 lists. The devbook chapter format, its parser and its write guard are
Devbook's language and go in `Backlog.Modules.Devbook`. The `devbook.db` builder, the
folder readers and the Roslyn code index go in `Backlog.Modules.Devbook.Adapters`.

### 4. Who references what

```meta
```

| Project | May reference | Never |
| --- | --- | --- |
| `Backlog.Infrastructure.<Technology>` | `Backlog.SharedKernel`, other technology projects | any `Backlog.Modules.*` project |
| `Backlog.Modules.<C>.Adapters` | its own module and `.Abstractions`, other modules' `.Abstractions` and `.Contracts.*`, technology projects | another module's implementation or `.Adapters`, any `.UI` |
| `Backlog.Modules.<C>` | its own `.Abstractions`, other modules' `.Abstractions` | its own `.Adapters`, any technology project |
| `Backlog.Modules.<C>.UI` | its own module, `.Abstractions` and `.Adapters`, technology projects | another module's `.Adapters` or implementation |
| composition roots | every `.Adapters` they register | — |

The UI row keeps today's rule that a module's UI may take an adapter. A UI that takes
one now takes its own module's adapter.

### 5. The architecture test

```meta
```

`tests/Backlog.ArchitectureTests/InfrastructureKnowsNoContextTests.cs` enforces §1, §2
and §4 by checking both the project files and the compiled references:

- **`No_infrastructure_project_references_a_module_project`.** It replaces the cap and
  its six exceptions in `InfrastructureGraphTests`, which keeps only its acyclic test.
- **`No_infrastructure_project_folder_or_namespace_names_a_context`.** The context
  names are the folder names under `src/Modules/`, so a new context is covered with no
  change to the test. A name matches as a whole dot or folder segment, so
  `NotificationHubs` does not match `Notifications`. Any exception carries a written
  reason, as the cap's exceptions do today.
- **`An_adapters_project_references_only_its_module_and_published_surfaces`.**
- **`A_module_references_neither_its_adapters_nor_a_technology`.** It extends
  `A_module_never_references_infrastructure`.

The test lands first with today's offenders as its exception list, a ratchet. Each move
deletes its own rows, and the last move leaves the list empty. The tests that pin a
context folder inside infrastructure follow their code to its new home in the same
change: `DevbookModuleRegistrationTests`, `BrainModuleBoundaryTests`, `DevPcAdapterTests`,
`InsightsAdapterPlacementTests`, and the `FileSystem.Events`, `FileSystem.Calendar` and
Sessions rules in `ModuleBoundaryTests`.

### 6. Stored values and names

```meta
```

**No stored value is renamed.** This decision moves code, not data. The values below
are literal strings, named after what the owning context stores, and they survive the
move unchanged:

- the SQLite tables in `backlog.db`
- `devbook.db` and the `devbook-cache` folder, and the other workspace file and folder
  names
- the Cosmos containers
- the replica document kinds, settings files, MCP tool names, the CNG key name and the
  HttpClient names

ADR 0073 §10 already settled the ones that carried an old context name.

**Two values derive from a namespace and are handled where Devbook moves:**

- **The `generatedBy` stamp in `devbook.db`.** It is written, and nothing in `src/`
  reads it. It takes the new assembly name, and `DevbookBuilderParityTests` is checked
  against it.
- **The schema resource name.** It keeps an explicit `LogicalName` in the project file,
  so the constant does not drift from the file.

**What changes with the namespaces:**

- **Namespaces.** They become `Backlog.Infrastructure.<Technology>[.<Concern>]` and
  `Backlog.Modules.<Context>.Adapters[.<Technology>]`.
- **Logger categories.** No `appsettings` filter names an infrastructure category. A
  saved telemetry query that does is rewritten with the move.
- **`InternalsVisibleTo`.** Each pair follows its code.
- **Test projects.** They mirror the new projects, as
  `Backlog.Modules.<Context>.Adapters.UnitTests`. The
  `Backlog.Infrastructure.<Context>.UnitTests` projects go.

The bootstrap order and idempotency of ADR 0006 hold. The SQLite runner runs every
registered module bootstrap when the database is opened, in today's table order.

## Consequences

```meta
```

- **The infrastructure graph is flat.** No technology project references a module, so
  the wide-adapter exceptions and the cross-context references between infrastructure
  projects (FileSystem to Devbook, Brain to Devbook and FileSystem) go.
- **About fifteen `.Adapters` projects are added, and five infrastructure projects
  leave.** `Backlog.Infrastructure.Brain`, `.DevPc`, `.Devbook` and `.Sessions` leave.
  `.Capture` becomes the technology project `Backlog.Infrastructure.Web`, which holds
  the feed, HTML, schema.org and video-page readers. `.Sync` becomes
  `Backlog.Modules.Sync.Adapters`.
- **Four modules gain an implementation project,** and their rules leave the UI and
  infrastructure layers.
- **A context is changed in one folder.** Its rules, adapters, tools and screens all sit
  under `src/Modules/<Context>/`.
- **Each move rides on ADR 0073 landing first.** The adapters are written against
  0073's modules (Work, Insights, Repositories, Goals), not the ones they replace.
- **The records follow on acceptance.** ADR 0064 §1 reads *adapters per module over raw
  SQLite and the Cosmos SDK*, not *data access stays in the infrastructure projects*.
  The *How Backlog applies it* sections of inherited ADRs 0005 and 0014, chapter 5,
  and `CLAUDE.md`'s Devbook paragraph are rewritten by the entry that empties the
  ratchet.
- **`Backlog.SharedKernel` still has `Devbook/` and `Mcp/` folders.** It is not
  infrastructure, and this record leaves it alone.

## Rejected

```meta
```

- **Rename and flatten only.** This keeps each port adapter in its technology project
  and only drops the context-named projects and folders. Infrastructure would still
  reference every module whose port it answers, so it would still know the contexts
  through its references. The cap exceptions would also stay.
- **One `{Module}.Data.{Provider}` project per module and technology,** the
  organization's layout. It would add about thirty projects for no rule that one
  `.Adapters` project per module cannot enforce. ADR 0064 rejected it together with
  EF Core.
- **Put the adapters in the module implementation project.** The technology SDKs would
  reach the domain, which inherited ADR 0005 forbids
  (`A_module_never_references_infrastructure`).
- **Keep the MCP tool groups in `Backlog.Infrastructure.Mcp`, organised by concern.**
  Each tool group is one context's inbound surface. Keeping them in one project keeps
  nine module references in it.
- **Give each single-use technology its own project now, such as Roslyn.** The project
  would have no second consumer, and §1 makes one when it does.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-27 | Proposed: infrastructure named and organised by technology and referencing no module; one `.Adapters` project per context; leaked rules into their module; a ratcheting architecture test; no stored value renamed. | The person, on `+boundary-redesign`: infrastructure should not know the boundary. |
