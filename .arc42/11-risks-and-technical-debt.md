# 11. Risks and Technical Debt

```meta
status: draft
```

Known open questions, risks, and debt carried by the current architecture. These are
mostly the "next steps" that must be resolved as the system moves from setup toward
MVP. Formally tracked debt should be promoted to TDRs (via `orch-tdr`) and linked
here rather than restated.

## Open Design Questions

```meta
status: draft
related: [".arc42/03-context-and-scope.md#business-context"]
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
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

| # | Risk | Mitigation direction |
|---|---|---|
| R5 | **Last-write-wins** edit conflicts can silently lose concurrent edits across devices. Accepted deliberately in local ADR 0005, which resolves whole documents rather than fields. | Server-assigned ordering removes clock skew as a cause (see `.arc42/08-crosscutting-concepts.md#task-sync`), but the loss itself remains. Surface conflicts to the user; consider field-level merge for high-value fields. |
| R9 | **File-syncing the local SQLite store corrupts it.** Already realized, not hypothetical: a workspace root on OneDrive produced six conflicted database copies and silently reverted committed status edits, because the file is binary and unmergeable and the WAL sidecars sync out of step with it. | Local ADR 0005 replaces file sync with the sync service, which is not built yet — so this stays open. Until it ships, keep the workspace root on a local disk. The Storage settings screen now says so instead of advising the opposite (D2, resolved), which removes the instruction that caused the loss but not the hazard itself: a root already on OneDrive stays there until somebody moves it, and nothing in the app detects that. |
| R10 | **A replica of personal task content moves into Azure.** Content that was purely local becomes cloud-resident, changing the threat model. | Partition-scoped device tokens, managed identity with a Cosmos data-plane role, and Key Vault. No account keys are issued. See local ADR 0005. |
| R6 | Markdown-as-canonical + JSON indexes can drift if writes are not transactional. | Define a rebuild/repair path for the JSON indexes from markdown. |
| R7 | Multiple candidate stacks are still open for some channels, especially mobile (.NET MAUI vs. Blazor PWA/Hybrid). The desktop client is intended to be Windows-only and close to the .NET stack. | Decide and record the remaining stack choices via ADRs before deep implementation to avoid rework. |
| R8 | Local fetch workers hold external credentials on-device. | Use OS secure storage (Keychain/Vault); document a rotation/backup story. |

## Technical Debt

```meta
status: draft
related: [".arc42/adr/guidelines/README.md"]
```

Debt against an inherited decision is recorded in that decision's own
**Deviations and gaps** section under `.arc42/adr/guidelines/`, next to the rule it
falls short of, rather than being copied here. Read those sections before
planning work on the sync service, persistence, or observability.

Debt that belongs to no single decision:

| # | Debt | Consequence |
|---|---|---|
| D1 | The organization's **coding and testing conventions** — C# style, unit/integration/e2e/architecture testing, object calisthenics, validation strategy, logging and audit logging — were left behind when the ADRs were imported on 2026-08-27. They are listed in `.arc42/adr/guidelines/README.md`. | Agents and contributors have no checked-in statement of those conventions now that the guidelines MCP is no longer consulted. They belong in `.github/instructions/`, where they load while code is being edited. |

| D2 | ~~The **Storage settings screen describes the pre-ADR-0003 world**~~ — **resolved.** The screen told the user every entry was a markdown file and invited them to point the folder at a synced one; since ADR 0003 the store is one binary SQLite file. The copy now states that the backlog is a single SQLite database, that it must stay on a local disk, and that a file-sync folder cannot merge it and will lose edits. | Kept in the table rather than deleted because this was not merely stale copy — it was the instruction that caused the data loss recorded in R9, and R9's own mitigation points here. `SettingsStorageCopyTests` pins both the warning and the absence of the retired advice, so it cannot come back unnoticed. |
| D3 | ~~**No migration mechanism for the local SQLite store**~~ — **story written, residual gap narrowed.** Local **ADR 0006** records what the mechanism is: additive, idempotent bootstrapping by the adapter on every open, in three permitted shapes, with the boundary stated. `updated_at` and `deleted_at` landed through it, seeded from `created_at`. | The residual gap is now specific rather than general: **there is no mechanism for a non-additive change** — no schema version, no ordered step list, no record of what has run, and no defined behaviour when an older build opens a newer database. ADR 0006 makes the first non-additive change the trigger for building one, and supersedes itself at that point. |

As further concrete debt lands, record it as a TDR and link it from this section.




