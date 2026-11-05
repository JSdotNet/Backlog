# Release and Operate

```meta
rating: trial
type: stage
```

> How an approved change reaches `main`, and how a production failure returns to the loop.
> Cadence runs are in `05-unattended-runs.md`.

## Merge-ready and CI repair

```meta
rating: trial
type: skill
stage: [integrate]
substage: [integrate/readiness]
evidence: none
depends-on: [".devbook/tech/ai-development.md#delivery-engine"]
related: [".devbook/ai/03-test.md#personal-validation-gate"]
date: 2026-09-25
```

An agent brings an open pull request to merge-ready: it levels the branch with `main`,
repairs failing checks, and answers review findings, through the delivery engine's
`pr-merge-ready`, `update-pr-branch`, and `fix-pr-checks` skills.

- **Used for** — pull requests left red by a flaky runner or a moved base.
- **Adopted by** — the repository owner, on demand.
- **Evidence** — none recorded yet.
- **Moves up when** — a red pull request is brought to merge-ready on record.
- **Limits** — it never merges; merging and auto-merge stay the owner's decision.

## Alerts to work items

```meta
rating: candidate
type: skill
stage: [operate]
substage: [operate/alerting]
feeds: [".devbook/ai/02-code.md#backlog-as-the-delivery-tracker: would enter Decide"]
evidence: none
depends-on: [".devbook/tech/ai-development.md#delivery-engine"]
date: 2026-09-25
```

Application Insights exceptions become tracker work items through the delivery engine's
`sre-alerts-to-work-items`, so a production failure enters the loop at `decide`.

- **Used for** — to be decided.
- **Adopted by** — nobody yet; the nightly exception workflow fails its preflight because
  the `backlog-sync` GitHub environment has no variables.
- **Evidence** — none yet.
- **Moves up when** — the `backlog-sync` environment gets its variables and the nightly
  workflow passes its preflight.
- **Limits** — it files items; it never fixes them.
