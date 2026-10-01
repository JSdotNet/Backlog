---
name: context-loading
description: Repository delivery policy - the gate that routes every change through a delivery flow, the specialist agents and procedures those flows use, what a flow verifies, and when the checked-in devbook folders (.devbook/arc42, .devbook/domain, .devbook/tech, .devbook/design, .devbook/ai) may be loaded as working context.
paths:
  - "**"
---

# Repository delivery and context policy

`CLAUDE.md` restates the gate for Claude Code; keep both in step. The folder rules themselves
live in `AGENTS.md`'s devbook section and `.agents/rules/devbook-*.md` and are not restated
here.

## The gate

**Before the first edit or create to any file under `src/` or `tests/`, invoke the matching
flow.** Exploration first is expected and does not consume the gate; the trigger is the first
write, not the first action. Never go straight from exploration to implementation.

| Change | Flow |
| --- | --- |
| Code: a feature, a bug fix, a refactor, a new module or service; and tooling, CI, scripting, documentation outside `.devbook/`, and housekeeping | `delivery:flow-code` |
| A devbook chapter, decision record or debt record under `.devbook/` | `delivery:flow-spec` |
| A dependency, package or framework move | `delivery:flow-update-packages` |
| Creating, governing or scaffolding a repository | `delivery:flow-project` |

Changes under `plugins/`, `tools/`, `build/`, `.github/` and `.claude/` are the first
row's tooling, CI and scripting, so they route through `delivery:flow-code` as well.

This repository ships no repo-native `flow-*` skill; all four come from the `delivery`
plugin. The repository owner authorizes running any of them, and the agents they hand
stages to, without per-session confirmation.

Apply the gate literally:

- **Size is not a criterion.** A one-control UI tweak and a multi-service feature route
  the same way.
- **A missing specification is not an exemption.** An ad-hoc request still routes through
  its flow, whose first stage derives the missing scope.
- **Unmet preconditions are not an exemption.** Invoke the flow anyway and derive the
  missing inputs inside it.
- **A phase skill is not a flow.** Loading `phase-validation` or another phase directly
  bypasses the run record and the Personal Validation gate.

Whatever the engine: never skip Personal Validation, and never open a pull request or
call a run complete without the person's explicit approval.

## What the flows lean on

`.devbook/config.json` binds them; read it rather than a copy here.

- **Specialist agents** — `architecture:architect` for the architecture role,
  `qa:qa` for QA, `domain-design:domain-architect` for domain, `ux-design:ux-designer` for
  UX, `documentation:documentation` for docs, and `csharp-coding:coding` for
  implementation. Product and security are deliberately unbound.
- **Procedures** — `.claude/skills/run-backlog/SKILL.md` (the `run` skill) starts the
  Aspire AppHost and says what healthy looks like and which harness answers which question;
  under `.agents/skills/`, `show.md` walks a branch's change in the harness that serves it;
  `capture.md` places evidence; `debug.md` finds a cause from logs and traces; `estimate.md`
  sizes work. A flow calls `run` at `app.start` rather than guessing a command.
- **QA depth** — the engine picks it from the change kind: full Playwright QA with capture
  for new behaviour, targeted checks for a fix, startup-only for a dependency move, and
  skipped when nothing runs. `policy` in the config caps it at `full`.
- **Checked-in end-to-end tests** — `tests/Backlog.EndToEndTests` is the repeatable form of
  scenarios QA has already walked, such as `ConferenceDayTests`.
  - Run it in Validation when the change touches a flow it covers:
    `$env:BACKLOG_E2E='1'; dotnet test --project tests\Backlog.EndToEndTests`, against this
    worktree's running AppHost. Its screenshots and `sync` logs under `.qa-workspace/e2e/`
    are evidence.
  - It stops and starts `sync` and resets the phone harness's pairing, so never run it while
    another scenario drives the same AppHost.
  - Its `README.md` covers the browser install and what a run changes.

A change that runs nothing — `.devbook/`, `.agents/`, `.claude/`, `.github/`, or
`README.md` alone — is verified by review plus the devbook checks:
`node .devbook/_tools/devbook-meta/build.mjs --check`, `node tools/devbook/check-metadata.mjs`
and `node tools/devbook/build-database.mjs --check`. Every rule here keeps `name`,
`description` and `paths`, and its wrappers stay derived from it:
`.claude/rules/<topic>.md` copies `paths`, and
`.github/instructions/<topic>.instructions.md` sets `applyTo` to `paths`, comma-joined.

## Context loading by flow and agent

Every edit to a devbook folder routes through `delivery:flow-spec`; say so when it answers.

- Architecture, arc42, ADR and TDR work — `flow-spec` on `.devbook/arc42/` and
  `architecture:architect` — may load `.devbook/arc42/`, but only the chapters in scope.
- Domain modelling — `flow-spec` on `.devbook/domain/` and `domain-design:domain-architect`
  — may load `.devbook/domain/`, but only the bounded contexts in scope.
- Design and UX work — `ux-design:ux-designer` — may load `.devbook/design/`, and stack,
  dependency or upgrade work may load `.devbook/tech/`, in both cases only the relevant files.
  `flow-spec` on `.devbook/tech/` may add the arc42 chapters that ground a stack choice.
- Work about how this team works with AI may load `.devbook/ai/`: `adoption-map.md` plus the
  stage files in scope. Agents do not read `ai/` to decide how to do their own task; it
  records a way of working, it does not instruct one.
- Implementation, bug-fix, package-update, documentation and UX flows do not load
  `.devbook/arc42/` by default. Consult it when the person asks for architecture context or
  the change depends on a specific decision, view, constraint or glossary term.
- UI work consults `.devbook/design/` when it touches visual design, interaction, content
  editing or accessibility, loading only the relevant guideline files.

## Documentation drift

After a change lands, check the devbook for what it moved — architecture, technology,
design, domain behaviour — and update the chapters in the same pull request.
