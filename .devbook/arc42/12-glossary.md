# 12. Glossary

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#shared-data-types", ".devbook/domain/context-map.md", ".devbook/arc42/adr/0027-the-brain-is-a-scope-beside-the-repositories.md"]
```

Domain-specific vocabulary (bounded context names, aggregates, domain events) is
owned by `.devbook/domain` — see `.devbook/domain/context-map.md` for the subdomain
landscape and the `## Ubiquitous Language` section of a context's
`.devbook/domain/<context>/domain.md` for that context's canonical terms. This glossary
lists only system-wide architecture terms that don't belong to a single domain.

## Terms

```meta
related: [".devbook/domain/inbox/domain.md#note", ".devbook/domain/capture/features.md#talk-note", ".devbook/domain/brain/domain.md#brain", ".devbook/arc42/08-crosscutting-concepts.md#session-record-sync", ".devbook/arc42/adr/0025-notes-ride-the-task-feed.md", ".devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md"]
```

| Term | Definition |
|---|---|
| **Local-first** | Design approach where the desktop app owns canonical data and works fully offline; the cloud is additive, never required. |
| **Standalone mode** | Desktop running without any cloud connection; all data local, GitHub sync direct via `gh` CLI. |
| **Connected mode** | Desktop additionally syncing state to the cloud for phone access and webhook forwarding. |
| **Brain** | The person's own notes, one folder per user outside every repository, and the scope of everything filed against no repository. The Brain context owns the term: see [Brain](../domain/brain/domain.md#brain). |
| **Dot-folder contract** | Scope-portable `.inbox/` folders present at workspace, repo, and project levels. A repository's devbook folders sit under `.devbook/` (`.devbook/arc42/adr/0016-knowledge-folders-adopt-the-devbook-convention.md`). The Brain is one folder per user outside every repository instead (`.devbook/arc42/adr/0026-the-brain-is-a-personal-devbook-outside-any-repository.md`). |
| **Fetch worker** | A local desktop background job that polls an external source (YouTube, website, email, GitHub). |
| **Thin cloud** | The principle that the cloud service only coordinates sync, forwards webhooks, pushes notifications, and hosts the PC registry — no domain data or fetching. |
| **Last-write-wins** | Conflict policy where the most recent edit prevails; new items always create rather than overwrite. |
| **WoL relay** | Wake-on-LAN relay in the cloud PC registry that can wake a registered sleeping/offline machine. |
| **Note** | An Inbox item of content kind `note`. It syncs both ways between the desktop and the phone, and either side may edit it. Not a *Knowledge Note*, which is a Devbook term. See `.devbook/domain/inbox/domain.md#note` and `.devbook/arc42/adr/0025-notes-ride-the-task-feed.md`. |
| **Sanitization boundary** | The line between what a session record may carry off the machine and what stays: the record is a whitelist of fields, and the transcript, prompts, tool output and file contents never cross. Local ADR 0005 decides each field; `.devbook/arc42/08-crosscutting-concepts.md#session-record-sync` lists them. |
| **Settled-fact cache** | A per-machine copy of a provider's answer about something that cannot change again, such as a merged pull request or a finished month's spend. It is refetched rather than synced, safe to delete, and never kept under the workspace root. See `.devbook/arc42/08-crosscutting-concepts.md#storage-and-sync`. |
| **Talk note** | A [note](../domain/inbox/domain.md#note) taken on the phone during a conference talk, written in the phone's Notes tab. Outbox kind `talk-note` sends a talk-note entry still waiting in the phone's outbox as a capture. Never called a *session note*: *session* already names a bounded context (Sessions, the record of an AI coding session), and the two must not share a word. See `.devbook/domain/capture/features.md#talk-note`. |

For bounded-context names (Capture, Inbox, Devbook, etc.) and their
business meaning, see `.devbook/domain/context-map.md#subdomain-landscape`.

## Shared Data Types

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#shared-data-types"]
```

Cross-container data types and the aggregate that owns their shape are listed
in `.devbook/arc42/08-crosscutting-concepts.md#shared-data-types`, which links out to
the owning chapter in `.devbook/domain` for each type instead of restating fields here.
