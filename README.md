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
- Backlog Management
- Second Brain
- Monitoring and Dashboard
- Technology Stack
- Dev PC Management
- Repository Management

Access channels:
- Desktop client
- IDE extensions (VS Code, Visual Studio)
- Phone app

See [domain/domain.md](domain/domain.md) for functional boundaries and [architecture/Architecture.md](architecture/Architecture.md) for technical design.

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
| `src/Modules/Backlog/Backlog.Modules.Backlog` | Backlog module — domain model (entries, sub-items, lifecycle rules), ports, and vertical-slice features |
| `src/Modules/Backlog/Backlog.Modules.Backlog.Abstractions` | The Backlog module's published surface — DTOs, the entry text format, and `IBacklogEntries` |
| `src/Modules/Backlog/Backlog.Modules.Backlog.UI` | Backlog Management's desktop face — the entry pane, its state, and the GitHub and Copilot CLI projections |
| `src/Modules/Inbox/Backlog.Modules.Inbox.UI` | Inbox's desktop face — what has been captured but not decided on |
| `src/Modules/Knowledge/Backlog.Modules.Knowledge.Abstractions` | Second Brain's published surface — `IKnowledgeFolderSource`, the configured-folder format, and the location a folder resolves to |
| `src/Modules/Knowledge/Backlog.Modules.Knowledge.UI` | Second Brain's desktop face — the knowledge menu and the arc42, domain, design, technology, and instruction panels |
| `src/Modules/Roadmap/Backlog.Modules.Roadmap.UI` | Roadmap planning over roadmap-ready entries — scaffolding only so far |
| `src/Modules/DevPc/Backlog.Modules.DevPc.UI` | Dev PC Management's desktop face — scaffolding only so far |
| `src/Modules/Monitoring/Backlog.Modules.Monitoring.UI` | Monitoring & Dashboard's desktop face — scaffolding only so far |
| `src/Infrastructure/Backlog.Infrastructure.FileSystem` | Cross-cutting adapter — Markdown + JSON file storage (canonical local data), and the workspace settings behind `IBacklogStore`, `IKnowledgeFolderSource` and `IAppFeatureSettings` |
| `src/Infrastructure/Backlog.Infrastructure.GitHub` | Cross-cutting adapter — GitHub issue projection |
| `src/App/Backlog.Desktop.UI` | Desktop shell — layout, routes, settings, and the composition that decides which context panes are on screen |
| `src/App/Backlog.Desktop` | Desktop channel — .NET MAUI Blazor Hybrid (Windows) |
| `src/App/Backlog.Mobile.UI` | Shared Razor components for the mobile channel |
| `src/App/Backlog.Mobile` | Mobile channel — .NET MAUI Blazor Hybrid (Android) |
| `src/App/Backlog.Ide.VsCode` | IDE channel — VS Code extension (TypeScript) |
| `src/Modules/Sync/Backlog.Modules.Sync.Api` | Sync module's API — thin ASP.NET Core sync service, deployed to Azure |
| `src/Harness/Backlog.Desktop.WebHarness` | **Test harness, not shipped** — Blazor Server host of `Backlog.Desktop.UI` for Aspire/Playwright |
| `src/Harness/Backlog.Mobile.WebHarness` | **Test harness, not shipped** — Blazor Server host of `Backlog.Mobile.UI` at phone width |
| `tests/Backlog.Modules.Backlog.UnitTests` | Unit tests for the Backlog module domain |
| `tests/Backlog.Infrastructure.FileSystem.UnitTests` | Unit tests for the file storage adapter |
| `tests/Backlog.Desktop.UI.UnitTests` | Unit tests for the desktop UI services and GitHub integration |
| `tests/Backlog.ArchitectureTests` | Executable structure rules — module boundaries, desktop context boundaries, and "harness is never shipped" |

### The desktop channel's bounded contexts

The desktop client is several bounded contexts, and each one is its own project
rather than a folder inside the shell. The split follows
[`.domain/context-map.md`](.domain/context-map.md) rather than layers:

| Project | Bounded context / role |
|---|---|
| `src/Modules/Inbox/Backlog.Modules.Inbox.UI` | Inbox — what has been captured but not decided on. Publishes `InboxItem`; reads nothing back |
| `src/Modules/Backlog/Backlog.Modules.Backlog.UI` | Backlog Management — the entry pane, its drafts, and its GitHub and Copilot CLI projections |
| `src/Modules/Knowledge/Backlog.Modules.Knowledge.UI` | Second Brain — arc42, domain, design, technology, and instruction knowledge, scoped by a repository alias |
| `src/App/Backlog.Desktop.UI` | Not a context — app chrome, routes, settings, and the composition root. The one place allowed to see all three at once |

Making each context a project turns most of the boundary into a reference graph:
the Inbox references nothing but the shared control library, Backlog Management
and Second Brain each reference only their own module's Abstractions, and the one
context-to-context edge the context map allows — Backlog Management conforming to
the Inbox's published `InboxItem` — is a project reference somebody had to write
down. `DesktopDomainBoundaryTests` covers what the graph alone cannot.

There used to be a `Backlog.Desktop.Workspace` project underneath the contexts
holding where the backlog lives, which repositories are configured and which
features are on. Being readable by everyone made it the place two contexts could
meet without either publishing anything: Second Brain read the backlog root and
Backlog Management read the knowledge-folder resolver, which is not the
Partnership `.domain/context-map.md` describes. Those four types are now module
ports — `IBacklogStore` in Backlog Management's Abstractions,
`IKnowledgeFolderSource` in Second Brain's, `IAppFeatureSettings` in the shared
kernel — with the adapters that answer them in
`Backlog.Infrastructure.FileSystem`. Resolving a repository's `.backlog` folder
still needs both contexts' settings; the adapter holds that join, because an
adapter is allowed to see both and a screen is not.
`ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface`
is what keeps it that way.

The client is a client: it dispatches use cases and holds DTOs. Deciding what a
backlog entry is belongs to `Backlog.Modules.Backlog`, which the UI reaches only
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
`.BacklogManagement`, `.Knowledge` — set explicitly in each
`.csproj` and deliberately not matching the project name. The Razor generator
emits a component's `@using` directives *inside* the component's namespace and
without a `global::` prefix, so a namespace carrying a second `Backlog` segment
shadows the repository root: under `Backlog.Modules.Backlog.UI`, `@using
Backlog.UI.Components.Markdown` binds to `Backlog.Modules.Backlog` and fails with
CS0234. The namespace segment is `BacklogManagement`, not `Backlog`, for the same
reason, and it is also the context's name in the context map — so the constraint
and the domain language agree. The scaffold modules that carry no moved code
(`Roadmap`, `DevPc`, `Monitoring`) use their own `Backlog.Modules.<Context>.UI`
namespaces.

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
through the manual **Deploy Foundry** GitHub Actions workflow. See
[`docs/deployment/foundry.md`](docs/deployment/foundry.md) for the subscription
target, model list, quota prerequisite, OIDC setup, and validate/what-if/deploy
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


## Language and conventions

Term definitions and naming conventions are in [domain/naming.md](domain/naming.md).
