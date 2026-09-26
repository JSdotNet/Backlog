# Backlog repository instructions

Read [AGENTS.md](../AGENTS.md) and follow it. It holds this repository's standing rules, and
everything it says applies here. Copilot-specific notes follow.

## Repository scope

Backlog is being organized as a multi-part, AI-first work management product with backlog, prompt, knowledge, and monitoring capabilities across desktop, IDE, and phone channels. This repository is still in the bootstrap phase, so prefer durable instruction-file guidance and deliberate structure decisions over ad hoc scaffolding.

## Authoritative guidance order

Repository guidance is **checked in, not fetched**. The `jsdotnet-project-guidelines` and
`jsdotnet-project-design` MCP servers were retired on 2026-08-27:

- `.devbook/arc42/adr/guidelines/` — the inherited organization architecture decisions that govern this
  repository's .NET code, indexed by its `README.md`. Read the one that governs the change.
- `.devbook/arc42/adr/` — the decisions Backlog took for itself.
- `.devbook/design/` — design and UX guidance, tokens, and the color scheme.

See `.agents/rules/mcp-usage.md` for the full authority order and for
which MCP servers are still in use. When a plugin-provided skill tells you to query a
guidelines MCP server, read `.devbook/arc42/adr/guidelines/` instead.

## Agent usage

> **Claude Code reads `CLAUDE.md` at the repository root, not this file.** The rules below
> are the GitHub Copilot form, naming the `copilot-app` plugin and its
> `copilot-app:orchestrator` agent. `CLAUDE.md` carries the same gate in Claude Code terms
> (`claude-desktop` plugin, `claude-desktop:orchestrator` agent). Keep the two in step when
> changing the gate.

Orchestration routing (which `orch-*` skill or specialist agent handles which task type) is delivered globally by the `copilot-app` plugin; it is not restated in this repository. See `.agents/rules/context-loading.md` for the Backlog-specific orchestration gate on code changes and the policy on which knowledge folders a workflow may load.

**Orchestration gate.** Before the first `edit` or `create` to any file under `src/` or
`tests/`, you MUST invoke the matching `orch-*` skill through the
`copilot-app:orchestrator` agent. Reading, searching, and exploring are always allowed
first — the gate is on the first write, not on the first action, so renaming the session
and orienting yourself does not consume it.

This gate holds regardless of how small the request looks and regardless of whether a
specification, acceptance criteria, or story already exists. If a skill's stated
preconditions are not met, invoke it anyway and derive the missing scope inside it. If
no `orch-*` skill matches the task category at all, invoke `orch-fallback`. Never
proceed straight from exploration to implementation.

At the start of every `orch-*` run, the orchestrator must open or reattach the
`orch-dashboard` canvas when available, call `start_run` for the selected skill, and keep
that dashboard as the run tracker. If the dashboard canvas is listed as available but the
current agent cannot call the canvas tools, treat that as a tooling/runtime failure: report
the missing canvas capability and stop instead of substituting chat-only tracking. Only skip
dashboard calls when the extension is genuinely unavailable. Code-modifying runs must
include the shared `phase-build-test` and `phase-qa-validation` phases using
`.github/copilot-orch-context.md` for startup, harness, and QA-depth settings. Never skip
Personal Validation, and never create a pull request or mark an orchestration complete
without explicit user approval.

## Orchestration configuration

- `.github/copilot-orch-context.md` — repo startup and QA context: how to run the Aspire AppHost, which harness resources to target for UI validation, and the default QA depth.

## Knowledge folders

The devbook folders sit under `.devbook/`, adopted through the `devbook` plugin (`components.devbook` in `.devbook/config.json`); `AGENTS.md` and `.agents/rules/devbook-*.md` are their rules. Check them before committing with `node .devbook/_tools/devbook-meta/build.mjs --check`, which `.github/workflows/devbook-meta.yml` also runs in CI. Never hand-edit `.devbook/_tools/`, the `AGENTS.md` markers, the `devbook-*` rule trios (`devbook:update` owns them), or anything under `_meta/`. The reading order is derived — `index: root`, the folder's convention root, then filename numbers; there is no `_reading-order.json` (local ADR 0016).

The derived layer is one SQLite database per repository path, built by the app in the background into its own storage and never into a repository (local ADRs 0004 and 0015). The Devbook panels read it and degrade to the Markdown, then to scanning the folder, so browsing always works; search alone needs the database. `Backlog.Infrastructure.Devbook` holds the reader, the builder and its refresher; `tools/devbook/build-database.mjs` is the reference `DevbookBuilderParityTests` holds that builder to (Node 22.5+). The product reads `.devbook/<name>` first and a root-level `.<name>` as the legacy fallback.

`tools/devbook/check-metadata.mjs` is the repo-native metadata half of the gate, run by `.github/workflows/devbook-metadata.yml` beside the installed `devbook-meta.yml`.

## UI components

See `.agents/rules/ui-components.md`: a screen under `src/App/` or `src/Modules/` renders the shared library's component (`src/Core/Backlog.UI.Components`) rather than growing its own copy, and a component that cannot wear the screen's classes gets the hook rather than a second implementation. `tests/Backlog.ArchitectureTests/SharedControlAdoptionTests.cs` enforces it and holds the documented exceptions.

See `.agents/rules/storybook.md` for authoring the review surface itself — which chrome component to use, which parameters to pass, and where a new page goes in `StorybookIndex`. The rules it satisfies are in `.devbook/design/README.md#living-reference-the-ui-storybook` and are not restated in either file.

## Naming

See `.agents/rules/naming.md` for repository-wide file and folder naming. Naming inside the devbook folders is `.agents/rules/devbook-naming.md`'s.

Path-scoped rules are authored once under `.agents/rules/` and wrapped per host — `.github/instructions/<topic>.instructions.md` (`applyTo`) for Copilot, `.claude/rules/<topic>.md` (`paths`) for Claude Code. `.agents/rules/README.md` is the convention.

## Guardrails

- Keep repository instruction files policy-focused. Long-form architecture guidance belongs in `.devbook/arc42/adr/guidelines/`, design guidance in `.devbook/design/` — link to them rather than restating them.
- Do not invent permanent project structure before architecture and domain decisions make the boundaries clear.
- Ground governance and coding decisions in the checked-in decision records instead of memory. `.devbook/arc42/adr/guidelines/` is a fork of the organization's corpus, not a mirror: change it here when Backlog diverges, and record the divergence in that document's **Deviations and gaps** section.
- Honor the standing product UX rules recorded in `.devbook/design/`: dark mode only, no save buttons (everything auto-saves), Markdown stays canonical behind the rich text editor, and every drag-and-drop reorder has a keyboard equivalent.
- Commit changes as they are made; do not leave edits uncommitted across multiple turns of the same task.
- Never open a pull request unless the user explicitly asks for one (via the create-PR action, a PR-creation skill, or a direct request). Committing to the session branch is not an implicit request to open a PR.

## Pull Request Creation

When creating a pull request in this repository, always invoke the `pr-jsdotnet` skill (`.github/skills/pr-jsdotnet/SKILL.md`) instead of the built-in PR creation tool, so the PR is authored using JSdotNet organization credentials via `gh pr create`.
