# Backlog — Claude Code instructions

Backlog is a local-first, AI-first work management product: desktop, mobile, and IDE
channels plus a thin cloud sync service. Solution: `Backlog.sln`. Product code under
`src/` (including development-time hosts under `src/harness/`), tests under `tests/`.

This file carries the repository rules that apply to **Claude Code**. The equivalent
GitHub Copilot instructions live in `.github/copilot-instructions.md`; where the two
describe the same rule, the difference is only which agent and tool names are used.

## Orchestration gate

**Before the first `Edit` or `Write` to any file under `src/` or `tests/`, invoke the
matching `orch-*` skill through the `claude-desktop:orchestrator` agent.**

Reading, searching, and exploring are always allowed first — the gate is on the first
write, not the first action, so orienting yourself does not consume it.

Apply the gate literally:

- **Size is not a criterion.** A one-control UI tweak and a multi-service feature route
  the same way. Do not reason about whether a request is "big enough" to orchestrate.
- **A missing specification is not an exemption.** Ad-hoc requests with no story,
  acceptance criteria, or approved design still route through `orch-feature` or
  `orch-bug`; the skill derives the missing scope in its first stage.
- **Unmet preconditions are not an exemption.** If the matched skill's stated inputs are
  absent, invoke it anyway and derive them inside it.
- **No match means `orch-fallback`,** not direct implementation. It is the generic
  entrypoint for categories no dedicated `orch-*` skill covers — a last resort, not an
  escape hatch from a skill whose preconditions are inconvenient.
- Never proceed straight from exploration to implementation.

Routing (which category maps to which `orch-*` skill) comes from the `claude-desktop`
plugin's `SessionStart` hook. If that routing context is not present in your session,
treat this file as the source of the gate and pick the skill by category from
`.github/instructions/context-loading.instructions.md`.

This repository ships no repo-native `orch-*` skills. Every entrypoint is plugin-provided:
the knowledge-folder orchestrations come from `knowledge-base`, and the rest — `orch-fallback`
included — from `claude-desktop`. The only skill under `.github/skills/` is `pr-jsdotnet`,
which is a pull-request workflow rather than an orchestration.

Changes confined to `.arc42/`, `.domain/`, `.backlog/`, `.tech/`, `.design/`, `.github/`,
or `README.md` are documentation work and do not pass through the code gate. See
`## QA Depth` in `.github/copilot-orch-context.md` for how they are verified instead.

## Dashboard

Every orchestration reports progress through the orch-dashboard MCP server. Because it is
plugin-provided, its tools are namespaced: `mcp__plugin_claude-desktop_orch-dashboard__*`
— for example `mcp__plugin_claude-desktop_orch-dashboard__open_dashboard`. They are **not**
`mcp__orch-dashboard__*`.

Open the dashboard once per run, call `start_run` for the selected skill, and track each
stage there. Skip dashboard calls only when the server is genuinely unavailable; do not
substitute chat-only tracking when the tools are present but erroring.

Never skip Personal Validation, and never create a pull request or mark an orchestration
complete without explicit user approval.

## Orchestration configuration

The `claude-desktop` plugin reads `.claude/orch-context.md` and `.claude/model-selection.md`,
falling back to the `.github/copilot-*` equivalents this repository already carries:

- `.github/copilot-orch-context.md` — how to run the Aspire AppHost, which harness
  resources to target for UI validation, healthy-startup signals, and default QA depth.
- `.github/copilot-model-selection.md` — per-category model overrides for orchestration runs.

## Running and testing

```powershell
aspire start --isolated --non-interactive --apphost src\Aspire\Backlog.Aspire.AppHost\Backlog.Aspire.AppHost.csproj
```

Use `--isolated` for worktree sessions and any other parallel local session so Aspire
assigns independent ports and user-secrets state per run. **Ports are dynamic** — every
host binds `localhost:0`. Never hard-code a port or reuse one from a previous session;
read the actual URLs from the Aspire dashboard or AppHost startup output.

```powershell
dotnet build Backlog.sln
dotnet test Backlog.sln
```

`desktop`, `mobile-android`, `ide-vscode-build`, and `ide-vscode-host` use
`WithExplicitStart()`. Them sitting `NotStarted` is expected, not a failed startup.

## Further guidance

- `.github/instructions/context-loading.instructions.md` — the full gate and the policy on
  which knowledge folders a workflow may load.
- `.github/instructions/mcp-usage.instructions.md` — MCP server authority order.
- `.github/copilot-orch-context.md` — repo runtime and QA context.
