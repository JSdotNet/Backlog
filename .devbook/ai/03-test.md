# Test

```meta
status: adopted
type: stage
```

> How a change is proven before a person approves it.

## QA agent on the running application

```meta
status: adopted
type: agent
stage: [test]
depends-on: [".devbook/tech/ai-development.md#subagents", ".devbook/tech/ai-development.md#model-context-protocol-servers", ".devbook/tech/testing.md#playwright"]
related: [".devbook/ai/concepts.md#evidence-over-assertion"]
date: 2026-09-25
```

After build and tests pass, `flow-code`'s `phase-verify` hands the change to the `qa:qa`
agent. It starts the application on Aspire through the `run` procedure, drives the Blazor harnesses with
Playwright, and keeps a monitor on Aspire's logs and traces for the whole run, returning an
evidence path for every scenario it reports.

- **Used for** — full Playwright QA for new functionality, targeted checks for fixes and
  changes to an existing flow, startup-only for dependency moves.
- **Adopted by** — every code-modifying flow run.
- **Evidence** — screenshots and logs under `.qa-workspace/`, attached to the run's
  Validation stage.
- **Limits** — the MAUI heads cannot be driven, so validation targets the harnesses; the
  Playwright profile is shared per machine, so parallel sessions stagger QA.

## Repository procedures

```meta
status: trial
type: skill
stage: [code, test]
depends-on: [".devbook/tech/ai-development.md#agent-skills", ".devbook/tech/ai-development.md#devbook-plugin"]
date: 2026-09-25
```

How to `run` the application, `capture` evidence, `diagnose` an observed issue, and
`estimate` work are this repository's own skills. Each is reached through a wrapper per host
that fixes its goal. `run` is a Claude Code recipe at `.claude/skills/run-backlog/SKILL.md`
with a Copilot twin at `.github/skills/run/SKILL.md`. The other bodies live under
`.agents/skills/`. The devbook stamp lists the four under `components.devbook.procedures`.
`flow-code`'s `phase-verify` starts the application through `run` instead of guessing a
command.

- **Used for** — starting the AppHost the way `run` says, taking evidence of a change, and
  finding a cause from logs and traces without handing the person a debugger.
- **Adopted by** — the flows, since the procedures were seeded on 2026-09-25. The `start`
  procedure became the `run` recipe on 2026-09-30. With devbook 1.19 the `debug` procedure
  became `diagnose`, because a `debug` project skill shadowed Claude Code's own `/debug`.
  The `show` procedure was removed in the same change: a flow walks a change through `run`
  and `capture`, and a person on Claude Code who wants to see it working uses `/verify`.
- **Evidence** — none yet beyond the flows that started the application this way.
- **Limits** — the body is the repository's to edit; the goal in the wrapper is not. Devbook
  also ships a `prototype` procedure, which this repository deliberately has not adopted.

## Devbook checks around a flow

```meta
status: trial
type: practice
stage: [test]
depends-on: [".devbook/tech/ai-development.md#devbook-plugin", ".devbook/tech/ai-development.md#delivery-engine"]
related: [".devbook/ai/05-unattended-runs.md#scheduled-devbook-upkeep"]
date: 2026-09-25
```

The flows' `phases` maps in `.devbook/config.json` hang the devbook skills on three phases.
`devbook:validate` runs as a chore before `phase-update-base`. `devbook:verify-change` is
`flow-code`'s `phase-spec-check` and asks whether the chapter still says what the code does.
`devbook:update` runs as a chore after `phase-summary`.

- **Used for** — catching a chapter the change left behind before the person reviews it.
- **Adopted by** — every flow run since 2026-09-25.
- **Evidence** — none yet recorded against a pull request.
- **Limits** — `verify-change` reports drift and writes nothing; the chapter is updated
  through `flow-spec`.

## Personal Validation gate

```meta
status: adopted
type: guardrail
stage: [test, release]
depends-on: [".devbook/tech/ai-development.md#delivery-engine"]
related: [".devbook/ai/concepts.md#human-in-the-loop"]
date: 2026-09-25
```

No pull request is opened and no flow is marked complete until the repository owner has
looked at the change — the application started and the review links handed over for code,
the changed chapters summarized for documentation — and approved it in the session.

- **Used for** — every flow, documentation and code alike.
- **Adopted by** — every flow run; configuration may add gates, but the engine refuses to
  let it remove this one.
- **Evidence** — the approval and the owner's own words recorded on each run.
- **Limits** — an unattended run parks at this gate rather than approving itself.
