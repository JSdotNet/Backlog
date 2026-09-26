@AGENTS.md

`AGENTS.md` holds this repository's standing rules; the import above expands it at launch, so
everything it says applies here. Claude-specific notes follow.

# Backlog — Claude Code instructions

Backlog is a local-first, AI-first work management product: desktop, mobile, and IDE
channels plus a thin cloud sync service. Solution: `Backlog.sln`. Product code under
`src/` (including development-time hosts under `src/Harness/`), tests under `tests/`.

This file carries the repository rules that apply to **Claude Code**.

## Delivery gate

**Before the first `Edit` or `Write` to any file under `src/` or `tests/`, invoke
`delivery:flow-code`** — or `delivery:flow-update-packages` for a dependency move.

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
`design/`, `ai/`) run through `delivery:flow-spec`, not the code gate. See `## QA Depth`
in `.claude/orch-context.md` for how they are verified instead.

## Delivery surfaces

Every flow reports its stages to the bound delivery surfaces — the `delivery-surface-*`
MCP servers, `mcp__plugin_delivery-surface-backlog_delivery-surface-backlog__*` and
`mcp__plugin_delivery-surface-dashboard_delivery-surface-dashboard__*`. Skip surface calls
only when a surface genuinely answers unavailable; do not substitute chat-only tracking
when the tools are present but erroring.

Never skip Personal Validation, and never create a pull request or mark a flow complete
without explicit user approval.

## Runtime configuration

`.claude/orch-context.md` holds the runtime facts a flow needs — how to run the Aspire
AppHost, which harness resources to target for UI validation, healthy-startup signals, and
default QA depth. `.agents/skills/start.md` is the procedure that uses them.

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

## Devbook database

The derived knowledge layer is **one generated SQLite database per repository path**,
holding the reference graph, the resolved reading outline, every chapter's text and
hashes, the FTS5 index and the Archify artifact rows. It lives in the **app's storage,
never in a repository**: `_databases/<name>-<hash>/devbook.db` under the devbook cache
folder (`devbook-cache` under the storage folder by default —
`%LOCALAPPDATA%\Backlog.Debug\devbook-cache` for a debug build and the harness), keyed by
the repository root's absolute path, so every worktree has its own. **The app builds it**,
in the background, the first time a repository's devbook is read and again whenever an
input changed; nothing needs running and nothing needs ignoring. Local ADR 0004
(`.devbook/arc42/adr/0004-knowledge-index-is-a-generated-local-database.md`) is what the
database is; local ADR 0015
(`.devbook/arc42/adr/0015-devbook-database-lives-in-app-storage-and-the-app-builds-it.md`)
is where it lives and who writes it. A `.devbook/_meta/devbook.db` or `_meta/devbook.db`
left by the old writer is ignored by the app and safe to delete.

Nothing about the layer is authored. The reading order is derived the way the devbook
generator derives it — `index: root` on a directory's root document, otherwise the
folder's convention root (`context-map.md`, `context.md`, `technology-graph.md`,
`README.md`, `adoption-map.md`), then the numbers in filenames and the folder's
convention slots. There is no `_reading-order.json`: local ADR 0016 retired it, and the
product ignores one in any repository. Moving a chapter means renaming or numbering it,
or marking a new root with `index: root`.

