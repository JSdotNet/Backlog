---
name: orch-fallback
description: 'Generic orchestration entrypoint for this repository, used when a task has no dedicated orch-* skill. Routes the task to the closest specialist agent per workflow-routing.instructions.md, runs a minimal plan-execute-review workflow, and recommends creating a dedicated orch-* skill for the task category if it recurs.'
---

# Orchestrate Fallback

Use this skill whenever `.github/instructions/workflow-routing.instructions.md`
has no dedicated orchestration skill for the requested task category. It keeps
the repository's orchestration-first policy intact by providing a minimal,
generic workflow instead of delegating directly to a specialist agent.

## Input Expectations

- Task description and desired outcome.
- Task category (e.g. testing, tooling, CI, misc script) so the closest
  specialist agent can be selected.
- Confirmation that no existing `orch-*` skill (repo or plugin) already
  covers this category — check `workflow-routing.instructions.md` first.

## Workflow Stages

> Agent transitions require explicit user approval before switching. If the
> chosen specialist agent is not installed, perform the step directly and
> continue.

### Stage 1: Routing Check
- Confirm no dedicated `orch-*` skill (repo-native or plugin) matches the
  task category in `workflow-routing.instructions.md`.
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
  orchestration instead of this fallback.

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

## Reference

- `.github/instructions/workflow-routing.instructions.md`
