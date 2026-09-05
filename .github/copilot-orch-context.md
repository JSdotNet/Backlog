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

Or, for a single local session, without invoking the Aspire CLI yourself:

```powershell
dotnet run --project src/Aspire/Backlog.Aspire.AppHost
```

The AppHost sets `AspireUseCliBundle=true`, so this is no longer a no-CLI path: `dotnet run`
acquires the Aspire CLI pinned to the AppHost SDK version through dnx and delegates to
`aspire run`. The first run on a machine downloads it, so allow for that before reading the
absence of dashboard output as a failed startup. It still needs no CLI to be installed, and
it still gets the matching version rather than whatever is on PATH — but it does need network
access the first time.

Build and test:

```powershell
dotnet build Backlog.sln
dotnet test Backlog.sln
```

Only `sync`, `azure-foundry-test`, `desktop-web-harness`, `mobile-web-harness`, and
`ui-storybook` start automatically. The `desktop`, `mobile-android`,
`mobile-maui-android-emulator`, `mobile-tunnel`, `ide-vscode-build`, and
`ide-vscode-host` resources are registered with
`WithExplicitStart()` and must be started deliberately from the dashboard — do not treat
them as failed startups when they sit idle. Each of them needs something this machine may
not have: a desktop window, an Android emulator, the `devtunnel` CLI and an account,
`npm`, or `code`.

Start `mobile-tunnel` before `mobile-maui-android-emulator` — that emulator resource is the
child `AddAndroidEmulator()` adds under the `mobile-maui` parent, and the parent itself is a
container with nothing to start. The tunnel publishes `sync`'s HTTP endpoint so
the emulator can reach it off its own loopback, and the head is held until the tunnel's
endpoint is allocated — started on its own it waits rather than fails.

`foundry-local` is not in either list, because it is not on every machine. Foundry Local
launches the `foundry` CLI as the app model comes up rather than when the resource is
started, so `WithExplicitStart()` cannot hold it back — registering it unconditionally on a
machine without the CLI was measured leaving a `FailedToStart` resource with no start
command for the whole run, while everything else came up healthy around it. The AppHost
therefore registers it **only when `foundry` is on PATH**, and where it is registered it
starts with the app model rather than on demand. Its **absence** from the dashboard is
expected on a machine without the CLI, not a missing resource. The same conditional wiring
applies to the Android head's OTLP tunnel: it resolves the dashboard OTLP port while the
app model is built, and this repository binds every endpoint to `localhost:0`, so it is
wired only when a run pins that port.

`desktop-web-harness` carries a **Reset local data** resource command. Never run it as part
of a QA flow unless the task asked for it: every git worktree of this repository shares one
per-user `Backlog.Debug` workspace, so it wipes the task database and workspace settings of
every session on the machine, not only this one. The confirmation names the folder it would
delete from rather than assuming that one: the settings screen can point the workspace at
any rooted path, and the command refuses rather than deleting if it has moved since the
AppHost started.

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

Startup is healthy when the Aspire dashboard is reachable and `sync`, `azure-foundry-test`,
`desktop-web-harness`, `mobile-web-harness`, and `ui-storybook` all reach **Running**. Every
`WithExplicitStart()` resource listed above staying `NotStarted` is expected, not a failure.

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

### Dashboard telemetry filtering

Aspire 13.5.2 added telemetry filtering to the dashboard, and the `plugin_qa_aspire` MCP
tools reach the same filtered slice: `list_structured_logs`, `list_traces` and
`list_console_logs` each take a `resourceName` to limit the answer to one resource and a
`search` string that is matched server-side across log text, span names, attribute values,
sources and IDs.

Ask for the slice rather than the stream. A log monitor — `qa:aspire-log-monitor`, or the
`qa-monitor` agent it delegates to — should pass `resourceName` and `search` on every call
instead of pulling every line of every resource and filtering the result itself. Pulling
everything is what fills a session with output nobody reads, and it makes the monitor slower
to notice the one error it was watching for. The dashboard's own filters are the human-facing
half of the same thing: when a run is being watched by a person, filter there rather than
pasting log dumps into the conversation.

This is a QA-side change only. Nothing in the app model configures it, and no code in this
repository had to change for it.


## Orchestration Skill Sources

This repository ships no repo-native `orch-*` skills. The knowledge-folder orchestrations
(`orch-arc42-content`, `orch-domain`, `orch-backlog`, `orch-tech`, `orch-design`) come from
the `knowledge-base` plugin; every other orchestration — including `orch-fallback`, the
generic entrypoint for task categories with no dedicated `orch-*` skill — comes from the
`copilot-app` plugin.

The only skill under `.github/skills/` is `pr-jsdotnet`, and it is a pull-request workflow
rather than an orchestration; see `.github/copilot-instructions.md`.

