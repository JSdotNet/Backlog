# 11. Risks and Technical Debt

```meta
status: draft
related: []
issue: null
```

Known open questions, risks, and debt carried by the current architecture. These are
mostly the "next steps" that must be resolved as the system moves from setup toward
MVP. Formally tracked debt should be promoted to TDRs (via `orch-tdr`) and linked
here rather than restated.

## Open Design Questions

```meta
status: draft
related: [".arc42/03-context-and-scope.md#business-context"]
issue: null
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
issue: null
```

| # | Risk | Mitigation direction |
|---|---|---|
| R5 | **Last-write-wins** edit conflicts can silently lose concurrent edits across devices. | Surface conflicts to the user; consider field-level merge for high-value fields. |
| R6 | Markdown-as-canonical + SQLite index can drift if writes are not transactional. | Define a rebuild/repair path for the SQLite index from markdown. |
| R7 | Multiple candidate stacks per channel (Electron vs. Tauri; RN vs. Flutter) are still open. | Decide and record via ADRs before deep implementation to avoid rework. |
| R8 | Local fetch workers hold external credentials on-device. | Use OS secure storage (Keychain/Vault); document a rotation/backup story. |

## Technical Debt

```meta
status: draft
related: []
issue: null
```

No implementation debt exists yet — the system is at the architecture-setup stage.
As code lands, record concrete debt items as TDRs and link them from this section.
