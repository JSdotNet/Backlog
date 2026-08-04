# Flow: Backlog Management

> Lifecycle and process flows for this bounded context. Flows describe how the
> aggregate moves through its states over time — complementary to `model.md`
> (structure) and `domain.md` (responsibilities/invariants).

## Backlog entry lifecycle

```mermaid
stateDiagram-v2
    [*] --> Draft : Created
    Draft --> Ready : Refined and actionable
    Ready --> InProgress : Work started (EntryProjected)
    InProgress --> Done : Work completed (EntryCompleted)
    Done --> Archived : Archived
    Archived --> Draft : Restored
    Ready --> Draft : Revision needed
    InProgress --> Ready : Paused
    Done --> InProgress : Reopened
```

- The `Ready → InProgress` transition emits `EntryProjected` (one artifact per
  `repo_id`); `Done` emits `EntryCompleted` to close all projections.
