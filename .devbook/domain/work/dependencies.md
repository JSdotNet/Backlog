# Work

```meta
type: dependencies
related: [.devbook/domain/context-map.md, .devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md, .devbook/arc42/adr/0050-events-is-its-own-bounded-context.md, .devbook/arc42/adr/0055-calendar-is-a-surface-of-its-own-that-reads-layers.md, .devbook/arc42/adr/0056-the-calendar-owns-its-grid-and-tasks-is-one-layer.md, .devbook/arc42/adr/0073-the-bounded-contexts-are-redrawn.md#1-work, .devbook/domain/calendar/context.md]
```

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the DDD relationship pattern,
> integration mechanism, and published contract for each relationship.

## Outbound dependencies

Work is the customer in every Customer/Supplier row. The task and the plan reach each
other inside the context, so no row names either; the [Notes](#notes) say how they
meet.

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| GitHub (external) | ACL (Work = supplier) | REST call via the Projection policy | `.devbook/domain/work/domain.md#projection` | Multi-repo tasks project to one GitHub issue per target repo; status syncs bidirectionally through an adapter. |
| Copilot CLI (external) | ACL (Work = supplier) | Command/task projection via the Projection policy | `.devbook/domain/work/domain.md#projection` | Entries can project to one CLI task per target repo without depending on CLI task internals. |
| [Brain](../brain/domain.md#knowledge-note) | Partnership | Id-based cross-link and read-side embedding | `.devbook/domain/brain/domain.md#cross-linking` | Entries embed or deep-link Knowledge Note content for context; each side keeps only foreign ids. |
| [Repositories](../repositories/domain.md#repository-registry) | Customer/Supplier (Work = customer) | Registry lookup by opaque id for a task, and by repository alias for a plan, on the read path; registration trigger | `.devbook/domain/repositories/domain.md#repository`, `.devbook/domain/repositories/features.md#repository-registration` | A task's `repo_ids` resolve to registered repos and their local clone paths, and a plan's Repository Scope aliases resolve to configured repositories, so a plan reads one project at a time. |
| [Repositories](../repositories/domain.md#pull-request-status), for pull request state | Customer/Supplier | Port `ILinkedPullRequestStates`, answered by an adapter over Repositories' contract | `.devbook/domain/repositories/domain.md#pull-request-status`, `.devbook/domain/repositories/domain.md#readiness-verdict`, `.devbook/domain/repositories/domain.md#merge-command` | A task row reads and acts on a linked pull request's state and verdict. |
| [Sessions](../sessions/domain.md#session-state) | Customer/Supplier | Port `ILinkedSessionStates`, answered by an adapter over `IAgentSessionSource`; an infrastructure adapter reads `IAgentSessionSource` for the plan, and a separate port serves actual hours | `.devbook/domain/sessions/domain.md#session-state`, `.devbook/domain/sessions/domain.md#activity-window`; for actual hours, [human turns](../sessions/domain.md#human-turn) and runs (`AgentActivityRun`) | A task's work badge colours a linked session by its state. Linked sessions date gathered tasks, and the roadmap's axis draws working stretches and actual hours from them. |
| [Devbook](../devbook/domain.md#shared-value-objects) | Customer/Supplier (Work = customer) | Read-side gather by `<path>#<slug>` reference and by tag | `.devbook/domain/devbook/domain.md#shared-value-objects` | A Roadmap Item gathers chapters by reference and by tag, and totals their registered effort. |
| [Sync](../sync/context.md) | OHS + Published Language (Sync = supplier) | `TaskSyncWorker` pushes and pulls the task feed; `TaskReplicaMerge` applies pulled documents | `TaskChange` and `TaskPayload` in `Backlog.Modules.Sync.Abstractions`; `.devbook/domain/sync/domain.md#change-precedence`; local ADR 0005 | A task reaches the person's other devices as one whole document, and a push never moves it backwards. The plan and the pace ride the same feed (ADR 0018). |

- **Brain:** Queries can span both contexts.
- **Repositories, for the registry:** Importing a plan that names an
  unregistered repository triggers registration rather than failing. Work conforms
  to the registry's identity and never becomes a second authority for what a
  repository is.
- **Repositories, for pull request state:** A task row's badge, its merge
  offer and a prompt entry's move to Done read the state and verdict. The row merges
  and switches auto-merge through the same port (local
  [ADR 0042 §4](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#4-tasks-reads-pull-requests-through-its-own-port)).
  The port is shaped like `ILinkedSessionStates`. The adapter is
  `LinkedPullRequestStates`, under `Infrastructure.FileSystem/Tasks/`.
  - Work keeps its own small record: the state, the verdict's label and tone, and
    for a merge the base and default branch.
  - That a pull request merged is Repositories' fact, read through the port's `Kept` from the first frame.
  - Which of a task's entries a merge has finished stays with Work, in `IPullRequestFinishMemory`.
  - A host that composes no Repositories answers nothing, and the badge
    draws as it did before.
- **Sessions, the work badge:** The badge highlights a stalled session as the state that needs the
  person ("Quiet 30 min"), draws a running one neutral, and mutes a finished one.
  - A finished session also says whether the Claude desktop app still holds it:
    Open in muted ink ("finished, still open") or Archived in quiet ink
    ("finished, archived"). Work reads this from the desktop app's records as
    `LinkedSession(State, Archived)`, and a session with no desktop record draws as
    before.
  - Work keeps only the session id. It reads state and archived fact on every load and never stores them. A session the record no longer holds draws with no state.
- **Sessions, dating a task:**
  - The adapter reads `IAgentSessionSource` since a horizon, on the read path, only when a gathered
    task needs it.
  - [Roadmap Item Gathering](domain.md#roadmap-item-gathering) dates a gathered
    task that recorded no start, or finished without a tick, from its linked AI
    sessions: the earliest start and the latest last activity.
  - It joins by the session ids the task carries and never writes a date back.
- **Sessions, actual hours (ADR 0019):**
  - A port in `Backlog.Modules.Roadmap.Abstractions` serves them, answered by an
    `Infrastructure.FileSystem` adapter over Sessions' agent-activity source and
    its cache, shaped like `RoadmapItemRollupService`.
  - Human turns and runs come from this machine and from the replicated records of
    the paired ones. A session without human turns adds nothing.
  - The axis draws the [working stretches](domain.md#working-stretch) and counts
    the [actual hours](domain.md#actual-hours) of begun days from them.
  - A failed read leaves the begun [column heads](domain.md#column-head) blank,
    never the plan
    ([requirement](requirements.md#requirement-a-begun-head-shows-no-hours-when-actual-hours-cannot-be-read)).
- **Devbook:** A plan resolves the chapters an item references directly
  (`knowledge_refs`) and those whose `roadmap` list names its tag. It reads their
  registered effort on the read path, and never writes a chapter or owns an effort value.
- **Sync:** The feed moves through the sync service, and `TaskReplicaMerge` lives in
  `Backlog.Infrastructure.Sync`. Sync stores the document without reading it. What a
  task is, and how a pulled copy merges into the local store, stay with Work
  ([Multi-device sync](features.md#multi-device-sync)).

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` | `.devbook/domain/inbox/domain.md#itemtriaged` | Work creating a draft task from a triaged item. |
| [Brain](../brain/domain.md#knowledge-note) | Partnership | Bi-directional link by id | `.devbook/domain/brain/domain.md#cross-linking` | Notes link to tasks and tasks link back; either can spawn the other without a shared aggregate. |
| [Insights](../insights/features.md#personal-productivity-dashboard) | OHS + Published Language (Work = supplier) | Intended subscription to status, projection and scheduling events; Work emits none yet | `.devbook/domain/work/domain.md#statuschanged`, `.devbook/domain/work/domain.md#taskprojected`, `.devbook/domain/work/domain.md#taskcompleted`, `.devbook/domain/work/domain.md#occurrencespawned`, `.devbook/domain/work/domain.md#roadmapitemscheduled` | Work-state and projection-state changes, occurrence provenance, and planned windows with the previous window carried, to compare intent against delivery. |
| [Insights](../insights/domain.md#productivity-ledger) | OHS + Published Language (Work = supplier) | Subscribes to `AIWorkLogged` | `.devbook/domain/work/domain.md#aiworklogged` | AI-assisted activity evidence linked to a task. |
| [Insights](../insights/features.md#hours-worked) | Shared Kernel (`WorkingHours` in `Backlog.SharedKernel`) | Reads the [working week](context.md#working-week) and the [day overrides](domain.md#day-override) inside it; reads Sessions itself for the stretches | `.devbook/domain/work/context.md#working-week`, `.devbook/domain/work/domain.md#day-override`, `.devbook/domain/work/domain.md#working-stretch` | The dashboard's hour grids outline the working week, and its Hours worked part applies the overrides and counts actual hours from working stretches. |
| [Sessions](../sessions/context.md) | Customer/Supplier (Work = supplier) | Calls a capability Work publishes in `Backlog.Modules.Tasks.Abstractions`, from `SessionValidationAnswers` in `Backlog.Infrastructure.Sessions`, in process | `.devbook/domain/work/features.md#filing-a-follow-up-after-an-entry`, `.devbook/domain/work/requirements.md#filing-a-follow-up-after-an-entry` | Work filing exactly one `ready` prompt after the gated entry per notification id. |
| [Events](../events/context.md) | Customer/Supplier (Work = supplier) | `IEventTaskTarget` in Events' abstractions, answered by `EventsTaskTarget` in `Backlog.Infrastructure.FileSystem` over `ITaskItems` and `ITaskChangeSignal`, in process | `.devbook/domain/events/domain.md#harvest-task-policy`; local [ADR 0050](../../arc42/adr/0050-events-is-its-own-bounded-context.md) §3 | Work filing a task from its text, under a caller's id only if no task holds it, live or deleted; reading a task by id; and ticking one off. |
| [Calendar](../calendar/context.md) | Customer/Supplier (Work = supplier) | `ICalendarTasks` and `ICalendarPlans` in the Calendar's abstractions, answered in `Backlog.Infrastructure.FileSystem/Calendar/`: `TasksCalendarTasks` over `ITaskItems`, `ITaskStore` and `ITaskChangeSignal`, and `RoadmapCalendarPlans` over the plan, in process; `NoCalendarTasks` in a host without the task module | Work's published contracts; local [ADR 0056](../../arc42/adr/0056-the-calendar-owns-its-grid-and-tasks-is-one-layer.md) §2 | The Tasks layer: the tasks with a due date in a window, their planned hours and the change signal; setting a task's due date, and its hours on a day. The Plans layer: the plans, the shelf and the working week. |
| [Goals](../goals/context.md) | Customer/Supplier (Work = supplier) | Reads a task by id and a plan by its tag, through Goals' own ports | `.devbook/domain/work/domain.task.md#task`, `.devbook/domain/work/domain.plan.md#roadmap-tag` | A task's status, due date, effort, blocked mark and linked pull requests; the plan tag vocabulary, the plan order and the pace. Work knows no goals. |
| Desktop shell (composition root), for Repositories' pull requests pane and Sessions' detail panel | OHS + Published Language (Work = supplier) | A lookup port in the task module's abstractions, `ITaskLookup`, from a session id or a pull request to its task | `.devbook/domain/work/domain.task.md#projection-ref` | A link being the `session` or `pull-request` Projection Ref a task carries. |

- **Insights:** Not built for the events. State changes would feed the GitHub-sync
  comparison, and occurrence provenance lets it read a recurring task as one series.
  The scheduling event breaks if the window stops being inclusive at both ends, or if
  the previous window is dropped.
- **Insights, the working week:**
  - The Hours worked part applies the overrides to give each date its [office
    hours](../insights/domain.md#office-hours), and its [actual
    hours](domain.md#actual-hours) follow Work's [working
    stretch](domain.md#working-stretch).
  - This breaks if an override stops being only blocked or unblocked, or if the
    stretch rule changes on one side alone.
- **Sessions:** The prompt lands in the gated entry's plan and repository. One prompt
  per notification id means an answer handed over twice files one follow-up.
  **Built**: `ITaskFollowUps`, implemented by the `FileFollowUp` feature.
- **Calendar:** Neither module references the other, and Work's UI registers nothing for
  the Calendar. No filter, open task or selection crosses: the Calendar reads the tasks
  under the window's repository scope alone. The due date is written through
  `ITaskItems.SetDueAsync` (ADR 0056 §2). Pressing a task on the Calendar opens it in the
  Tasks view by the `tasks` surface id and its `entry` key (local ADR 0043 §3). No Work
  type holds an event id.
- **Desktop shell:** These find their task through the port
  (local [ADR 0042 §9](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#9-tasks-publishes-a-lookup-from-a-session-or-a-pull-request-to-its-task)):
  - the pane's Mine view;
  - the task card on a pull request;
  - the task card on a session;
  - a task's side panel.

## Notes

- **The task and the plan meet inside Work** (local ADR 0073 §1). Until the modules
  merge into `Backlog.Modules.Work`, their meeting points are adapters in
  `Backlog.Infrastructure.FileSystem` between `Backlog.Modules.Tasks` and
  `Backlog.Modules.Roadmap`, and they fold into the module when it merges:
  - **Task link by id.** A Roadmap Item may name a task by one optional
    foreign id, held on the item. The plan never writes a task's status or priority,
    and a link made once keeps pointing at the same planned work: `roadmap_item_id`
    stays stable across a reschedule. The link may dangle: deleting a task must not
    corrupt a plan.
  - **Tag vocabulary.** The task's tag picker offers every roadmap tag
    (`IRoadmapTagSource`). A roadmap tag is a slug a task conforms to, not one it
    defines, which is why renaming a roadmap item's title must not change it. The
    picker reads the tags again whenever the plan changes, so it and the Goals view's
    attach search offer an item just planned without a reload. Its stability across a
    rename is also what the devbook chapters' `roadmap` lists rest on
    ([Roadmap Tag](domain.plan.md#roadmap-tag)).
  - **Gathering.** A plan gathers tasks by tag and by link and totals their
    registered `effort`; the task registers the effort and files the tag. The
    [shelf](features.md#laying-out-a-plan-whose-tasks-arrived-first) reads the
    imported plans the backlog holds, the tasks grouped by their import plan, to
    offer the plans no item carries the tag of (`IImportedPlanSource`).
  - **Import.** The task's Import hands its `plan` entries and the plan tags the
    tasks carried to the plan's own import command (`IRoadmapPlanIntake`), once per
    Import run, after the task entries are written. The plan creates or updates one
    item per plan, [places it](features.md#placing-a-plan-in-time), and changes the
    length of items an import placed whose tasks changed. An import never deletes an
    item.
  - **My Day.** A plan's date lives on its Roadmap Item and is read by the one
    [My Day](domain.md#my-day) rule a task's date is read by.
- *Priority* is split inside Work: **a task owns its status and execution
  priority; a plan owns planning priority and sequence.** Neither writes or
  derives the other's value
  ([the invariant](domain.plan.invariants.md#invariant-planning-priority-never-touches-the-task)).
- The plan [publishes and does not subscribe](features.md#publishing-planned-intent-for-observation).
  Import is no exception: the plan does not react to a task being created. A person
  pressing Import is a person changing the plan, and a command writes the plan
  exactly as a drag on the timeline does.
- The tag is also how an imported plan and its item stay one thing without a
  link. The item holds the bare slug of the tag, and the plan's tasks carry it with the
  [Plan Sigil](domain.md#plan-sigil), which only the adapters on the two edges
  lift.
- Keep `repo_ids` and repository aliases as **opaque strings** so Work does not
  couple to Repositories' internals. Only the Projection policy resolves `repo_ids`
  for GitHub/CLI, and only
  [Repository Scope Resolution](domain.md#repository-scope-resolution) resolves the
  aliases, on the read path, so an unreachable registry degrades the reading of a
  plan, never the plan.
- Import triggers registration, when a plan names a repository the registry lacks, through Repositories' own capability. Work, the customer, therefore
  gains no authority over what a registered repository holds.
- There is no dependency on a repository's [environments](../repositories/domain.md#environment):
  a plan reads the registry for its scope alone, and shows no launch link.
- GitHub issue sync runs both ways. Mismatch detection, such as the backlog saying
  done while the issue is open, is owned jointly with Insights, which does not show it yet.
- Treat the `ItemTriaged` payload as Inbox's stable published language.
- Task-to-task dependencies add no cross-context relationship. `depends_on` holds
  Task ids only, and readiness is derived on the task rather than published. A plan's
  dependencies are a separate graph over planned work, not the same edge seen twice.
- `OccurrenceSpawned` links a completed occurrence to its successor as provenance,
  not ownership: each occurrence is a separate task and a series is not one work item.
- Pull requests run both ways through two different parts (local ADR 0042). Work reads a pull request's state through its own port, and an adapter translates Repositories' contract into it. The desktop shell asks Work's lookup which task a pull request belongs to, never Repositories. Neither module references the other, so the two form no cycle.
- The working week is Work's own: it is set on the Working week settings page, travels
  as the `workingWeek` key in the synced `planning-pace` document, and is held in the
  shared kernel as `WorkingHours`, which Insights reads. The pace file's key wins,
  then the device's `working-hours.json`, then the default week.
