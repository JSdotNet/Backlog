# Architecture Decision Records

```meta
index: root
related: [".devbook/arc42/09-architecture-decisions.md", ".devbook/arc42/adr/guidelines/README.md"]
```

The decisions this architecture is built on. `.devbook/arc42/09-architecture-decisions.md`
says which one governs which part of the system; this folder holds the records
themselves.

They come from two authors, and are kept apart for that reason alone:

| Where | What | Numbering |
|---|---|---|
| `*.md` here | Decisions **Backlog took for itself** — specific to this product, taken by this repository. | Local, from 0001. |
| `guidelines/` | Decisions **Backlog inherited** from the organization, imported on 2026-08-27 and authoritative here since. | The organization's, from 0001. |

Both sequences start at 0001, so **always say which set you mean**: inherited ADR
0003 is Aspire, local ADR 0003 is SQLite as the canonical store.

A local ADR may deliberately override an inherited one. When it does it says so —
see `0002-backlog-module-owns-the-entry-text-language.md`, which takes a position
between inherited ADRs 0005 and 0009.

## Local decisions

- **[0001 — Desktop channel uses .NET MAUI Blazor Hybrid, not plain WinUI 3](0001-desktop-stack-maui-blazor-hybrid.md)**
- **[0002 — The Backlog module owns the entry text language](0002-backlog-module-owns-the-entry-text-language.md)**
- **[0003 — SQLite is the canonical local task store; markdown is the content](0003-sqlite-is-the-canonical-local-task-store.md)**
- **[0004 — One generated local database holds the derived knowledge layer; markdown stays canonical](0004-knowledge-index-is-a-generated-local-database.md)** *(proposed)*
- **[0005 — An Azure-hosted task replica carries multi-device sync; the local store stays canonical](0005-azure-hosted-task-replica-for-multi-device-sync.md)** *(accepted)*
- **[0006 — Additive, idempotent bootstrapping is the local store's migration mechanism](0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md)** *(proposed)*
- **[0007 — Import reuses the entry text grammar; a plan is multi-task entry text](0007-import-reuses-the-entry-text-grammar.md)** *(accepted)*
- **[0008 — The Devbook reads from a cached branch snapshot when there is no clone; only a clone is editable](0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md)** *(proposed)*
- **[0009 — Captures are a document kind on the replica; the desktop acknowledges by tombstone](0009-captures-are-a-document-kind-on-the-replica.md)** *(accepted)*
- **[0010 — A backup is the database committed to a GitHub repository, one way, on a schedule](0010-backup-is-the-database-committed-to-a-repository.md)** *(accepted)*
- **[0011 — Devbook annotations are the person's data, in a Backlog-owned store, replicated through a third container](0011-devbook-annotations-are-a-third-replica-container.md)** *(accepted)*
- **[0012 — Backlog is an MCP server hosted inside the running desktop application](0012-backlog-is-an-mcp-server-inside-the-desktop-app.md)** *(accepted, not yet built)*
- **[0013 — An imported plan is one Roadmap Item; a `plan` entry is the same grammar, and the importer places it](0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md)** *(accepted)*
- **[0014 — Attachments travel through a blob store beside the replica; the sync service is the only door](0014-attachments-travel-through-a-blob-store-beside-the-replica.md)** *(accepted, service side built)*
- **[0015 — The devbook database lives in the app's storage, one per repository path, and the app builds it](0015-devbook-database-lives-in-app-storage-and-the-app-builds-it.md)** *(accepted; supersedes parts of 0004)*
- **[0016 — The knowledge folders adopt the devbook convention under `.devbook/`; the derived layer stays a local build output](0016-knowledge-folders-adopt-the-devbook-convention.md)** *(accepted, partly built)*

## Inherited decisions

Indexed in **[guidelines/README.md](guidelines/README.md)**, which also records
what was deliberately *not* imported and why. Each document there ends with a
**Deviations and gaps** section stating where Backlog actually stands against the
rule — read that before planning work in its area.
