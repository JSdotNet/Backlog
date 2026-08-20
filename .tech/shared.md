# Shared Technologies

```meta
status: candidate
related: [".tech/technology-graph.md", ".arc42/02-constraints.md#technical-constraints"]
```

> Technologies used by more than one channel. Every layer file points at these
> chapters with `depends-on` instead of redefining them locally.

## Markdown

```meta
status: adopted
kind: format
related: [".arc42/02-constraints.md#technical-constraints", ".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

The canonical storage format for all user content: inbox items, backlog items,
knowledge notes, and prompts.

- **Used for** — the single source of truth on disk; also the format of this
  repository's own `.arc42`/`.domain`/`.backlog`/`.tech` knowledge folders.
- **Why** — plain text is durable, diffable, greppable, and editable without the
  app; it is a hard constraint of the architecture.

## JSON

```meta
status: adopted
kind: format
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync"]
```

The derived-data format sitting beside the canonical Markdown.

- **Used for** — indexes (`_meta/*.json`), metadata, relationships, the repo
  registry (`config/repos.json`), and the tag graph (`.tags/`).
- **Why** — cheap to write from every channel's stack and fast to load into
  memory for local search without a database engine.

## YAML

```meta
status: adopted
kind: format
```

The structured-metadata format embedded in Markdown documents.

- **Used for** — the fenced `meta` blocks that make the knowledge folders
  machine-readable, plus GitHub workflow and issue-template configuration.
- **Why** — human-writable inside Markdown and trivially parseable by tooling.

## Mermaid

```meta
status: adopted
kind: format
depends-on: [".tech/shared.md#markdown"]
```

Diagram-as-text notation embedded directly in Markdown.

- **Used for** — the technology graph, C4/deployment/sequence diagrams in
  `.arc42`, and domain model/flow diagrams in `.domain`.
- **Why** — diagrams stay version-controlled and reviewable in the same diff as
  the prose they belong to; rendered natively by GitHub and the knowledge canvas.

## .NET Runtime

```meta
status: candidate
kind: runtime
version: "10.0"
related: [".arc42/04-solution-strategy.md#technology-choices", ".arc42/09-architecture-decisions.md"]
alternatives: ["Node.js only", "Rust + Tauri"]
```

The primary managed runtime for the desktop, mobile, IDE (Visual Studio), and
cloud channels.

- **Used for** — hosting every C#-based component of the system.
- **Why** — the organization's governed .NET guidance applies to the cloud
  service, and reusing one runtime across channels maximizes shared code.

## C# Language

```meta
status: candidate
kind: language
depends-on: [".tech/shared.md#net-runtime"]
related: [".arc42/04-solution-strategy.md#technology-choices"]
```

The main implementation language of the system.

- **Used for** — desktop, mobile, Visual Studio extension, and cloud service.
- **Why** — one language across four of the five channels keeps domain logic and
  contracts shareable.

## Node.js

```meta
status: candidate
kind: runtime
related: [".tech/ide.md#vs-code-extension-api"]
```

The JavaScript runtime hosting the VS Code extension and repository-local
tooling.

- **Used for** — running the VS Code extension host and this repository's
  Copilot canvas extension.
- **Why** — mandated by the VS Code extension model; already present on
  developer machines.

## TypeScript

```meta
status: candidate
kind: language
depends-on: [".tech/shared.md#nodejs"]
related: [".tech/ide.md#vs-code-extension-api"]
```

The implementation language for the VS Code channel and repository tooling.

- **Used for** — the VS Code extension and its webview UI, plus the
  `knowledge-meta` generator in `.github/tools/`.
- **Why** — the only first-class option for VS Code extensibility.

## .NET MAUI

```meta
status: adopted
kind: framework
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

## Blazor Hybrid

```meta
status: adopted
kind: framework
depends-on: [".tech/shared.md#net-maui"]
related: [".arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md"]
alternatives: ["XAML-only MAUI UI", "Plain WinUI 3", "Blazor WebAssembly PWA"]
```

Razor Components rendered inside the MAUI shell's embedded WebView2/WebView.

- **Used for** — the desktop client's entire UI (accepted, per ADR 0001) —
  authored as Razor components instead of XAML, so Playwright can attach over
  WebView2's CDP debugging port. On mobile it remains a candidate fallback to
  plain MAUI XAML UI, for sharing components between channels.
- **Why** — accepted for desktop by
  `.arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md`: unlike plain MAUI XAML,
  it gives desktop a Chromium-based, Playwright-drivable UI surface while
  keeping full native filesystem/background-worker access.

## GitHub Platform

```meta
status: adopted
kind: service
related: [".arc42/02-constraints.md#organizational--process-constraints"]
```

The external system of record for issues, repositories, and automation.

- **Used for** — backlog issue sync, webhooks into the cloud service, repository
  health signals, CI/CD, and this repository itself.
- **Why** — a hard organizational constraint: GitHub is the external issue
  system, so the product integrates with it rather than replacing it.

## Anthropic Claude Platform

```meta
status: candidate
kind: service
related: [".domain/productivity/features.md#ai-vendor-usage-import"]
alternatives: ["Local accumulation of per-response token counts"]
```

The external source of measured Claude usage: token counts, cost, and Claude
Code session activity.

- **Used for** — importing usage evidence for productivity metrics through the
  Admin API's usage, cost, and Claude Code reports.
- **Why** — it is the only authoritative record of what was actually spent.
  Kept a candidate because the reports are organization-scoped: Anthropic
  documents the Admin API as unavailable to individual accounts, so this
  dependency only pays off for a person whose subscription sits behind an
  organization. The recorded alternative covers everyone else.

## GitHub Copilot Usage APIs

```meta
status: candidate
kind: service
depends-on: [".tech/shared.md#github-platform"]
related: [".domain/productivity/features.md#ai-vendor-usage-import"]
```

The organization-level record of Copilot seat activity and usage metrics.

- **Used for** — importing Copilot activity evidence alongside Claude usage,
  over the same transport the product already uses for issues.
- **Why** — the same reasoning as above, with a sharper limit: GitHub publishes
  no endpoint for an individual subscriber's own Copilot usage at all, and the
  organization endpoints are owner-only. Everything here is therefore
  conditional on the person owning an organization.
