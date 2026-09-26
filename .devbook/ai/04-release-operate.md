# Release and Operate

```meta
status: trial
type: stage
```

> How an approved change reaches `main`, and how the repository is looked after between
> changes.

## Merge-ready and CI repair

```meta
status: trial
type: skill
stage: [release]
depends-on: [".devbook/tech/ai-development.md#agent-skills"]
date: 2026-09-25
```

An agent brings an open pull request to merge-ready: it updates the branch, repairs failing
checks, and answers review findings, through the delivery engine's `pr-merge-ready` and
`fix-pr-checks` skills.

- **Used for** — pull requests left red by a flaky runner or a moved base.
- **Adopted by** — the repository owner, on demand.
- **Evidence** — none recorded yet.
- **Limits** — it never merges; auto-merge stays the owner's decision.

## Scheduled routines

```meta
status: candidate
type: workflow
stage: [operate, monitor]
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins"]
date: 2026-09-25
```

Recurring unattended runs from `delivery-schedule` — an issue sweep, reviews, a devbook
validation — each running one `schedule-*` entry point in a cloud session on a cadence.

- **Used for** — to be decided when the routines are set up.
- **Adopted by** — nobody yet.
- **Evidence** — none yet.
- **Limits** — a routine never runs a `flow-*` skill: a flow ends at a gate no unattended
  run can pass.

## Alerts to work items

```meta
status: candidate
type: skill
stage: [monitor]
depends-on: [".devbook/tech/ai-development.md#agent-skills"]
date: 2026-09-25
```

Application Insights exceptions become tracker work items through the delivery engine's
`sre-alerts-to-work-items`, so a production failure enters the loop at `plan`.

- **Used for** — to be decided.
- **Adopted by** — nobody yet; the nightly exception workflow fails preflight on missing
  environment variables.
- **Evidence** — none yet.
- **Limits** — it files items; it never fixes them.
