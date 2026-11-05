# Roadmap Planning

```meta
type: dependencies
related: [.devbook/domain/context-map.md]
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Note the DDD relationship pattern, integration mechanism, and
> published contract for each relationship.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| [Repository Management](../repository-management/domain.md#repository-registry) | Customer/Supplier (Roadmap Planning = customer) | Registry lookup by repository alias, on the read path | `.devbook/domain/repository-management/domain.md#repository` | Repository Scope aliases resolve to configured repositories, so a plan reads one project at a time. |
| [Tasks](../tasks/domain.md#task) | Partnership | Optional foreign-id cross-link, read-side gather by tag, and a read of imported plans (`IImportedPlanSource`) | `.devbook/domain/tasks/domain.md#task` | An item may name its executing task, gathers tasks by tag, and totals their registered effort. |
| [Sessions](../sessions/domain.md#agent-session) | Customer/Supplier (Roadmap Planning = customer) | Infrastructure adapter reads `IAgentSessionSource`; a separate port serves actual hours | `.devbook/domain/sessions/domain.md#activity-window`; for actual hours, [human turns](../sessions/domain.md#human-turn) and runs (`AgentActivityRun`) | Linked sessions date gathered tasks, and the axis draws working stretches and actual hours from them. |
| [Devbook](../devbook/domain.md#shared-value-objects) | Customer/Supplier (Roadmap Planning = customer) | Read-side gather by `<path>#<slug>` reference and by tag | `.devbook/domain/devbook/domain.md#shared-value-objects` | A Roadmap Item gathers chapters by reference and by tag, and totals their registered effort. |
| [Productivity](../productivity/context.md#working-week) | Shared Kernel (`WorkingHours` in `Backlog.SharedKernel`) | Reads the person's working week on the placement path; carries it in the synced `planning-pace` document | `.devbook/arc42/adr/0019-roadmap-counts-the-working-week.md` | An effort-sized window counts the person's working hours; a pace's week is that week's hours. |

- **Repository Management:** Roadmap conforms to the registry's identity and never
  becomes a second authority for what a repository is.
- **Tasks:**
  - The [shelf](features.md#laying-out-a-plan-whose-tasks-arrived-first) reads the
    imported plans the backlog holds: the tasks grouped by the plan they were
    imported with. It uses them to offer the plans no item carries the tag of.
  - Only ids cross, in both directions. Neither side holds the other's aggregate
    and neither writes to it, and Roadmap reads effort it never registers.
- **Sessions, dating a task:**
  - The infrastructure adapter reads `IAgentSessionSource` since a horizon, on the
    read path, only when a gathered task needs it.
  - [Roadmap Item Gathering](domain.md#roadmap-item-gathering) dates a gathered
    task that recorded no start, or finished without a tick, from the AI sessions
    linked to it: the earliest session start, and the latest last activity.
  - It joins them by the session ids the task carries, reads only, and never
    writes a date back to the task.
- **Sessions, actual hours (ADR 0019):**
  - A port in `Backlog.Modules.Roadmap.Abstractions` serves them, answered by an
    `Infrastructure.FileSystem` adapter over Sessions' agent-activity source and
    its cache. It has the same shape as `RoadmapItemRollupService`.
  - Each session's human turns and runs come from this machine and from the
    replicated records of the paired ones.
  - The axis draws the person's [working stretches](domain.md#working-stretch)
    from each session's human turns and runs, and counts the [actual
    hours](domain.md#actual-hours) of the days that have begun from them.
  - A session without human turns adds nothing.
  - A failed read leaves the begun [column heads](domain.md#column-head) blank,
    never the plan
    ([requirement](requirements.md#requirement-a-begun-head-shows-no-hours-when-actual-hours-cannot-be-read)).
- **Devbook:**
  - The chapters are the knowledge chapters an item references directly
    (`knowledge_refs`) and the chapters whose own `roadmap` list names its tag.
  - Roadmap only reads, on the read path. It resolves chapters by reference or tag
    and reads the effort they registered, and never writes a chapter or owns an
    effort value.
- **Productivity:**
  - The working week comes from the pace file's `workingWeek` key, else the
    device's `working-hours.json`, else the default week.
  - Productivity's settings screen sets the weekly pattern, and the roadmap sets
    the [day overrides](domain.md#day-override) from a [column
    head](domain.md#column-head) or the Days off dialog.
  - Roadmap carries both between devices so every device draws the same bars.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| [Monitoring & Dashboard](../monitoring/domain.md#progress-signal) | OHS + Published Language (Roadmap Planning = supplier) | Subscribes to `RoadmapItemScheduled` | `.devbook/domain/roadmap/domain.md#roadmapitemscheduled` | Planned windows, and the previous window being carried, to compare intent against delivery. |
| [Productivity](../productivity/features.md#hours-worked) | Shared Kernel (`WorkingHours` in `Backlog.SharedKernel`) | Reads the [day overrides](domain.md#day-override) inside the working week; reads Sessions itself for the stretches | `.devbook/domain/roadmap/domain.md#day-override`, `.devbook/domain/roadmap/domain.md#working-stretch` | The dashboard's Hours worked part applies the overrides and counts actual hours from working stretches. |
| [Tasks](../tasks/domain.md#task) | Partnership | Cross-link by foreign id | `.devbook/domain/roadmap/domain.md#roadmap-item` | `roadmap_item_id` staying stable across a reschedule. |
| [Tasks — Import](../tasks/features.md#import) | Customer/Supplier (Roadmap Planning = supplier) | A Tasks-side port (`IRoadmapPlanIntake`), answered by an infrastructure adapter calling Roadmap's own import command | `.devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md` | The item's tag being the plan's bare slug, and an import never deleting an item. |

- **Monitoring & Dashboard:** This breaks if the window stops being inclusive at
  both ends, or if the previous window is dropped.
- **Productivity:**
  - The Hours worked part applies the overrides to find each date's [office
    hours](../productivity/domain.md#office-hours), and counts [actual
    hours](domain.md#actual-hours) by this context's definition of a [working
    stretch](domain.md#working-stretch).
  - This breaks if an override stops being only blocked or unblocked, or if the
    stretch rule changes on one side alone.
- **Tasks:** A link made once keeps pointing at the same planned work.
- **Tasks — Import:**
  - The adapter calls the command once per Import run, after the task entries are
    written.
  - Import hands over the `plan` entries a document held and the plan tags its
    task entries carried.
  - Roadmap creates or updates one item per plan, [places
    it](features.md#placing-a-plan-in-time), and changes the length of items an
    import placed whose tasks changed.
  - Tasks conforms to what the command accepts. It holds no roadmap model and
    receives none back.

## Notes

- Repository aliases are held as **opaque strings**. Only
  [Repository Scope Resolution](domain.md#repository-scope-resolution)
  resolves them, and it does so on the read path. An unreachable or changed
  registry therefore degrades the reading of a plan, never the plan itself.
- The Partnership with Tasks is the same shape as the existing
  Tasks ↔ [Brain](../brain/domain.md#cross-linking)
  relationship: both sides keep only foreign ids, and the link semantics are
  coordinated rather than shared through an aggregate.
- The tag vocabulary flows **out** of this context. Its stability across a rename
  is the contract the Tasks picker and the devbook chapters' `roadmap` lists
  rest on ([Roadmap Tag](domain.md#roadmap-tag)). This context supplies the
  vocabulary and reads back what was filed under it. It does not learn the tag
  from either consumer.
- Both contexts use the word *priority*: **Tasks owns task status and task
  priority; Roadmap Planning owns planning priority and sequence.** Neither writes
  the other's value.
- Roadmap Planning [publishes and does not subscribe](features.md#publishing-planned-intent-for-observation).
  The inbound edge from Tasks' Import is **not** an exception to that. It is not a
  subscription, and Roadmap does not react to a task being created. A person
  pressing Import is a person changing the plan. The port carries that gesture,
  and a command in this context writes the plan exactly as a drag on the timeline
  does: being *told* by a person's command is how every edit reaches it.
- The tag is also how an imported plan and its item stay one thing without a
  link. The item holds the bare slug, and the plan's tasks carry it with the
  [Plan Sigil](domain.md#plan-sigil), which only the adapters on the two edges
  lift.
- There is no dependency on [Environment](../environment/domain.md#environment-catalog).
  Surfacing environment shortcuts beside planned work is
  [Environment's own feature](../environment/features.md#environment-aware-work-context),
  offered to whatever view asks for it, and does not put a Roadmap dependency in
  the model.
