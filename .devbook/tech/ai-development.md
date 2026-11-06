# AI-Assisted Development Stack

```meta
rating: adopted
related: [".devbook/tech/technology-graph.md", ".devbook/tech/technology-graph.md#ai-development-vocabulary", ".devbook/arc42/02-constraints.md#organizational--process-constraints"]
```

> The agent harnesses, plugins, protocols, and file conventions this repository
> is built *with*, registered at the maturity each has here as a technology.
>
> How they are used, at which stage, by whom, and how far, is the AI adoption
> record, [`.devbook/ai/`](../ai/adoption-map.md), which points back here.
>
> The chapters use the vocabulary of
> [aicodingdictionary.com](https://www.aicodingdictionary.com/), mapped to this
> repository once in
> [`technology-graph.md`](technology-graph.md#ai-development-vocabulary).

## Claude Code

```meta
rating: adopted
type: tool
related: [".devbook/tech/shared.md#anthropic-claude-platform"]
```

The agent harness this repository is built with, in its CLI and desktop-app
forms.

- **Used for** — every agent session that changes the repository; it loads
  `CLAUDE.md`, which imports `AGENTS.md`, first.
- **Why** — the project is explicitly AI-first, so its harness is part of the
  stack, not a personal preference.
- **Practices** — the adoption record's: [code gate](../ai/02-code.md#code-gate-through-flow-code),
  [worktree session per change](../ai/02-code.md#worktree-session-per-change).

## Claude Code Plugins

```meta
rating: adopted
type: plugin
depends-on: [".devbook/tech/ai-development.md#claude-code"]
related: [".devbook/tech/ai-development.md#agent-skills", ".devbook/tech/ai-development.md#devbook-plugin", ".devbook/tech/ai-development.md#delivery-engine"]
```

The distribution unit for every agent, skill, hook, and MCP server this
repository uses.

- **Used for** — three marketplaces:
  - `JSdotNet/devbook` (`jsdotnet-devbook`), for the delivery engine, its
    schedules and surfaces, and the devbook convention;
  - `JSdotNet/ai-plugins`, for the specialist agents the delivery roles bind to;
  - this repository, for [`backlog-tools`](#backlog-tools-plugin).

  `.claude/settings.json` declares the `jsdotnet-devbook` marketplace and
  enables `delivery`, `delivery-schedule`, and `devbook` for everyone. The
  surfaces, `devbook-config`, and the `JSdotNet/ai-plugins` agents are installed
  per machine.
- **Not used** — `devbook-derived`, because local ADR 0004 keeps the derived
  layer a build output and commits nothing under `_meta/`. Neither the
  `knowledge-base` nor the `claude-desktop` plugin appears in the repository or in any instruction file.
  `devbook-procedures` is [retired](#devbook-procedures).
- **Known gap** — `.tools/ai-tools.json` still records the retired
  `JSdotNet/Copilot` marketplace and its plugin set. It is a stale inventory,
  not a description of what runs.
- **Why** — the conventions are reusable across repositories, so they live in
  versioned marketplaces.

## Agent Skills

```meta
rating: adopted
type: skill
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/shared.md#markdown", ".devbook/tech/shared.md#yaml"]
related: [".devbook/tech/ai-development.md#repository-instruction-files"]
```

`SKILL.md` — a Markdown file with YAML front matter, loaded on demand by name.

- **Used for** — the plugin-provided `flow-*`, `phase-*`, and `schedule-*` skills,
  and the repository-owned procedures with their wrappers. The `run` recipe is at
  `.claude/skills/run-backlog/SKILL.md`; `capture`, `diagnose`, and `estimate` are
  under `.agents/skills/`; the `update-devbook-index` command sits beside them.
- **Why** — a skill loads only when invoked, so a large body of procedure costs
  nothing until needed.

## Subagents

```meta
rating: adopted
type: agent
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins"]
```

Specialist agents a flow hands a stage to, from the `JSdotNet/ai-plugins`
marketplace.

- **Used for** —
  - `architecture:architect`
  - `domain-design:domain-architect`
  - `ux-design:ux-designer`
  - `documentation:documentation`
  - `csharp-coding:coding`
  - `qa:qa` with `qa:qa-monitor`

  The roles they fill are the adoption record's:
  [roles bound to specialist agents](../ai/02-code.md#roles-bound-to-specialist-agents).
- **Why** — a stage runs with only the tools and instructions it needs, and its
  work does not consume the driving session's context.

## Delivery Engine

```meta
rating: adopted
type: plugin
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins"]
related: [".devbook/tech/ai-development.md#delivery-surfaces", ".devbook/tech/ai-development.md#subagents"]
version: "1.18.0"
date: 2026-10-06
```

The `delivery` plugin: the `flow-*` skills, the shared phases they run
through, and the stack config they read.

- **Used for** — `flow-code` and `flow-spec`, and the pull-request skills beside
  them. Each flow runs the `phase-*` skills in order, from `phase-scope` through
  `phase-verify` and `phase-personal-validation` to `phase-create-pr`.
  `components.delivery` in `.devbook/config.json` stamps its release. The same
  file holds the tracker binding, the policy, and one `phases` map per flow. Each
  map names the agent, skill, MCP servers, or app a phase uses.
- **Why** — one engine for every change, configured by the repository rather
  than rewritten in it.

## Delivery Schedule

```meta
rating: trial
type: plugin
depends-on: [".devbook/tech/ai-development.md#delivery-engine", ".devbook/tech/ai-development.md#claude-code"]
version: "1.18.0"
date: 2026-10-06
```

The `delivery-schedule` plugin: a catalog of `schedule-*` entry points and the
skills that register them with the host's scheduler.

- **Used for** — the eleven schedules selected under `components.schedule` in
  `.devbook/config.json`, registered as Claude Code routines on 2026-09-25 and
  2026-09-26.
- **Why `trial`** — the schedules have fired only a handful of times, with no
  published report issue or pull request.

## Delivery Surfaces

```meta
rating: adopted
type: plugin
depends-on: [".devbook/tech/ai-development.md#model-context-protocol-servers", ".devbook/tech/ai-development.md#delivery-engine"]
version: "1.18.0"
date: 2026-10-06
```

The `delivery-surface-backlog` and `delivery-surface-dashboard` plugins: the
MCP servers a flow run reports its stages to, released together with the engine.

- **Used for** — `delivery-surface-backlog` records the run in the Backlog desktop
  app's Sessions pane. `delivery-surface-dashboard` is a local web page with the
  run, its diagrams, and its documents.
- **Why** — a run is otherwise invisible, and the surface's run record is what a
  resumed session reattaches to.

## Model Context Protocol Servers

```meta
rating: adopted
type: protocol
depends-on: [".devbook/tech/ai-development.md#claude-code"]
related: [".devbook/tech/ai-development.md#delivery-surfaces", ".devbook/tech/testing.md#playwright", ".devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md"]
```

The tool-server protocol that gives agents capabilities they do not ship with.

- **Used for** — the servers `.mcp.json` declares: `backlog`, served by the
  running desktop app with its bearer token from
  `tools/mcp/backlog-mcp-headers.mjs` (local ADR 0012); `aspire`; `playwright`;
  and `microsoft-learn`. Plugins add the `qa` plugin's Aspire and Playwright pair
  and the delivery surfaces.
- **Why** — these expose live state an agent cannot read from the repository.
- **Not used for guidance.** The `jsdotnet-project-guidelines` and
  `jsdotnet-project-design` servers were retired on 2026-08-27; their content
  lives in `.devbook/arc42/adr/guidelines/` and `.devbook/design/`.

## Repository Instruction Files

```meta
rating: adopted
type: format
depends-on: [".devbook/tech/shared.md#markdown"]
related: [".devbook/tech/ai-development.md#claude-code", ".devbook/tech/ai-development.md#agent-skills"]
alternatives: ["prompt-only conventions"]
```

The standing brief an agent loads at session start, plus the scoped rule files
it pulls in per task.

- **Used for** —
  - `AGENTS.md`, imported by `CLAUDE.md`;
  - the rules under `.agents/rules/` with their wrappers in `.claude/rules/`;
  - the runtime facts in the `run` recipe at `.claude/skills/run-backlog/SKILL.md`
    and the `diagnose` procedure under `.agents/skills/`.
- **Why** — the standing brief stays short enough to read every time, so it
  points at the detail. Authoring a rule once and wrapping it is the adoption
  record's: [rules wrapped per host](../ai/02-code.md#rules-wrapped-per-host).

## Claude Code Hooks

```meta
rating: adopted
type: tool
depends-on: [".devbook/tech/ai-development.md#claude-code"]
related: [".devbook/tech/tooling.md#powershell", ".devbook/tech/shared.md#nodejs"]
```

Deterministic commands the harness runs around a session event or a tool call.

- **Used for** — the hooks the enabled plugins ship; `.claude/settings.json`
  registers none of its own. The adoption record has a `hook` chapter for each,
  saying what it does and at which stage.
- **Why** — a hook executes whether or not the model decides to; see
  [Deterministic where it must happen](../ai/concepts.md#deterministic-where-it-must-happen).

## Backlog Tools Plugin

```meta
rating: adopted
type: plugin
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/ai-development.md#agent-skills", ".devbook/tech/ai-development.md#claude-code-hooks"]
related: [".devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md", ".devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md"]
version: "0.17.0"
```

This repository's own plugin, `plugins/backlog-tools`, with a Claude Code manifest.

- **Used for** — five skills:
  - `import-plan`
  - `run-plan-item`
  - `execute-plan`
  - `import-inbox`
  - `handle-remarks`

  It also ships three hooks: the plan-item nudge, the telemetry forwarder, and
  `hooks/register.tsx`, which draws the plan-progress band and pane. It is installed
  on demand from this repository as a marketplace; see its `README.md`.
- **Why** — the skills speak Backlog's entry grammar and MCP tools, so they ship
  with the product, not in a general-purpose marketplace.

## Devbook Plugin

```meta
rating: adopted
type: plugin
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/ai-development.md#agent-skills", ".devbook/tech/shared.md#nodejs"]
related: [".devbook/tech/ai-development.md#delivery-engine", ".devbook/tech/shared.md#net-aspire", ".devbook/tech/tooling.md#devbook-meta-generator", ".devbook/tech/tooling.md#devbook-tech-inventory-scripts", ".devbook/arc42/adr/0016-knowledge-folders-adopt-the-devbook-convention.md"]
version: "1.24.0"
date: 2026-10-13
```

The `devbook` plugin from `JSdotNet/devbook`, which owns the devbook folder
convention and the repository procedures.

- **Used for** — all five folders under `.devbook/` at contract 30, and:
  - the folder rules installed as `.agents/rules/devbook-*.md`, with a wrapper
    per host;
  - the checker at `.devbook/_tools/devbook-meta/`, the inventory scripts at
    `.devbook/_tools/devbook-tech/`, and the scenario tools at
    `.devbook/_tools/scenarios/`;
  - the chapter skills `validate`, `capture-specs`, `apply-change`,
    `verify-change`, `prose-check`, and `tech-update`.

  `components.devbook` in `.devbook/config.json` stamps its release and contract,
  the adopted folders, the materialized files and the migration ledger.
- **Procedures** — `run`, `capture`, `diagnose`, and `estimate`, listed under
  `components.devbook.procedures`. Each has a body this repository wrote and a
  managed wrapper per host that the plugin refreshes. `run` is a Claude Code
  recipe at `.claude/skills/run-backlog/SKILL.md` with a Copilot twin at
  `.github/skills/run/SKILL.md`; the other bodies live under `.agents/skills/`.
  The delivery engine reaches `run` through `phases.flow-code.phase-verify.app`,
  which names the `repo:run` provider on the `aspire` host. The plugin also ships
  `prototype`, which this repository has not adopted.
- **Why** — the convention is reusable, so it lives in one versioned plugin. The
  procedures let the engine start and capture the application without knowing how
  this repository does it.

## Devbook Config

```meta
rating: adopted
type: plugin
version: "1.18.0"
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/shared.md#json"]
related: [".devbook/tech/ai-development.md#devbook-plugin", ".devbook/tech/ai-development.md#delivery-engine"]
date: 2026-10-06
```

The `devbook-config` plugin: the owner of `.devbook/config.json` as a whole and
of the personal overlay beside it.

- **Used for** — four skills. `update` moves the stack forward and fans out to
  each component's own update. `doctor` reads every stamp against the disk.
  `adoption` checks the `ai/` record against what is installed. `local` writes the
  per-machine overlay outside the clone.
- **Why** — one skill reads every component's stamp, so no component has to know
  about the others.

## Devbook Procedures

```meta
rating: retired
type: skill
version: "1.13.0"
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/ai-development.md#agent-skills"]
related: [".devbook/tech/ai-development.md#devbook-plugin"]
date: 2026-10-06
```

The `devbook-procedures` plugin, which owned the repository procedures until
devbook 1.19.0 folded it into the [`devbook` plugin](#devbook-plugin).

- **Used for** — nothing now; the `devbook` plugin's chapter lists the
  procedures. Migration `027-procedures-in-devbook` moved the
  `components.devbook-procedures` stamp to `components.devbook.procedures` and
  moved no file. In the same update, migration `002-debug-is-diagnose` renamed
  `debug` to `diagnose`, because the project skill shadowed Claude Code's own
  `/debug` command, and migration `003-show-removed` removed `show`.
- **Why `retired`** — the plugin no longer exists on its own. This chapter records what it was.

