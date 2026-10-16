# Sessions

```meta
type: dependencies
related: [.devbook/domain/context-map.md, .devbook/domain/dev-pc-management/domain.md#machine-registry, .devbook/arc42/08-crosscutting-concepts.md#session-record-sync, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Use explicit DDD relationship semantics, integration mechanism
> details, and contract references.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| Collections MCP | ACL (Sessions = customer) | Optional append/read of sanitized session-activity documents keyed by `Session Identity` | `.devbook/domain/sessions/domain.md#session-activity-stream`, `.devbook/domain/sessions/domain.md#session-activity-publishing` | To layer externally reported milestone activity over the locally read session record, without making the collection authoritative for whether a session exists. Sessions owns the meaning of the stream and translates to and from the MCP's stored shape. **Planned.** Missing configuration is ordinary. Reporting that is configured but unreachable reads as degraded rather than failed. |
| Dev PC Management | Customer/Supplier (Sessions = customer) | Machine-name lookup against the machine registry | `.devbook/domain/dev-pc-management/domain.md#machine-registry` | To say which registered machine an `Environment` corresponds to, when the two name the same box. **Not built**, because `src/` has no machine registry to ask. A session is stamped with the product-wide device identity, its id and name, issued by the shared kernel. A registry lookup would only correlate that device id with a registered Machine. Local ADR 0005 sends the machine's name on the replicated record, which meets the need without discharging it. That name is the sending machine's reading of itself when it last reported. So a box renamed since reads under its old name until it reports again, and a machine that has never reported has no name here. The cost is bounded because a section is keyed on the machine id, not the name: a stale label misnames a section and never splits or merges one. The row stays because only a registry could turn one of these names into a machine this product knows more about. |
| Repository Management | Conformist | None for the recorded string, which is the `owner/name` an agent wrote, held verbatim. One read-only lookup places a working folder inside a registered clone directory. | `.devbook/domain/repository-management/domain.md#repository-registry` | A session's `Working Location` names a repository in that context's form. Conformist and deliberately inert for the recorded string: it is displayed, never resolved, so a repository this product does not know about still reads correctly. The resolved repository is the one question asked of that context: which registered clone contains this folder. It is asked through a port that can only answer, never register. |
| [Sync](../sync/context.md) | OHS + Published Language (Sync = supplier) | `SessionSyncWorker` and `SessionSyncSession` in `Backlog.Infrastructure.Sync` push this machine's session records to the sync service and pull the other machines' into the replicated session store | `SessionRecord` and `SessionRecordEntry` in `Backlog.Modules.Sync.Abstractions`; `.devbook/domain/sync/domain.md#session-record-key`; local ADR 0005 §Session records | A session this context read on one machine reads again on the person's other machines. Sync keys a record by the machine that wrote it, so no machine overwrites another's, and carries only the whitelisted metadata. It never reads what a record says; what a session is stays with this context. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| Productivity | Customer/Supplier (Sessions = supplier) | Infrastructure adapters read the Session Log directly. `AgentSessionAssistantSessionSource` maps `IAgentSessionSource` onto Productivity's `IAssistantSessionSource`, and `AgentActivityAssistantActivitySource` maps `IAgentActivitySource` onto `IAssistantActivitySource`. | `.devbook/domain/sessions/domain.md#session-log` | AI-assisted work needs to know which agent worked where and for how long. **Built.** The session adapter supplies machine id, machine name, assistant, started at, last activity, the sources it could not read, and whether the read fell short of its horizon. It is a synchronous read of session facts since a horizon ([Session History](domain.md#session-history)), not a published event. It never uses the list's capped reading, because a count over that showed the session limit as every machine's total. Since records replicate, both ports also answer with sessions from the person's other machines, and the consumer reads them without a change on its side. A machine dimension keys on the machine id, and the name it maps is the one that travelled on the record. The figures on that side therefore widen when sync is switched on elsewhere. A record carries the runs and waits its gathering environment folded, so per-machine active and waiting time is measured for the other machines too, joined on the machine id. What a session's spawned agents did stays local, so concurrency figures remain figures about this box. The dashboard's [Hours worked](../productivity/features.md#hours-worked) part reads the same activity source through `IHoursWorkedSource` in `Backlog.Modules.Dashboard.Abstractions`, answered by an `Infrastructure.FileSystem` adapter. It reads each session's [human turns](domain.md#human-turn) and runs, and draws the person's [working stretches](../roadmap/domain.md#working-stretch) from them as Roadmap Planning does. It relies on the instant of every human turn, carried on a replicated record too (local ADR 0019). |
| Roadmap Planning | Customer/Supplier (Sessions = supplier) | Infrastructure adapters: `RoadmapItemRollupService` reads `IAgentSessionSource` since a horizon at the timestamps [reading depth](domain.md#reading-depth), so no transcript is opened for a date, joined on the session ids a task's projections carry; `RoadmapActualHours` reads `IAgentActivitySource` | `.devbook/domain/sessions/domain.md#activity-window`; for actual hours, `.devbook/domain/sessions/domain.md#human-turn` | A roadmap item's actual stretch falls back on the sessions linked to a task that recorded no start or no completion. **Built.** It relies on `started_at` and `last_activity_at` keeping their meaning, and on a session id being the same string the task was linked with. The horizon is the Session History retention, widened to the oldest such task's creation. A session no source still holds leaves the task's dates as Tasks recorded them. The roadmap axis reads the [actual hours](../roadmap/domain.md#actual-hours) of each day from each session's [human turns](domain.md#human-turn) and runs, and draws the person's working stretches from them. **Built** (local ADR 0019). It relies on a run marking a stretch in which an agent was producing, and on the activity record carrying the instant of every human turn. A replicated record carries those instants too, so a session on a paired machine counts. A session whose agent does not record who made a turn carries none, and Roadmap counts nothing for it. |
| Tasks | Customer/Supplier (Sessions = supplier) | Infrastructure adapter (`LinkedSessionStates`) maps `IAgentSessionSource`'s newest-per-agent reading, taken at the timestamps [reading depth](domain.md#reading-depth), onto Tasks' `ILinkedSessionStates`, joined on the session ids a task's projections carry | `.devbook/domain/sessions/domain.md#session-state` | A task's work badge shows the state of the sessions linked to it. **Built.** It relies on `running`, `stalled` and `finished` keeping their meaning, and on a session id being the same string the task was linked with. `stalled` means quiet past the threshold, which the badge words as "Quiet 30 min". A finished session old enough to have left the newest-per-agent reading is absent, and its badge draws without a state. A record replicated from another machine reads `finished` once quiet and never `stalled`, so only this machine's sessions are ever highlighted. |
| Monitoring & Dashboard | Customer/Supplier (Sessions = supplier) | Stalled-session observation | `.devbook/domain/sessions/domain.md#session-state` | Monitoring alerts on sessions that have gone quiet, which is exactly what `stalled` means here. **Not built.** Monitoring keeps its own `copilot_session` signal kind until it consumes this context instead. A record from another machine carries no liveness marker, so it reads `finished` once quiet and never `stalled`. An alert on `stalled` would therefore fire only for sessions on the box doing the alerting. A consumer that wanted more would have to say what evidence it thinks it has for a machine it is not sitting at. |

## Notes

- **This context publishes no bounded-context domain event.** The tables above say so
  rather than inventing one early. The planned Collections MCP integration is a
  module-level reporting path, not a published language into another bounded context.
  The consumers above read the Session Log through infrastructure adapters instead of
  subscribing to an event. When Monitoring is wired up, or Sessions begins publishing
  an event of its own, the event belongs in `domain.md` as a first-class chapter. It
  goes into the context map's published-language table at the same time.

- **`Environment` here is not the Environment context's Environment.** The two terms
  collide and the concepts do not; see [Environment](domain.md#environment).

- **The overlap with Dev PC Management is resolved, not tolerated.** Sessions models
  every agent session, and Dev PC Management models none. Its `dependencies.md`
  records this context as the dependent that took the subject.

- **No dependency here crosses a boundary without a published language or an
  anti-corruption layer.** The planned Collections MCP path is an ACL, so the storage
  module never becomes the authority for session meaning. The Repository Management
  row stays conformist because the string is held verbatim. The Dev PC Management row
  is a future lookup rather than a live call. The Productivity row is a direct
  Customer/Supplier read of the Session Log's own published shape
  ([Session Log](domain.md#session-log)), not a translation of it, so it needs no ACL
  either.

- **Replicating session records is the one Sync row above.** Local ADR 0005 carries a
  record this context already wrote to another of the person's machines, where this
  context reads it again. [Sync](../sync/context.md) is the supplier of that replica
  and of its protocol, and nothing else: it is asked for no fact about a session and
  learns none of this context's language. Replication is also not the Collections MCP;
  the [Session Log](domain.md#session-log) says why the two stay apart.