The desktop Devbook panels **load from the database at runtime** and degrade in
defined steps rather than on or off: a current row is served from the database, a file
that has changed since it was indexed is read from its Markdown, an unrecognised schema
version is ignored entirely, and an absent or unreadable database falls back to scanning
the folder — which is what the panels did before any index existed. So browsing always
works. Search is the one exception: without a database it is unavailable and says so,
because scanning the corpus per query is a hang rather than a fallback — until the first
background build lands. `Backlog.Infrastructure.Devbook` holds the reader, the builder
(`DevbookDatabaseBuilder`, a port of the Node writer's parse) and the refresher that
schedules it.

The product reads both layouts: `.devbook/<name>` first and the root-level `.<name>` as
the legacy fallback. This repository is on the `.devbook/` layout, adopted through the
`devbook` plugin (`components.devbook` in `.devbook/config.json` records the release,
contract, adopted folders, and what it materialized).

`tools/devbook/build-database.mjs` stays as the reference the C# builder is held to: it
imports the devbook generator materialized at `.devbook/_tools/devbook-meta/`, and
`DevbookBuilderParityTests` builds this repository's `.devbook/` both ways and compares
the tables, which needs Node 22.5+ on the path. A devbook plugin release that changes the
parse fails that test; port the change into `Backlog.Infrastructure.Devbook/Building/`
in the same pull request. The schema both writers load is `tools/devbook/devbook-schema.sql`.

```powershell
node tools/devbook/build-database.mjs --check
```

The devbook folders follow the plugin's rules, installed as `.agents/rules/devbook-*.md`
with a wrapper per host, and its section of `AGENTS.md`. Check them before committing; the
check writes nothing, and `.github/workflows/devbook-meta.yml` runs it in CI:

```powershell
node .devbook/_tools/devbook-meta/build.mjs --check
```

Never edit anything under `.devbook/_tools/`, the `AGENTS.md` markers, or the
`devbook-*` rule trios by hand: `devbook:update` refreshes them, and a hand edit makes it
report the file customized and stop maintaining it. Never hand-edit anything under
`_meta/`.

`tools/devbook/build-database.mjs` and `tools/devbook/check-metadata.mjs` are repo-native;
the second is the metadata half of the gate, run by `.github/workflows/devbook-metadata.yml`
beside the installed `devbook-meta.yml`. The `knowledge-base` install that preceded the
`devbook` plugin is retired with the root layout it indexed. Where this repository departs from the derived-artifacts
convention — on format, and on committing — ADR 0004 says so and says why.
`update-devbook-index` is this repository's own command and ships as
`.claude/commands/update-devbook-index.md`.

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

Repository guidance is **checked in, not fetched**. The `jsdotnet-project-guidelines` and
`jsdotnet-project-design` MCP servers were retired on 2026-08-27 and their relevant content
lives in the repository:

- `.devbook/arc42/adr/guidelines/` — the inherited organization architecture decisions that govern this
  repository's .NET code (framework, package management, Aspire, Result objects, module and
  feature-slice structure, CQRS, Minimal APIs, observability, styling tokens, identity,
  authorization, persistence, resilience, error contract, configuration). Read the single
  document that governs the change you are making; `README.md` indexes them, and each one
  ends with a **Deviations and gaps** section recording where Backlog actually stands.
- `.devbook/arc42/adr/` — the decisions Backlog took for itself. Both sequences start at 0001, so
  name the folder when citing one.
- `.devbook/design/` — design and UX guidance, tokens, and the color scheme.

A skill that instructs you to consult `jsdotnet-guidelines-mcpserver` is served from
`.devbook/arc42/adr/guidelines/` instead; the absent server is not a blocked precondition. The MCP
servers still in use are runtime and tooling servers — Backlog's own, Aspire, Playwright,
Microsoft Learn, and the delivery surfaces.

## Further guidance

Path-scoped rules are authored once under `.agents/rules/` and wrapped in
`.claude/rules/<topic>.md` (`paths`). `.agents/rules/README.md` is the convention.

- `.agents/rules/context-loading.md` — the full gate and the policy on
  which devbook folders a workflow may load.
- `.agents/rules/ui-components.md` — shared component adoption in the
  application screens.
- `.agents/rules/storybook.md` — authoring a storybook page and a
  story; the rules it satisfies are in `.devbook/design/README.md#living-reference-the-ui-storybook`.
- `.agents/rules/mcp-usage.md` — guidance authority order and which MCP servers remain in use.
- `.claude/orch-context.md` — repo runtime and QA context.
- `plugins/backlog-tools/skills/backlog-import-plan/SKILL.md` — generates a Backlog import
  plan (ADR 0007) from an agreed specification; user-invoked only.
- `plugins/backlog-tools/skills/backlog-run-plan-item/SKILL.md` — runs one item of such a
  plan pasted back out of the Backlog app, after checking it is still outstanding.
