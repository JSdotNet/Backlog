# 12. Glossary

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md#shared-data-types"]
```

Ubiquitous terms used across Prompt Backlog. Domain-specific vocabulary is owned per
bounded context in `.domain`; this glossary lists the system-wide terms and shared
data types. Keep it aligned with the `.domain` ubiquitous language.

## Terms

```meta
status: active
```

| Term | Definition |
|---|---|
| **Local-first** | Design approach where the desktop app owns canonical data and works fully offline; the cloud is additive, never required. |
| **Standalone mode** | Desktop running without any cloud connection; all data local, GitHub sync direct via `gh` CLI. |
| **Connected mode** | Desktop additionally syncing state to the cloud for phone access and webhook forwarding. |
| **Capture** | The domain covering all input sources that feed items into the system. |
| **Inbox** | The domain that triages, classifies, and routes captured items after arrival. |
| **Second Brain** | Knowledge domain organizing project knowledge using PARA (Projects, Areas, Resources, Archive). |
| **PARA** | Projects / Areas / Resources / Archive — the organizing structure for knowledge notes. |
| **Dot-folder contract** | Scope-portable `.inbox/`, `.backlog/`, `.brain/` folders present at workspace, repo, and project levels. |
| **Fetch worker** | A local desktop background job that polls an external source (YouTube, website, email, GitHub). |
| **Thin cloud** | The principle that the cloud service only coordinates sync, forwards webhooks, pushes notifications, and hosts the PC registry — no domain data or fetching. |
| **Last-write-wins** | Conflict policy where the most recent edit prevails; new items always create rather than overwrite. |
| **WoL relay** | Wake-on-LAN relay in the cloud PC registry that can wake a registered sleeping/offline machine. |

## Shared Data Types

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md#shared-data-types"]
```

| Type | Meaning |
|---|---|
| **InboxItem** | A captured, not-yet-routed item entering the inbox. |
| **BacklogEntry** | A refined work item, optionally spanning multiple repos and linked to GitHub issues. |
| **KnowledgeNote** | A knowledge/second-brain note linkable to backlog entries. |
| **ProgressSignal** | A detected progress/health signal feeding monitoring dashboards. |
| **RoutingRule** | A rule mapping source/tag patterns to a target repo. |
| **MachineRegistration** | A registered developer machine in the PC registry. |
| **RepositoryRegistration** | A tracked repository with local path and package manifests. |
| **TechBaseline** | A required tool/version baseline with rollout status. |
