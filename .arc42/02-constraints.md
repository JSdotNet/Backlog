# 02. Constraints

```meta
status: active
```

Constraints the architecture must respect. They are stable design boundaries, not
decisions open for reconsideration per feature.

## Technical Constraints

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

| Constraint | Implication |
|---|---|
| **Local-first canonical storage** | The desktop's own local store is the single source of truth. Tasks live in one SQLite database; a task's content is markdown text inside it. See `.arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md`. |
| **Local-first, offline-capable** | All core workflows run without connectivity; the cloud is additive only. |
| **All capture runs locally** | YouTube, website, and email polling execute on the desktop via background workers, so external credentials stay on the user's machine. |
| **Cloud is a thin sync/coordination layer** | No inbox fetching, domain CRUD, or full-text search in the cloud — only sync state, webhook forwarding, push, and machine registry. |
| **Scope-portable dot-folder contract** | `.inbox/`, `.backlog/`, `.brain/` exist at workspace, repo, and project levels; shared tags/relationships in workspace-root `.tags/`. |
| **Cloud service targets the .NET stack** | The optional cloud service follows the organization's .NET guidance (see `.arc42/09-architecture-decisions.md`). Desktop, phone, and IDE use their own native/cross-platform stacks. |

## Organizational & Process Constraints

```meta
status: active
```

| Constraint | Implication |
|---|---|
| **No account required for personal use** | Standalone mode works without login; cloud connection uses device-based auth. |
| **GitHub as the external issue system** | Backlog entries sync to GitHub issues; the system integrates via `gh` CLI / GitHub API and webhooks. |
| **Personal, single-user only** | The documented scope is personal use; multi-user and collaborative workflows are outside the current architecture baseline. |
| **Governed guidance grounding** | Cloud-service decisions are grounded in the inherited organization ADRs checked in under `.arc42/guidelines/`; arc42 chapters link to a decision record rather than restating it. |




