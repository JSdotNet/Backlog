# Architecture Decision Records

```meta
index: root
related: [".devbook/arc42/09-architecture-decisions.md", ".devbook/arc42/adr/guidelines/README.md"]
```

`.devbook/arc42/09-architecture-decisions.md` says which one governs which part of the system; this folder holds the records
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

```meta
```

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
- **[0012 — Backlog is an MCP server hosted inside the running desktop application](0012-backlog-is-an-mcp-server-inside-the-desktop-app.md)** *(accepted, partly built)*
- **[0013 — An imported plan is one Roadmap Item; a `plan` entry is the same grammar, and the importer places it](0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md)** *(accepted)*
- **[0014 — Attachments travel through a blob store beside the replica; the sync service is the only door](0014-attachments-travel-through-a-blob-store-beside-the-replica.md)** *(accepted, built)*
- **[0015 — The devbook database lives in the app's storage, one per repository path, and the app builds it](0015-devbook-database-lives-in-app-storage-and-the-app-builds-it.md)** *(accepted; supersedes parts of 0004)*
- **[0016 — The knowledge folders adopt the devbook convention under `.devbook/`; the derived layer stays a local build output](0016-knowledge-folders-adopt-the-devbook-convention.md)** *(accepted, built)*
- **[0017 — Inbox import is a capture source; its manifest is Markdown with front matter, not entry text](0017-inbox-import-is-a-capture-source-with-a-markdown-manifest.md)** *(accepted, not yet built)*
- **[0018 — The roadmap plan and the planning pace ride the task feed as two whole documents](0018-roadmap-plan-and-pace-ride-the-task-feed.md)** *(answers an open question in 0005, amends 0013)*
- **[0019 — The roadmap counts the person's working week in hours, and the week travels with the pace](0019-roadmap-counts-the-working-week.md)** *(proposed; amends 0013 and 0018)*
- **[0020 — Items from GitHub and spec-manager arrive as linked tasks, through one connector contract](0020-external-items-arrive-as-linked-tasks.md)**
- **[0021 — The GitHub accounts and the repository registry ride the task feed as two whole documents](0021-github-accounts-and-repository-registry-ride-the-task-feed.md)** *(accepted, built; amends 0005)*
- **[0022 — The desktop shell shows one main view, picked by a view switch, and takeovers replace it](0022-the-shell-shows-one-main-view-picked-by-a-view-switch.md)** *(accepted, built; §2's In progress view superseded by 0035)*
- **[0023 — Inbox items may be read by the Foundry model](0023-inbox-items-may-be-read-by-the-foundry-model.md)** *(accepted, built but for Ask again; reverses the "no model reads the item" line of the Inbox features)*
- **[0024 — Claude Code's telemetry arrives as OTLP/HTTP logs on the in-app MCP listener](0024-claude-code-telemetry-arrives-on-the-mcp-listener.md)** *(accepted; extends 0012)*
- **[0025 — Notes ride the task feed as a second Inbox document kind, synced both ways](0025-notes-ride-the-task-feed.md)** *(proposed, built; extends 0009)*
- **[0026 — The Brain is a personal devbook, one folder per user outside any repository](0026-the-brain-is-a-personal-devbook-outside-any-repository.md)** *(proposed, not yet built; builds on 0004, 0015 and 0016)*
- **[0027 — The Brain is a scope beside the repositories, the scope of everything filed against no repository](0027-the-brain-is-a-scope-beside-the-repositories.md)** *(proposed, not yet built; builds on 0026, amends 0022)*
- **[0028 — Progress is its own boundary, and Tasks knows no sprints](0028-progress-is-its-own-boundary-and-tasks-knows-no-sprints.md)** *(proposed, not yet built; sprint parts superseded by 0035)*
- **[0029 — An archived repository stays in the registry, and every consumer reads the flag](0029-an-archived-repository-stays-in-the-registry.md)** *(built, proposed; amends 0008, 0020, 0021 and 0027)*
- **[0030 — Every stored credential goes through the OS-protected envelope](0030-stored-credentials-go-through-the-os-protected-envelope.md)** *(proposed, built; the MCP token of 0012 is its one exception)*
- **[0031 — GitHub calls resolve their account per repository, and a token taken from the GitHub CLI is never stored](0031-github-calls-resolve-their-account-per-repository-and-never-store-a-gh-token.md)** *(proposed, built in PR #493; ADR 0021 syncs the accounts it resolves)*
- **[0032 — The My Day preferences ride the task feed as one whole document, and the phone folds it](0032-my-day-preferences-ride-the-task-feed.md)** *(accepted, not yet built; follows 0018 and 0021)*
- **[0033 — The Dashboard sends its figures to Foundry, whichever part was pressed](0033-the-dashboard-sends-its-figures-to-foundry-whichever-part-was-pressed.md)** *(accepted, built in PR #1316; widens what Ask AI sends, beside 0023)*
- **[0034 — Archify renders are generated on demand into app storage, not committed](0034-archify-renders-are-generated-on-demand-not-committed.md)** *(proposed, built; amends 0004 and 0008, changes the remediation of TDR 0001)*
- **[0035 — Goals and impediments replace sprints](0035-goals-and-impediments-replace-sprints.md)** *(proposed, not yet built; supersedes the sprint parts of 0022 §2 and 0028; §2, §8 and §9 amended by 0037; §7's GitHub edge amended by 0042)*
- **[0036 — The iteration settings ride the task feed as one whole document](0036-the-iteration-settings-ride-the-task-feed.md)** *(accepted, built; refines 0035 §3, follows 0032)*
- **[0037 — A session attaches to a goal as its own item](0037-a-session-attaches-to-a-goal-as-its-own-item.md)** *(proposed, not yet built; amends 0035 §2, §8 and §9)*
- **[0038 — Backlog queues session requests and a Manager session dispatches them](0038-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md)** *(proposed, not yet built; adds a route and a tool group to 0012's listener, beside 0024; wait route and capacity amended by 0041)*
- **[0039 — Notifications are an owned document kind on their own replica container, and the realtime channel only nudges](0039-notifications-are-an-owned-document-kind-and-push-only-nudges.md)** *(proposed, built and declared in Bicep, not deployed; extends 0005, follows 0011; MCP `ask`, `get_answer` and `close` amended by 0041)*
- **[0040 — The Brain is its own bounded context](0040-the-brain-is-its-own-bounded-context.md)** *(proposed, specification only; supersedes 0026 in part)*
- **[0041 — Agent questions reach Backlog by presence and come back through the hook or the Manager](0041-agent-questions-reach-backlog-by-presence-and-come-back-through-the-hook-or-the-manager.md)** *(proposed, not yet built; amends 0038 and 0039)*
- **[0042 — Repository Management owns pull-request state, and every reader goes through its one contract](0042-repository-management-owns-pull-request-state.md)** *(proposed, not yet built; amends 0035 §7's GitHub edge)*
- **[0043 — A surface can open in its own window, and modules register the surfaces the shell shows](0043-a-surface-can-open-in-its-own-window.md)** *(accepted, built; keeps 0022's one view per window and replaces its surface enums, adds routes and a tool to 0012's listener)*
- **[0044 — A devbook subject's workflow state lives in Backlog's own store, outside git, and replicates through its own container](0044-devbook-subject-state-lives-in-backlogs-own-store.md)** *(proposed, built; amends 0004, 0008, 0011, 0012 and 0029)*

## Inherited decisions

```meta
```

Indexed in **[guidelines/README.md](guidelines/README.md)**, which also records
what was deliberately *not* imported and why. Each document there ends with a
**Deviations and gaps** section stating where Backlog actually stands against the
rule — read that before planning work in its area.
