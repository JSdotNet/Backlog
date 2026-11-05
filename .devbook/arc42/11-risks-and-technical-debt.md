# 11. Risks and Technical Debt

```meta
```

The open questions below must be resolved as the system moves toward MVP. Formally tracked debt is promoted to TDRs (through `flow-spec`) and linked here.

## Open Design Questions

```meta
related: [".devbook/arc42/03-context-and-scope.md#business-context"]
```

| # | Question / gap | Risk if unresolved |
|---|---|---|
| R1 | Event and sync contracts between Technology Stack, Dev PC Management, Repository Management, and Monitoring are undefined. | Domains drift apart; ad-hoc integrations become hard to evolve. |
| R2 | Data-ownership boundaries for the machine registry and repository registry (desktop vs. optional cloud mirror) are unspecified. | Ambiguous source of truth; sync conflicts on registry data. |
| R3 | MVP scope per domain and per access channel is not yet defined. | Scope creep; unclear what "done" means for the first release. |
| R4 | Capture and Inbox are kept as one coupled pipeline. | A future need to own capture tooling independently forces a later, costly split. |

## Architectural Risks

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#storage-and-sync", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".devbook/arc42/adr/0030-stored-credentials-go-through-the-os-protected-envelope.md"]
```

| # | Risk | Mitigation direction |
|---|---|---|
| R5 | **Last-write-wins** edit conflicts can silently lose concurrent edits across devices. | Surface conflicts to the user; consider field-level merge for high-value fields. |
| R6 | The generated devbook database can lag the markdown it is built from. | A reader serves a drifted file from its markdown, and a background check rebuilds the database (`.devbook/arc42/08-crosscutting-concepts.md#devbook-database`). |
| R7 | Multiple candidate stacks are still open for some channels, especially mobile (.NET MAUI vs. Blazor PWA/Hybrid). | Decide and record the remaining stack choices via ADRs before deep implementation to avoid rework. |
| R8 | The device holds external credentials, listed below the table. | Local ADR 0030 seals every one with the OS. |
| R9 | **File-syncing the local SQLite store corrupts it.** | Local ADR 0005 replaces file sync with the sync service. Keep the workspace root on a local disk. |
| R10 | **A replica of personal task content moves into Azure**, which changes the threat model. | Partition-scoped device tokens, managed identity with a Cosmos data-plane role, and Key Vault. |

- **R5:** Local ADR 0005 accepts this because it resolves whole documents, not fields. Server-assigned ordering removes clock skew as a cause (`.devbook/arc42/08-crosscutting-concepts.md#task-sync`), but the loss remains.
- **R7:** The desktop client is intended to be Windows-only and close to the .NET stack.
- **R8:** The credentials are GitHub tokens, Claude and Azure Foundry keys, the sync device credential and the spec-manager tokens. The OS seal is DPAPI under the signed-in user on Windows and the platform secure store on Android. A copied settings folder carries no usable key, but code running as the same user can open them. Keys are not in the backup (ADR 0010), so a new machine or reset profile means entering them again. Rotation happens at the provider and is pasted into Settings. The MCP loopback token stays plaintext by ADR 0012.
- **R9:** This has happened. A workspace root on OneDrive produced conflicted database copies and silently reverted committed status edits.
  - The sync service replaces file sync. It is built and provisioned by hand, but its delivery pipeline and a second device's real use are unproven (`.devbook/arc42/07-deployment-view.md#provisioning-and-delivery`).
  - The Storage settings screen tells the person to keep the root on a local disk (D2). `SyncedFolderDetector` names the provider when the root sits inside a synced folder. It knows OneDrive (personal and business), Dropbox, Google Drive and iCloud Drive.
  - The detector is heuristic and warns without preventing. It misses a provider folder moved away from `%USERPROFILE%`, Google Drive mounted as a drive letter, and non-Windows machines.
  - Committing a new folder on the Storage screen copies `backlog.db` through SQLite's backup API, with everything beside it, before repointing. It refuses a folder that already holds a backlog and leaves the old folder for the user to delete. "Switch without moving" only repoints.
  - A backup to a GitHub repository (local ADR 0010) makes the loss recoverable without lessening the hazard.
  - A per-device database filename was rejected: it trades contention for two silently divergent backlogs (ADR 0005, *The database filename*).
  - A second machine on a shared folder sees the first one's writes only at restart. `backlog.db` is the one database under the root, but the per-device settings and feature-flag JSON files remain there.
