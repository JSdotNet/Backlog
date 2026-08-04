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
| **Markdown is the canonical format** | The desktop's local markdown files are the single source of truth; SQLite is a derived index (FTS, relationships). |
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
| **Governed guidance grounding** | Cloud-service decisions are grounded in the organization's ADRs via `jsdotnet-project-guidelines`; arc42 links to ADRs rather than restating them. |


