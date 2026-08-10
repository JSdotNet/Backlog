# Backlog

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

The solution follows the modular-monolith layout: `src/Aspire/` for orchestration,
one folder per bounded context (`src/Entries/`), `src/App/` for the channel front
ends, `src/Cloud/` for the sync service, and `src/Harness/` for hosts that only
exist to run the UI under Aspire. Automated test projects live in `tests/`.

| Project | Channel / role |
|---|---|
| `src/Aspire/Backlog.Aspire.AppHost` | .NET Aspire app model that composes all channels |
| `src/Aspire/Backlog.Aspire.ServiceDefaults` | Shared OpenTelemetry, resilience, and service discovery defaults |
| `src/Entries/Backlog.Entries` | Backlog module — domain model (entries, sub-items, lifecycle rules) |
| `src/Entries/Backlog.Entries.Data.FileSystem` | Backlog module — Markdown + JSON file storage adapter (canonical local data) |
| `src/Entries/Backlog.Entries.Integrations.GitHub` | Backlog module — GitHub issue projection adapter |
| `src/App/Backlog.Desktop.UI` | Shared Razor components for the desktop channel |
| `src/App/Backlog.Desktop` | Desktop channel — .NET MAUI Blazor Hybrid (Windows) |
| `src/App/Backlog.Mobile.UI` | Shared Razor components for the mobile channel |
| `src/App/Backlog.Mobile` | Mobile channel — .NET MAUI Blazor Hybrid (Android) |
| `src/App/Backlog.Ide.VsCode` | IDE channel — VS Code extension (TypeScript) |
| `src/Cloud/Backlog.Cloud` | Cloud channel — thin ASP.NET Core sync service (Azure) |
| `src/Harness/Backlog.Desktop.WebHarness` | **Test harness, not shipped** — Blazor Server host of `Backlog.Desktop.UI` for Aspire/Playwright |
| `src/Harness/Backlog.Mobile.WebHarness` | **Test harness, not shipped** — Blazor Server host of `Backlog.Mobile.UI` at phone width |
| `tests/Backlog.Entries.UnitTests` | Unit tests for the Backlog module (domain + storage) |
| `tests/Backlog.Desktop.UI.UnitTests` | Unit tests for the desktop UI services and GitHub integration |

Everything under `src/Harness/` is a development-time host. It ships nothing to a
user; it exists so the shared Razor components can be started by the Aspire
AppHost and driven by Playwright, which the MAUI heads cannot be.

## Running locally

```powershell
dotnet run --project src/Aspire/Backlog.Aspire.AppHost
```

The AppHost starts the cloud service and the two web test harnesses. The remaining
resources need something Aspire cannot provide on its own — a desktop
window, an Android emulator, or a VS Code extension host — so they are registered
with **explicit start** and launched on demand from the dashboard:

| Resource | Starts | Needs |
|---|---|---|
| `cloud`, `desktop-web-harness`, `mobile-web-harness` | automatically | — |
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

## Installing the desktop app

The Windows desktop app is distributed as a signed **MSIX** sideloaded from
GitHub Releases, with an App Installer (`.appinstaller`) that keeps it updated —
there is no Microsoft Store listing.

1. Open the [latest release](https://github.com/JSdotNet/Backlog/releases/latest)
   and download `Backlog.Desktop.appinstaller`.
2. Because the package is **self-signed**, trust the signing certificate on the
   machine first (import it into *Local Machine → Trusted People*), then open the
   `.appinstaller` to install.
3. Updates are checked automatically on launch (and in the background). You can
   also check on demand from **Settings → About and updates**, which offers
   "Check for updates" and "Install and restart".

Debug builds run **unpackaged** (so Aspire and the WebView2 debugging attach keep
working); the in-app updater reports "unsupported" there, which is expected.


## Language and conventions

Term definitions and naming conventions are in [domain/naming.md](domain/naming.md).
