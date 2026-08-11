---
name: orch-fallback
description: 'Generic orchestration entrypoint for this repository, used when a task has no dedicated orch-* skill, or when the matched orch-* skill exists but is genuinely inapplicable. Routes the task to the closest specialist agent per the plugin-provided orchestration routing, runs a minimal plan-execute-review workflow, and recommends creating or amending a dedicated orch-* skill for the task category if it recurs.'
---

# Orchestrate Fallback

Use this skill whenever no dedicated orchestration skill — repo-native or
`copilot-app` plugin-provided — covers the requested task category, **or**
when the skill that does match is genuinely inapplicable to the task. It keeps
the repository's orchestration-first policy intact by providing a minimal,
generic workflow instead of delegating directly to a specialist agent.

> **Unmet preconditions are not "inapplicable."** If a dedicated skill matches
> the task category but its stated preconditions do not hold — no approved
> specification, no acceptance criteria, no prior architecture sign-off — invoke
> that skill anyway and derive the missing inputs inside it. Reach for this
> fallback only when no skill covers the category, or when the matched skill
> targets a fundamentally different kind of work.

## Input Expectations

- Task description and desired outcome.
- Task category (e.g. testing, tooling, CI, misc script) so the closest
  specialist agent can be selected.
- Confirmation that no existing `orch-*` skill (repo or plugin) already
  covers this category — or a stated reason why the matched skill targets
  fundamentally different work.

## Workflow Stages

> Agent transitions require explicit user approval before switching. If the
> chosen specialist agent is not installed, perform the step directly and
> continue.

### Stage 1: Routing Check
- Confirm no dedicated `orch-*` skill (repo-native or plugin) matches the
  task category.
- If one does match but its preconditions are unmet, **stop and invoke that
  skill instead** — unmet preconditions do not justify this fallback.
- Pick the closest specialist agent for the task category (e.g.
  `csharp-coding:coding` for implementation, `architecture:architect` for
  architecture-adjacent work, `documentation:documentation` for docs).

**Agents:** none (routing decision only)

### Stage 2: Plan
- Restate the task goal and scope in one or two sentences.
- Identify the files/folders likely touched and any instructions files that
  govern them (`.github/instructions/*.instructions.md`).

**Agents:** the specialist agent selected in Stage 1

### Stage 3: Execute
- Perform the change directly, following any applicable instructions files
  and repository guardrails.
- Keep edits scoped to the stated task; do not invent unrelated structure.

**Agents:** the specialist agent selected in Stage 1

### Stage 4: Review & Recommend
- Verify the change against the smallest relevant validation (build/lint/test)
  if applicable.
- Summarize what changed for the user.
- **Recommend creating a dedicated `orch-*` skill** (in a new session) if this
  task category is likely to recur, so future requests route through a proper
  orchestration instead of this fallback. If the fallback was reached because
  an existing skill was inapplicable, recommend amending that skill's scope
  instead of adding a new one.

**Agents:** the specialist agent selected in Stage 1

## Usage Pattern

```text
Invoke: orch-fallback
- Task: "Add a PowerShell lint script to CI"
- Category: tooling/CI
- Goal: closest specialist agent implements it directly, no dedicated orch-* skill exists yet
```

## Output Expectations

- Task completed via the closest specialist agent, following repo guardrails.
- Changed files/paths summarized for the user.
- A recommendation to create a dedicated `orch-*` skill in a new session if
  the task category recurs.

## Canvas Interface

This skill reports progress through the `orch-dashboard` canvas extension
(`plugins/copilot-app/extensions/orch-dashboard/`). If the extension is not
installed, skip the canvas calls below and continue through standard chat
interaction.

- Open canvas `orch-dashboard`, then call `start_run` with
  `skillId: "orch-fallback"` and these stages: Routing Check, Plan, Execute,
  Review & Recommend.
- Before each stage, call `update_stage` with `status: "in_progress"`.
- After each stage, call `update_stage` again with `status: "done"` (or
  `"blocked"`/`"skipped"`) and an `output` summary.
- Call `finish_run` with the final status and a summary once the task is
  complete, including the recommendation from Stage 4 if applicable.

See `plugins/copilot-app/extensions/orch-dashboard/README.md` for the full
canvas action contract.

## Reference

- `.github/instructions/context-loading.instructions.md`
