# TDR 0004: The side panel reads sessions outside ILinkedSessionStates

```meta
date: 2026-11-02
related: [".devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md#consequences", ".devbook/arc42/11-risks-and-technical-debt.md#technical-debt", ".devbook/domain/tasks/dependencies.md", ".devbook/domain/sessions/features.md#from-a-tasks-side-panel"]
severity: low
```

## Status

**Identified** — 2026-11-02, as the follow-up local ADR 0042 named under its
Consequences. No option below is chosen. The repository owner decides which one is
taken.

TDR lifecycle: identified → planned → in-progress → resolved.

## The debt

A task's linked session is read two ways in one window. The task row's badge reads it
through Tasks' own port, `ILinkedSessionStates`. The task's side panel, *Sessions and
pull requests*, reads the session record straight from the Sessions adapter in the
shell. One session can therefore draw from two reads of different depth and age.

| Reader | Through | Depth |
| --- | --- | --- |
| The task row's badge | `ILinkedSessionStates`, answered by `LinkedSessionStates` | timestamps only |
| The side panel, and the Goals view that shares its reader | `IAgentSessionSource`, read by the shell's `WorkInProgressReader` | the whole session |

Pull requests no longer work this way. Since local ADR 0042, the badge and the side
panel read one pull request through one contract, `IPullRequests`, and one cache.

## Evidence

The badge's read:

- The port is `LinkedSession(State, Archived)` and `ILinkedSessionStates`
  (`src/Modules/Tasks/Backlog.Modules.Tasks.Abstractions/Services/ILinkedSessionStates.cs:42,54`).
- `TasksDesktopState` takes it (`src/Modules/Tasks/Backlog.Modules.Tasks.UI/TasksDesktopState.cs:112,287,314`)
  and reads it for every linked session id (`:1294-1305`).
- `LinkedSessionStates` answers it at timestamps depth, through
  `AgentSessionQuery.Newest.TimestampsOnly()`
  (`src/Infrastructure/Backlog.Infrastructure.FileSystem/Tasks/LinkedSessionStates.cs:31,45`).
  `TasksCrossContextAdapterRegistration.cs:34` registers it.

The side panel's read:

- `WorkInProgressReader` takes `IAgentSessionSource` as a constructor parameter
  (`src/App/Backlog.Desktop.UI/Goals/WorkInProgressReader.cs:40-44`), and `From`
  resolves it from the container (`:55-65`).
- It reads every session at full depth through `sessions.GetSessionsAsync()` (`:188-198`).
- `LinkedWorkSection.razor:99-108` builds `WorkLinksProjection` from the reader's
  sessions, together with the task list's own rows (`State.Rows`) and its
  `ILinkedSessionStates` answer (`State.SessionOf`).
- `WorkLinksProjection.SessionsOf` (`WorkLinksProjection.cs:96-110`) draws the session
  the shell read when it has one. It falls back to the badge's answer otherwise.

The boundary tests allow this, because the shell is the composition root:
`ModuleBoundaryTests.A_module_ui_may_take_an_adapter_but_never_another_modules_implementation`
(`tests/Backlog.ArchitectureTests/ModuleBoundaryTests.cs:153`).

## Origin

Local ADR 0042 moved every pull-request read behind one contract, and Tasks' port
`ILinkedPullRequestStates` was shaped like `ILinkedSessionStates`. Sessions had no such
move. The side panel already read the session record directly before ADR 0042, and
that ADR left it so, naming it as a follow-up.

## Affected components

| Component | Role |
| --- | --- |
| `Backlog.Modules.Tasks.Abstractions` | Publishes `ILinkedSessionStates`. |
| `Backlog.Infrastructure.FileSystem/Tasks/LinkedSessionStates.cs` | Answers the port from the session record. |
| `Backlog.Modules.Tasks.UI/TasksDesktopState.cs` | Reads the port for the task row's badge. |
| `Backlog.Desktop.UI/Goals/` | `WorkInProgressReader`, `LinkedWorkSection.razor` and `WorkLinksProjection` read and draw the side panel. |

## Impact

- **Consistency.** The badge and the side panel card of one session can disagree for
  a moment: one shows a stalled session while the other shows it running, or one
  shows it finished while the other shows it archived.
- **Cost.** The session record is read twice per refresh, once per reader.
- **Coupling.** The side panel stays coupled to the Sessions adapter's surface rather
  than to Tasks' port.

## Severity

**Low.** The mismatch is short-lived and visual, no data is lost, and both reads are
of local files.

## Remediation options

### Option A — move the side panel onto `ILinkedSessionStates`

Widen `LinkedSession` with what the side panel card draws, such as its title, detail
and last activity, and have `WorkLinksProjection` draw from the port alone.

- *Pro:* one reading per session, through the port the badge already uses, as for
  pull requests.
- *Con:* widens a Tasks contract with fields only one screen draws, and makes the
  port's read deeper than timestamps.

### Option B — have one read answer both

Let `WorkInProgressReader`'s read answer `ILinkedSessionStates`' adapter too, so the
badge and the side panel share one read of the session record.

- *Pro:* one read per refresh, with no change to the port's shape.
- *Con:* ties the Tasks adapter to a reader that lives in the shell, and the read the
  badge needs becomes as deep as the side panel's.

### Option C — accept it and close the record

Treat the side panel's direct read as the composition root's prerogative, which the
boundary tests already allow.

- *Pro:* no change.
- *Con:* the two readings and the moment of disagreement stay.

## Measurable outcome

Resolved when one session's badge and side panel card draw from the same read, or when
the repository owner accepts the two reads under option C and says so here.

## Links

- Local ADR 0042, *Consequences* — the follow-up this record captures.
- `.devbook/arc42/11-risks-and-technical-debt.md`, *Technical Debt* — row D6.
- `.devbook/domain/tasks/dependencies.md` — Tasks' dependency on Sessions.
- `.devbook/domain/sessions/features.md#from-a-tasks-side-panel` — what the side panel
  shows of a session.
