# Copilot Orchestration Repo Context

Repo-specific startup and QA context for `orch-*` orchestration runs in `JSdotNet/Backlog`.
General orchestration routing and enforcement come from the `copilot-app` plugin (GitHub
Copilot) or the `claude-desktop` plugin (Claude Code); this file only supplies what is
specific to this repository, and is read by both.

The `claude-desktop` plugin reads `.claude/orch-context.md`, which carries the same facts in
that plugin’s required schema; neither toolchain supports includes, so the two files are
maintained side by side. **When the AppHost path, resource names, startup signals, or QA
depth change, update both.** Everything below is toolchain-neutral runtime fact — AppHost
path, how to run, base URLs, healthy startup, QA depth.

## Application

**Runnable application:** Backlog — a local-first work management product composed of
desktop, mobile, and IDE channels plus a thin cloud sync service.

**AppHost project:** `src/Aspire/Backlog.Aspire.AppHost/Backlog.Aspire.AppHost.csproj`
(also declared in `aspire.config.json`).

Solution: `Backlog.sln`. Product code lives under `src/`, including development-time hosts under `src/Harness/`,
and automated tests live under `tests/`.

The `src/Harness/` projects are **test harnesses, not shipped channels**. They are Blazor
Server hosts of the shared Razor components, and they exist specifically so the UI can be
started by Aspire and driven by Playwright — the MAUI heads cannot be automated that way.
Target them for UI validation. `ui-storybook` is the exception in kind: it hosts the shared
component library on its own, with no app or cloud reference, so a single component can be
validated without the application around it.

This repository also carries the checked-in knowledge folders (`.arc42/`, `.domain/`,
`.backlog/`, `.tech/`, `.design/`) and generator tooling under `.github/tools/knowledge-meta/`.
Changes confined to those folders are documentation work — see `## QA Depth`.

## How to Run

From the repository root:

```powershell
aspire start --isolated --non-interactive --apphost src\Aspire\Backlog.Aspire.AppHost\Backlog.Aspire.AppHost.csproj
```

Use `--isolated` for Copilot worktrees and any other parallel local session so Aspire
assigns independent resource ports and user-secrets state per run. For a single human-run
foreground session, `aspire run` from the repository root is also acceptable.

Or without the Aspire CLI for a single local session:

```powershell
dotnet run --project src/Aspire/Backlog.Aspire.AppHost
```

Build and test:

```powershell
dotnet build Backlog.sln
dotnet test Backlog.sln
```

Only `sync`, `azure-foundry-test`, `desktop-web-harness`, `mobile-web-harness`, and
`ui-storybook` start automatically. The `desktop`, `mobile-android`, `ide-vscode-build`, and
`ide-vscode-host` resources are registered with `WithExplicitStart()` and must be started
deliberately from the dashboard — do not treat them as failed startups when they sit idle.

## Base URLs

**Ports are dynamic.** Every host is configured with `localhost:0`, so the OS assigns
a free port per run. Never hard-code a port or assume one from a previous session — read the
actual URLs from the Aspire dashboard or the AppHost startup output, then use those.

| Resource | What it is |
| --- | --- |
| Aspire dashboard | Entry point; lists every resource with its resolved URL |
| `desktop-web-harness` | Desktop UI components in the browser — primary Playwright target |
| `mobile-web-harness` | Same components at phone width — mobile Playwright target |
| `ui-storybook` | Every shared component on its own, with no app behind it |
| `sync` | Thin sync service the harnesses reference |

## Test Credentials

None required. Backlog is local-first and the harnesses expose no authenticated surface.

If credentials become necessary later, record only a **pointer** here (for example the name
of the secret store, vault, or user-secrets entry). Never place actual secrets, tokens, or
passwords in this file or anywhere else in the repository.

GitHub operations use the repository account, as stated in `.github/github-app.yml`; pull
requests are created through the `pr-jsdotnet` skill.

## MCP Servers

Authority order and fallbacks are defined in
`.github/instructions/mcp-usage.instructions.md`.

**Guidance no longer comes from an MCP server.** The `jsdotnet-project-guidelines` and
`jsdotnet-project-design` servers were retired on 2026-08-27, and their relevant content now
lives in the repository:

- **`.arc42/adr/guidelines/`** — the inherited organization architecture decisions that govern
  this repository's .NET code. Read the one that governs the change before making it; the
  folder's `README.md` indexes them.
- **`.arc42/adr/`** — the decisions Backlog took for itself.
- **`.design/`** — design and UX guidance, including the color scheme and design tokens.

A plugin-provided skill that instructs you to query `jsdotnet-guidelines-mcpserver` should be
served from `.arc42/adr/guidelines/` instead; the absent server is not a blocked precondition.
The MCP servers still in use are runtime and tooling servers — Aspire, Playwright, and the
orchestration dashboard.

## Healthy Startup

Startup is healthy when the Aspire dashboard is reachable and `sync`,
`desktop-web-harness`, `mobile-web-harness`, and `ui-storybook` all reach **Running**. The
four `WithExplicitStart()` resources staying `NotStarted` is expected, not a failure.

`mobile-web-harness` has a `WaitFor(sync)` dependency, so it starts after `sync` becomes
healthy — a brief wait there is normal. `ui-storybook` waits for nothing, because it
references nothing.

For changes confined to the knowledge folders, "healthy" instead means the repository-level
checks pass:

- Governed Markdown keeps the `meta` blocks required by the `knowledge-base` plugin's
  `knowledge-chapter-metadata.instructions.md`.
- Instruction files keep valid `applyTo` and `description` frontmatter.
- Derived `_meta/` artifacts are regenerated rather than hand-edited, and
  `node .github/tools/knowledge-meta/build.mjs --check` passes with a clean
  `git diff` over `*_meta/*.json`.

## QA Depth

playwright-qa

Validate UI behavior against `desktop-web-harness` (and `mobile-web-harness` for
phone-width behavior), discovering their URLs at run time rather than assuming ports.

Two standing exceptions:

- **Documentation-only changes** — edits confined to `.arc42/`, `.domain/`, `.backlog/`,
  `.tech/`, `.design/`, `.github/`, or `README.md` have no runtime surface. Verification is
  documentation review plus `build.mjs --check`; skip startup and Playwright.
- **Non-UI code changes** — work confined to `tests/`, `src/Core/Backlog.SharedKernel`, or
  `src/Infrastructure/` with no user-visible behavior change is adequately covered by
  `dotnet test`; `targeted` depth is sufficient. `src/Core/` as a whole does **not**
  qualify: `src/Core/Backlog.UI.Components` is the shared control library every screen
  renders, so a change there takes the full `playwright-qa` depth against
  `ui-storybook` and `desktop-web-harness`.


## Orchestration Skill Sources

This repository ships no repo-native `orch-*` skills. The knowledge-folder orchestrations
(`orch-arc42-content`, `orch-domain`, `orch-backlog`, `orch-tech`, `orch-design`) come from
the `knowledge-base` plugin; every other orchestration — including `orch-fallback`, the
generic entrypoint for task categories with no dedicated `orch-*` skill — comes from the
`copilot-app` plugin.

The only skill under `.github/skills/` is `pr-jsdotnet`, and it is a pull-request workflow
rather than an orchestration; see `.github/copilot-instructions.md`.

