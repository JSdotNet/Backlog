---
name: context-loading
description: Repository-specific delivery policy - the gate on code changes under src/ and tests/, and when the checked-in devbook folders (.devbook/arc42, .devbook/domain, .devbook/tech, .devbook/design, .devbook/ai) may be loaded as working context.
paths:
  - "**"
---

# Repository delivery and context policy

Routing — which `flow-*` skill handles which task category — is delivered by the `delivery`
plugin's session context and is not restated here. `CLAUDE.md` restates the gate; keep both
in step when changing it.

This file covers only what is specific to Backlog: **the gate that forces code changes
through a flow**, and **which checked-in devbook folders a given workflow may read, and how
much of them.** Treat those folders as task-scoped context, not baseline context, per
`.agents/rules/mcp-usage.md`.

## The gate

**Before the first edit or create to any file under `src/` or `tests/`, invoke
`delivery:flow-code`** — or `delivery:flow-update-packages` for a dependency move.
Exploration first is expected and does not consume the gate; the trigger is the first
write, not the first action. A change to a devbook folder runs through `delivery:flow-spec`.

Every flow reports its stages to every bound delivery surface and stops at Personal
Validation before a pull request. Loading a phase skill directly instead of the flow is not
sufficient for code-modifying work, because it bypasses the run record, Validation, and the
Personal Validation gate.

Apply the gate literally:

- **Size is not a criterion.** A one-control UI tweak and a multi-service feature route
  the same way. Do not reason about whether a request is "big enough" for a flow.
- **A missing specification is not an exemption.** Ad-hoc requests with no story,
  acceptance criteria, or approved design still route through `flow-code`, whose Scope
  Discovery stage derives the missing scope.
- **Unmet preconditions are not an exemption.** If the flow's stated inputs are absent,
  invoke it anyway and derive them inside it.

## Context loading by flow and agent

- `flow-spec` on `.devbook/arc42/` and `architecture:architect` may load `.devbook/arc42/`
  as working context, but should load only the chapter(s) relevant to the requested scope.
- `flow-spec` on `.devbook/domain/` and `domain-design:domain-architect` may load
  `.devbook/domain/` as working context, but should load only the relevant bounded-context
  chapters.
- `flow-spec` on `.devbook/tech/` may load `.devbook/tech/`, plus the `.devbook/arc42`
  chapters (solution strategy, deployment view, ADRs) that ground the stack choices it
  records.
- `flow-spec` on `.devbook/design/` and `ux-design:ux-designer` may load `.devbook/design/`,
  but should load only the relevant guideline file(s).
- `flow-spec` on `.devbook/ai/` loads `adoption-map.md` plus the files in scope.
- Implementation, bug-fix, package-update, and UX work should not load `.devbook/arc42/` by
  default. Consult it only when the user explicitly asks for architecture context or when
  implementation depends on a specific documented decision, view, constraint, or glossary
  term.
- UI implementation and UI bug-fix work should consult `.devbook/design/` when the change
  touches visual design, interaction behavior, content editing, or accessibility —
  loading only the relevant guideline file(s), not the whole folder.

## Runtime and QA context

Startup and QA expectations live in `.claude/orch-context.md`. Model choice is not
configured in this repository; flows use the plugin defaults unless overridden per run.
For code-modifying runs the Validation phase uses the Aspire AppHost, dynamic harness URLs,
and the configured QA depth: Playwright QA for UI behavior, with the documented exceptions
in `.claude/orch-context.md` for documentation-only and non-UI code changes.
