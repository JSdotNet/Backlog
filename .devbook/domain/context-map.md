# Backlog

```meta
type: context-map
status: draft
```

> Strategic DDD view of the Backlog product domain: bounded-context roles,
> subdomain classification, and the relationships that carry work, knowledge,
> standards, and signals across the system.

> See also: `.devbook/arc42/03-context-and-scope.md` for the system-level context this
> map's bounded contexts sit inside, and `.devbook/arc42/05-building-block-view.md#container-view` for
> how these contexts are realized across the desktop, mobile, IDE, and cloud
> containers.

## Subdomain landscape

| Bounded context | Subdomain type | Why |
|---|---|---|
| Capture | Core | It is the front door for turning raw external input into normalized intent for the product. |
| Inbox | Core | It owns triage and the decision point where captured input becomes work, knowledge, deferral, or archive. |
| Tasks | Core | It owns the durable work model, prioritization, and multi-repository execution planning. |
| Roadmap Planning | Core | It owns the forward plan — what is intended, when, in what order — and is the only context that holds a dependency between two pieces of planned work. Sequencing intent across repositories is a judgement the product exists to support, not a report over work someone else owns. |
| Devbook | Core | It reads the devbook a repository carries beside its code in place, builds its database, renders and searches it, and supplies that reading machinery to the Brain. |
| Brain | Core | It owns one person's own notes, organized by that person and their agent, linked to their work by id and, one way, into a repository's devbook (local ADR 0040). |
| Productivity | Supporting | It turns AI-assisted work activity into personal productivity insight without owning the work items or execution tools. |
| Environment | Supporting | It provides quick access to named local, cloud, and project environments without becoming the authority for repository or health data. |
| Monitoring & Dashboard | Supporting | It observes the core flow and turns signals into visibility and follow-up actions rather than owning the work itself. |
| Technology Stack | Supporting | It supplies portfolio-wide standards, baselines, and deprecation policy to the core work contexts. |
| Repository Management | Supporting | It provides repository inventory, health, and dependency visibility used by the core work contexts. |
| Dev PC Management | Supporting | It provides machine inventory, compliance, and operator support capabilities for the work system. |
| Progress | Supporting | It keeps the goals a person's work is attached to and the impediments that hold it back, and reads both over an iteration as a stand-up, naming tasks by id, plans by tag and pull requests by number without owning any of them. |
| Sessions | Supporting | It records what the AI coding agents have been doing on each environment, without owning the work, the machines, or the repositories those sessions touch. |
| Notifications | Supporting | It lets any context tell the person something or ask them a question, and hands the answer back from whichever paired device they used. It carries no work of its own (local ADR 0039). |
| Sync | Supporting | It keeps one person's data the same across their devices. It is built in-house (local ADR 0005), and its pairing and precedence rules are cut to Backlog's local-first model, but it carries other contexts' documents rather than being what makes the product different. |

## Context map

