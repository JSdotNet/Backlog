# Backlog

[![Release desktop](https://github.com/JSdotNet/Backlog/actions/workflows/release-desktop.yml/badge.svg)](https://github.com/JSdotNet/Backlog/actions/workflows/release-desktop.yml)
[Latest release](https://github.com/JSdotNet/Backlog/releases/latest)

A personal work management system built for AI-driven development. Capture work items, prompts, and knowledge across projects, organize them through an inbox-first workflow, and access them where the work happens: desktop, IDE, and phone.

## Current state

Backlog is a local-first, AI-first work management product: desktop, mobile, and IDE
channels plus a thin cloud sync service. It runs fully standalone on a single desktop and
optionally connects to that sync service for multi-device sync.

The desktop app is released as a signed MSIX and the Android app as a signed APK — see
[Installing the desktop app](#installing-the-desktop-app) and
[Installing the Android app](#installing-the-android-app). Of the primary domains below,
Capture, Inbox, Tasks, Roadmap Planning, Devbook, Monitoring and Dashboard, Dev PC
Management and Sessions each have a module under `src/Modules/`; the others are described
in the devbook and have no module yet. The [project table](#solution-structure) lists
every project on `main`.

## Why

AI-driven development generates a different kind of work artifact: prompts, sessions, decisions, and context that traditional task trackers weren't designed for. Without a structured system, these slip through chat history, scattered notes, and one-off files. This project treats AI work artifacts as first-class items — versioned, searchable, linked to projects and work items, and available wherever you work.

## What it does

### Capture

Get ideas and work items in quickly from any context — mobile speech shortcuts, web clipper, email, IDE, or manual entry. The goal is zero friction between thought and storage.

### Inbox and triage

All captured items land in a shared inbox. Triage classifies, enriches, and routes each item to the right destination: active backlog, project knowledge, or archive. Nothing is lost; everything is intentional.

### Backlog management

Refine and prioritize work items linked to projects and GitHub repositories. Items can carry AI context — the prompt that created them, the session they belong to, decisions made along the way.

### Prompt library

Prompts are stored, versioned, and linked to the project and work item they belong to. One-click copy delivers a prompt directly to your active tooling. Usage is tracked so high-value prompts surface again when they are relevant.

### Devbook

Project knowledge, cross-project notes, and reference material are organized in a PARA-aligned structure. AI sessions and decisions are stored alongside the work they informed.

### Monitoring and dashboards

Progress signals pulled from GitHub, Application Insights, and queue stats give a live view of what is moving and what is blocked — per project and across the portfolio.

### Technology and operations

The system also tracks technology stack baselines, repository health, and development machine compliance so planning and execution stay connected to operational reality.

## Domains and channels

Primary domains:
- Capture
- Inbox
- Tasks
- Roadmap Planning
- Devbook
- Productivity
- Environment
- Monitoring and Dashboard
- Technology Stack
- Dev PC Management
- Repository Management
- Sessions

Access channels:
- Desktop client
- IDE extensions (VS Code, Visual Studio)
- Phone app

See [`.devbook/domain/context-map.md`](.devbook/domain/context-map.md) for functional boundaries and
[`.devbook/arc42/`](.devbook/arc42/) for technical design.

## Solution structure

`src/` holds shipping code only. It is laid out as a modular monolith:
`src/Core/` for the shared kernel and the shared UI component library,
`src/Modules/<Context>/` for one vertically sliced module per bounded context,
`src/Infrastructure/` for cross-cutting adapters that no single module owns,
`src/App/` for the channel front ends, and `src/Aspire/` for orchestration.

A module folder is named after what the module owns, and its projects carry that
name — so the sync service is `src/Modules/Sync/Backlog.Modules.Sync.Api`, not a
folder named after where it happens to be deployed. A module that is exposed over
HTTP owns its own `.Api` project, which is a host: nothing else in the solution
references it. A module with a desktop face owns a `.UI` project beside its other
projects — `src/Modules/Inbox/Backlog.Modules.Inbox.UI` and its siblings — so a
context's screens ship with the context instead of inside the shell.

`Sync` is the one module that is not a bounded context from
[`.devbook/domain/context-map.md`](.devbook/domain/context-map.md). It owns no domain — it
pairs devices and carries between them the replicas other contexts sync: tasks, session
records, devbook annotations, and Inbox captures with their attachments. If it ever grows
rules of its own, it needs an entry in the context map before it grows projects.

Development-time hosts live under `src/Harness/` so runnable project hosts stay below
`src/`, and automated test projects live in `tests/`.

| Project | Channel / role |
|---|---|
| `src/Aspire/Backlog.Aspire.AppHost` | .NET Aspire app model that composes all channels |
| `src/Aspire/Backlog.Aspire.ServiceDefaults` | Shared OpenTelemetry, resilience, and service discovery defaults |
| `src/Core/Backlog.SharedKernel` | Shared kernel — `Result`, `Result<T>`, and `Error` primitives used by every module |
| `src/Core/Backlog.UI.Components` | Shared Razor control library — no domain in it, rendered on its own in the storybook |
| `src/Modules/Tasks/Backlog.Modules.Tasks` | Tasks module — domain model (`TaskItem`, sub-items, lifecycle rules), the `ITaskRepository` port, and vertical-slice features |
| `src/Modules/Tasks/Backlog.Modules.Tasks.Abstractions` | The Tasks module's published surface — DTOs, the entry text format, and `ITaskItems` |
| `src/Modules/Tasks/Backlog.Modules.Tasks.UI` | Tasks's desktop face — the task pane, its state, and the GitHub and Copilot CLI projections |
| `src/Modules/Inbox/Backlog.Modules.Inbox` | Inbox module — the Inbox Item, List and Group aggregates, the `IInboxItemRepository` and `IInboxOrganizerRepository` ports, and vertical-slice features (intake, capture, triage, route to backlog, create plan, organiser) |
| `src/Modules/Inbox/Backlog.Modules.Inbox.Abstractions` | The Inbox module's published surface — DTOs, `IInboxItems`, and the `IInboxIntake`, `IInboxCaptureOutbox`, `IInboxBacklogTarget` and `IInboxPlanDrafter` ports |
| `src/Modules/Inbox/Backlog.Modules.Inbox.UI` | Inbox's desktop face — the pane, its side menu of lists and groups, and the per-kind detail view |
| `src/Modules/Capture/Backlog.Modules.Capture` | Capture module — the capture run over the monitored sources, the `ICaptureSourceAdapter` and `ICaptureDelivery` ports, and the deterministic capture ids that keep a rerun from capturing twice |
| `src/Modules/Capture/Backlog.Modules.Capture.Abstractions` | The Capture module's published surface — the source settings, the run result and run log DTOs, `ICaptureRunner`, `ICaptureRunLog` and `ICaptureSourceSettings` |
| `src/Modules/Capture/Backlog.Modules.Capture.UI` | Capture's desktop face — the sources panel behind the Inbox's Capture button |
| `src/Modules/Devbook/Backlog.Modules.Devbook.Abstractions` | Devbook's published surface — `IDevbookFolderSource`, the configured-folder format, and the location a folder resolves to |
| `src/Modules/Devbook/Backlog.Modules.Devbook.UI` | Devbook's desktop face — the Devbook menu and the arc42, domain, design, technology, and instruction panels |
| `src/Modules/Roadmap/Backlog.Modules.Roadmap` | Roadmap module — the plan and its items, the sequencing rules between them, and the `IRoadmapPlanRepository` port |
| `src/Modules/Roadmap/Backlog.Modules.Roadmap.Abstractions` | The Roadmap module's published surface — the plan DTOs, `IRoadmapPlanning`, and `RoadmapFeatures` |
| `src/Modules/Roadmap/Backlog.Modules.Roadmap.UI` | Roadmap Planning's desktop face — the band above the panes and its editor |
| `src/Modules/Sessions/Backlog.Modules.Sessions.Abstractions` | Sessions' published surface — the session record, its states and groupings, and the `IAgentSessionSource` port |
| `src/Modules/Sessions/Backlog.Modules.Sessions.UI` | Sessions' desktop face — the full-screen session list over the readers `Backlog.Infrastructure.Sessions` provides |
| `src/Modules/DevPc/Backlog.Modules.DevPc.Abstractions` | Dev PC Management's published surface — `DevPcFeatures` and the types its screens exchange |
| `src/Modules/DevPc/Backlog.Modules.DevPc.UI` | Dev PC Management's desktop face — the tools surface |
| `src/Modules/Dashboard/Backlog.Modules.Dashboard` | Dashboard module — the derivations behind the dashboard: productivity scoring, weekly bucketing, churn rates, month-to-date spend, and the session cache in front of the providers |
| `src/Modules/Dashboard/Backlog.Modules.Dashboard.Abstractions` | The Dashboard module's published surface — the scope, the insight DTOs, `IProductivityInsights` and `ICostInsights`, and the four ports its adapters answer |
| `src/Modules/Dashboard/Backlog.Modules.Dashboard.UI` | The Dashboard's face — the full-screen surface, its seven independent parts, and the adapters over GitHub and Anthropic |
| `src/Infrastructure/Backlog.Infrastructure.Sqlite` | Cross-cutting adapter — the canonical local store, one SQLite database holding the tasks behind `ITaskRepository` and the roadmap plan behind `IRoadmapPlanRepository` as a single document row. Two tables with an owner each: they share the file, not the schema. See [ADR 0003](.devbook/arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md) |
| `src/Infrastructure/Backlog.Infrastructure.Devbook` | Cross-cutting adapter — the generated devbook database: the reference graph, reading outline, chapter text, full-text index and Archify rows. The app builds it itself, one per repository path, into its own storage rather than any repository, and every read degrades to the Markdown rather than failing. See [ADR 0004](.devbook/arc42/adr/0004-knowledge-index-is-a-generated-local-database.md) and [ADR 0015](.devbook/arc42/adr/0015-devbook-database-lives-in-app-storage-and-the-app-builds-it.md) |
| `src/Infrastructure/Backlog.Infrastructure.DevPc` | Dev PC Management's adapter — the one `IDevToolService` both hosts compose: the desktop app runs the Copilot, Claude, dotnet and winget CLIs through it, and the web harness uses its catalog-only configuration, which reads the catalog JSON and starts no process. The command lines it runs and the refresh ordering live beside it |
| `src/Infrastructure/Backlog.Infrastructure.FileSystem` | Cross-cutting adapter — the JSON on local disk: the workspace settings and feature flags behind `ITaskStore`, `IDevbookFolderSource` and `IAppFeatureSettings`, all per-device and deliberately unsynced. Also the roadmap plan's two cross-context joins, which are lookups rather than storage |
| `src/Infrastructure/Backlog.Infrastructure.Claude` | Cross-cutting adapter — Claude usage and spend from the Anthropic organization APIs |
| `src/Infrastructure/Backlog.Infrastructure.Copilot` | Cross-cutting adapter — starting the GitHub Copilot CLI from a Backlog workflow |
| `src/Infrastructure/Backlog.Infrastructure.AzureFoundry` | Cross-cutting adapter — the Azure Foundry chat client behind the AI assistant |
| `src/Infrastructure/Backlog.Infrastructure.GitHub` | Cross-cutting adapter — GitHub issue projection, pull request and issue activity with review detail, Copilot seats, and AI-credit billing |
| `src/Infrastructure/Backlog.Infrastructure.Capture` | Cross-cutting adapter — the YouTube channel and website feed monitors behind `ICaptureSourceAdapter`, fetched through the named `capture-feeds` HTTP client with its own resilience pipeline, delivering new entries straight into the Inbox over `IInboxIntake` |
| `src/Infrastructure/Backlog.Infrastructure.Mcp` | Cross-cutting adapter — the read-only MCP tools the desktop app and its harness publish, with no transport under them. See [ADR 0012](.devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md) |
| `src/Infrastructure/Backlog.Infrastructure.Sync` | Cross-cutting adapter — the client side of sync: device pairing and credentials, and the task, session and annotation sync workers with their local sync state |
| `src/Infrastructure/Backlog.Infrastructure.Cosmos` | Cross-cutting adapter — the sync service's Cosmos DB replicas behind the Sync module's ports: tasks, session records, annotations, devices and pairing codes. See [ADR 0005](.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md) |
| `src/Infrastructure/Backlog.Infrastructure.BlobStorage` | Cross-cutting adapter — the sync service's attachment store, one private blob container behind `IAttachmentStore`. See [ADR 0014](.devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md) |
| `src/Infrastructure/Backlog.Infrastructure.SpecManager` | Cross-cutting adapter — the spec-manager connector for linked tasks: OAuth sign-in with PKCE on a loopback redirect, the refresh token kept under DPAPI, a thin REST client with its own resilience pipeline, and the mapping of a product's backlog items onto `SourceItem`. See [ADR 0020](.devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md) |
| `src/Infrastructure/Backlog.Infrastructure.Sessions` | Cross-cutting adapter — the Sessions module's disk adapters: the Claude and Copilot session and transcript readers, agent activity, the delivery-run stores and their telemetry, and the Archify runner that draws a run |
| `src/App/Backlog.Desktop.UI` | Desktop shell — layout, routes, settings, and the composition that decides which context panes are on screen |
| `src/App/Backlog.Desktop.Composition` | What both desktop heads compose — every module, adapter, Ask AI source and settings page behind one `AddDesktopComposition` call, with `DesktopCompositionOptions` for what the MAUI head and the web harness do differently |
| `src/App/Backlog.Desktop` | Desktop channel — .NET MAUI Blazor Hybrid (Windows) |
| `src/App/Backlog.Mobile.UI` | Shared Razor components for the mobile channel |
| `src/App/Backlog.Mobile` | Mobile channel — .NET MAUI Blazor Hybrid (Android) |
| `src/App/Backlog.Ide.VsCode` | IDE channel — VS Code extension (TypeScript) |
| `src/Modules/Sync/Backlog.Modules.Sync` | Sync module — device pairing, the push and pull features for each replica, the replica, device, pairing-code and attachment-store ports, and in-memory adapters for them |
| `src/Modules/Sync/Backlog.Modules.Sync.Abstractions` | The Sync module's published surface — the wire contracts, routes, claims and error codes the client and the service share |
| `src/Modules/Sync/Backlog.Modules.Sync.Api` | Sync module's API — thin ASP.NET Core sync service, deployed to Azure |
| `src/Modules/Sync/Backlog.Modules.Sync.UI` | Sync module's desktop face — the Devices page on the settings screen: registering and pairing this device, the sync service address, and the task and session sync loops |
| `src/Harness/Backlog.Desktop.WebHarness` | **Test harness, not shipped** — Blazor Server host of `Backlog.Desktop.UI` for Aspire/Playwright |
| `src/Harness/Backlog.Mobile.WebHarness` | **Test harness, not shipped** — Blazor Server host of `Backlog.Mobile.UI` at phone width |
| `src/Harness/Backlog.UI.Storybook` | **Test harness, not shipped** — the shared control library rendered on its own, with each page's governing `.devbook/design` rule beside it |
| `src/Harness/Backlog.AzureFoundry.TestService` | **Test harness, not shipped** — a stand-in for Azure Foundry so the assistant can be driven without a cloud account |
| `tests/Backlog.SharedKernel.UnitTests` | Unit tests for the shared kernel — the AI content budget |
| `tests/Backlog.Aspire.ServiceDefaults.UnitTests` | Unit tests for the service defaults — how a clone, a worktree and a detached head are told apart |
| `tests/Backlog.HostComposition.UnitTests` | Unit tests for how the hosts compose — the MCP server, the MCP and telemetry endpoint gates, and the web harness host |
| `tests/Backlog.Modules.Tasks.UnitTests` | Unit tests for the Tasks module domain |
| `tests/Backlog.Modules.Inbox.UnitTests` | Unit tests for the Inbox module — the item and organizer aggregates, capture and intake, attachments, triage, and routing to the backlog |
| `tests/Backlog.Modules.Capture.UnitTests` | Unit tests for the Capture module — the capture run, the capture ids, and the source kinds |
| `tests/Backlog.Modules.Sync.UnitTests` | Unit tests for the Sync module — device pairing, the attachment store handler, and change precedence in the in-memory replicas |
| `tests/Backlog.Modules.Sync.Api.UnitTests` | Unit tests for the sync service's endpoints — tasks, sessions, annotations, attachments, captures, device pairing, and authentication |
| `tests/Backlog.Modules.Dashboard.UnitTests` | Unit tests for the Dashboard module's derivations — scoring, bucketing, churn rates, spend aggregation, and the cache |
| `tests/Backlog.Modules.Roadmap.UnitTests` | Unit tests for the Roadmap module — plan items, sequencing, and the scheduling rules |
| `tests/Backlog.Infrastructure.Sqlite.UnitTests` | Unit tests for the SQLite store — round-tripping a task aggregate and rank order, and round-tripping the roadmap plan document, its `updated_at` stamp, and the two tables coexisting in one file |
| `tests/Backlog.Infrastructure.Devbook.UnitTests` | Unit tests for the devbook database — every rung of the degradation ladder, the scope projection, and the cross-language schema contract, whose fixtures are built from the writer's own DDL text so the two languages cannot drift apart silently |
| `tests/Backlog.Infrastructure.DevPc.UnitTests` | Unit tests for the tools adapter and the catalog it reads — the command lines, the catalog merge, the Claude desktop config, the version comparison and the plugin cache |
| `tests/Backlog.Infrastructure.FileSystem.UnitTests` | Unit tests for the knowledge graph and the roadmap item rollup. The same adapter's workspace-settings and feature-flag tests sit in `Backlog.Desktop.UI.UnitTests`, where the collection fixture they serialize on lives |
| `tests/Backlog.Infrastructure.GitHub.UnitTests` | Unit tests for the GitHub adapter — issue projection, activity, and billing |
| `tests/Backlog.Infrastructure.Claude.UnitTests` | Unit tests for the Claude usage adapter |
| `tests/Backlog.Infrastructure.AzureFoundry.UnitTests` | Unit tests for the Azure Foundry adapter — the settings store, the chat, embeddings and cost clients, the Inbox plan drafter, the plan fixture of the local test service, and its registrations |
| `tests/Backlog.Infrastructure.Copilot.UnitTests` | Unit tests for the Copilot CLI launcher — what it starts, and that it releases the process handle |
| `tests/Backlog.Infrastructure.Capture.UnitTests` | Unit tests for the capture adapters — the feed fetcher and reader, the YouTube, website and import-file adapters, and delivery into the Inbox |
| `tests/Backlog.Infrastructure.Mcp.UnitTests` | Unit tests for the MCP tools — each tool group, chapter reading, and tool creation |
| `tests/Backlog.Infrastructure.Sync.UnitTests` | Unit tests for the sync client — device pairing and credentials, the task, session and annotation sync workers, and replica merging |
| `tests/Backlog.Infrastructure.Cosmos.UnitTests` | Unit tests for the Cosmos DB adapter — the replica documents, the task replica, and its registration |
| `tests/Backlog.Infrastructure.BlobStorage.UnitTests` | Unit tests for the attachment store — the blob adapter against a fake container, and its registration |
| `tests/Backlog.Infrastructure.SpecManager.UnitTests` | Unit tests for the spec-manager connector — every mapping row against recorded responses, the fetch and its status cache, who-am-I, the OAuth sign-in and token refresh against a stub server, and the DPAPI token store |
| `tests/Backlog.Infrastructure.Sessions.UnitTests` | Unit tests for the Sessions adapters — the session and activity sources, transcript work, delivery-run reading, and the delivery surface lifecycle |
| `tests/Backlog.UI.Components.UnitTests` | Unit tests for the shared control library, rendered without an application behind it |
| `tests/Backlog.Desktop.UI.UnitTests` | Unit tests for the desktop UI services, the context panes, and GitHub integration |
| `tests/Backlog.Mobile.UI.UnitTests` | Unit tests for the mobile channel's components |
| `tests/Backlog.EndToEndTests` | Playwright end-to-end tests against the running AppHost's harnesses; skipped unless `BACKLOG_E2E=1` — see [End-to-end tests](#end-to-end-tests) |
| `tests/Backlog.ArchitectureTests` | Executable structure rules — module boundaries, desktop context boundaries, design-token and storybook coverage, and "harness is never shipped" |

### The desktop channel's bounded contexts

The desktop client is several bounded contexts, and each one is its own project
rather than a folder inside the shell. The split follows
[`.devbook/domain/context-map.md`](.devbook/domain/context-map.md) rather than layers:

| Project | Bounded context / role |
|---|---|
| `src/Modules/Inbox/Backlog.Modules.Inbox.UI` | Inbox — what has been captured but not decided on. Reaches Tasks only through a port its own module publishes |
| `src/Modules/Tasks/Backlog.Modules.Tasks.UI` | Tasks — the task pane and its GitHub and Copilot CLI projections |
| `src/Modules/Devbook/Backlog.Modules.Devbook.UI` | Devbook — arc42, domain, design, technology, and instruction knowledge, scoped by a repository alias |
| `src/Modules/Roadmap/Backlog.Modules.Roadmap.UI` | Roadmap Planning — the forward plan, as a band above the panes |
| `src/Modules/Dashboard/Backlog.Modules.Dashboard.UI` | Dashboard — productivity and cost insight over what the other systems already hold. Reads only; writes nothing back |
| `src/Modules/DevPc/Backlog.Modules.DevPc.UI` | Dev PC Management — the tools surface: plugins, repository tools, and MCP servers |
| `src/Modules/Sessions/Backlog.Modules.Sessions.UI` | Sessions — what the coding agents have been doing on this PC. Reads the profile; writes nothing |
| `src/App/Backlog.Desktop.UI` | Not a context — app chrome, routes, settings, and the composition root. The one place allowed to see all of them at once |

Making each context a project turns most of the boundary into a reference graph:
the Inbox, Tasks and Devbook each reference only their own module's
Abstractions, the Dashboard references its own Abstractions plus the two adapters
it reads providers through and no sibling context at all, and no context
references another. Where two contexts have to meet — the Inbox routing an item
into the backlog — the join is a port in the supplier's Abstractions
(`IInboxBacklogTarget`) answered by an adapter in `Backlog.Infrastructure.FileSystem`
that may see both, never a project reference between them.
`DesktopDomainBoundaryTests` covers what the graph alone cannot.

There used to be a `Backlog.Desktop.Workspace` project underneath the contexts
holding where the backlog lives, which repositories are configured and which
features are on. Being readable by everyone made it the place two contexts could
meet without either publishing anything: Devbook read the backlog root and
Tasks read the knowledge-folder resolver, which is not the
Partnership `.devbook/domain/context-map.md` describes. Those four types are now module
ports — `ITaskStore` in Tasks's Abstractions,
`IDevbookFolderSource` in Devbook's, `IAppFeatureSettings` in the shared
kernel — with the adapters that answer them in
`Backlog.Infrastructure.FileSystem` and, for tasks themselves,
`Backlog.Infrastructure.Sqlite`. Where an answer needs more than one context's
settings the adapter holds that join, because an adapter is allowed to see both and
a screen is not.
`ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface`
is what keeps it that way.

The client is a client: it dispatches use cases and holds DTOs. Deciding what a
task is belongs to `Backlog.Modules.Tasks`, which the UI reaches only
through its Abstractions project — see
[ADR 0002](.devbook/arc42/adr/0002-backlog-module-owns-the-entry-text-language.md).
Anything the contexts genuinely share — the status, priority and repository
selectors, the GitHub connection — lives in the shared component library, in the
shared kernel, or in a cross-cutting adapter, never in whichever context happened
to need it first.

The shell's `_Imports.razor` deliberately imports none of the contexts: each
module UI project carries its own, so a component can only reach into another
context by saying so in writing.

The projects keep their original root namespaces — `Backlog.Desktop.UI.Inbox`,
`.Tasks`, `.Devbook` — set explicitly in each
`.csproj` and deliberately not matching the project name. The Razor generator
emits a component's `@using` directives *inside* the component's namespace and
without a `global::` prefix, so a namespace carrying a second `Backlog` segment
shadows the repository root: under `Backlog.Modules.Tasks.UI`, `@using
Backlog.UI.Components.Markdown` binds to `Backlog.Modules.Tasks` and fails with
CS0234. The namespace segment is `Tasks`, not `Backlog`, for the same
reason, and it is also the context's name in the context map — so the constraint
and the domain language agree. The modules whose code was never in the shell to
begin with (`Dashboard`, `Roadmap`, `DevPc`, `Sessions`) use their own
`Backlog.Modules.<Context>.UI` namespaces, because there was no original namespace
for them to keep.

Everything under `src/Harness/` is a development-time host. It ships nothing to a
user; it exists so the shared Razor components can be started by the Aspire
AppHost and driven by Playwright, which the MAUI heads cannot be. That intent is
enforced rather than documented: `src/Harness/Directory.Build.props` marks every
harness project non-packable and non-publishable, and
`tests/Backlog.ArchitectureTests` fails the build if a shipping `src/` project ever
references one. See [`src/Harness/README.md`](src/Harness/README.md).

## Build and test

```powershell
dotnet build Backlog.sln
dotnet test Backlog.sln
```

Build the solution rather than a project: only the solution build builds the mobile head,
so a green test run can sit on a red build. Read the error and warning counts, not only the
exit code.

Do not trust `dotnet test`'s exit code either. In this repository it can report
"Zero tests ran" without running anything. Read what it printed — the list of test
assemblies and the total — and check the assembly count matches the test projects under
`tests/`. When it does not, run each built test executable instead, from its own project
folder, since some tests resolve files relative to the working directory:

```powershell
Get-ChildItem tests -Directory | ForEach-Object {
    $exe = Join-Path $_.FullName "bin\Debug\net10.0\$($_.Name).exe"
    if (Test-Path $exe) { Push-Location $_.FullName; & $exe; Pop-Location }
}
```

Pick each executable by its project's name, as above, never with `*.exe`: the sync service
and the harnesses are copied into some test output folders, and starting one of those
starts a server that never exits. Filter a run with `-class <Full.Name>` or `-method`.
[End-to-end tests](#end-to-end-tests) need a running AppHost and are skipped otherwise.

## Running locally

```powershell
aspire start --isolated --non-interactive --apphost src/Aspire/Backlog.Aspire.AppHost/Backlog.Aspire.AppHost.csproj
```

Run it from the repository root; it needs the .NET SDK, the Aspire CLI, and a running
container runtime. `--isolated` gives the run its own ports and user-secrets state, so pass
it every time. `aspire start` starts the AppHost in the background and exits `0` even when
the AppHost failed to come up, so judge its health from the dashboard, not the exit code.
Stop it with `aspire stop --apphost` and the same path.

The AppHost starts the sync service, the Foundry test service, and the three web test
harnesses. The remaining resources need something
Aspire cannot provide on its own — a desktop window, an Android emulator, a CLI,
or a VS Code extension host — so they are registered with **explicit start** and
launched on demand from the dashboard:

| Resource | Starts | Needs |
|---|---|---|
| `sync`, `azure-foundry-test`, `desktop-web-harness`, `mobile-web-harness`, `ui-storybook` | automatically | — |
| `desktop` | on demand | Windows desktop session |
| `mobile-android` | on demand | running Android emulator or attached device; its **Set run target** command chooses the target framework and the `adb` serial |
| `mobile-maui-android-emulator` | on demand | Android emulator, started for you; reaches `sync` through `mobile-tunnel`, so start the tunnel first — the head waits for the tunnel's endpoint before it comes up |
| `mobile-tunnel` | on demand | `devtunnel` CLI and a signed-in account; publishes `sync`'s HTTP endpoint so the emulator can reach it off its own loopback |
| `ide-vscode-build` | on demand | `npm install` in `src/App/Backlog.Ide.VsCode` |
| `ide-vscode-host` | on demand | `code` on PATH |

`mobile-maui` itself is not in the table because it is not something you start. It
is the parent `Aspire.Hosting.Maui` registration, a container whose build is
deferred until one of its platform children runs; `mobile-maui-android-emulator`
is that child, and the one with the start button.

`foundry-local` is not in that table because it is not on every machine.
`RunAsFoundryLocal()` launches the `foundry` CLI as the app model comes up rather
than when the resource is started, so explicit start cannot hold it back and a
machine without the CLI would carry a failed resource through every run. The
AppHost therefore registers it **only when `foundry` is on PATH** — and where it
is registered, it starts with the app model rather than on demand. Not seeing it
in the dashboard means this machine has no Foundry Local, not that something went
wrong.

`desktop-web-harness` carries a **Reset local data** command that deletes the task
database and the workspace settings. Every git worktree of this repository shares
one per-user `Backlog.Debug` workspace, so it resets every session on the machine,
not only this one.

Each channel with a MAUI head also has a browser harness sharing the same Razor
components, so the UI can be developed and tested without a device:
`Backlog.Desktop.UI` → `Backlog.Desktop.WebHarness` for desktop, and `Backlog.Mobile.UI` → `Backlog.Mobile.WebHarness`
(rendered at phone width) for mobile.

All ports are dynamic (`port 0` in every `launchSettings.json`), so several git
worktrees of this repository can run their own AppHost side by side, each started with
`--isolated` so each gets its own ports and user-secrets state. Read the
actual dashboard and resource URLs from the `aspire start` output, or with
`aspire describe`.

## End-to-end tests

`tests/Backlog.EndToEndTests` drives the harnesses of this worktree's running AppHost with
Playwright. It takes resources away and back through the Aspire CLI and reads the dashboard's
telemetry for evidence. Its tests skip unless `BACKLOG_E2E=1`. With the AppHost healthy:

```powershell
$env:BACKLOG_E2E = '1'
dotnet test --project tests\Backlog.EndToEndTests
```

[`tests/Backlog.EndToEndTests/README.md`](tests/Backlog.EndToEndTests/README.md) covers:

- what each test covers;
- the one-time browser install;
- where the screenshots and logs go;
- what a run changes in the harnesses and the shared workspace.

## Deploying everything at once

**Deploy All** runs the Foundry models, the sync service and the desktop release in a
single GitHub Actions run, choosing which components take part and how far to take them.
It owns no deployment logic — each component is the workflow below, called as a reusable
workflow — so a component behaves the same whether it runs on its own or from there.
Mobile is deliberately left out for now.

See [`docs/deployment/all.md`](docs/deployment/all.md).

## Deploying Azure Foundry models

Azure AI Foundry model deployments are described in `infra/foundry/` and deployed
through the manual **Deploy Foundry** GitHub Actions workflow, which runs on a
self-hosted runner that already has Azure access. See
[`docs/deployment/foundry.md`](docs/deployment/foundry.md) for the subscription
target, model list, quota prerequisite, runner setup, and validate/what-if/deploy
commands. A target is a GitHub environment plus a matching
`infra/foundry/<environment>.bicepparam` file, so another subscription can be added
without changing the workflow.

## Deploying the sync service

The cloud sync tier is described in `infra/sync/` and `azure.yaml`, and is
provisioned and deployed with the **Azure Developer CLI** through the **Deploy
Sync** GitHub Actions workflow. That one runs on a GitHub-hosted runner and
authenticates with an **OIDC federated credential**, so no publish profile or
service principal secret is stored in the repository.

See [`docs/deployment/sync.md`](docs/deployment/sync.md) for the deployment
target, the four manual prerequisites, and how to run it locally. Nothing is
provisioned in Azure yet — the resource group, the federated credential and the
budget alert are created by hand once, and the doc says how.

**Local development needs no Azure account.** The Aspire AppHost starts the
Cosmos DB emulator as a container, declaring the same database and the same two
containers the deployed account has, so the sync path builds and runs offline.
Docker must be running; the emulator image is large and its first cold start
takes a couple of minutes, after which the container is reused.

## Installing the desktop app

The Windows desktop app is distributed as a signed **MSIX** sideloaded from
GitHub Releases, with an App Installer (`.appinstaller`) that keeps it updated —
there is no Microsoft Store listing.

1. Open the [latest release](https://github.com/JSdotNet/Backlog/releases/latest)
   and download `Backlog.Desktop.cer` and `Backlog.Desktop.appinstaller`.
2. Because the package is **self-signed**, trust the public signing certificate
   on the machine first, then open the `.appinstaller` to install. In an elevated
   PowerShell session from the download folder, run:

   ```powershell
   Import-Certificate -FilePath .\Backlog.Desktop.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

3. Updates are checked automatically on launch (and in the background). You can
   also check on demand by clicking the version in the app header, which reports
   the outcome and offers "Install update" when a newer build is available.

Debug builds run **unpackaged** (so Aspire and the WebView2 debugging attach keep
working); the in-app updater reports "unsupported" there, which is expected.

## Installing the Android app

The Android app is distributed as a signed **APK** sideloaded from GitHub
Releases — there is no Google Play listing. An APK (not an `.aab`) is published
precisely so it can be installed straight onto a phone.

1. On the phone, open the
   [latest release](https://github.com/JSdotNet/Backlog/releases/latest) and
   download the `Backlog.Mobile_<version>.apk` asset.
2. Open the downloaded file. Android will ask for permission to install from
   this source the first time — allow your browser or file manager under
   **Settings → Apps → Special app access → Install unknown apps**.
3. Install, then launch **Backlog** from the app drawer.

Because the package is **self-signed**, later releases only install over an
existing copy while they keep the same signing key. If Android reports "App not
installed" after a key rotation, uninstall the old copy first.

Nightly builds off `main` are published under separate `mobile-v*` tags and are
not marked as the latest release; tagged `v*` releases carry both the desktop
MSIX and the Android APK.

### Sideloading a local build

For development, `build/Install-AndroidApp.ps1` builds a signed APK and installs
it over USB or onto a running emulator:

```powershell
./build/Install-AndroidApp.ps1
```

It creates a throwaway developer keystore under `build/.local/` (gitignored) on
first run — that is a local identity only, never the release one, so an APK it
signs cannot upgrade a release-signed install. Enable **Developer options → USB
debugging** on the phone and accept the authorization prompt first. Use
`-Device <serial>` when more than one device is attached, `-VersionCode <n>` to
install over a previous local build, and `-SkipInstall` to produce the APK
without installing it.

The script needs the `maui-android` workload, a JDK, and the Android SDK
platform-tools; it locates the Visual Studio installations of the latter two
automatically, so they do not need to be on `PATH`.


## Language and conventions

Term definitions and naming conventions are in each context's `naming.md` under
[`.devbook/domain/`](.devbook/domain/), indexed by [`.devbook/domain/context-map.md`](.devbook/domain/context-map.md).
