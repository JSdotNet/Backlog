# Backlog

[![Release desktop](https://github.com/JSdotNet/Backlog/actions/workflows/release-desktop.yml/badge.svg)](https://github.com/JSdotNet/Backlog/actions/workflows/release-desktop.yml)
[Latest release](https://github.com/JSdotNet/Backlog/releases/latest)

A personal work management system built for AI-driven development. Capture work items, prompts, and knowledge across projects, organize them through an inbox-first workflow, and access them where the work happens: desktop, IDE, and phone.

## Current state

The project is currently in setup mode while feature ideas are being shaped and validated.

Current focus:
- Finalize project setup and working conventions
- Define and prioritize the first feature set
- Turn feature ideas into implementation-ready backlog items

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

### Second brain

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
- Second Brain
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

See [`.domain/context-map.md`](.domain/context-map.md) for functional boundaries and
[`.arc42/`](.arc42/) for technical design.

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
[`.domain/context-map.md`](.domain/context-map.md). It owns no domain — it
coordinates transient state between devices for the Capture and Inbox flow, and
holds it only until the desktop picks it up. If it ever grows rules of its own, it
needs an entry in the context map before it grows projects.

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
| `src/Modules/Inbox/Backlog.Modules.Inbox.UI` | Inbox's desktop face — what has been captured but not decided on |
| `src/Modules/Knowledge/Backlog.Modules.Knowledge.Abstractions` | Second Brain's published surface — `IKnowledgeFolderSource`, the configured-folder format, and the location a folder resolves to |
| `src/Modules/Knowledge/Backlog.Modules.Knowledge.UI` | Second Brain's desktop face — the knowledge menu and the arc42, domain, design, technology, and instruction panels |
| `src/Modules/Roadmap/Backlog.Modules.Roadmap` | Roadmap module — the plan and its items, the sequencing rules between them, and the `IRoadmapPlanRepository` port |
| `src/Modules/Roadmap/Backlog.Modules.Roadmap.Abstractions` | The Roadmap module's published surface — the plan DTOs, `IRoadmapPlanning`, and `RoadmapFeatures` |
| `src/Modules/Roadmap/Backlog.Modules.Roadmap.UI` | Roadmap Planning's desktop face — the band above the panes and its editor |
| `src/Modules/Sessions/Backlog.Modules.Sessions.Abstractions` | Sessions' published surface — the session record, its states and groupings, and the `IAgentSessionSource` port |
| `src/Modules/Sessions/Backlog.Modules.Sessions.UI` | Sessions' desktop face — the full-screen session list, and the readers over what Claude and Copilot leave in the user profile |
| `src/Modules/DevPc/Backlog.Modules.DevPc.Abstractions` | Dev PC Management's published surface — `DevPcFeatures` and the types its screens exchange |
| `src/Modules/DevPc/Backlog.Modules.DevPc.UI` | Dev PC Management's desktop face — the tools surface |
| `src/Modules/Dashboard/Backlog.Modules.Dashboard` | Dashboard module — the derivations behind the dashboard: productivity scoring, weekly bucketing, churn rates, month-to-date spend, and the session cache in front of the providers |
| `src/Modules/Dashboard/Backlog.Modules.Dashboard.Abstractions` | The Dashboard module's published surface — the scope, the insight DTOs, `IProductivityInsights` and `ICostInsights`, and the four ports its adapters answer |
| `src/Modules/Dashboard/Backlog.Modules.Dashboard.UI` | The Dashboard's face — the full-screen surface, its seven independent parts, and the adapters over GitHub and Anthropic |
| `src/Infrastructure/Backlog.Infrastructure.Sqlite` | Cross-cutting adapter — the canonical local task store, one SQLite database behind `ITaskRepository`. See [ADR 0003](.arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md) |
| `src/Infrastructure/Backlog.Infrastructure.FileSystem` | Cross-cutting adapter — the JSON on local disk: the workspace settings and feature flags behind `ITaskStore`, `IKnowledgeFolderSource` and `IAppFeatureSettings`, and the stored roadmap plan |
| `src/Infrastructure/Backlog.Infrastructure.Claude` | Cross-cutting adapter — Claude usage and spend from the Anthropic organization APIs |
| `src/Infrastructure/Backlog.Infrastructure.Copilot` | Cross-cutting adapter — starting the GitHub Copilot CLI from a Backlog workflow |
| `src/Infrastructure/Backlog.Infrastructure.AzureFoundry` | Cross-cutting adapter — the Azure Foundry chat client behind the AI assistant |
| `src/Infrastructure/Backlog.Infrastructure.GitHub` | Cross-cutting adapter — GitHub issue projection, pull request and issue activity with review detail, Copilot seats, and AI-credit billing |
| `src/App/Backlog.Desktop.UI` | Desktop shell — layout, routes, settings, and the composition that decides which context panes are on screen |
| `src/App/Backlog.Desktop` | Desktop channel — .NET MAUI Blazor Hybrid (Windows) |
| `src/App/Backlog.Mobile.UI` | Shared Razor components for the mobile channel |
| `src/App/Backlog.Mobile` | Mobile channel — .NET MAUI Blazor Hybrid (Android) |
| `src/App/Backlog.Ide.VsCode` | IDE channel — VS Code extension (TypeScript) |
| `src/Modules/Sync/Backlog.Modules.Sync.Api` | Sync module's API — thin ASP.NET Core sync service, deployed to Azure |
| `src/Harness/Backlog.Desktop.WebHarness` | **Test harness, not shipped** — Blazor Server host of `Backlog.Desktop.UI` for Aspire/Playwright |
| `src/Harness/Backlog.Mobile.WebHarness` | **Test harness, not shipped** — Blazor Server host of `Backlog.Mobile.UI` at phone width |
| `src/Harness/Backlog.UI.Storybook` | **Test harness, not shipped** — the shared control library rendered on its own, with each page's governing `.design` rule beside it |
| `src/Harness/Backlog.AzureFoundry.TestService` | **Test harness, not shipped** — a stand-in for Azure Foundry so the assistant can be driven without a cloud account |
| `tests/Backlog.Modules.Tasks.UnitTests` | Unit tests for the Tasks module domain |
| `tests/Backlog.Modules.Dashboard.UnitTests` | Unit tests for the Dashboard module's derivations — scoring, bucketing, churn rates, spend aggregation, and the cache |
| `tests/Backlog.Modules.Roadmap.UnitTests` | Unit tests for the Roadmap module — plan items, sequencing, and the scheduling rules |
| `tests/Backlog.Infrastructure.Sqlite.UnitTests` | Unit tests for the SQLite task store — round-tripping an aggregate, and rank order |
| `tests/Backlog.Infrastructure.FileSystem.UnitTests` | Unit tests for the stored roadmap plan. The same adapter's workspace-settings and feature-flag tests sit in `Backlog.Desktop.UI.UnitTests`, where the collection fixture they serialize on lives |
| `tests/Backlog.Infrastructure.GitHub.UnitTests` | Unit tests for the GitHub adapter — issue projection, activity, and billing |
| `tests/Backlog.Infrastructure.Claude.UnitTests` | Unit tests for the Claude usage adapter |
| `tests/Backlog.UI.Components.UnitTests` | Unit tests for the shared control library, rendered without an application behind it |
| `tests/Backlog.Desktop.UI.UnitTests` | Unit tests for the desktop UI services, the context panes, and GitHub integration |
| `tests/Backlog.Mobile.UI.UnitTests` | Unit tests for the mobile channel's components |
| `tests/Backlog.ArchitectureTests` | Executable structure rules — module boundaries, desktop context boundaries, design-token and storybook coverage, and "harness is never shipped" |

### The desktop channel's bounded contexts

The desktop client is several bounded contexts, and each one is its own project
rather than a folder inside the shell. The split follows
[`.domain/context-map.md`](.domain/context-map.md) rather than layers:

| Project | Bounded context / role |
|---|---|
| `src/Modules/Inbox/Backlog.Modules.Inbox.UI` | Inbox — what has been captured but not decided on. Publishes `InboxItem`; reads nothing back |
| `src/Modules/Tasks/Backlog.Modules.Tasks.UI` | Tasks — the task pane, its drafts, and its GitHub and Copilot CLI projections |
| `src/Modules/Knowledge/Backlog.Modules.Knowledge.UI` | Second Brain — arc42, domain, design, technology, and instruction knowledge, scoped by a repository alias |
| `src/Modules/Roadmap/Backlog.Modules.Roadmap.UI` | Roadmap Planning — the forward plan, as a band above the panes |
| `src/Modules/Dashboard/Backlog.Modules.Dashboard.UI` | Dashboard — productivity and cost insight over what the other systems already hold. Reads only; writes nothing back |
| `src/Modules/DevPc/Backlog.Modules.DevPc.UI` | Dev PC Management — the tools surface: plugins, repository tools, and MCP servers |
| `src/Modules/Sessions/Backlog.Modules.Sessions.UI` | Sessions — what the coding agents have been doing on this PC. Reads the profile; writes nothing |
| `src/App/Backlog.Desktop.UI` | Not a context — app chrome, routes, settings, and the composition root. The one place allowed to see all of them at once |

Making each context a project turns most of the boundary into a reference graph:
the Inbox references nothing but the shared control library, Tasks
and Second Brain each reference only their own module's Abstractions, the Dashboard
references its own Abstractions plus the two adapters it reads providers through
and no sibling context at all, and the one context-to-context edge the context map
allows — Tasks conforming to the Inbox's published `InboxItem` — is a
project reference somebody had to write down. `DesktopDomainBoundaryTests` covers what the graph alone cannot.

There used to be a `Backlog.Desktop.Workspace` project underneath the contexts
holding where the backlog lives, which repositories are configured and which
features are on. Being readable by everyone made it the place two contexts could
meet without either publishing anything: Second Brain read the backlog root and
Tasks read the knowledge-folder resolver, which is not the
Partnership `.domain/context-map.md` describes. Those four types are now module
ports — `ITaskStore` in Tasks's Abstractions,
`IKnowledgeFolderSource` in Second Brain's, `IAppFeatureSettings` in the shared
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
[ADR 0002](.arc42/adr/0002-backlog-module-owns-the-entry-text-language.md).
Anything the contexts genuinely share — the status, priority and repository
selectors, the GitHub connection — lives in the shared component library, in the
shared kernel, or in a cross-cutting adapter, never in whichever context happened
to need it first.

The shell's `_Imports.razor` deliberately imports none of the contexts: each
module UI project carries its own, so a component can only reach into another
context by saying so in writing.

The projects keep their original root namespaces — `Backlog.Desktop.UI.Inbox`,
`.Tasks`, `.Knowledge` — set explicitly in each
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

## Running locally

```powershell
dotnet run --project src/Aspire/Backlog.Aspire.AppHost
```

The AppHost starts the sync service and the two web test harnesses. The remaining
resources need something Aspire cannot provide on its own — a desktop
window, an Android emulator, or a VS Code extension host — so they are registered
with **explicit start** and launched on demand from the dashboard:

| Resource | Starts | Needs |
|---|---|---|
| `sync`, `desktop-web-harness`, `mobile-web-harness` | automatically | — |
| `desktop` | on demand | Windows desktop session |
| `mobile-android` | on demand | running Android emulator or attached device |
| `ide-vscode-build` | on demand | `npm install` in `src/App/Backlog.Ide.VsCode` |
| `ide-vscode-host` | on demand | `code` on PATH |

Each channel with a MAUI head also has a browser harness sharing the same Razor
components, so the UI can be developed and tested without a device:
`Backlog.Desktop.UI` → `Backlog.Desktop.WebHarness` for desktop, and `Backlog.Mobile.UI` → `Backlog.Mobile.WebHarness`
(rendered at phone width) for mobile.

All ports are dynamic (`port 0` in every `launchSettings.json`), so several git
worktrees of this repository can run their own AppHost side by side. Read the
actual dashboard and resource URLs from the `aspire start` output, or with
`aspire describe`.

## Deploying Azure Foundry models

Azure AI Foundry model deployments are described in `infra/foundry/` and deployed
through the manual **Deploy Foundry** GitHub Actions workflow, which runs on a
self-hosted runner that already has Azure access. See
[`docs/deployment/foundry.md`](docs/deployment/foundry.md) for the subscription
target, model list, quota prerequisite, runner setup, and validate/what-if/deploy
commands. A target is a GitHub environment plus a matching
`infra/foundry/<environment>.bicepparam` file, so another subscription can be added
without changing the workflow.

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
[`.domain/`](.domain/), indexed by [`.domain/context-map.md`](.domain/context-map.md).
