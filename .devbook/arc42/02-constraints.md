# 02. Constraints

```meta
```

These constraints are stable design boundaries, not reopened per feature.

## Technical Constraints

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#storage-and-sync", ".devbook/arc42/adr/0045-the-sync-service-relays-dictation-audio-to-foundry-and-keeps-none-of-it.md"]
```

| Constraint | Implication |
|---|---|
| **Local-first canonical storage** | The desktop's own local store is the single source of truth. |
| **The local store is never file-synced** | The workspace root is a local folder, and multi-device use goes through the sync service. |
| **A repository devbook stays markdown-canonical** | A repository's hand-edited devbook markdown remains the source of truth. |
| **Local-first, offline-capable** | All core workflows run without connectivity; the cloud is additive only. |
| **All capture runs locally** | Desktop workers poll YouTube, websites and email, so credentials stay on the machine. |
| **Cloud is a thin sync/coordination layer** | The cloud holds only sync state, webhook forwarding, push, and the machine registry. |
| **Scope-portable dot-folder contract** | `.inbox/` exists at workspace, repo, and project levels; the workspace-root `.tags/` holds shared tags and relationships. |
| **Cloud service targets the .NET stack** | It follows the organization's .NET guidance (`.devbook/arc42/09-architecture-decisions.md`); desktop, phone and IDE use their own stacks. |

- **All capture runs locally:** The one exception is the phone's opt-in Cloud dictation engine: the sync service turns a recording into text, and no external credential moves to a device (`.devbook/arc42/adr/0045-the-sync-service-relays-dictation-audio-to-foundry-and-keeps-none-of-it.md`).
- **Local-first canonical storage:** Tasks live in one SQLite database, and a task's content is markdown text inside it (`.devbook/arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md`). A second device's canonical copy reconciles through the cloud replica; neither the replica nor any other device becomes the system of record.
- **The local store is never file-synced:** A file-sync product such as OneDrive or Dropbox corrupts the SQLite database: the file is binary and unmergeable, and WAL sidecars sync out of step with it (`.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`). The same holds for every derived database: the Brain folder may live in a file-sync product, so nothing derived is written into it (`.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md`).
- **A repository devbook stays markdown-canonical:** The graph, reading outline, retrieval and diagram indexes are generated, never authoritative, and a reader falls back to the markdown when they are absent (`.devbook/arc42/adr/0004-knowledge-index-is-a-generated-local-database.md`).
- **Cloud is a thin sync/coordination layer:** This is the glossary's *Thin cloud* (`.devbook/arc42/12-glossary.md#terms`). It does no inbox fetching, domain CRUD or full-text search. A replica held only to reconcile devices counts as sync state: it enforces no invariant, answers no UI query and runs no domain logic (`.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`). Its one compute beyond coordination is a stateless transcription relay: it hands a phone dictation to Foundry and returns the text, keeping neither (`.devbook/arc42/adr/0045-the-sync-service-relays-dictation-audio-to-foundry-and-keeps-none-of-it.md`).
- **Scope-portable dot-folder contract:** A repository's devbook folders sit under
  `.devbook/` (`.devbook/arc42/adr/0016-knowledge-folders-adopt-the-devbook-convention.md`).
  The Brain is one folder per user, outside every repository
  (`.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md`).

## Organizational & Process Constraints

```meta
```

| Constraint | Implication |
|---|---|
| **No account required for personal use** | Standalone mode works without login; cloud connection uses device-based auth. |
| **GitHub as the external issue system** | Tasks sync to GitHub issues; the system integrates via `gh` CLI / GitHub API and webhooks. |
| **Personal, single-user only** | The documented scope is personal use; multi-user and collaborative workflows are outside the current architecture baseline. |
| **Governed guidance grounding** | Cloud-service decisions are grounded in the inherited organization ADRs under `.devbook/arc42/adr/guidelines/`. |

- **Governed guidance grounding:** those ADRs are checked in, and arc42 chapters link to a record rather than restating it.




