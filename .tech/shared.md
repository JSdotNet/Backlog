# Shared Technologies

```meta
status: adopted
related: [".tech/technology-graph.md", ".arc42/02-constraints.md#technical-constraints"]
```

> Technologies used by more than one channel. Every layer file points at these
> chapters with `depends-on` instead of redefining them locally.

## Markdown

```meta
status: adopted
type: format
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md"]
```

The language a task's content is written in, and the format of this repository's
own knowledge folders.

- **Used for** — the text of an entry, held as a column in the SQLite store; the
  `.arc42`/`.domain`/`.backlog`/`.tech`/`.design` documents; and the knowledge
  notes the product manages.
- **Why** — plain text is durable, diffable, greppable, and editable without the
  app. Note the boundary drawn by
  `.arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md`: markdown is the
  *content* of a task, no longer its *storage format*.

## JSON

```meta
status: adopted
type: format
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

The derived-data and settings format sitting beside the canonical stores.

- **Used for** — the knowledge folders' derived indexes (`_meta/*.json`), the
  Archify artifact indexes (`_archify/index.json`), settings, the repo registry
  (`config/repos.json`), the dev-tool catalog the DevPc module reads, and the
  owned-collection columns inside the SQLite schema.
- **Why** — cheap to write from every channel's stack and fast to load, with no
  migration story needed for data that is either rebuildable or user-scoped.

## YAML

```meta
status: adopted
type: format
```

The structured-metadata format embedded in Markdown documents.

- **Used for** — the fenced `meta` blocks that make the knowledge folders
  machine-readable, GitHub workflow and issue-template configuration, and
  `SKILL.md` front matter.
- **Why** — human-writable inside Markdown and trivially parseable by tooling.

## Mermaid

```meta
status: adopted
type: format
depends-on: [".tech/shared.md#markdown"]
related: [".tech/tooling.md#archify"]
```

Diagram-as-text notation embedded directly in Markdown.

- **Used for** — the technology graph, C4/deployment/sequence diagrams in
  `.arc42`, and domain model/flow diagrams in `.domain`.
- **Why** — diagrams stay version-controlled and reviewable in the same diff as
  the prose they belong to; rendered natively by GitHub and by the app's own
  knowledge viewer. A mermaid fence stays canonical even where an Archify
  artifact renders the same diagram more richly.

## .NET Runtime

```meta
status: adopted
type: runtime
version: "10.0"
related: [".arc42/04-solution-strategy.md#technology-choices", ".arc42/09-architecture-decisions.md"]
alternatives: ["Node.js only", "Rust + Tauri"]
```

The primary managed runtime for the desktop, mobile, IDE (Visual Studio), and
cloud channels.

- **Used for** — hosting every C#-based component of the system. `net10.0` is
  set once in the root `Directory.Build.props`; only the MAUI heads override it
  with a platform TFM.
- **Why** — the organization's governed .NET guidance applies to the cloud
  service, and reusing one runtime across channels maximizes shared code.

## C# Language

```meta
status: adopted
type: language
version: "latest"
depends-on: [".tech/shared.md#net-runtime"]
related: [".arc42/04-solution-strategy.md#technology-choices"]
```

The main implementation language of the system.

- **Used for** — desktop, mobile, Visual Studio extension, cloud service,
  harness hosts, and the whole test suite.
- **Why** — one language across four of the five channels keeps domain logic and
  contracts shareable. `LangVersion=latest`, with `Nullable` and
  `ImplicitUsings` enabled solution-wide.

## Node.js

```meta
status: adopted
type: runtime
version: ">=18"
related: [".tech/ide.md#vs-code-extension-api"]
```

The JavaScript runtime hosting the VS Code extension and every repository-local
tool.

- **Used for** — the VS Code extension host, the `knowledge-meta` generator, the
  vendored Archify renderer, and the `tools/diagrams` artifact commands.
- **Why** — mandated by the VS Code extension model, already present on
  developer machines, and it lets the repository's own tooling run with nothing
  installed beyond Node itself.

## TypeScript

```meta
status: adopted
type: language
version: "^7.0.0"
depends-on: [".tech/shared.md#nodejs"]
related: [".tech/ide.md#vs-code-extension-api"]
```

The implementation language for the VS Code channel.

- **Used for** — the VS Code extension and its webview UI, compiled with `tsc`
  in the pull-request workflow and watched by the `ide-vscode-build` Aspire
  resource.
- **Why** — the only first-class option for VS Code extensibility. The
  repository's own Node tooling is plain ESM `.mjs` instead, so it needs no
  build step.

## .NET MAUI

```meta
status: adopted
type: framework
version: "10.0.100"
depends-on: [".tech/desktop.md#winui-3", ".tech/mobile.md#android", ".tech/shared.md#c-language"]
related: [".arc42/04-solution-strategy.md#technology-choices", ".arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md"]
alternatives: ["Plain WinUI 3", "Blazor WebAssembly PWA", "Kotlin native"]
```

The cross-platform native app shell used by both the desktop and mobile
channels: a WinUI 3 head on Windows, native Android views on Android.

- **Used for** — the desktop client's native shell (per
  `.arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md`) and the phone client's
  native Android app shell, platform integrations, and offline storage.
- **Why** — accepted by ADR 0001 for desktop, so the whole stack can be
  launched from one Aspire AppHost and driven by Playwright; named as the
  preferred stack for mobile in `.arc42/04-solution-strategy.md#technology-choices`,
  keeping both channels in C# alongside the cloud service. Mobile's choice is
  not yet hardened into its own ADR.
- **How** — the workload version is a single `$(MauiVersion)` property in the
  root `Directory.Build.props`, so the two heads and `Microsoft.Maui.Core` in
  ServiceDefaults cannot drift apart.

## Blazor Hybrid

```meta
status: adopted
type: framework
depends-on: [".tech/shared.md#net-maui", ".tech/shared.md#razor-components"]
related: [".arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md", ".tech/desktop.md#webview2"]
alternatives: ["XAML-only MAUI UI", "Plain WinUI 3", "Blazor WebAssembly PWA"]
```

Razor Components rendered inside the MAUI shell's embedded WebView, through
`Microsoft.AspNetCore.Components.WebView.Maui`.

- **Used for** — the desktop client's entire UI (accepted, per ADR 0001) —
  authored as Razor components instead of XAML, so Playwright can attach over
  WebView2's CDP debugging port. The Android head renders the same components.
- **Why** — accepted for desktop by
  `.arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md`: unlike plain MAUI XAML,
  it gives desktop a Chromium-based, Playwright-drivable UI surface while
  keeping full native filesystem/background-worker access.

## Razor Components

```meta
status: adopted
type: framework
version: "10.0.11"
depends-on: [".tech/shared.md#aspnet-core", ".tech/shared.md#c-language"]
related: [".tech/testing.md#bunit", ".design/component-libraries.md"]
```

The component model every screen in the product is written in
(`Microsoft.AspNetCore.Components.Web`).

- **Used for** — the shared component library `Backlog.UI.Components`, the seven
  per-module UI projects, and both channel UI projects (`Backlog.Desktop.UI`,
  `Backlog.Mobile.UI`) — ten projects reference the package directly.
- **Why** — one component model renders identically inside the MAUI WebView and
  inside the Blazor Server harness hosts, so a component is written once and can
  be reviewed in a browser.

## Blazor Server

```meta
status: adopted
type: framework
depends-on: [".tech/shared.md#razor-components", ".tech/shared.md#aspnet-core"]
related: [".tech/testing.md#playwright", ".tech/shared.md#net-aspire"]
```

The interactive-server render mode used by the development-time harness hosts
under `src/Harness/`.

- **Used for** — `desktop-web-harness` and `mobile-web-harness`, which host the
  same Razor components the MAUI heads render, and `ui-storybook`, which hosts
  the shared component library alone with no module or infrastructure project in
  its graph.
- **Why** — Aspire cannot start a MAUI head as a project resource and Playwright
  cannot drive one without an emulator, so the harnesses give the same UI a URL.
  Everything under `src/Harness/` is non-publishable and is fenced off from
  shipping code by `Backlog.ArchitectureTests`.

## ASP.NET Core

```meta
status: adopted
type: framework
depends-on: [".tech/shared.md#net-runtime", ".tech/shared.md#c-language"]
```

The web host underneath every HTTP surface in the solution
(`Microsoft.NET.Sdk.Web`).

- **Used for** — the sync service, the three harness hosts, and the local Azure
  Foundry test service.
- **Why** — the organization's governed .NET stack for services; it is also what
  Blazor Server and the Aspire service defaults build on.

## .NET Aspire

```meta
status: adopted
type: framework
version: "13.5.2"
depends-on: [".tech/shared.md#aspnet-core", ".tech/shared.md#c-language"]
related: [".tech/tooling.md#aspire-cli", ".tech/shared.md#opentelemetry", ".arc42/07-deployment-view.md"]
alternatives: ["Docker Compose only", "no orchestration"]
```

The app model that composes every runnable piece of this repository into one
local run.

- **Used for** — the AppHost starts the sync service, the three web harnesses,
  and the Foundry test service, and registers the desktop head, the Android
  head, and two VS Code extension resources behind `WithExplicitStart()`.
  `Backlog.Aspire.ServiceDefaults` gives every host the same telemetry,
  resilience, service discovery, and health checks.
- **Why** — one command starts the whole system with a dashboard, logs, and
  traces, which is what makes agent-driven QA possible at all. It spans every
  channel rather than only the cloud service, which is why it is documented here
  and not in `cloud.md`.
- **How** — ports are always dynamic (`localhost:0`), and worktree sessions run
  `aspire start --isolated` so parallel sessions get independent ports and
  user-secrets state. The AppHost stays opted out of the Aspire CLI bundle
  (`AspireUseCliBundle=false`, with `ASPIRE010` silenced deliberately).

## OpenTelemetry

```meta
status: adopted
type: library
version: "1.18.0"
depends-on: [".tech/shared.md#net-runtime"]
related: [".arc42/08-crosscutting-concepts.md", ".arc42/09-architecture-decisions.md", ".tech/shared.md#net-aspire"]
```

The observability stack wired into every host through `ServiceDefaults`.

- **Used for** — traces, metrics, and logs exported over OTLP to the Aspire
  dashboard, with HTTP-client and runtime instrumentation enabled
  (`OpenTelemetry.Extensions.Hosting`, `.Exporter.OpenTelemetryProtocol`,
  `.Instrumentation.Http`, `.Instrumentation.Runtime`).
- **Why** — org ADR 0010 governs observability for the sync service, and the same
  wiring is what the QA workflow monitors during a validation run.

## SQLite

```meta
status: adopted
type: library
depends-on: [".tech/shared.md#markdown"]
related: [".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/08-crosscutting-concepts.md#storage-and-sync"]
alternatives: ["Markdown files with YAML frontmatter", "LiteDB", "Azure Cosmos DB"]
```

The embedded database that is the canonical local store for tasks.

- **Used for** — one `tasks` table: scalar columns for the scalar fields, JSON
  text columns for the six owned collections, and the entry's markdown as one of
  those columns.
- **Why** — accepted by
  `.arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md`, which retired
  an arrangement of three files that could disagree about one task. It
  supersedes "markdown is the canonical format" for storage without touching
  markdown as the published entry language (ADR 0002).

## Microsoft.Data.Sqlite

```meta
status: adopted
type: package
version: "10.0.11"
depends-on: [".tech/shared.md#sqlite", ".tech/shared.md#net-runtime"]
```

The ADO.NET provider the SQLite adapter is written against.

- **Used for** — `Backlog.Infrastructure.Sqlite`, the only project that
  references it; everything else reaches the store through a port.
- **Why** — the first-party, dependency-light provider. No ORM is wanted for a
  single-table schema.

## Microsoft.Extensions.DependencyInjection

```meta
status: adopted
type: package
version: "10.0.11"
depends-on: [".tech/shared.md#net-runtime"]
```

The container abstraction each module registers itself into.

- **Used for** — the `Add<Module>Module()` extension methods that let a host
  compose modules without seeing their internals. Module projects reference the
  `.Abstractions` package only, never a container implementation.
- **Why** — the standard .NET composition seam, and taking only the abstractions
  keeps a module host-agnostic.

## Microsoft.Extensions.Http

```meta
status: adopted
type: package
version: "10.0.11"
depends-on: [".tech/shared.md#net-runtime"]
```

`IHttpClientFactory` and typed HTTP clients.

- **Used for** — the outbound clients in `Backlog.Mobile.UI`, and the base every
  vendor adapter (GitHub, Claude, Azure Foundry) is configured through.
- **Why** — correct socket lifetime handling, and one place to attach handlers.

## Microsoft.Extensions.Http.Resilience

```meta
status: adopted
type: package
version: "10.9.0"
depends-on: [".tech/shared.md#microsoftextensionshttp"]
```

Standard retry, timeout, and circuit-breaker handlers for HTTP clients.

- **Used for** — the standard resilience handler `Backlog.Aspire.ServiceDefaults`
  applies to every registered client.
- **Why** — every external call in this system crosses a network the app does not
  control, and the shipped defaults beat per-call ad-hoc retries.

## Microsoft.Extensions.ServiceDiscovery

```meta
status: adopted
type: package
version: "10.9.0"
depends-on: [".tech/shared.md#net-aspire"]
```

Logical-name resolution for service endpoints.

- **Used for** — resolving `sync` and `azure-foundry-test` from a harness or a
  channel host, so no run hard-codes a port.
- **Why** — Aspire assigns dynamic ports on every run; service discovery is what
  makes that safe.

## Microsoft.Extensions.Logging

```meta
status: adopted
type: package
version: "10.0.11"
depends-on: [".tech/shared.md#net-runtime"]
related: [".tech/shared.md#opentelemetry"]
```

The logging abstraction, plus the debug provider on the two MAUI heads.

- **Used for** — application logging everywhere.
  `Microsoft.Extensions.Logging.Debug` is referenced by `Backlog.Desktop` and
  `Backlog.Mobile` so a debugger-attached run has output without a console.
- **Why** — it is the abstraction OpenTelemetry's log pipeline consumes.

## YamlDotNet

```meta
status: hold
type: package
version: "18.1.0"
depends-on: [".tech/shared.md#net-runtime"]
related: [".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md"]
alternatives: ["Markdig", "hand-written meta-block parser"]
```

A YAML serializer, pinned centrally but no longer referenced by any project.

- **Used for** — nothing directly today. It reaches the build transitively
  through the Aspire AppHost; the `PackageVersion` entry records the version
  that resolution lands on.
- **Why `hold`** — it was the frontmatter serializer for the markdown task store
  that `.arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md` retired, and
  that ADR names its round-trip repair pass as one of the reasons for the move.
  Kept because the transitive dependency is still real; do not add new usage.

## GitHub Platform

```meta
status: adopted
type: service
related: [".arc42/02-constraints.md#organizational--process-constraints"]
```

The external system of record for issues, repositories, and automation.

- **Used for** — backlog issue sync, webhooks into the cloud service, repository
  health signals, release hosting, CI/CD, and this repository itself.
- **Why** — a hard organizational constraint: GitHub is the external issue
  system, so the product integrates with it rather than replacing it.

## Anthropic Claude Platform

```meta
status: candidate
type: service
related: [".domain/productivity/features.md#ai-vendor-usage-import", ".tech/ai-development.md#claude-code"]
alternatives: ["Local accumulation of per-response token counts"]
```

The external source of measured Claude usage: token counts, cost, and Claude
Code session activity.

- **Used for** — importing usage evidence for productivity metrics through the
  Admin API's usage, cost, and Claude Code reports, via
  `Backlog.Infrastructure.Claude`.
- **Why** — it is the only authoritative record of what was actually spent.
  Kept a candidate because the reports are organization-scoped: Anthropic
  documents the Admin API as unavailable to individual accounts, so this
  dependency only pays off for a person whose subscription sits behind an
  organization. The recorded alternative covers everyone else.

## GitHub Copilot Usage APIs

```meta
status: candidate
type: service
depends-on: [".tech/shared.md#github-platform"]
related: [".domain/productivity/features.md#ai-vendor-usage-import"]
```

The organization-level record of Copilot seat activity and usage metrics.

- **Used for** — importing Copilot activity evidence alongside Claude usage, over
  the same transport the product already uses for issues (`CopilotUsageClient` in
  `Backlog.Infrastructure.GitHub`).
- **Why** — the same reasoning as above, with a sharper limit: GitHub publishes
  no endpoint for an individual subscriber's own Copilot usage at all, and the
  organization endpoints are owner-only. Everything here is therefore
  conditional on the person owning an organization.
