# AI-Assisted Development Stack

```meta
status: adopted
related: [".devbook/tech/technology-graph.md", ".devbook/tech/technology-graph.md#ai-development-vocabulary", ".devbook/arc42/02-constraints.md#organizational--process-constraints"]
```

> The agent harnesses, plugins, protocols, and file conventions this repository
> is built *with*, registered at the maturity each has here as a technology.
>
> How they are used — at which stage of the loop, by whom, and how far that use
> has got — is not recorded here. That is the AI adoption record,
> [`.devbook/ai/`](../ai/adoption-map.md), which points back at these chapters.
>
> The vocabulary these chapters use — agent, harness, skill, subagent, context
> window, handoff — is the one defined at
> [aicodingdictionary.com](https://www.aicodingdictionary.com/). The mapping from
> each term to where this repository uses it is tabulated once, in
> [`technology-graph.md`](technology-graph.md#ai-development-vocabulary).

## Claude Code

```meta
status: adopted
type: tool
related: [".devbook/tech/shared.md#anthropic-claude-platform"]
```

The agent harness this repository is built with, in its CLI and desktop-app
forms.

- **Used for** — every agent session that changes the repository; `CLAUDE.md`
  at the root, which imports `AGENTS.md`, is what it loads first.
- **Why** — the project is explicitly AI-first, so the harness it is built with
  is part of its stack rather than a personal preference.
- **Practices** — the gate, the flows, and the sessions it runs are the adoption
  record's: [code gate](../ai/02-code.md#code-gate-through-flow-code),
  [worktree session per change](../ai/02-code.md#worktree-session-per-change).

## Claude Code Plugins

```meta
status: adopted
type: tool
depends-on: [".devbook/tech/ai-development.md#claude-code"]
related: [".devbook/tech/ai-development.md#agent-skills", ".devbook/tech/ai-development.md#devbook-plugin", ".devbook/tech/ai-development.md#delivery-engine"]
```

The distribution unit for every agent, skill, hook, and MCP server this
repository uses.

- **Used for** — three marketplaces: `JSdotNet/devbook` (`jsdotnet-devbook`)
  for the delivery engine, its schedules and surfaces, and the devbook
  convention; `JSdotNet/ai-plugins` for the specialist agents the delivery roles
  bind to; and this repository itself for
  [`backlog-tools`](#backlog-tools-plugin). `.claude/settings.json` declares the
  `jsdotnet-devbook` marketplace and enables `delivery`, `delivery-schedule`,
  and `devbook` for everyone who opens the repository; the surfaces,
  `devbook-config`, `devbook-procedures`, and the `JSdotNet/ai-plugins` agents
  are installed per machine.
- **Not used** — `devbook-derived` from the same marketplace: local ADR 0004
  keeps the derived layer a build output, so nothing under `_meta/` is
  committed. The earlier `knowledge-base` and `claude-desktop` plugins are gone
  from the repository and from every instruction file.
- **Known gap** — `.tools/ai-tools.json` still records the retired
  `JSdotNet/Copilot` marketplace and its plugin set, not the three above. It is
  the stale inventory, not a description of what runs.
- **Why** — the conventions are reusable across repositories, so they live in
  versioned marketplaces instead of being copied per repository.

## Agent Skills

```meta
status: adopted
type: format
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/shared.md#markdown", ".devbook/tech/shared.md#yaml"]
related: [".devbook/tech/ai-development.md#repository-instruction-files"]
```

`SKILL.md` — a Markdown file with YAML front matter, loaded on demand by name.

- **Used for** — the plugin-provided `flow-*`, `phase-*`, and `schedule-*`
  skills; the repository-owned procedures (the `run` recipe at
  `.claude/skills/run-backlog/SKILL.md`, and `show`, `capture`, `debug`,
  `estimate` under `.agents/skills/`) with their wrappers; the five Aspire
  skills beside them; and the `update-devbook-index` command.
- **Why** — a skill is loaded only when its name is invoked, so a large body of
  procedure costs nothing until it is needed.

## Subagents

```meta
status: adopted
type: tool
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins"]
```

Specialist agents a flow hands a stage to, from the `JSdotNet/ai-plugins`
marketplace.

- **Used for** — `architecture:architect`, `domain-design:domain-architect`,
  `ux-design:ux-designer`, `documentation:documentation`, `csharp-coding:coding`,
  and `qa:qa` with `qa:qa-monitor`. Which role each one fills is the adoption
  record's: [roles bound to specialist agents](../ai/02-code.md#roles-bound-to-specialist-agents).
- **Why** — a stage runs with only the tools and instructions it needs, and its
  work does not consume the driving session's context.

## Delivery Engine

```meta
status: adopted
type: tool
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins"]
related: [".devbook/tech/ai-development.md#delivery-surfaces", ".devbook/tech/ai-development.md#subagents"]
version: "1.13.0"
date: 2026-09-30
```

The `delivery` plugin: the `flow-*` skills, the shared phases they close
through, and the stack config they read.

- **Used for** — `flow-code`, `flow-spec`, `flow-update-packages`, and
  `flow-project`; the phases `phase-build-test`, `phase-validation`, and
  `phase-personal-validation`; and the pull-request skills beside them. Its
  release is stamped under `components.delivery` in `.devbook/config.json`,
  which also holds the bindings, extensions, and policy it reads.
- **Why** — one engine for every change, configured by the repository rather
  than rewritten in it.

## Delivery Schedule

```meta
status: trial
type: tool
depends-on: [".devbook/tech/ai-development.md#delivery-engine", ".devbook/tech/ai-development.md#claude-code"]
version: "1.13.0"
date: 2026-09-30
```

The `delivery-schedule` plugin: a catalog of `schedule-*` entry points and the
skills that register them with the host's scheduler.

- **Used for** — the eleven schedules selected under `components.schedule` in
  `.devbook/config.json`, registered as Claude Code routines on 2026-09-25 and
  2026-09-26.
- **Why `trial`** — the schedules have fired only a handful of times, and no
  run has yet published a report issue or a pull request.

## Delivery Surfaces

```meta
status: adopted
type: tool
depends-on: [".devbook/tech/ai-development.md#model-context-protocol-servers", ".devbook/tech/ai-development.md#delivery-engine"]
version: "1.13.0"
date: 2026-09-30
```

The `delivery-surface-backlog` and `delivery-surface-dashboard` plugins: the
MCP servers a flow run reports its stages to, released together with the engine.

- **Used for** — `delivery-surface-backlog`, which records the run in the
  Backlog desktop app's Sessions pane, and `delivery-surface-dashboard`, a local
  web page with the run, its diagrams, and its documents.
- **Why** — a run is otherwise invisible, and the surface's run record is what a
  resumed session reattaches to.

## Model Context Protocol Servers

```meta
status: adopted
type: protocol
depends-on: [".devbook/tech/ai-development.md#claude-code"]
related: [".devbook/tech/ai-development.md#delivery-surfaces", ".devbook/tech/testing.md#playwright", ".devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md"]
```

The tool-server protocol that supplies agents with capabilities they do not ship
themselves.

- **Used for** — the servers `.mcp.json` declares: `backlog`, served by the
  running desktop app with its bearer token supplied by
  `tools/mcp/backlog-mcp-headers.mjs` (local ADR 0012); `aspire`; `playwright`;
  and `microsoft-learn`. Plugins add the `qa` plugin's own Aspire and Playwright
  pair and the delivery surfaces.
- **Why** — these expose live state an agent cannot read from the repository.
- **Not used for guidance.** The `jsdotnet-project-guidelines` and
  `jsdotnet-project-design` servers were retired on 2026-08-27; their content
  lives in `.devbook/arc42/adr/guidelines/` and `.devbook/design/`.

## Repository Instruction Files

```meta
status: adopted
type: format
depends-on: [".devbook/tech/shared.md#markdown"]
related: [".devbook/tech/ai-development.md#claude-code", ".devbook/tech/ai-development.md#agent-skills"]
alternatives: ["prompt-only conventions"]
```

The standing brief an agent loads at session start, plus the scoped rule files
it pulls in per task.

- **Used for** — `AGENTS.md`, imported by `CLAUDE.md`; the rules under
  `.agents/rules/` with their wrappers in `.claude/rules/`; and the runtime
  facts in the `run` recipe at `.claude/skills/run-backlog/SKILL.md` and the
  `debug` procedure under `.agents/skills/`.
- **Why** — the standing brief has to stay short enough to be read every time,
  so it points at the detail rather than containing it. How the rules are
  authored once and wrapped is the adoption record's:
  [rules wrapped per host](../ai/02-code.md#rules-wrapped-per-host).

## Claude Code Hooks

```meta
status: adopted
type: tool
depends-on: [".devbook/tech/ai-development.md#claude-code"]
related: [".devbook/tech/tooling.md#powershell", ".devbook/tech/shared.md#nodejs"]
```

Deterministic commands the harness runs around a session event or a tool call.

- **Used for** — the hooks the enabled plugins ship; `.claude/settings.json`
  registers none of its own. What each one does, and at which stage, is recorded as a
  `hook` chapter in the adoption record.
- **Why** — a hook executes whether or not the model decides to; anything that
  must happen every time belongs here rather than in an instruction file.

## Backlog Tools Plugin

```meta
status: adopted
type: tool
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/ai-development.md#agent-skills", ".devbook/tech/ai-development.md#claude-code-hooks"]
related: [".devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md", ".devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md"]
version: "0.11.2"
```

This repository's own plugin, `plugins/backlog-tools`, with a Claude Code manifest.

- **Used for** — five skills (`import-plan`, `run-plan-item`, `execute-plan`,
  `import-inbox`, `handle-remarks`) and two hooks (the plan-item nudge and the telemetry
  forwarder). Installed on demand from this repository as a marketplace; see
  its `README.md`.
- **Why** — the skills speak Backlog's own entry grammar and MCP tools, so they
  ship with the product rather than in a general-purpose marketplace.

## Devbook Plugin

```meta
status: adopted
type: tool
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/shared.md#nodejs"]
related: [".devbook/tech/tooling.md#devbook-meta-generator", ".devbook/tech/tooling.md#devbook-tech-inventory-scripts", ".devbook/arc42/adr/0016-knowledge-folders-adopt-the-devbook-convention.md"]
version: "1.16.0"
date: 2026-10-02
```

The `devbook` plugin from `JSdotNet/devbook`, which owns the devbook folder
convention this repository follows.

- **Used for** — all five folders under `.devbook/` at contract 25; the folder
  rules installed as `.agents/rules/devbook-*.md` with a wrapper per host; the
  checker at `.devbook/_tools/devbook-meta/` and the inventory scripts at
  `.devbook/_tools/devbook-tech/`; and the chapter skills (`validate`,
  `capture-specs`, `apply-change`, `verify-change`, `prose-check`,
  `tech-update`). Its release, contract, adopted folders, materialized files,
  and migration ledger are stamped under `components.devbook` in
  `.devbook/config.json`.
- **Why** — the convention is reusable across repositories, so it lives in one
  versioned plugin instead of being duplicated per repository.

## Devbook Config

```meta
status: adopted
type: tool
version: "1.13.0"
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/shared.md#json"]
related: [".devbook/tech/ai-development.md#devbook-plugin", ".devbook/tech/ai-development.md#delivery-engine"]
date: 2026-09-30
```

The `devbook-config` plugin: the owner of `.devbook/config.json` as a whole and
of the personal overlay beside it.

- **Used for** — `update` moved this repository's stack forward and fanned out
  to each component's own update; `doctor` reads every stamp against the disk;
  `adoption` checks the `ai/` record against what is installed; `local` writes
  the per-machine overlay outside the clone.
- **Why** — one skill reads every component's stamp, so no single component has
  to know about the others.

## Devbook Procedures

```meta
status: adopted
type: tool
version: "1.13.0"
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/ai-development.md#agent-skills"]
related: [".devbook/tech/ai-development.md#delivery-engine", ".devbook/tech/shared.md#net-aspire"]
date: 2026-09-30
```

The `devbook-procedures` plugin: the fixed goal of each repository procedure,
seeded once and then owned by the repository.

- **Used for** — `run`, `show`, `capture`, `debug`, and `estimate`: a body
  this repository wrote from what it actually does, and a managed wrapper per
  host that the plugin refreshes. `run` is a Claude Code recipe at
  `.claude/skills/run-backlog/SKILL.md` with a Copilot twin at
  `.github/skills/run/SKILL.md`; the other bodies live under `.agents/skills/`.
  `extensions.app.start` points the delivery engine at `repo:run`. Stamped under
  `components.devbook-procedures`.
- **Why** — the engine needs to start, show, and capture the application
  without knowing how this repository does it; the procedure is the seam.

