# Sessions

```meta
type: dependencies
related: [.devbook/domain/context-map.md, .devbook/domain/sessions/domain.md#agent-session, .devbook/domain/dev-pc-management/domain.md#machine-registry, .devbook/arc42/08-crosscutting-concepts.md#session-record-sync, .devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md, .devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md]
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Use explicit DDD relationship semantics, integration mechanism
> details, and contract references.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| Claude desktop app (session store) | Conformist (Sessions = downstream, read-only) | `ClaudeDesktopSessionReader` reads the store's files read-only, once per reading. | None published. The format is the desktop app's private, undocumented one. | The only record of open sessions and sidebar names. **Built.** |
| Collections MCP | ACL (Sessions = customer) | Optional append/read of sanitized session-activity documents keyed by `Session Identity` | `.devbook/domain/sessions/domain.md#session-activity-stream`, `.devbook/domain/sessions/domain.md#session-activity-publishing` | To layer reported milestone activity over the local record. **Planned.** |
| Dev PC Management | Customer/Supplier (Sessions = customer) | Machine-name lookup against the machine registry | `.devbook/domain/dev-pc-management/domain.md#machine-registry` | To say which registered machine an `Environment` corresponds to. **Not built.** |
| Repository Management | Conformist | None for the recorded string. One read-only lookup places a working folder in a registered clone directory. | `.devbook/domain/repository-management/domain.md#repository-registry` | A session's `Working Location` names a repository in that context's form. |
| [Repository Management](../repository-management/domain.md#readiness-verdict), for pull request state | Customer/Supplier (Sessions = customer) | None in this module. The desktop shell asks Repository Management's contract for the card's verdict. | `.devbook/domain/repository-management/domain.md#readiness-verdict`, `.devbook/domain/repository-management/domain.md#pull-request-status` | A session's detail panel shows each produced pull request's verdict. |
| [Sync](../sync/context.md) | OHS + Published Language (Sync = supplier) | `SessionSyncWorker` and `SessionSyncSession` in `Backlog.Infrastructure.Sync` push and pull session records. | `SessionRecord` and `SessionRecordEntry` in `Backlog.Modules.Sync.Abstractions`; `.devbook/domain/sync/domain.md#session-record-key`; local ADR 0005 §Session records | A session this context read on one machine reads again on the person's others. |
| [Tasks](../tasks/features.md#filing-a-follow-up-after-an-entry) | Customer/Supplier (Sessions = customer) | `SessionValidationAnswers` hands Tasks the answer through `ITaskFollowUps`, in process. | `.devbook/domain/tasks/features.md#filing-a-follow-up-after-an-entry`, `.devbook/domain/tasks/requirements.md#filing-a-follow-up-after-an-entry` | An Approve + follow-up answer files one follow-up entry. **Built.** |

- **Claude desktop app, the read:** `ClaudeDesktopSessionReader` reads `%APPDATA%\Claude\claude-code-sessions\<account>\<org>\local_*.json`, the first store this product reads from another application's `%APPDATA%`. It never reads `scheduled-tasks.json` in the same folders, or `titleSource`, because the sidebar shows the title whichever source gave it. It keeps only these top-level properties:
  - `cliSessionId`;
  - `sessionId`;
  - `isArchived`;
  - `scheduledTaskId`;
  - `lastActivityAt`;
  - `title`.
- **Claude desktop app, the need:** The store alone says which sessions the desktop app holds open, which the Open view lists ([Session View](domain.md#session-view)), and what its sidebar calls each one. A session row takes that name, and the pane reads its purpose, plan tag and plan state from it ([Session Title Reading](domain.md#session-title-reading)). Without it the transcript's title is read, which seldom carries a prefix, so most rows read Other.
- **Claude desktop app, the failures:** A missing store is ordinary: another OS, or no desktop app. An absent property reads as null, never as a default, so a record with no archived flag is never open. A format change costs the Open view, the scheduled section and the sidebar names, which fall back to the transcript's titles, but not the rest of the reading. Some running sessions, such as side sessions, have no record and are never open. The archived flag, scheduled task and sidebar title never travel: local ADR 0005's whitelist is unchanged.
- **Collections MCP:** The activity layers over the locally read session record without making the collection authoritative for whether a session exists. Sessions owns the meaning of the stream and translates to and from the MCP's stored shape. Missing configuration is ordinary. Reporting that is configured but unreachable reads as degraded rather than failed.
- **Dev PC Management, the gap:** The lookup is not built, because `src/` has no machine registry to ask. The shared kernel stamps a session with the product-wide device identity, its id and name. A registry lookup would only correlate that device id with a registered Machine. Local ADR 0005 sends the machine's name on the replicated record, which meets the need without discharging it. The row stays because only a registry can turn a name into a machine this product knows more about.
- **Dev PC Management, the name:** That name is the sending machine's reading of itself when it last reported. A renamed machine reads under its old name until it reports again, and a machine that never reported has no name here. A section is keyed on the machine id, so a stale label misnames a section and never splits or merges one.
- **Repository Management, the recorded string:** It is the `owner/name` an agent wrote, held verbatim. The row is conformist and inert for this string: it is displayed, never resolved, so an unknown repository still reads correctly.
- **Repository Management, the resolved repository:** The one question asked of that context is which registered clone contains this folder. The port can only answer, never register.
- **Repository Management, pull request state:** A session's detail panel shows on each pull request it produced the same verdict the pull requests pane shows (local [ADR 0042 §6](../../arc42/adr/0042-repository-management-owns-pull-request-state.md#6-the-pull-requests-pane-and-the-session-card-read-the-contract)). A task's side panel reads its linked pull requests through the same contract and cache. Sessions holds only the pull request's number and references nothing of Repository Management, so the two form no cycle. The task card beside it comes from Tasks' lookup, which the shell asks too. `SessionPullRequestStatuses` draws the card and `WorkInProgressReader` reads the side panel, both in the shell and both through `IPullRequests`.
- **Sync:** The two classes push this machine's session records to the sync service and pull the other machines' into the replicated session store. Sync keys a record by the machine that wrote it, so no machine overwrites another's, and carries only the whitelisted metadata. It never reads what a record says.
- **Tasks, the call:** `SessionValidationAnswers` is the answer handler in `Backlog.Infrastructure.Sessions`. It hands Tasks the gated entry (by id, or by plan and item), the person's words and the notification id. Tasks publishes `ITaskFollowUps` in `Backlog.Modules.Tasks.Abstractions`, and neither module references the other.
- **Tasks, the answer:** An Approve + follow-up answer to the Personal Validation question files one follow-up entry after the gated entry, even when no orchestrator is running. Sessions interprets the answer and records it on the run; Tasks decides what the follow-up is born with. The follow-up is a task and never a session request (local ADR 0038 §1). When the gated entry is gone, the run records the approval with the person's words and no follow-up.

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| Productivity | Customer/Supplier (Sessions = supplier) | Infrastructure adapters read the Session Log directly. | `.devbook/domain/sessions/domain.md#session-log` | AI-assisted work needs to know which agent worked where and for how long. **Built.** |
| Roadmap Planning | Customer/Supplier (Sessions = supplier) | Infrastructure adapters `RoadmapItemRollupService` and `RoadmapActualHours` read the session and activity sources. | `.devbook/domain/sessions/domain.md#activity-window`; for actual hours, `.devbook/domain/sessions/domain.md#human-turn` | A roadmap item's actual stretch and each day's actual hours. **Built.** |
| Tasks | Customer/Supplier (Sessions = supplier) | `LinkedSessionStates` maps `IAgentSessionSource`'s newest-per-agent reading onto Tasks' `ILinkedSessionStates`. | `.devbook/domain/sessions/domain.md#session-state` | A task's work badge shows the state of the sessions linked to it. **Built.** |
| [Progress](../progress/domain.md#goal-attachment) | Customer/Supplier (Sessions = supplier) | In-process read of the Session Log by session identity, live and replicated records alike | `.devbook/domain/sessions/domain.md#session-identity`, `.devbook/domain/sessions/domain.md#session-state`, `.devbook/domain/sessions/domain.md#working-location` | A goal attaches a session as an item of its own (local ADR 0037). **Not built.** |
| Monitoring & Dashboard | Customer/Supplier (Sessions = supplier) | Stalled-session observation | `.devbook/domain/sessions/domain.md#session-state` | Monitoring alerts on sessions gone quiet, which is exactly what `stalled` means here. **Not built.** |

- **Productivity, the adapters:** `AgentSessionAssistantSessionSource` maps `IAgentSessionSource` onto Productivity's `IAssistantSessionSource`, and `AgentActivityAssistantActivitySource` maps `IAgentActivitySource` onto `IAssistantActivitySource`. The read is synchronous, of session facts since a horizon ([Session History](domain.md#session-history)), not a published event. It never uses the list's capped reading, because a count over that showed the session limit as every machine's total. The session adapter supplies machine id and name, assistant, started at and last activity, the sources it could not read, and whether the read fell short of its horizon.
- **Productivity, replicated records:** Since records replicate, both ports also answer with sessions from the person's other machines, with no change on the consumer's side. A machine dimension keys on the machine id, and its name is the one that travelled on the record. The figures therefore widen when sync is switched on elsewhere. A record carries the runs and waits its gathering environment folded, so per-machine active and waiting time is measured for other machines too. What a session's spawned agents did stays local, so concurrency figures remain about this machine.
- **Productivity, hours worked:** The dashboard's [Hours worked](../productivity/features.md#hours-worked) part reads the same activity source through `IHoursWorkedSource` in `Backlog.Modules.Dashboard.Abstractions`, answered by an `Infrastructure.FileSystem` adapter. It draws the person's [working stretches](../roadmap/domain.md#working-stretch) from each session's [human turns](domain.md#human-turn) and runs, as Roadmap Planning does. It relies on the instant of every human turn, carried on a replicated record too (local ADR 0019).
- **Roadmap Planning, the actual stretch:** A roadmap item's actual stretch falls back on the sessions linked to a task that recorded no start or no completion. `RoadmapItemRollupService` reads `IAgentSessionSource` since a horizon at the timestamps [reading depth](domain.md#reading-depth), so no transcript is opened, and joins on the session ids a task's projections carry. It relies on `started_at` and `last_activity_at` keeping their meaning, and on a session id being the same string the task was linked with. The horizon is the Session History retention, widened to the oldest such task's creation. A session no source still holds leaves the task's dates as Tasks recorded them.
- **Roadmap Planning, actual hours:** `RoadmapActualHours` reads `IAgentActivitySource`. The roadmap axis reads the [actual hours](../roadmap/domain.md#actual-hours) of each day from each session's [human turns](domain.md#human-turn) and runs (local ADR 0019). It relies on a run marking a stretch in which an agent was producing, and on the activity record carrying the instant of every human turn, as a replicated record does. A session whose agent does not record who made a turn carries none, and Roadmap counts nothing for it.
- **Tasks, the reading:** The adapter reads at the timestamps [reading depth](domain.md#reading-depth) and joins on the session ids a task's projections carry. It relies on `running`, `stalled` and `finished` keeping their meaning, and on a session id being the string the task was linked with.
- **Tasks, the badge:** `stalled` means quiet past the threshold, which the badge words as "Quiet 30 min". A finished session old enough to have left the newest-per-agent reading is absent, unless the Claude desktop app still holds it open, and its badge then draws without a state. A record replicated from another machine reads `finished` once quiet and never `stalled`, so only this machine's sessions are ever highlighted.
- **Progress:** It relies on `agent` plus `session_id` naming one session, and on the session id being the string a task was linked with. It also relies on the branch and resolved repository to find the pull request on the session's branch. It reads the state only to say whether the item is doing, never whether it is done.
- **Monitoring & Dashboard:** Monitoring keeps its own `copilot_session` signal kind until it consumes this context instead. A record from another machine carries no liveness marker, so it reads `finished` once quiet and never `stalled`. An alert on `stalled` would therefore fire only for sessions on the alerting machine; a consumer wanting more needs evidence for a machine it is not on.

## Notes

- **This context publishes no bounded-context domain event.** The planned Collections MCP integration is a
  module-level reporting path, not a published language into another bounded context.
  The consumers above read the Session Log through infrastructure adapters instead of
  subscribing to an event. When Monitoring is wired up or Sessions begins publishing
  an event, it belongs in `domain.md` as a chapter and in the context map's published-language table.

- **`Environment` here is not the Environment context's Environment.** The two terms
  collide and the concepts do not; see [Environment](domain.md#environment).

- **The overlap with Dev PC Management is resolved.** Sessions models
  every agent session, and Dev PC Management models none. Its `dependencies.md`
  records this context as the dependent that took the subject.

- **No dependency here crosses a boundary without a published language or an
  anti-corruption layer.** The planned Collections MCP path is an ACL, so the storage
  module never becomes the authority for session meaning. The first Repository
  Management row stays conformist because the string is held verbatim. The second
  reads Repository Management's published pull request contract, through the shell. The
  Dev PC Management row is a future lookup. The Productivity row is a direct Customer/Supplier read of the Session Log's own published shape
  ([Session Log](domain.md#session-log)), so it needs no ACL.

- **Replicating session records is the one Sync row above.** Local ADR 0005 carries a
  record this context already wrote to another of the person's machines, where this
  context reads it again. [Sync](../sync/context.md) supplies that replica
  and its protocol, and nothing else: it is asked for no fact about a session and
  learns none of this context's language. Replication is not the Collections MCP;
  the [Session Log](domain.md#session-log) says why.
