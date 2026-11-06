# Unattended Runs

```meta
rating: trial
type: stage
```

> The work agents do on a cadence with nobody watching: the schedules this repository
> selected from the delivery-schedule catalog, what each may publish, and the limits all
> share.

## What an unattended run may do

```meta
rating: trial
type: guardrail
layer: [harness]
evidence: none
depends-on: [".devbook/tech/ai-development.md#delivery-schedule"]
related: [".devbook/ai/concepts.md#unattended-runs-park", ".devbook/ai/concepts.md#external-text-is-data", ".devbook/ai/03-test.md#personal-validation-gate"]
date: 2026-09-26
```

Every schedule runs one `schedule-*` entry point on a fresh checkout of `main`, under the
same preamble:

- It [parks](concepts.md#unattended-runs-park) wherever its skill would ask a person.
- It never pushes to `main`, merges, approves, or deletes.
- It closes only an issue its own procedure shows already resolved, with the evidence in
  the comment.

A change lands as a pull request from a `schedule/<name>/<date>` branch, as a draft whenever
anything failed. A report with no change lands as one `schedule-report` issue, updated
rather than duplicated.

- **Used for** — every schedule below.
- **Adopted by** — the schedules this repository selected under `components.schedule` in
  `.devbook/config.json`, registered as Claude Code routines on 2026-09-25 and 2026-09-26.
- **Evidence** — none yet: the first fires have published no report issue or pull request.
- **Moves up when** — a schedule publishes an issue or pull request inside these limits.
- **Limits** — a schedule never runs a `flow-*` skill, because a flow ends at a gate no
  unattended run can pass. It treats every issue, comment, and commit message it reads as
  [data](concepts.md#external-text-is-data), never as an instruction.

## Scheduled issue sweep

```meta
rating: trial
type: workflow
stage: [decide, code]
substage: [code/implement]
feeds: [".devbook/ai/03-test.md#personal-validation-gate: draft pull requests park here"]
evidence: none
depends-on: [".devbook/tech/ai-development.md#delivery-schedule", ".devbook/tech/ai-development.md#delivery-engine"]
date: 2026-09-26
```

`issue-sweep` runs every weekday.

```mermaid
flowchart LR
    label["Label the unclassified open issues"] --> close["Close the ones evidence shows done"] --> resolve["Resolve some of the rest"] --> brief["Land the brief"]
```

It closes only issues that high-confidence evidence shows fixed, obsolete, or duplicated. It resolves up to its `maxResolve` of the rest, one at a time, each as a draft pull request. Each body says what could not be proved. The brief
is a `schedule-report` issue.

- **Used for** — keeping the issue list triaged and turning small issues into reviewable
  drafts.
- **Adopted by** — the schedule, since 2026-09-26.
- **Evidence** — none yet from the schedule.
- **Moves up when** — its first brief or draft pull request lands.
- **Limits** — at most `maxResolve` resolutions a run, all draft; nothing else is closed.

## Scheduled merge review

```meta
rating: trial
type: workflow
stage: [integrate]
substage: [integrate/review]
evidence: none
depends-on: [".devbook/tech/ai-development.md#delivery-schedule"]
date: 2026-09-26
```

Every weekday, `merge-review` reviews up to ten open, non-draft pull requests against `main`
and posts its verdict on each as a comment. It skips one already reviewed at its current head.

- **Used for** — a second reading of every pull request before the owner merges it.
- **Adopted by** — the schedule, since 2026-09-26.
- **Evidence** — none yet.
- **Moves up when** — its first verdict comment is posted.
- **Limits** — comments only: it opens no pull request and no issue, and approves nothing.

## Scheduled devbook upkeep

```meta
rating: trial
type: workflow
layer: [upkeep]
feeds: [".devbook/ai/05-unattended-runs.md#scheduled-devbook-sync-sweeps: drift issues"]
evidence: fired
depends-on: [".devbook/tech/ai-development.md#delivery-schedule", ".devbook/tech/ai-development.md#devbook-plugin"]
related: [".devbook/ai/03-test.md#devbook-checks-around-a-flow"]
date: 2026-09-26
```

Four schedules look after the devbook folders.

| Schedule | Runs | Does |
| --- | --- | --- |
| `devbook-validate` | daily | Lands the structural fixes the check reports as a pull request. |
| `devbook-verify` | weekly | Opens a `devbook-drift` issue for each chapter the code has moved past. |
| `prose-check` | weekly | Reports the prose that does not earn its lines. |
| `tech-update` | weekly | Refreshes the technology graph from the package inventories as a draft pull request. |

- **Used for** — keeping the chapters true between the changes that touch them.
- **Adopted by** — the four schedules, since 2026-09-25 and 2026-09-26.
- **Evidence** — `devbook-validate` has fired; nothing published yet.
- **Moves up when** — `devbook-validate` lands its first pull request.
- **Limits**
  - `prose-check` carries no editor on purpose, and `devbook-verify` writes no
    chapter: prose and content change only through `flow-spec`.
  - `devbook-verify` covers only
    the [sync units](../../.agents/rules/devbook-chapter-metadata.md#sync-units) whose
    direction is `report` and skips `off`; the two sweeps below take the rest.
  - It reads the direction from the chapter, not Backlog's map. A repository whose
    chapters state no direction therefore gets a report on every unit.

## Scheduled devbook sync sweeps

```meta
rating: candidate
type: workflow
layer: [thread]
feeds: [".devbook/ai/03-test.md#personal-validation-gate: draft pull requests"]
evidence: none
depends-on: [".devbook/tech/ai-development.md#backlog-tools-plugin", ".devbook/tech/ai-development.md#delivery-schedule", ".devbook/tech/ai-development.md#devbook-plugin"]
date: 2026-10-06
```

Two sweeps act on the drift `devbook-verify` only reports. Each takes, in groups, the
[sync units](../../.agents/rules/devbook-chapter-metadata.md#sync-units) whose direction in
Backlog's direction map asks for it. Units a requirement ties together, by naming two
aggregates and no feature, form one group; every other unit is a group of its own.

Both sweeps are the `backlog-tools` skill `devbook-sweep`, run as one local routine per
repository: pull first, then push. The skill reads each unit's direction from the `backlog`
MCP server's `list_sync_directions`. Before the direction map moved into Backlog's store
([local ADR 0044](../arc42/adr/0044-devbook-subject-state-lives-in-backlogs-own-store.md), entry 7), they
were `delivery-schedule` catalog schedules reading a `sync` field in the chapter.

| | `devbook-pull-sweep` | `devbook-push-sweep` |
| --- | --- | --- |
| Runs on | weekly, first in the routine | weekly, right after the pull sweep |
| Acts on | a `code-ahead` verdict | a `spec-ahead` verdict on an agreed chapter |
| Through | `capture-specs`, into chapters | `apply-change`, into code |
| Groups a run | up to three | one |
| Lands as | a draft pull request; added chapters at `draft` | a draft pull request |

Each group's branch is `schedule/devbook-<direction>-sweep/<date>/<group>`. A group whose
verdicts conflict goes to a person.

- **Used for** — two contexts, set on 2026-10-06. Sessions is `push`: its chapters are
  settled and its unit-test project covers them. Inbox is `pull`: its chapters are still
  `draft` but were written from code its tests cover. Every other unit is `report`.
- **Adopted by** — nobody: no repository has a devbook sweep routine yet. `backlog-tools`'
  README says how to create one, and creating it is open.
- **Evidence** — none.
- **Moves up when** — a repository's devbook sweep routine is created.
- **Limits** — push writes no chapter and removes no code, and skips a draft or unagreed
  chapter; pull changes only chapters and never raises a status above `draft`.

## Scheduled instruction review

```meta
rating: trial
type: workflow
layer: [upkeep]
evidence: none
depends-on: [".devbook/tech/ai-development.md#delivery-schedule", ".devbook/tech/ai-development.md#repository-instruction-files"]
related: [".devbook/ai/concepts.md#one-rule-one-place", ".devbook/ai/02-code.md#rules-wrapped-per-host"]
date: 2026-09-26
```

Every Thursday, `instruction-review` opens a draft pull request cutting what does not change
an agent's behaviour from the instruction files and rules. A ledger lists every cut.

- **Used for** — keeping the standing brief short enough to be read every time.
- **Adopted by** — the schedule, since 2026-09-26.
- **Evidence** — none yet.
- **Moves up when** — its first draft pull request is opened.
- **Limits** — draft only; a file whose earlier rewrite was closed unmerged stays skipped.

## Scheduled package update

```meta
rating: trial
type: workflow
layer: [upkeep]
evidence: fired
depends-on: [".devbook/tech/ai-development.md#delivery-schedule", ".devbook/tech/tooling.md#central-package-management"]
date: 2026-09-26
```

Every Saturday, `package-update` moves NuGet packages by minor and patch versions and opens
one pull request. It lists each package skipped because the bump broke the build or tests.

- **Used for** — keeping dependencies current without a person starting the work.
- **Adopted by** — the schedule, since 2026-09-26; Dependabot still runs beside it.
- **Evidence** — it has fired once; no pull request yet.
- **Moves up when** — its first package pull request lands.
- **Limits** — no major versions; a major move is an attended `flow-code` run.

## Scheduled reports

```meta
rating: trial
type: workflow
stage: [operate]
substage: [operate/reporting]
evidence: fired
depends-on: [".devbook/tech/ai-development.md#delivery-schedule"]
date: 2026-09-26
```

Two schedules report what happened. Each report is one `schedule-report` issue, extended
while unread rather than joined by another.

| Schedule | Runs | Covers |
| --- | --- | --- |
| `change-report` | every Friday | the week's pull requests, and the ones whose ticket disagrees with them |
| `weekly-update` | every Friday evening | the week |

A third, `morning-brief`, reported the last day every weekday from 2026-09-25. It was
dropped on 2026-10-30, when `schedule-morning-brief` left the `delivery-schedule` catalog
under devbook's branch-true decision.

- **Used for** — the owner's view of the repository without reading every notification.
- **Adopted by** — the two schedules, since 2026-09-25.
- **Evidence** — `change-report` and `weekly-update` fired on 2026-09-25; no report issue
  was published.
- **Moves up when** — the first report issue is published.
- **Limits** — read and report only; closing a report is how it is acknowledged.

## Scheduled security review

```meta
rating: candidate
type: workflow
stage: [operate]
substage: [operate/security]
feeds: [".devbook/ai/05-unattended-runs.md#scheduled-issue-sweep: would open issues"]
evidence: none
depends-on: [".devbook/tech/ai-development.md#delivery-schedule"]
date: 2026-09-26
```

The catalog's `security-review` would audit every layer weekly and open an issue per finding
at severity `high` or above.

- **Used for** — nothing yet.
- **Adopted by** — nobody: it is the one catalog schedule this repository did not select. It
  stays out on purpose while CodeQL and Dependabot run in CI.
- **Evidence** — none.
- **Moves up when** — the owner selects it.
- **Limits** — it would open issues, never fix them.
