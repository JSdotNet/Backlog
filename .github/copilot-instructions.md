# Backlog repository instructions

## Repository scope

Backlog is being organized as a multi-part, AI-first work management product with backlog, prompt, knowledge, and monitoring capabilities across desktop, IDE, and phone channels. This repository is still in the bootstrap phase, so prefer durable instruction-file guidance and deliberate structure decisions over ad hoc scaffolding.

## Authoritative guidance order

See `.github/instructions/mcp-usage.instructions.md` for MCP server usage and authority order.

## Agent usage

Orchestration routing (which `orch-*` skill or specialist agent handles which task type) is delivered globally by the `copilot-app` plugin; it is not restated in this repository. See `.github/instructions/context-loading.instructions.md` for the Backlog-specific policy on which knowledge folders a workflow may load, plus the repo-native `orch-*` entrypoints.

## Orchestration configuration

- `.github/copilot-model-selection.md` — per-category model overrides for orchestration runs.
- `.github/copilot-orch-context.md` — repo startup and QA context. This repository has no runnable application, so QA validation is skipped.

## Knowledge folders

The `.arc42/`, `.domain/`, `.backlog/`, `.tech/`, and `.design/` convention — chapter structure, `meta` blocks, derived `_meta/` indexes, the knowledge graph canvas, and the `orch-arc42-content` / `orch-domain` / `orch-backlog` / `orch-tech` / `orch-design` orchestrations — is provided by the `knowledge-base` plugin (`JSdotNet/Copilot:plugins/knowledge-base`). Do not restate those authoring rules in this repository. Repository-specific policy that the plugin deliberately does not ship lives in `.github/instructions/context-loading.instructions.md`.

The generator at `.github/tools/knowledge-meta/` and the `knowledge-meta` workflow are installed copies of the plugin's tooling; re-sync them from the plugin rather than editing them locally.

## Naming

See `.github/instructions/naming.instructions.md` for repository-wide file and folder naming. Naming inside the knowledge folders is governed by the plugin's `knowledge-naming.instructions.md`.

## Guardrails

- Keep repository instruction files policy-focused; do not duplicate long-form MCP guidance into them.
- Do not invent permanent project structure before architecture and domain decisions make the boundaries clear.
- Ground governance and coding decisions in repository guidance instead of memory.
- Treat checked-in knowledge folders such as `.arc42/`, `.domain/`, `.backlog/`, `.tech/`, and `.design/` as **task-scoped context**, not baseline context. Load only the relevant chapters after routing to the correct orchestration or specialist agent, or when the user explicitly asks for that knowledge.
- Never hand-edit anything under an `_meta/` folder; it is generated. Re-run the generator instead.
- Honor the standing product UX rules recorded in `.design/`: dark mode only, no save buttons (everything auto-saves), Markdown stays canonical behind the rich text editor, and every drag-and-drop reorder has a keyboard equivalent.
- Commit changes as they are made; do not leave edits uncommitted across multiple turns of the same task.
- Never open a pull request unless the user explicitly asks for one (via the create-PR action, a PR-creation skill, or a direct request). Committing to the session branch is not an implicit request to open a PR.

## Pull Request Creation

When creating a pull request in this repository, always invoke the `pr-jsdotnet` skill (`.github/skills/pr-jsdotnet/SKILL.md`) instead of the built-in PR creation tool, so the PR is authored using JSdotNet organization credentials via `gh pr create`.
