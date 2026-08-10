---
applyTo: "**"
description: Repository-specific policy for when the checked-in knowledge folders (.arc42, .domain, .backlog, .tech, .design) may be loaded as working context, and when they must not be.
---

# Knowledge context loading

General orchestration routing — which `orch-*` skill or specialist agent handles which task
category, and its fallbacks — is delivered globally by the `copilot-app` plugin and is no
longer restated in this repository.

This file covers only what is specific to Backlog: **which checked-in knowledge folders a
given workflow may read, and how much of them.** Treat these folders as task-scoped context,
not baseline context, per `.github/instructions/mcp-usage.instructions.md`.

## Context loading by orchestration and agent

- `orch-architecture`, `orch-arc42`, `orch-arc42-content`, `orch-blueprint`, `orch-adr`,
  `orch-tdr`, and `architecture:architect` may load `.arc42/` as working context, but
  should load only the chapter(s) relevant to the requested scope.
- `orch-domain` and `domain-design:domain-architect` may load `.domain/` as
  working context, but should load only the relevant bounded-context chapters.
- `orch-backlog` and other backlog-writing or issue-writing workflows may load
  `.backlog/` as working context, but should load only the relevant work-item chapters.
- `orch-tech` may load `.tech/` as working context, plus the
  `.arc42` chapters (solution strategy, deployment view, ADRs) that ground the
  stack choices it records.
- `orch-design` and `ux-design:ux-designer` may load `.design/` as working
  context, but should load only the relevant guideline file(s).
- Non-architecture implementation, bug-fix, package-update, documentation, and UX flows
  should not load `.arc42/` by default. Consult it only when the user explicitly asks
  for architecture context or when implementation depends on a specific documented
  decision, view, constraint, or glossary term.
- UI implementation and UI bug-fix flows should consult `.design/` when the change
  touches visual design, interaction behavior, content editing, or accessibility —
  loading only the relevant guideline file(s), not the whole folder.

## Repository-native orchestration entrypoints

Backlog adds knowledge-folder orchestration skills that the plugin does not provide:
`orch-arc42-content`, `orch-domain`, `orch-backlog`, `orch-tech`, and `orch-design`. Prefer
these for any change to their folder. When a task has no dedicated `orch-*` skill from either
the plugin or this repository, use the repo-native generic entrypoint `orch-fallback`.

## Runtime and QA context

This repository has no runnable application. Startup and QA expectations live in
`.github/copilot-orch-context.md`; per-category model overrides live in
`.github/copilot-model-selection.md`.
