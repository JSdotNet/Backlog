---
name: weekly-cost-analysis
description: 'Report what the delivery runs on this machine cost over a window, across every repository and grouped by repository first, from the token usage the backlog MCP server measured — by model, stage and sub-agent, ranked on the uncached figure — and name the changes that would save the most. Use when: what did the runs cost, token spend this week, cost spike, which repository or agent burns the most tokens.'
---

# Weekly cost analysis

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

Reads only what the `backlog` MCP server measured and never estimates. Without a `backlog`
server in the live tool list, or with no run in the window, the report says so and stops.
It writes nothing — no file, no issue, no comment — and changes no repository: the report is
this session's last message.

## Inputs

- Window: `7` days (default), or an ISO range `YYYY-MM-DD:YYYY-MM-DD`, both days included.
- Format: `summary` (default, the top tips) or `full` (every tip and the breakdown).
- Tips: `5` (default) — how many the `summary` format keeps, across every repository.

## Workflow

### Phase 1 — Retrieve

1. `list_run_usage` on the `backlog` server: `days` for a count, or `from` and `to` for a
   range. It answers every run whose start falls in the window, on every repository this
   machine recorded, each placed in a repository or in none, with `repositories` — the
   totals per repository — beside them. A refusal names what is wrong with the window:
   report it and stop.
2. A second call for the window just before it, for the change on last window, as whole
   days: `from` the answer's `from` date less the window's length in days, `to` the day
   before the answer's `from` date. A `days` window starts part-way through a day, so that
   comparison is approximate by part of a day; the footer says so. Keep only its
   `repositories` totals.
3. Every name in `unreadable` is a run folder that could not be read: a run it held is
   missing, and the report says so in *Run*.

### Phase 2 — Analyse

Per repository first, the runs nothing placed last, as one group:

4. Rank on `uncachedTokens` — input, cache writes and output, the cache reads left out. The
   headline `tokens` counts the cached prompt again on every turn and runs to many windows'
   worth; it is shown, never ranked on.
5. Aggregate the repository's runs: per model from each run's `models` — a `reported`
   model's share is of the sum of the reported models' uncached figures, since they come
   from Claude Code's request telemetry rather than the run files the totals come from; a
   `named` model has no figures and is listed as ran, never given a share; per stage from
   `stages`; per delegated agent from `subAgents`; and the runs whose `compactions` is above
   zero, which outgrew their window. A run with a null `total` recorded no usage — its
   session did not report — and counts as a run with no figure, never as zero.
6. Name the drivers: the model with the largest share, the most expensive stage and agent,
   the run whose compactions mark a stage pushing it past its window, and a stage whose
   delegated share is zero where it was meant to be delegated (Build & Test and Verify are,
   by default), since it charged the owner session for the whole of it.
7. Turn each into a repository action, a *Needs you* row: a cheaper model in the `## Model`
   section of an agent spent on low-complexity work, the prompt of a costly skill trimmed,
   a stage delegated that ran inline, or runs restarted where a handoff would have resumed
   them from persisted state. Name the file as that repository spells it; one not checked
   out here is named without a path.

### Phase 3 — Report

8. The message in `assets/cost-analysis-shape.md`, one section per repository in order of its
   uncached total, largest first.

## Never

- Estimate, interpolate or hand-write a token figure: every number comes from
  `list_run_usage`.
- Read `get_run` for figures: it leaves them out on purpose.
- Turn a null into a zero, or a `named` model into a share.
