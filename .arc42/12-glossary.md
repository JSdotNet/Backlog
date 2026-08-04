# 12. Glossary

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md#shared-data-types", ".domain/context-map.md"]
```

Ubiquitous terms used across Prompt Backlog. Domain-specific vocabulary (bounded
context names, aggregates, domain events) is owned by `.domain` — see
`.domain/context-map.md` for the subdomain landscape and each
`.domain/<context>/naming.md` for that context's canonical terms. This glossary
lists only system-wide architecture terms that don't belong to a single domain.

## Terms

```meta
status: active
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

For bounded-context names (Capture, Inbox, Second Brain, etc.) and their
business meaning, see `.domain/context-map.md#subdomain-landscape`.

## Shared Data Types

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md#shared-data-types"]
```

Cross-container data types and the aggregate that owns their shape are listed
in `.arc42/08-crosscutting-concepts.md#shared-data-types`, which links out to
the owning chapter in `.domain` for each type instead of restating fields here.
