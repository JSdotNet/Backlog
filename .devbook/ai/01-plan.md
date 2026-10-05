# Plan

```meta
status: adopted
type: stage
```

> How work is agreed, written down, and sized before anyone changes code.

## Devbook chapters through flow-spec

```meta
status: adopted
type: workflow
stage: [plan]
depends-on: [".devbook/tech/ai-development.md#delivery-engine", ".devbook/tech/ai-development.md#devbook-plugin"]
related: [".devbook/ai/concepts.md#task-scoped-context", ".devbook/ai/03-test.md#personal-validation-gate"]
date: 2026-09-26
```

A change to an arc42 chapter, a decision record, a bounded context, the technology graph,
a design guideline, or this record runs through the delivery engine's `flow-spec`. It loads
the folder's own rule, drafts through the role the folder maps to, runs the devbook check,
and stops at Personal Validation before a pull request.

- **Used for** — decision records, domain model changes, technology ratings, and the
  adoption record itself.
- **Adopted by** — every session that edits a devbook folder, routed there by the delivery
  engine's session context.
- **Evidence** — the devbook-adoption plan's documentation items, this chapter included,
  each ran as a `flow-spec` run with its stages on the delivery surfaces.
- **Limits** — never used to implement a chapter: that is `flow-code`, fed by
  `apply-change`.

## Import plans

```meta
status: adopted
type: skill
stage: [plan]
depends-on: [".devbook/tech/ai-development.md#backlog-tools-plugin"]
related: [".devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md", ".devbook/ai/02-code.md#plan-items-pasted-into-a-session"]
date: 2026-09-25
```

An agreed specification becomes a Backlog import plan through `backlog-import-plan`: one
`plan` entry that Import turns into a Roadmap Item, and under it the step entries — a
`prompt` an agent session runs, or a `task` or `test` only a person does — ordered by
`after:`.

- **Used for** — breaking a multi-session feature into roadmap items and the prompts that
  deliver them, with a review page beside the raw plan.
- **Adopted by** — the repository owner, for every multi-session feature.
- **Evidence** — the roadmap import decision (local ADR 0013) and the plans run through it,
  `devbook-adoption` among them.
- **Limits** — user-invoked only (`disable-model-invocation`); an agent never writes a plan
  on its own initiative, and the skill never talks to the app or to GitHub.

## Estimate against landed work

```meta
status: trial
type: skill
stage: [plan]
depends-on: [".devbook/tech/ai-development.md#agent-skills"]
date: 2026-09-26
```

A unit of work gets story points on the 1/2/3/5/8/13/21 scale through the repository's own
`estimate` procedure, sized against a table of Backlog's own finished work and naming the
reference each estimate was compared with.

- **Used for** — the `effort:` on plan entries and issues.
- **Adopted by** — the procedure was adopted on 2026-09-26; plan entries written before it
  carry efforts sized without the reference table.
- **Evidence** — none yet: no estimate has been checked against how long the work took.
- **Limits** — points size work, never time; turning them into a date is a measured pace's
  job, not the estimate's.

## Answer reading notes

```meta
status: trial
type: skill
stage: [plan]
depends-on: [".devbook/tech/ai-development.md#backlog-tools-plugin", ".devbook/tech/ai-development.md#model-context-protocol-servers"]
related: [".devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md"]
date: 2026-09-26
```

The private notes a person leaves on devbook chapters while reading them in the app are read
over the Backlog MCP server by `backlog-handle-remarks`, answered in the chapter as a devbook
`annotation` fence, and only then resolved in the app.

- **Used for** — turning a reader's questions into written answers next to the passage they
  were asked about.
- **Adopted by** — the repository owner, on demand; `backlog-run-plan-item` runs the short
  form of it after an item rewrites a chapter.
- **Evidence** — the answer loop shipped in PRs #580 and #581; no sweep of a full inbox is
  on record yet.
- **Limits** — the app is never the fence's writer, and a note is resolved only after its
  fence is written.
