@AGENTS.md

`AGENTS.md` holds this repository's standing rules; the import above expands it at launch, so
everything it says applies here. Claude-specific notes follow.

# Backlog — Claude Code instructions

Backlog is a local-first, AI-first work management product: desktop, mobile, and IDE
channels plus a thin cloud sync service. Solution: `Backlog.sln`. Product code under
`src/` (including development-time hosts under `src/Harness/`), tests under `tests/`.

This file carries the repository rules that apply to **Claude Code**.

## Delivery gate

**Before the first `Edit` or `Write` to any file under `src/` or `tests/`, invoke the
matching flow:**

| Change | Flow |
| --- | --- |
| Code, and tooling, CI, scripting, documentation outside `.devbook/`, housekeeping | `delivery:flow-code` |
| A devbook chapter, decision record or debt record under `.devbook/` | `delivery:flow-spec` |
| A dependency, package or framework move | `delivery:flow-update-packages` |
| Creating, governing or scaffolding a repository | `delivery:flow-project` |

Reading, searching, and exploring are always allowed first — the gate is on the first
write, not the first action, so orienting yourself does not consume it.

Apply the gate literally:

- **Size is not a criterion.** A one-control UI tweak and a multi-service feature route
  the same way. Do not reason about whether a request is "big enough" for a flow.
- **A missing specification is not an exemption.** Ad-hoc requests with no story,
  acceptance criteria, or approved design still route through `flow-code`; its Scope
  Discovery stage derives the missing scope.
- **Unmet preconditions are not an exemption.** If the flow's stated inputs are absent,
  invoke it anyway and derive them inside it.
- Never proceed straight from exploration to implementation.

**Standing authorization.** The repository owner authorizes running any `flow-*` skill
and the agents it hands stages to for work that hits this gate, without per-session
confirmation. A session-level instruction restricting agent spawning does not exempt code
changes from the gate — treat this paragraph as the request.

Routing (which category maps to which flow) comes from the `delivery` plugin's
`SessionStart` context. If that context is not present in your session, treat this file as
the source of the gate and `.agents/rules/context-loading.md` as the detail.

This repository ships no repo-native `flow-*` skill; every flow comes from the `delivery`
plugin, which `.claude/settings.json` enables with `delivery-schedule` and `devbook`.
`.devbook/config.json` holds the bindings, extensions, and policy the flows read.

`plugins/backlog-tools` is this repository's own plugin, installed on demand rather than
auto-enabled — see `plugins/backlog-tools/README.md`. Neither of its skills changes the
paragraph above: `backlog-import-plan` is user-invoked (`disable-model-invocation: true`)
and one-shot, so it is not a flow and does not go through the gate;
`backlog-run-plan-item` is model-invoked when a plan item is pasted in, but it runs the
item's instructions *through* the gate — the matching flow — rather than adding an
execution path beside it.

Changes confined to the devbook folders under `.devbook/` (`arc42/`, `domain/`, `tech/`,
`design/`, `ai/`) run through `delivery:flow-spec`, not the code gate.
`.agents/rules/context-loading.md` says how they are verified instead.

## Delivery surfaces

Every flow reports its stages to the bound delivery surfaces — the `delivery-surface-*`
MCP servers, `mcp__plugin_delivery-surface-backlog_delivery-surface-backlog__*` and
`mcp__plugin_delivery-surface-dashboard_delivery-surface-dashboard__*`. Skip surface calls
only when a surface genuinely answers unavailable; do not substitute chat-only tracking
when the tools are present but erroring.

Never skip Personal Validation, and never create a pull request or mark a flow complete
without explicit user approval.

## Runtime configuration

The runtime facts a flow needs are the procedures': `.agents/skills/start.md` runs the
Aspire AppHost, says what healthy startup looks like and which harness answers which
question; `show.md` picks the harness for a branch's change; `debug.md` queries logs and
traces. QA depth is the engine's per change kind, capped by `policy` in
`.devbook/config.json`.

This repository configures no model overrides. Flows use each plugin's default model per
category unless a run is given an explicit model instruction.

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

## Devbook

