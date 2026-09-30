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

After build and tests pass, a code-modifying flow's Validation phase hands the change to the
`qa` agent. It starts the application through Aspire, drives the Blazor harnesses with
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
depends-on: [".devbook/tech/ai-development.md#agent-skills", ".devbook/tech/ai-development.md#devbook-procedures"]
date: 2026-09-25
```

How to `run` the application, `show` a branch's change, `capture` evidence, `debug` an
observed issue, and `estimate` work are this repository's own skills, each reached through a
wrapper per host that fixes its goal. `run` is a Claude Code recipe at
`.claude/skills/run-backlog/SKILL.md` with a Copilot twin; the other bodies live under
`.agents/skills/`. A flow calls `run` at its `app.start` extension instead of guessing a
command.

- **Used for** — starting the AppHost the way `run` says, walking a
  change for a reviewer, and finding a cause from logs and traces without handing the person
  a debugger.
- **Adopted by** — the flows, since the procedures were seeded on 2026-09-25. The `start`
  procedure became the `run` recipe on 2026-09-30.
- **Evidence** — none yet beyond the flows that started the application this way.
- **Limits** — the body is the repository's to edit; the goal in the wrapper is not.

## Devbook checks around a flow

```meta
status: trial
type: practice
stage: [test]
depends-on: [".devbook/tech/ai-development.md#devbook-plugin", ".devbook/tech/ai-development.md#delivery-engine"]
related: [".devbook/ai/05-unattended-runs.md#scheduled-devbook-upkeep"]
date: 2026-09-25
```

The stack config hangs the devbook skills on the flows' extension points: `devbook:validate`
when a session starts, `devbook:verify-change` as the flow's `verify` step — does the chapter
still say what the code does — and `devbook:update` when a flow ends.

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
