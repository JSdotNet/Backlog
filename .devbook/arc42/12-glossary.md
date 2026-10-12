# 12. Glossary

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#shared-data-types", ".devbook/domain/context-map.md"]
```

Domain-specific vocabulary (bounded context names, aggregates, domain events) is
owned by `.devbook/domain` — see `.devbook/domain/context-map.md` for the subdomain
landscape and the `## Ubiquitous Language` section of a context's
`.devbook/domain/<context>/domain.md` for that context's canonical terms. This glossary
lists only system-wide architecture terms that don't belong to a single domain.

## Terms

```meta
related: [".devbook/domain/inbox/domain.md#note", ".devbook/domain/capture/features.md#talk-note", ".devbook/arc42/adr/0024-notes-ride-the-task-feed.md"]
```

| Term | Definition |
|---|---|
| **Local-first** | Design approach where the desktop app owns canonical data and works fully offline; the cloud is additive, never required. |
| **Standalone mode** | Desktop running without any cloud connection; all data local, GitHub sync direct via `gh` CLI. |
| **Connected mode** | Desktop additionally syncing state to the cloud for phone access and webhook forwarding. |
| **Dot-folder contract** | Scope-portable `.inbox/`, `.backlog/`, `.brain/` folders present at workspace, repo, and project levels. |
| **Fetch worker** | A local desktop background job that polls an external source (YouTube, website, email, GitHub). |
| **Thin cloud** | The principle that the cloud service only coordinates sync, forwards webhooks, pushes notifications, and hosts the PC registry — no domain data or fetching. |
| **Last-write-wins** | Conflict policy where the most recent edit prevails; new items always create rather than overwrite. |
| **WoL relay** | Wake-on-LAN relay in the cloud PC registry that can wake a registered sleeping/offline machine. |
| **Note** | An Inbox item of content kind `note`. It syncs both ways between the desktop and the phone, and either side may edit it. Not a *Knowledge Note*, which is a Devbook term. See `.devbook/domain/inbox/domain.md#note` and `.devbook/arc42/adr/0024-notes-ride-the-task-feed.md`. |
| **Talk note** | A note taken on the phone during a conference talk. The talk note was replaced by the note (`.devbook/domain/inbox/domain.md#note`): it is now a note written in the phone's Notes tab. Never called a *session note*: *session* already names a bounded context (Sessions, the record of an AI coding session), and the two must not share a word. See `.devbook/domain/capture/features.md#talk-note`. |

For bounded-context names (Capture, Inbox, Devbook, etc.) and their
business meaning, see `.devbook/domain/context-map.md#subdomain-landscape`.

## Shared Data Types

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#shared-data-types"]
```

Cross-container data types and the aggregate that owns their shape are listed
in `.devbook/arc42/08-crosscutting-concepts.md#shared-data-types`, which links out to
the owning chapter in `.devbook/domain` for each type instead of restating fields here.
