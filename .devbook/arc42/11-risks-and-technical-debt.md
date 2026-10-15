# 11. Risks and Technical Debt

```meta
status: draft
```

The open questions below are the "next steps" that must be resolved as the system
moves from setup toward MVP. Formally tracked debt should be promoted to TDRs
(through `flow-spec`) and linked here rather than restated.

## Open Design Questions

```meta
status: draft
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
status: draft
related: [".devbook/arc42/08-crosscutting-concepts.md#storage-and-sync", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".devbook/arc42/adr/0030-stored-credentials-go-through-the-os-protected-envelope.md"]
```

| # | Risk | Mitigation direction |
|---|---|---|
| R5 | **Last-write-wins** edit conflicts can silently lose concurrent edits across devices. Accepted deliberately in local ADR 0005, which resolves whole documents rather than fields. | Server-assigned ordering removes clock skew as a cause (see `.devbook/arc42/08-crosscutting-concepts.md#task-sync`), but the loss itself remains. Surface conflicts to the user; consider field-level merge for high-value fields. |
| R6 | The generated devbook database can lag the markdown it is built from. | A reader serves a drifted file from its markdown, and a background check rebuilds the database (`.devbook/arc42/08-crosscutting-concepts.md#devbook-database`). |
| R7 | Multiple candidate stacks are still open for some channels, especially mobile (.NET MAUI vs. Blazor PWA/Hybrid). The desktop client is intended to be Windows-only and close to the .NET stack. | Decide and record the remaining stack choices via ADRs before deep implementation to avoid rework. |
| R8 | The device holds external credentials: GitHub tokens, Claude and Azure Foundry keys, the sync device credential and the spec-manager tokens. | Local ADR 0030 seals every one with the OS: DPAPI under the signed-in user on Windows, the platform secure store on Android. A copied settings folder carries no usable key. Code running as the same user can still open them. Keys are not in the backup (ADR 0010). A new machine or a reset profile means entering them again, and rotation is done at the provider and pasted into Settings. The MCP loopback token stays plaintext by ADR 0012. |
| R9 | **File-syncing the local SQLite store corrupts it.** This has happened: a workspace root on OneDrive produced conflicted database copies and silently reverted committed status edits. The file is binary and unmergeable, and the WAL sidecars sync out of step with it. | Local ADR 0005 replaces file sync with the sync service. The service is built and provisioned by hand, but its delivery pipeline and a second device's real use are unproven (`.devbook/arc42/07-deployment-view.md#provisioning-and-delivery`). Keep the workspace root on a local disk; the Storage settings screen says so (D2). `SyncedFolderDetector` checks the root and the screen names the provider when the root sits inside OneDrive, OneDrive for Business, Dropbox, Google Drive, or iCloud Drive. The detector is heuristic and warns without preventing. It misses a provider folder moved away from `%USERPROFILE%`, Google Drive mounted as a drive letter, and non-Windows machines. Committing a new folder on the Storage screen moves the backlog: it copies `backlog.db` through SQLite's backup API, with everything beside it, before repointing. It refuses a folder that already holds a backlog and leaves the old folder for the user to delete. "Switch without moving" only repoints. A backup to a GitHub repository (local ADR 0010) makes the loss recoverable without lessening the hazard. A per-device database filename was rejected: it trades contention for two silently divergent backlogs (ADR 0005, *The database filename*). A second machine on a shared folder sees the first one's writes only at restart. `backlog.db` is the one database under the root, but the per-device settings and feature-flag JSON files remain there. |
| R10 | **A replica of personal task content moves into Azure.** Content that was purely local becomes cloud-resident, changing the threat model. | Partition-scoped device tokens, managed identity with a Cosmos data-plane role, and Key Vault. No account keys are issued. See local ADR 0005. |

## Technical Debt

```meta
status: draft
related: [".devbook/arc42/adr/guidelines/README.md"]
```

Debt against an inherited decision is recorded in that decision's own
**Deviations and gaps** section under `.devbook/arc42/adr/guidelines/`, next to the rule it
falls short of, rather than being copied here. Read those sections before
planning work on the sync service, persistence, or observability.

Debt that belongs to no single decision:

| # | Debt | Consequence |
|---|---|---|
| D1 | The organization's **coding and testing conventions** — C# style, unit/integration/e2e/architecture testing, object calisthenics, validation strategy, logging and audit logging — were left behind when the ADRs were imported on 2026-08-27. They are listed in `.devbook/arc42/adr/guidelines/README.md`. See [TDR 0002](tdr/0002-coding-and-testing-conventions-have-no-checked-in-home.md). | Agents and contributors have no checked-in statement of those conventions. They belong in `.agents/rules/`, where they load while code is being edited. |
| D2 | ~~The **Storage settings screen describes the pre-ADR-0003 world**~~ — **resolved.** The screen told the user every entry was a markdown file and invited them to point the folder at a synced one; since ADR 0003 the store is one binary SQLite file. The copy now states that the backlog is a single SQLite database, that it must stay on a local disk, and that a file-sync folder cannot merge it and will lose edits. | Kept in the table rather than deleted because this was not merely stale copy — it was the instruction that caused the data loss recorded in R9, and R9's own mitigation points here. `SettingsStorageCopyTests` pins both the warning and the absence of the retired advice, so it cannot come back unnoticed. |
| D3 | ~~**No migration mechanism for the local SQLite store**~~ — **story written, residual gap narrowed.** Local **ADR 0006** records what the mechanism is: additive, idempotent bootstrapping by the adapter on any open whose file is not stamped with the store's current schema definition, in three permitted shapes, with the boundary stated. `updated_at` and `deleted_at` landed through it, seeded from `created_at`. | The residual gap is now specific rather than general: **there is no mechanism for a non-additive change** — no schema version, no ordered step list, no record of which step has run, and no defined behaviour when an older build opens a newer database. ADR 0006 makes the first non-additive change the trigger for building one, and supersedes itself at that point. |
| D4 | **Archify artifacts are invisible on a branch-sourced repository.** The lazy branch snapshot (local ADR 0008, third amendment) excludes `_archify/` from the content fetch so that it can be fetched only when a rendered diagram is shown — but the on-demand half was never built, and `ArchifyDiagramArtifacts` reads the snapshot folder directly. See [TDR 0001](tdr/0001-archify-artifacts-are-invisible-on-a-branch-snapshot.md). Since [local ADR 0033](adr/0033-archify-renders-are-generated-on-demand-not-committed.md) the renders are not committed and the app draws them from the specifications, so `_archify/` holds only small JSON and the exclusion's cost premise is gone: fetching the folder with its area (TDR option C) is now the cheap fix. | Every diagram in a branch-sourced repository falls back to mermaid with no hint why; the out-of-date signal and the render/author affordances never appear. The same repository read from a clone shows all of it. |
| D5 | **The devbook check still prints warnings, and a set of prose findings stands deliberately** (2026-09-26, plan `devbook-adoption`, entry `fix-check-findings`). Both devbook checks exit 0 with nothing blocking, but still warn on the section headings inside decision and debt records and domain events, on Inbox requirements proved only by `unit` tests, and on terms two chapters claim. The prose findings left standing each have an owning plan entry. See [TDR 0003](tdr/0003-the-devbook-check-is-not-a-zero-warning-signal.md) for the counts and the list. | The check is not a zero-warning signal, so a new warning is found by comparing counts rather than by the exit code. The deferred prose is read as current by any agent loading those chapters until the owning entries land. |

As further concrete debt lands, record it as a TDR under `tdr/` and link it from this
section; `tdr/README.md` is the index.




