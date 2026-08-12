---
applyTo: "**"
description: Repository-specific orchestration policy - the gate on code changes under src/ and tests/, and when the checked-in knowledge folders (.arc42, .domain, .backlog, .tech, .design) may be loaded as working context.
---

# Repository orchestration and context policy

General orchestration routing — which `orch-*` skill or specialist agent handles which task
category, and its fallbacks — is delivered globally by the `copilot-app` plugin and is no
longer restated in this repository.

This file covers only what is specific to Backlog: **the gate that forces code changes
through an orchestration skill**, and **which checked-in knowledge folders a given workflow
may read, and how much of them.** Treat those folders as task-scoped context, not baseline
context, per `.github/instructions/mcp-usage.instructions.md`.

## The gate

**Before the first `edit` or `create` to any file under `src/` or `tests/`, you MUST
invoke the matching `orch-*` skill through the `copilot-app:orchestrator` agent.**
Exploration first is expected and does not consume the gate; the trigger is the first
write, not the first action.

Every orchestration owner must open or reattach the `orch-dashboard` canvas when the
canvas is available, call `start_run` for the selected `orch-*` skill, and track each
stage there. Loading a skill directly without the orchestrator owner is not sufficient
for code-modifying work because it can bypass dashboard state, shared QA Validation, and
the Personal Validation gate.

Apply the gate literally:

- **Size is not a criterion.** A one-control UI tweak and a multi-service feature route
  the same way. Do not reason about whether a request is "big enough" to orchestrate.
- **A missing specification is not an exemption.** Ad-hoc requests with no story,
  acceptance criteria, or approved design still route through `orch-feature` or
  `orch-bug`; the skill derives the missing scope as its first stage.
- **Unmet preconditions are not an exemption.** If the matched skill's stated
  preconditions do not hold, invoke it anyway and say so — do not fall through to
  direct implementation.
- **No match means `orch-fallback`,** not direct implementation.

## Repository override for `orch-feature`

> **Temporary bridge.** This override exists only until the upstream fix in
> `JSdotNet/Copilot` (branch `orch-feature-scope-discovery`, commit `5d9c288`) ships,
> which adds a Stage 0 "Scope Discovery" to `orch-feature` and `orch-bug` and amends
> `orch-shared-phases.instructions.md`. Once the updated plugin is installed, delete this
> section — keeping both is two sources of truth that will drift.

The plugin-provided `orch-feature` skill states a precondition that the feature
specification, acceptance criteria, and architecture are already approved. **That
precondition does not apply in this repository.** Invoke `orch-feature` for ad-hoc
feature requests too, and treat scope discovery as part of its first stage: restate the
requested behavior, derive at least one measurable acceptance criterion, confirm it with
the user, then continue through the remaining stages. The same applies to `orch-bug`
when no reproduction has been written up yet.

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

The knowledge-folder orchestrations are provided by the `knowledge-base` plugin. The only
repo-native orchestration entrypoint left is `orch-fallback`, for task categories with no
dedicated `orch-*` skill from either source. See `.github/copilot-orch-context.md`.

## Runtime and QA context

Startup and QA expectations live in `.github/copilot-orch-context.md`; per-category model
overrides live in `.github/copilot-model-selection.md`.
For code-modifying runs, keep `phase-build-test` before `phase-qa-validation` and pass the
repo context into QA so it uses the Aspire AppHost, dynamic harness URLs, and configured QA
depth. The current repo default is Playwright QA for UI behavior, with the documented
exceptions in `.github/copilot-orch-context.md` for documentation-only and non-UI code
changes.