```mermaid
flowchart LR
    Capture[Capture]
    Inbox[Inbox]
    Tasks[Tasks]
    Roadmap[Roadmap Planning]
    Devbook[Devbook]
    Brain[Brain]
    Productivity[Productivity]
    Environment[Environment]
    Monitor["Monitoring & Dashboard"]
    Tech[Technology Stack]
    Repo[Repository Management]
    DevPC[Dev PC Management]
    Sessions[Sessions]
    Progress[Progress]
    Sync[Sync]
    Notify[Notifications]

    Capture -->|OHS + Published Language<br/>ItemCaptured| Inbox
    %% Inbox -> Tasks is built as an in-process port (IInboxBacklogTarget in
    %% the Inbox's Abstractions) answered by an infrastructure adapter over
    %% Tasks' published ITaskItems — not an async event. See
    %% .devbook/domain/inbox/dependencies.md.
    Inbox -->|OHS + Published Language<br/>ItemTriaged| Tasks
    Inbox -->|OHS + Published Language<br/>ItemTriaged: To Brain| Brain
    Tasks <-->|Partnership<br/>Task links by id| Brain
    Devbook -->|Customer/Supplier<br/>Reader, database builder, viewer, search, remarks| Brain
    Brain -.->|"One-way reference<br/>repo: link to a repository chapter"| Devbook
    Repo -->|Customer/Supplier<br/>Repo registry lookup by alias| Brain
    Tasks <-->|Partnership<br/>Optional cross-link by id| Roadmap
    Roadmap -->|Customer/Supplier<br/>Roadmap tag vocabulary| Tasks
    Devbook -->|Customer/Supplier<br/>Chapters by tag/ref + registered effort| Roadmap

    Roadmap -->|OHS + Published Language<br/>RoadmapItemScheduled| Monitor

    Tasks -->|OHS + Published Language<br/>StatusChanged / TaskProjected / TaskCompleted| Monitor
    Inbox -->|Customer/Supplier<br/>Queue-health feed| Monitor
    Repo -->|Customer/Supplier<br/>Health and scan feed| Monitor
    DevPC -->|OHS + Published Language<br/>MachineStatusChanged / ComplianceUpdated| Monitor
    Productivity -->|OHS + Published Language<br/>ProductivityRecorded| Monitor
    Monitor -->|Customer/Supplier<br/>Health and availability signals| Environment
    Monitor -->|OHS + Published Language<br/>FollowUpCaptured| Inbox

    Tech -->|Customer/Supplier<br/>Technology baselines| Repo
    Tech -->|Customer/Supplier<br/>Team tools baseline| DevPC
    Repo -->|Customer/Supplier<br/>Adoption and technology scans| Tech
    DevPC -->|Customer/Supplier<br/>Tool-version reports| Tech

    Tasks -->|OHS + Published Language<br/>AIWorkLogged| Productivity
    Monitor -->|OHS + Published Language<br/>CopilotSession signal| Productivity
    Productivity -->|Customer/Supplier<br/>Productivity summaries| Tasks

    Repo -->|Customer/Supplier<br/>Repository/environment registry lookup| Environment
    Environment -->|Customer/Supplier<br/>Launchable environment links| Tasks
    Environment -->|Customer/Supplier<br/>Environment shortcuts| DevPC

    %% Sessions. The Sessions -> Productivity and Sessions -> Tasks edges are
    %% built, each through an infrastructure adapter rather than a published
    %% event. The other two below are named and not built: see the strategic
    %% rules below and .devbook/domain/sessions/dependencies.md.
    DevPC -.->|"Customer/Supplier (not built)<br/>Machine identity for an environment"| Sessions
    Sessions -->|Customer/Supplier<br/>Agent session facts| Productivity
    Sessions -->|Customer/Supplier<br/>Linked session state| Tasks
    Sessions -.->|"Customer/Supplier (not built)<br/>Attached session: state, branch"| Progress
    Sessions -.->|"Customer/Supplier (not built)<br/>Stalled-session observation"| Monitor

    Repo -->|Customer/Supplier<br/>Repo registry lookup| Tasks
    Repo -->|Customer/Supplier<br/>Repo registry lookup by alias| Roadmap

    %% Progress. All three edges are in-process reads; Progress publishes no
    %% event. See .devbook/domain/progress/context.md and local ADR 0035.
    Tasks -->|Customer/Supplier<br/>Task by id: status, due date, effort, blocked mark, linked pull requests| Progress
    Roadmap -->|Customer/Supplier<br/>Plan tag vocabulary, plan order, pace| Progress
    Repo -->|Customer/Supplier<br/>Repo registry lookup by alias| Progress

    %% Sync. It carries the documents of the contexts below and reads none of
    %% their payloads. Each downstream context reaches it through an adapter in
    %% Backlog.Infrastructure.Sync. See .devbook/domain/sync/context.md.
    Sync -->|OHS + Published Language<br/>Task replica feed| Tasks
    Sync -->|OHS + Published Language<br/>Session record replica| Sessions
    Sync -->|OHS + Published Language<br/>Annotation replica| Devbook
    Sync -->|Customer/Supplier<br/>capture-kind documents for IInboxIntake| Inbox

    %% Notifications. Not built yet: local ADR 0039. It conforms to Sync's replica
    %% protocol and offers INotifications to the contexts that publish.
    Sync -.->|"Conformist (not built)<br/>Notification and reply replica"| Notify
    Notify -.->|"OHS + Published Language (not built)<br/>INotifications, answer handler"| Sessions
    Notify -.->|"OHS + Published Language (not built)<br/>INotifications, answer handler"| Sync
    Notify -.->|"OHS + Published Language (not built)<br/>INotifications"| Devbook
    Notify -.->|"OHS + Published Language (not built)<br/>Close by subject"| Tasks
```

## Published languages and contracts

