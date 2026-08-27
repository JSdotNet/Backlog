# AI-Assisted Development Stack

```meta
status: adopted
related: [".tech/technology-graph.md", ".tech/technology-graph.md#ai-development-vocabulary", ".arc42/02-constraints.md#organizational--process-constraints"]
```

> The agent harnesses, protocols, and file conventions this repository is built
> *with*. The product is AI-first, and so is its construction: this is a real
> layer of the stack rather than incidental tooling, and it is entirely
> `adopted`.
>
> The vocabulary these chapters use — agent, harness, skill, subagent, context
> window, handoff — is the one defined at
> [aicodingdictionary.com](https://www.aicodingdictionary.com/). The mapping from
> each term to where this repository uses it is tabulated once, in
> [`technology-graph.md`](technology-graph.md#ai-development-vocabulary); the
> chapters below carry the technologies themselves.

## Claude Code

```meta
status: adopted
type: tool
related: [".tech/shared.md#anthropic-claude-platform", ".tech/ai-development.md#github-copilot-cli"]
alternatives: ["GitHub Copilot CLI alone"]
```

The primary agent harness for work in this repository.

- **Used for** — every orchestration run: the sessions that write code, the
  `orch-*` skills the repository gate routes to, and the subagents those skills
  hand work to. `CLAUDE.md` at the repository root is what it loads first.
- **Why** — the project is explicitly AI-first, so the harness it is built with
  is part of its stack rather than a personal preference. It runs alongside the
  Copilot CLI rather than replacing it: the two read parallel instruction sets
  that this repository keeps deliberately in step.
- **How** — sessions run in git worktrees so several can work at once without
  colliding; per-session state lives under `.claude/`.

## GitHub Copilot CLI

```meta
status: adopted
type: tool
depends-on: [".tech/shared.md#github-platform"]
related: [".tech/ai-development.md#claude-code"]
```

The second agent harness this repository supports.

- **Used for** — the same orchestration model as Claude Code, driven by
  `.github/copilot-instructions.md` and `.github/instructions/*.instructions.md`
  instead of `CLAUDE.md`, and by the `copilot-app` plugin's agents and canvases.
- **Why** — the repository is developed from both harnesses, so its governance
  is written twice rather than assuming one vendor. Where the two documents
  describe the same rule, only the agent and tool names differ.

## Claude Code Plugins

```meta
status: adopted
type: tool
depends-on: [".tech/ai-development.md#claude-code", ".tech/ai-development.md#github-copilot-cli"]
related: [".tech/ai-development.md#agent-skills", ".tech/ai-development.md#knowledge-base-plugin"]
```

The distribution unit for every agent, skill, MCP server, and canvas this
repository uses.

- **Used for** — all of it. `.tools/ai-tools.json` records the marketplace
  (`JSdotNet/Copilot`) and the twenty-two plugins with their installed and
  available versions: `claude-desktop`/`copilot-app` (orchestration),
  `knowledge-base`, `architecture`, `domain-design`, `csharp-coding`, `qa`,
  `review`, `ux-design`, `product-owner`, `documentation`, `spec-builder`, and
  the two `jsdotnet-*` guideline servers, among others.
- **Why** — the conventions are reusable across repositories, so they live in one
  versioned marketplace instead of being copied per repository. This repository
  ships no `orch-*` skill of its own; every orchestration entrypoint is
  plugin-provided.

## Agent Skills

```meta
status: adopted
type: format
depends-on: [".tech/ai-development.md#claude-code-plugins", ".tech/shared.md#markdown", ".tech/shared.md#yaml"]
related: [".tech/ai-development.md#repository-instruction-files"]
```

`SKILL.md` — a Markdown file with YAML front matter, loaded on demand by name.

- **Used for** — the plugin-provided `orch-*` orchestrations the repository gate
  routes to, the repository-local `.github/skills/pr-jsdotnet` pull-request
  workflow, and the five `.agents/skills/` Aspire skills (`aspire`,
  `aspire-init`, `aspire-deployment`, `aspire-monitoring`,
  `aspire-orchestration`) with their reference folders.
- **Why** — a skill is loaded only when its name is invoked, so a large body of
  procedure costs nothing until it is needed. The reference files beside a
  `SKILL.md` are a second step down the same ladder.

## Subagents

```meta
status: adopted
type: tool
depends-on: [".tech/ai-development.md#claude-code-plugins"]
```

Specialist agents an orchestration hands a stage to.

- **Used for** — the named personas each `orch-*` skill delegates to:
  `architecture:architect` for arc42/ADR/blueprint work,
  `domain-design:domain-architect` for the domain model, `csharp-coding:coding`
  for implementation, `qa:qa` and `qa:qa-monitor` for validation,
  `ux-design:ux-designer` for design, `product-owner:product-owner` for work
  items.
- **Why** — a stage runs with only the tools and instructions it needs, and its
  work does not consume the orchestrating session's context.

## Model Context Protocol Servers

```meta
status: adopted
type: protocol
depends-on: [".tech/ai-development.md#claude-code", ".tech/ai-development.md#github-copilot-cli"]
related: [".tech/ai-development.md#orchestration-dashboard", ".tech/testing.md#playwright"]
```

The tool-server protocol that supplies agents with capabilities and with
authoritative guidance.

- **Used for** — `jsdotnet-project-guidelines` (repository conventions) and
  `jsdotnet-project-design` (design and UX guidance), which are the authority
  order `.github/instructions/mcp-usage.instructions.md` defines; the Aspire and
  Playwright servers the `qa` plugin supplies; and the orchestration dashboard.
- **Why** — it keeps governance out of prompt memory and in a queryable source,
  and it is how a harness reaches a tool it does not ship.

## Orchestration Dashboard

```meta
status: adopted
type: tool
depends-on: [".tech/ai-development.md#model-context-protocol-servers"]
related: [".tech/ai-development.md#repository-instruction-files"]
```

The run tracker every orchestration reports progress through.

- **Used for** — opening a run, registering its stages, recording each stage's
  status and output, and holding the Personal Validation decision that gates
  pull-request creation. Because it is plugin-provided, its tools are namespaced
  `mcp__plugin_claude-desktop_orch-dashboard__*`.
- **Why** — an unattended or long run is otherwise invisible; the dashboard is
  also what survives a session handoff, so a resumed run reattaches instead of
  starting a duplicate.

## Repository Instruction Files

```meta
status: adopted
type: format
depends-on: [".tech/shared.md#markdown"]
related: [".tech/ai-development.md#claude-code", ".tech/ai-development.md#github-copilot-cli", ".tech/ai-development.md#agent-skills"]
alternatives: ["AGENTS.md", "prompt-only conventions"]
```

The standing brief an agent loads at session start, plus the scoped instruction
files it pulls in per task.

- **Used for** — `CLAUDE.md` and `.github/copilot-instructions.md` carry the
  orchestration gate and the repository rules, one per harness. The four scoped
  files under `.github/instructions/` (`context-loading`, `mcp-usage`, `naming`,
  `ui-components`) carry the detail, and `.claude/orch-context.md` and
  `.github/copilot-orch-context.md` carry the runtime facts — how to start the
  AppHost, which harness resources to validate against, and the default QA
  depth.
- **Why** — the standing brief has to stay short enough to be read every time, so
  it points at the detail rather than containing it. This is why the pair of
  orch-context files is duplicated per harness rather than shared: each is the
  file its own harness actually reads, and they are updated together.

## Claude Code Hooks

```meta
status: adopted
type: tool
depends-on: [".tech/ai-development.md#claude-code", ".tech/tooling.md#powershell"]
related: [".tech/shared.md#github-platform"]
```

Deterministic commands the harness runs around a tool call.

- **Used for** — `.claude/settings.json` registers one `PostToolUse` hook on
  `spawn_task`, running `.claude/hooks/spawn-task-to-issue.ps1` so an
  out-of-scope finding an agent flags mid-run becomes a GitHub issue rather than
  a note that scrolls away.
- **Why** — a hook executes whether or not the model decides to; anything that
  must happen every time belongs here rather than in an instruction file.

## Knowledge Base Plugin

```meta
status: adopted
type: tool
depends-on: [".tech/ai-development.md#claude-code-plugins", ".tech/shared.md#nodejs"]
related: [".tech/ai-development.md#knowledge-canvas-extension", ".tech/tooling.md#knowledge-meta-generator"]
```

The plugin that owns the knowledge-folder convention this repository follows.

- **Used for** — the authoring instructions for `.arc42`, `.domain`, `.backlog`,
  `.tech`, and `.design`; the per-folder `orch-*` and `capture-*`/`build-*`
  skills; the `knowledge-canvas` extension; and the `knowledge-meta` generator
  installed into `.github/tools/`.
- **Why** — the convention is reusable across repositories, so it lives in one
  versioned plugin instead of being duplicated per repository.
- **Sourced from** — `JSdotNet/Copilot:plugins/knowledge-base`.

## Knowledge Canvas Extension

```meta
status: adopted
type: tool
depends-on: [".tech/ai-development.md#claude-code-plugins", ".tech/shared.md#nodejs", ".tech/shared.md#mermaid"]
related: [".tech/ai-development.md#knowledge-base-plugin"]
```

The `knowledge-base` plugin's canvas for viewing knowledge folders.

- **Used for** — rendering `.arc42`, `.domain`, `.backlog`, `.tech`, and
  `.design` Markdown with live Mermaid diagrams and a metadata/lint side panel,
  plus the `knowledge-graph` view that walks the derived reference graph.
- **Why** — the metadata convention is designed for machine reading, so a viewer
  is what makes the graph usable rather than merely stored.
- **Sourced from** — `JSdotNet/Copilot:plugins/knowledge-base`; installed as a
  plugin rather than checked into this repository.

## Git Worktree Sessions

```meta
status: adopted
type: tool
depends-on: [".tech/tooling.md#git"]
related: [".tech/shared.md#net-aspire", ".tech/ai-development.md#claude-code"]
```

One worktree per agent session, as the isolation boundary for parallel work.

- **Used for** — every session that changes code: a branch and a working copy of
  its own under `.claude/worktrees/`, so several sessions can build, test, and
  run the app at once.
- **Why** — it is what makes unattended and parallel runs safe. Two shared
  resources still leak across worktrees and are handled explicitly: the git
  stash stack (so a WIP commit is preferred over a bare `git stash`), and local
  ports (so `aspire start --isolated` gives each session its own).
