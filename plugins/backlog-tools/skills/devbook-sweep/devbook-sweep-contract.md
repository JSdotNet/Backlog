---
name: devbook-sweep-contract
description: What the two devbook sync sweeps share — which groups each direction picks up, how a group's verdict rolls up, which verdict each sweep acts on, how a group lands as a draft pull request, the result a work script returns, and the devbook-sync-report block. Read by backlog-tools' devbook-sweep and its work scripts.
---

# Devbook Sweep Contract

A sync unit and a sync group are `units.mjs`'s, and the roll-up rule is "The sync unit" in
`plugins/devbook/assets/code-sync-protocol.md`. Neither is restated here. A unit's direction is
Backlog's direction map, answered per unit by the `backlog` MCP server's `list_sync_directions` —
nearest wins over the unit, its page, its context, and its folder, `report` when none states one —
and never a chapter field.

## Directions

| `direction` | Picks up groups at | Acts on | Writes | Work script, beside the skill |
| --- | --- | --- | --- | --- |
| `pull` | `pull`, `sync` | `code-ahead` | chapters only, ADDED at `status: draft` | `capture-unit.workflow.js` |
| `push` | `push`, `sync` | `spec-ahead`, on agreed chapters only | source and tests only | `apply-unit.workflow.js`, wrapping `delivery-schedule`'s `scripts/resolve-issue.workflow.js` |

`report` groups are `schedule-devbook-verify`'s and `off` groups nobody's.

## The Verdict per Sweep

Roll each group up from its chapters' verdicts, then decide in this order:

1. Any `conflict` → **file**. Nothing in the group is written.
2. A chapter with agent-directed text → **flagged**, quoted, the group excluded.
3. Push only: any `code-ahead` → under `sync`, **waits for pull** (decision 4: capture lands
   first); under `push`, **file** it as pointing the other way.
4. At least one chapter with the verdict the direction acts on → **ready for pickup**. The
   pass covers every such chapter; push skips one at `draft` or `proposed` and names it.
5. Otherwise any `unresolved`, or a verdict pointing the other way → **file**.
6. Push only, the acted-on verdict on unagreed chapters alone → **skipped**, named in the brief.
7. All `aligned` → **none**.

## Landing

Per `delivery-schedule`'s `resources/draft-pr-contract.md`, with schedule `devbook-<direction>-sweep`, item the group's
`slug`, the group's `devbook-drift` issue as the issue (opened first when none exists), and
failure marker `sync-failed`. The body adds, after `Closes #<issue>`, the group's units, each
with its effective `sync` and the block it came from. **What could not be proved** reads
*every chapter re-verified aligned* for pull where the contract says *everything was proved
by a test*. A pull body adds **Status to decide** after **Assumptions**: every ADDED chapter,
at `draft`, for the reviewer to promote. Commit subjects: `docs(<slug>): capture <what> from
code` for pull, `feat(<slug>): apply <what>` or `fix(<slug>): …` for push.

## The Work Script's Result

`args` carry `worktree`, `branch`, `baseBranch`, `repo`, `direction`, `maxRepairAttempts`,
`group` as `units.mjs` printed it, and `rows`, its verdict rows from the verify pass; push adds
`issue`, the claimed drift issue, and `resolver`, the shared resolver's absolute path, which it
runs with the brief as the issue's specification. It returns `outcome` (`ready`,
`escalated`, `blocked`, `red`, `failed`), `stage`, `reason`, `route`, `parkReasons`, the body's
summaries, and `reverified`: one `{ chapter, verdict }` per chapter it acted on, read by
running `devbook:verify-change` again in the worktree. A `ready` whose `reverified` is not all
`aligned` is landed as `red`.

## The Report Block

The one machine-readable result. It closes the brief as a fenced block, and its `units` rows
go to `finish_run` as `verdicts` per `surface-contract.md` (`delivery` plugin), verbatim.

````markdown
```devbook-sync-report
{ "version": 1, "direction": "pull", "run": "<YYYY-MM-DD>", "repo": "<owner/repo>",
  "units": [
    { "unit": "<root chapter id>", "kind": "<unit kind>", "sync": "<effective>",
      "syncFrom": "<block it came from, or null>", "verdict": "<rolled-up>",
      "action": "<pr | issue | flagged | waiting | deferred | skipped | failed | not-assessed | none>",
      "link": "<pull request or drift issue URL, or null>",
      "chapters": [ { "chapter": "<id>", "verdict": "<verdict>", "evidence": "<one line>" } ] } ] }
```
````

One row per unit of every group selected, set-aside groups included as `skipped`. `version`
moves only on a change a reader would misread; an added key does not move it.