| Contract owner | Published language / contract | Used by |
|---|---|---|
| Capture | `.devbook/domain/capture/domain.md#itemcaptured` | Inbox |
| Inbox | `.devbook/domain/inbox/domain.md#itemtriaged` | Tasks, Brain |
| Tasks | `.devbook/domain/tasks/domain.md#statuschanged`, `.devbook/domain/tasks/domain.md#taskprojected`, `.devbook/domain/tasks/domain.md#taskcompleted` | Monitoring & Dashboard |
| Tasks | `.devbook/domain/tasks/domain.md#aiworklogged` | Productivity |
| Roadmap Planning | `.devbook/domain/roadmap/domain.md#roadmapitemscheduled` | Monitoring & Dashboard |
| Roadmap Planning | `.devbook/domain/roadmap/domain.md#roadmap-item` | Tasks |
| Roadmap Planning | `.devbook/domain/roadmap/domain.md#roadmap-tag` | Tasks, Devbook |
| Devbook | The devbook convention (local ADR 0016) and the [planning fields](devbook/domain.md#shared-value-objects) a chapter carries | Brain, Roadmap Planning |
| Monitoring & Dashboard | `.devbook/domain/monitoring/domain.md#followupcaptured` | Inbox |
| Dev PC Management | `.devbook/domain/dev-pc-management/domain.md#machinestatuschanged`, `.devbook/domain/dev-pc-management/domain.md#complianceupdated` | Monitoring & Dashboard |
| Productivity | `.devbook/domain/productivity/domain.md#productivityrecorded` | Monitoring & Dashboard |
| Environment | `.devbook/domain/environment/domain.md#environment-shortcut-resolution` | Tasks, Dev PC Management |
| Technology Stack | `Technology Baseline` / `BaselineProvided` contract in `.devbook/domain/technology-stack/domain.md#deprecation-management` | Dev PC Management, Repository Management |
| Notifications | `INotifications`, `INotificationAnswerHandler` and `NotificationDraft` in `Backlog.Modules.Notifications.Abstractions`, under `.devbook/domain/notifications/domain.md#notification` (not built) | Sessions, Sync, Devbook, Tasks |
| Sync | The replica protocol: the routes and DTOs in `Backlog.Modules.Sync.Abstractions` (`SyncRoutes`, `DataTransferObjects`), under `.devbook/domain/sync/domain.md#change-precedence` | Tasks, Sessions, Devbook, Inbox |

## Strategic rules

- Only the owning context defines a published language. Consumers conform to that
  contract and do not reach into the supplier's internal aggregate shape.
- `Tasks` and `Roadmap Planning` split the word *priority* on
  purpose, and the split is load-bearing. Tasks owns a task's **status and
  execution priority**. Roadmap owns **planning priority and sequence**. Neither
  writes the other's value, and neither derives its own from the other. A roadmap
  item may name the task that executes it. A plan must also hold work that has no
  task yet, which is why the plan is stored rather than projected.
- `Inbox` publishes `ItemTriaged` and `Tasks` conforms to it, carried in-process
  rather than by an asynchronous event. The payload is a DTO in the Inbox's
  Abstractions, handed over the `IInboxBacklogTarget` port. An infrastructure
  adapter answers that port over Tasks' published `ITaskItems`. Neither module
  references the other, and the adapter is the only place an inbox item becomes
  entry text.
- A capture reaches the Inbox in one of two ways:
  - **From another device**, as a `capture`-kind document on the sync replica
    (`.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md`).
  - **From a source monitor on the desktop** (a YouTube channel, a website),
    in-process and without the replica. It delivers through the same
    `IInboxIntake` port the sync path hands `capture`-kind documents to.

  Both are keyed by the same deterministic per-entry id, so idempotency belongs
  to the port, not to the transport.
- `Tasks` and `Brain` are a deliberate `Partnership`: both
  sides keep only foreign ids and the link semantics are coordinated through the
  [Cross-Linking](brain/domain.md#cross-linking) service rather than a shared
  aggregate.
- `Brain` is downstream of `Devbook` as a Customer/Supplier, not a Shared Kernel
  (local ADR 0040). It conforms to the devbook convention and reads, indexes and
  renders its notes through Devbook's machinery; a need of its own enters through
  a hook Devbook offers, and Devbook knows no PARA, area or capture. Its repo links
  run one way, into a repository chapter: no repository devbook links into a
  Brain, and its backlinks are read from the owner's Brain, never written into
  the repository.
- `Roadmap Planning` **totals** what it does not **own**. Story-point `effort` is
  registered on Tasks and on devbook chapters. A
  [roadmap tag](roadmap/domain.md#roadmap-tag) is the vocabulary Tasks offers in
  its picker and a devbook chapter names in its `roadmap` list. Roadmap gathers
  tasks and chapters by tag and by direct reference, and adds up their registered
  effort. It registers no effort and defines no tag.
- `Technology Stack` is the standards supplier for both `Repository Management`
  and `Dev PC Management`; those contexts may cache or copy baselines locally,
  but they do not become the authority for baseline meaning.
- `Monitoring & Dashboard` is an observer/read context. Its only write-back into
  the core flow is the published `FollowUpCaptured` contract into `Inbox`.
- `Productivity` measures personal and AI-assisted work from published activity
  signals. It never changes backlog status, agent sessions (`Sessions`), or repository state.
- `Environment` owns the user's launchable environment shortcuts and access
  preferences. It resolves repository and health facts through suppliers instead
  of duplicating Repository Management or Monitoring data.
- `Sessions` is the single owner of the agent-session record. No other context
  models agent sessions. `Monitoring`'s own `copilot_session` signal kind gives way
  to this context once the two are wired together.
- `Sessions` publishes no bounded-context domain event. It owns no row of the
  published-language table above (it only uses Sync's replica protocol), and its Dev PC Management and Monitoring edges
  stay dashed. Two infrastructure adapters read it directly, as Customer/Supplier
  reads rather than published contracts:
  - for `Productivity`, the
    [Session Log](sessions/domain.md#session-log);
  - for `Tasks`, the [session state](sessions/domain.md#session-state) a task's
    work badge shows.

  That is why those two edges draw solid without a table row. When Monitoring
  consumes Sessions too, or Sessions publishes an event of its own, the event goes
  into `.devbook/domain/sessions/domain.md` and into that table in the same change.
- A `Sessions` [Environment](sessions/domain.md#environment) is not an
  [`Environment`-context Environment](environment/domain.md#environment), and not a
  `Dev PC Management` [Machine](dev-pc-management/domain.md#machine). The two words
  collide across three contexts and the concepts do not. Each context defines its
  own under `## Ubiquitous Language` in its `domain.md`, and no aggregate holds
  another context's identity for it. The Sessions Environment's `EnvironmentId` is
  the product-wide device identity issued by the shared kernel
  (`.devbook/domain/tasks/domain.md#device`), which no bounded context owns. That
  identity is still not Dev PC Management's Machine, so the rule stands unchanged.
- `Progress` names a task by its id, a plan by its
  [plan tag](roadmap/domain.md#roadmap-tag) and a session by its agent and session
  id, and reads each from its owner on every reading. `Tasks` holds no knowledge of goals or impediments: no task
  field, tag, status or entry-text token names one, and attaching a task to a goal
  changes nothing on it. Marking a task blocked raises an impediment because
  Progress reacts to the mark; Tasks does not call Progress and stores no
  impediment id
  (`.devbook/arc42/adr/0035-goals-and-impediments-replace-sprints.md`, which keeps
  the boundary of
  `.devbook/arc42/adr/0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md`).
  Progress publishes no event, so it has no row in the published-language table;
  its edges are Customer/Supplier reads. The Sessions edge is local ADR 0037's.
- `Sync` carries other contexts' documents without interpreting them.
  It stores each one whole under its [Owner](sync/domain.md#owner), orders
  pushed copies by stamp and tombstone
  ([Change Precedence](sync/domain.md#change-precedence)), and hands them back.
  It reads no payload field, and the merge into a device's store stays with the
  document's owner: Tasks, Sessions, Devbook, or the Inbox for a `capture`-kind
  document. A context that wants its documents on every device conforms to the
  replica protocol through an adapter in `Backlog.Infrastructure.Sync`; it never
  reaches into another context's documents on the replica.
- `Notifications` is downstream of `Sync` and upstream of every context that
  publishes (local ADR 0039). It conforms to the replica protocol for its
  `notifications` container, as Tasks, Sessions and Devbook do for theirs. The
  contexts that publish depend on `Backlog.Modules.Notifications.Abstractions`, the
  way they depend on `Tasks.Abstractions`, and never on its module. Notifications
  reads no publisher's data: the context that owns a subject reports the end
  through `CloseForSubjectAsync`, and answers a probe at startup. The pairing
  question makes `Sync` both its supplier and a publisher. The two edges run
  through different parts: the desktop sync client carries the documents, and the
  pairing flow publishes the question.