The devbook folders sit under `.devbook/`, adopted through the `devbook` plugin
(`components.devbook` in `.devbook/config.json`). `AGENTS.md` and
`.agents/rules/devbook-*.md` are their rules. Check them before committing — the check
writes nothing, and `.github/workflows/devbook-meta.yml` runs it in CI:

```powershell
node .devbook/_tools/devbook-meta/build.mjs --check
```

Never hand-edit `.devbook/_tools/`, the `AGENTS.md` markers, the `devbook-*` rule trios
(`devbook:update` owns them), or anything under `_meta/`. The reading order is derived —
`index: root`, the folder's convention root, then filename numbers; there is no
`_reading-order.json` (local ADR 0016).

The derived layer is one SQLite database per repository path, built by the app in the
background into its own storage and never into a repository (local ADRs 0004 and 0015) —
`%LOCALAPPDATA%\Backlog.Debug\devbook-cache\_databases\` for a debug build and the
harness. The Devbook panels read it and degrade to the Markdown, then to scanning the
folder, so browsing always works; search alone needs the database.
`Backlog.Infrastructure.Devbook` holds the reader, the builder and its refresher. The
product reads `.devbook/<name>` first and a root-level `.<name>` as the legacy fallback.

`tools/devbook/build-database.mjs` is the reference the C# builder is held to:
`DevbookBuilderParityTests` builds `.devbook/` both ways and compares the tables (Node
22.5+). A devbook release that changes the parse fails it; port the change into
`Backlog.Infrastructure.Devbook/Building/` in the same pull request.
`tools/devbook/check-metadata.mjs` is the repo-native metadata half of the gate, run by
`.github/workflows/devbook-metadata.yml` beside the installed `devbook-meta.yml`.
`.claude/commands/update-devbook-index.md` runs the build check.

## UI components

A screen under `src/App/` or `src/Modules/` renders the shared library's component
(`src/Core/Backlog.UI.Components`) rather than writing its own version of one. That covers
both a raw `button`/`input`/`select`/`textarea` and a plain `div`/`span`/`p` wearing a
component's own class. When a component cannot wear the screen's classes, add the hook to
the library — `BaseClass`, `CssClass`, `Bare`, or a per-part class parameter usually
already exists — rather than hand-rolling a second implementation.

`tests/Backlog.ArchitectureTests/SharedControlAdoptionTests.cs` enforces this and holds the
documented exceptions. See `.agents/rules/ui-components.md` for the full
rule, including what the test cannot see.

## Authoritative guidance

Repository guidance is **checked in, not fetched**: `.devbook/arc42/adr/guidelines/` holds
the inherited organization decisions that govern the .NET code (read the one that governs
your change; its `README.md` indexes them), `.devbook/arc42/adr/` Backlog's own — both
start at 0001, so name the folder when citing one — and `.devbook/design/` the design and
UX guidance. A skill that says to consult a guidelines MCP server reads
`.devbook/arc42/adr/guidelines/` instead; the absent server is not a blocked precondition.
`.agents/rules/mcp-usage.md` has the full authority order and the MCP servers still in use.

## Further guidance

Path-scoped rules are authored once under `.agents/rules/` and wrapped in
`.claude/rules/<topic>.md` (`paths`). `.agents/rules/README.md` is the convention.

- `.agents/rules/context-loading.md` — the repository context file: the full gate, the
  bound agents and procedures, how a no-runtime change is verified, and which devbook
  folders a workflow may load.
- `.agents/rules/ui-components.md` — shared component adoption in the
  application screens.
- `.agents/rules/storybook.md` — authoring a storybook page and a
  story; the rules it satisfies are in `.devbook/design/README.md#living-reference-the-ui-storybook`.
- `.agents/rules/mcp-usage.md` — guidance authority order and which MCP servers remain in use.
- `.agents/skills/` — the `start`, `show`, `capture`, `debug` and `estimate` procedures.
- `plugins/backlog-tools/skills/backlog-import-plan/SKILL.md` — generates a Backlog import
  plan (ADR 0007) from an agreed specification; user-invoked only.
- `plugins/backlog-tools/skills/backlog-run-plan-item/SKILL.md` — runs one item of such a
  plan pasted back out of the Backlog app, after checking it is still outstanding.
