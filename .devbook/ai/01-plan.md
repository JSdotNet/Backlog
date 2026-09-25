# Plan

```meta
status: trial
type: stage
```

> How work is agreed and written down before anyone changes code.

## Devbook chapters through flow-spec

```meta
status: trial
type: workflow
stage: [plan]
depends-on: [".devbook/tech/ai-development.md#devbook-plugin", ".devbook/tech/ai-development.md#claude-code-plugins"]
related: [".devbook/ai/concepts.md#task-scoped-context"]
date: 2026-09-25
```

A change to an arc42 chapter, a decision record, a bounded context, the technology graph,
or a design guideline runs through the delivery engine's `flow-spec`, which drafts through
the role the folder maps to and runs the devbook check before the human gate.

- **Used for** — decision records, domain model changes, and technology ratings.
- **Adopted by** — the repository owner, since the move to the `.devbook/` layout; the
  earlier `orch-*` flows carried the same work under the root-level folders.
- **Evidence** — none yet under this flow; the chapters it governs predate it.
- **Limits** — never used to implement a chapter; that is `flow-code`.

## Import plans

```meta
status: adopted
type: skill
stage: [plan]
depends-on: [".devbook/tech/ai-development.md#agent-skills"]
related: [".devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md"]
date: 2026-09-25
```

An agreed specification becomes a Backlog import plan through the repository's own
`backlog-import-plan` skill, and each item of it is later pasted back into a session and
run by `backlog-run-plan-item`.

- **Used for** — breaking a specification into roadmap items and the prompts that deliver
  them.
- **Adopted by** — the repository owner, for multi-session features.
- **Evidence** — the roadmap import decision (local ADR 0013) and the plans run through it.
- **Limits** — user-invoked only; an agent never generates a plan on its own initiative.
