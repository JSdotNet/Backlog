# Orchestration Repo Context

Repo-specific startup and QA context for `orch-*` orchestration runs in `JSdotNet/Backlog`,
read by the `claude-desktop` plugin's orchestrator once per run.

This is the Claude Code copy of the runtime facts. `.github/copilot-orch-context.md` is the
GitHub Copilot copy and carries the same facts; neither toolchain supports includes, so the
two files are maintained side by side. **When the AppHost path, resource names, startup
signals, or QA depth change, update both.** Model choice is not configured in this
repository; orchestration runs use the plugin defaults unless overridden per run.

## Application

- **Runnable application:** Backlog — a local-first work management product composed of
  desktop, mobile, and IDE channels plus a thin cloud sync service.
- **AppHost project:** `src/Aspire/Backlog.Aspire.AppHost/Backlog.Aspire.AppHost.csproj`
  (also declared in `aspire.config.json`).

Solution: `Backlog.sln`. Product code lives under `src/`, including development-time hosts
under `src/Harness/`, and automated tests live under `tests/`.

The `src/Harness/` projects are **test harnesses, not shipped channels**. They are Blazor
Server hosts of the shared Razor components, and they exist specifically so the UI can be
started by Aspire and driven by Playwright — the MAUI heads cannot be automated that way.
Target them for UI validation. `ui-storybook` is the exception in kind: it hosts the shared
component library on its own, with no app or cloud reference, so a single component can be
validated without the application around it.

This repository also carries the checked-in knowledge folders (`.arc42/`, `.domain/`,
`.backlog/`, `.tech/`, `.design/`) and generator tooling under
`.github/tools/knowledge-meta/`. Changes confined to those folders are documentation work —
see `## QA Depth`.

## How to Run

From the repository root:

```powershell
aspire start --isolated --non-interactive --apphost src\Aspire\Backlog.Aspire.AppHost\Backlog.Aspire.AppHost.csproj
```

Use `--isolated` for worktree sessions and any other parallel local session so Aspire
assigns independent resource ports and user-secrets state per run. For a single human-run
foreground session, `aspire run` from the repository root is also acceptable.

Or without the Aspire CLI, for a single local session:

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

**Ports are dynamic.** Every host is configured with `localhost:0`, so the OS assigns a free
port per run. Never hard-code a port or assume one from a previous session — read the actual
URLs from the Aspire dashboard or the AppHost startup output, then use those.

- **Aspire dashboard** — entry point; lists every resource with its resolved URL.
- **`desktop-web-harness`** — desktop UI components in the browser; primary Playwright target.
- **`mobile-web-harness`** — the same components at phone width; mobile Playwright target.
- **`ui-storybook`** — every shared component on its own, with no app behind it.
- **`sync`** — the thin sync service the harnesses reference.
- **`azure-foundry-test`** — local Azure Foundry chat stand-in that `desktop-web-harness`
  waits for and reads through `BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT`.

## Test Credentials

None required. Backlog is local-first and the harnesses expose no authenticated surface.

If credentials become necessary later, record only a **pointer** here (for example the name
of the secret store, vault, or user-secrets entry). Never place actual secrets, tokens, or
passwords in this file or anywhere else in the repository.

GitHub operations use the repository account, as stated in `.github/github-app.yml`; pull
requests are created through the `pr-jsdotnet` skill.

## MCP Servers

Authority order and fallbacks are defined in
`.github/instructions/mcp-usage.instructions.md`, which remains the source of truth.

**Guidance no longer comes from an MCP server.** The `jsdotnet-project-guidelines` and
`jsdotnet-project-design` servers were retired on 2026-08-27; read `.arc42/adr/guidelines/` for
inherited architecture decisions, `.arc42/adr/` for local ones, and `.design/` for design and
UX guidance. A plugin skill that tells you to query `jsdotnet-guidelines-mcpserver` should be
served from `.arc42/adr/guidelines/` instead — its absence is not a blocked precondition.

Available to orchestration runs in Claude Code:

- `plugin_qa_aspire` — Aspire resource state, console logs, structured logs, and traces.
- `plugin_qa_playwright` — browser automation for QA validation.
- `plugin_claude-desktop_orch-dashboard` — orchestration progress reporting. Its tools are
  namespaced `mcp__plugin_claude-desktop_orch-dashboard__*`, **not** `mcp__orch-dashboard__*`.
- `jsdotnet-publish-results` — publishing orchestration reports and artifacts.

If a runtime MCP server is unavailable, say so plainly rather than substituting a guess —
and for guidance, there is nothing to fall back from: the checked-in documents are the
authority.

## Healthy Startup

Startup is healthy when the Aspire dashboard is reachable and `sync`, `azure-foundry-test`,
`desktop-web-harness`, `mobile-web-harness`, and `ui-storybook` all reach **Running**. The
four `WithExplicitStart()` resources staying `NotStarted` is expected, not a failure.

`desktop-web-harness` has a `WaitFor(azure-foundry-test)` dependency and `mobile-web-harness`
has a `WaitFor(sync)` dependency, so both start after their dependency becomes healthy — a
brief wait there is normal. `ui-storybook` waits for nothing, because it references nothing.

For changes confined to the knowledge folders, "healthy" instead means the repository-level
checks pass:

- Governed Markdown keeps the `meta` blocks required by the `knowledge-base` plugin's
  `knowledge-chapter-metadata.instructions.md`.
- Instruction files keep valid `applyTo` and `description` frontmatter.
- Derived `_meta/` artifacts are regenerated rather than hand-edited, and
  `node .github/tools/knowledge-meta/build.mjs --check` passes with a clean `git diff` over
  `*_meta/*.json`.

## QA Depth

`playwright-qa`

Validate UI behavior against `desktop-web-harness` (and `mobile-web-harness` for phone-width
behavior), discovering their URLs at run time rather than assuming ports. Use `ui-storybook`
when a single shared component can be validated without the application around it.

Two standing exceptions:

- **Documentation-only changes** — edits confined to `.arc42/`, `.domain/`, `.backlog/`,
  `.tech/`, `.design/`, `.github/`, `.claude/`, or `README.md` have no runtime surface.
  Verification is documentation review plus `build.mjs --check`; skip startup and Playwright.
- **Non-UI code changes** — work confined to `tests/`, `src/Shared/`, or
  `src/Infrastructure/` with no user-visible behavior change is adequately covered by
  `dotnet test`; `targeted` depth is sufficient.