- **R10:** Content that was purely local becomes cloud-resident. No account keys are
  issued. See local ADR 0005.

## Technical Debt

```meta
related: [".devbook/arc42/adr/guidelines/README.md"]
```

Debt against an inherited decision sits in that decision's own **Deviations and gaps** section under `.devbook/arc42/adr/guidelines/`. Read those sections before planning work on the sync service, persistence or observability.

Debt that belongs to no single decision:

| # | Debt | Consequence |
|---|---|---|
| D1 | The organization's **coding and testing conventions** were left behind when the ADRs were imported on 2026-08-27. | Agents and contributors have no checked-in statement of those conventions. |
| D2 | ~~The **Storage settings screen describes the pre-ADR-0003 world**~~ — **resolved.** | Kept in the table because this copy caused the data loss recorded in R9. |
| D3 | ~~**No migration mechanism for the local SQLite store**~~ — **story written, residual gap narrowed.** | **There is no mechanism for a non-additive change.** |
| D4 | **Archify artifacts are invisible on a branch-sourced repository.** | Every diagram in a branch-sourced repository falls back to mermaid with no hint why. |
| D5 | **The devbook check still prints warnings, and a set of prose findings stands deliberately** (2026-09-26). | The check is not a zero-warning signal, and agents read the deferred prose as current. |
| D6 | **The side panel reads sessions outside `ILinkedSessionStates`** (2026-11-02, the follow-up local ADR 0042 named). | One session's badge and side panel card can briefly disagree, and each refresh reads the record twice. |

- **D1:** The conventions cover C# style and testing (unit, integration, e2e, architecture). They also cover object calisthenics, validation strategy, logging and audit logging (`.devbook/arc42/adr/guidelines/README.md`). They belong in `.agents/rules/`, where they load while code is edited. See
  [TDR 0002](tdr/0002-coding-and-testing-conventions-have-no-checked-in-home.md).
- **D2:** The screen told the user every entry was a markdown file and invited them to point the folder at a synced one, though since ADR 0003 the store is one binary SQLite file. The copy now says the backlog is a single SQLite database that must stay on a local disk, and that a file-sync folder cannot merge it and will lose edits. It was the instruction that caused the loss in R9, whose mitigation points here. `SettingsStorageCopyTests` pins the warning and the absence of the retired advice.
- **D3:** Local **ADR 0006** records the mechanism: additive, idempotent bootstrapping by the adapter on any open whose file is not stamped with the store's current schema definition. It allows three shapes and states the boundary. `updated_at` and `deleted_at` landed through it, seeded from `created_at`. The residual gap: no schema version, no ordered step list or record of which step has run, and no defined behaviour when an older build opens a newer database. The first non-additive change triggers building one, and ADR 0006 supersedes itself then.
- **D4:** The lazy branch snapshot (local ADR 0008, third amendment) excludes `_archify/`
  from the content fetch so that it can be fetched only when a rendered diagram is
  shown. The on-demand half was never built; `ArchifyDiagramArtifacts` reads the snapshot folder directly. See
  [TDR 0001](tdr/0001-archify-artifacts-are-invisible-on-a-branch-snapshot.md). Since
  [local ADR 0034](adr/0034-archify-renders-are-generated-on-demand-not-committed.md) the
  renders are not committed and the app draws them from the specifications. `_archify/` therefore holds only small JSON and the exclusion's cost premise is gone, so fetching it with its area (TDR option C) is now the cheap fix. Meanwhile the out-of-date signal and the render/author affordances never appear, though a clone shows all of it.
- **D5:** This dates from plan `devbook-adoption`, entry `fix-check-findings`. Both devbook checks exit 0 with nothing blocking, but warn on the section headings inside decision and debt records and domain events, Inbox requirements proved only by `unit` tests, and terms two chapters claim. Each prose finding left standing has an owning plan entry; [TDR 0003](tdr/0003-the-devbook-check-is-not-a-zero-warning-signal.md) has the counts and list. A new warning is found by comparing counts, not by the exit code, and agents read the deferred prose as current until the owning entries land.
- **D6:** A task row's badge reads a linked session through Tasks' port, while the task's
  side panel reads the session record straight from the Sessions adapter in the shell.
  See [TDR 0004](tdr/0004-the-side-panel-reads-sessions-outside-ilinkedsessionstates.md)
  for the evidence and the options, none chosen.

New concrete debt is recorded as a TDR under `tdr/` and linked here; `tdr/README.md` is the index.




