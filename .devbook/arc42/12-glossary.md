# 12. Glossary

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#shared-data-types", ".devbook/domain/context-map.md", ".devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md"]
```

`.devbook/domain` owns domain vocabulary (context names, aggregates, events): see `.devbook/domain/context-map.md#subdomain-landscape` for the landscape and the `## Ubiquitous Language` section of a context's `.devbook/domain/<context>/domain.md`. This glossary lists only system-wide architecture terms.

## Terms

```meta
related: [".devbook/domain/inbox/domain.md#note", ".devbook/domain/capture/features.md#talk-note", ".devbook/domain/brain/domain.md#brain", ".devbook/arc42/08-crosscutting-concepts.md#session-record-sync", ".devbook/arc42/adr/0025-notes-ride-the-task-feed.md", ".devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md", ".devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md"]
```

| Term | Definition |
|---|---|
| **Local-first** | The desktop app owns canonical data and works fully offline; the cloud is additive, never required. |
| **Standalone mode** | Desktop running without any cloud connection; all data local, GitHub sync direct via `gh` CLI. |
| **Connected mode** | Desktop additionally syncing state to the cloud for phone access and webhook forwarding. |
| **Brain** | The person's own notes, one folder per user outside every repository. |
| **Dot-folder contract** | Scope-portable `.inbox/` folders present at workspace, repo, and project levels. |
| **Fetch worker** | A local desktop background job that polls an external source (YouTube, website, email, GitHub). |
| **Thin cloud** | Coordinates sync, forwards webhooks, pushes notifications and hosts the PC registry; holds no domain data and fetches nothing. |
| **Last-write-wins** | Conflict policy where the most recent edit prevails; new items always create rather than overwrite. |
| **WoL relay** | Wake-on-LAN relay in the cloud PC registry that can wake a registered sleeping/offline machine. |
| **Note** | An Inbox item of content kind `note`, synced both ways and editable on either side. |
| **Readiness verdict** | The one word saying whether an open pull request is ready to merge, or what it waits on. |
| **Sanitization boundary** | The line between what a session record may carry off the machine and what stays. |
| **Settled-fact cache** | A per-machine copy of a provider's answer about something that cannot change again. |
| **Talk note** | A [note](../domain/inbox/domain.md#note) taken on the phone during a conference talk, written in the Notes tab. |

- **Brain:** also the scope of everything filed against no repository. The Brain context owns the term ([Brain](../domain/brain/domain.md#brain)).
- **Dot-folder contract:** a repository's devbook folders sit under `.devbook/` (`.devbook/arc42/adr/0016-knowledge-folders-adopt-the-devbook-convention.md`); the Brain is one folder per user outside every repository (`.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md`).
- **Note:** not a *Knowledge Note*, which is a Devbook term. See `.devbook/domain/inbox/domain.md#note` and `.devbook/arc42/adr/0025-notes-ride-the-task-feed.md`.
- **Readiness verdict:** The word is one of:
  - Conflicts
  - Failing checks
  - Changes requested
  - Behind
  - Draft
  - Checks running
  - Review required
  - Ready

  One function computes it from the pull request's status, and the pull requests pane, session card and task row all show it. Repository Management owns the term: see
  [One verdict per pull request](../domain/repository-management/features.md#one-verdict-per-pull-request)
  and `.devbook/arc42/adr/0042-repository-management-owns-pull-request-state.md`.
- **Sanitization boundary:** the record is a whitelist of fields. The transcript, prompts, tool output and file contents never cross. Local ADR 0005 decides each field; `.devbook/arc42/08-crosscutting-concepts.md#session-record-sync` lists them.
- **Settled-fact cache:** for example a merged pull request or a finished month's spend. It is refetched rather than synced, safe to delete, and never kept under the workspace root. See `.devbook/arc42/08-crosscutting-concepts.md#storage-and-sync`.
- **Talk note:** outbox kind `talk-note` sends a talk-note entry still waiting in the phone's outbox as a capture. It is never called a *session note*, because *session* already names a bounded context (Sessions, the record of an AI coding session). See `.devbook/domain/capture/features.md#talk-note`.

## Shared Data Types

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#shared-data-types"]
```

`.devbook/arc42/08-crosscutting-concepts.md#shared-data-types` lists cross-container data types and links each to its owning chapter in `.devbook/domain`.
