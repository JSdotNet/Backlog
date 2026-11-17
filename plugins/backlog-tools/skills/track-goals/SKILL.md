---
name: track-goals
description: "Keep the person's Backlog goals current through the backlog MCP server, so the stand-up and the weekly update read correct facts and each goal says what to do next. Each tick reads the goals and the last and next iteration, attaches a new task or plan to the one goal that holds everything it depends on, when it depends on something, proposes linking a pull request to the held entry it names and every other attachment with its evidence, proposes raising or resolving an impediment, applies a proposal only on the person's yes — in chat, or through a Backlog question when nobody is present — and reports per goal its standing, what finished in the last iteration, the next step and its open impediments, then what needs the person. Use when asked to update, track or tidy the goals — once, or under /loop /backlog-tools:track-goals to keep doing it. Writes only to Backlog's goals, impediments and pull request links; changes no file and is no flow."
argument-hint: "[weekly] [blocked=2] [failing=1]"
---

# Track the goals

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

A goal **holds** a plan (and each entry with its `+tag`), a task (and its linked pull requests) or a
pull request. Backlog's store is the only state: a tick over unchanged work changes nothing. Arguments
[defaults]: `weekly` (a week, worded for a stakeholder), `blocked` [2] worked days, `failing` [1] day
of failed checks. **Attended** is a run the person started in chat; a `/loop` tick is not.

## Each tick

0. **Mark.** No `backlog` server → say so, stop. Under `/loop`, first turn only: mark this session
   per `../../resources/standing-session.md`.
1. **Read** `read_iterations` (`length: week` with `weekly`): windows, standings, impediments; and
   `list_goals`: what each goal holds, by `id`. Repositories: this remote's and each a held item names.
2. **Candidates**, per repository: tasks in progress (`list_entries`); pull requests (`gh pr list
   --repo <owner/name> --state all --search "updated:>=<last.first>" --json
   number,headRefName,body,state,statusCheckRollup`), each standing for the entry `find_item`
   (`repoId` its `owner/name`, `externalId` its number) finds; `get_roadmap` tags with an open entry.
   Held, directly or by its plan's `+tag` → skip: attaching a task past its plan moves its count. A
   pull request with no entry whose branch or body names a held entry id or `+tag` is that work, never
   attached: propose `link_change` (`id` the entry, `repository`, `externalId`); a tag alone → *needs you*.
3. **Attach** a task or plan with `dependsOn` items, one goal holding them all: `attach_to_goal`
   (`taskId` or `planTag`). Weak: no `dependsOn`; title words; an unlinked pull request naming nothing
   held (`repository`, `number`). No goal, several, or `progress.item_in_other_goal` refused → *needs you*.
4. **Propose** each weak one with its evidence; `raise_impediment` (naming what it holds back) for a
   held task `blockedSince` ≥ `blocked` worked days or a held open pull request with checks failing
   ≥ `failing` days that no open impediment holds back; `set_impediment_state` `resolved` for one
   reading `readyToResolve`. Attended → ask in chat. A `/loop` tick → one `ask` (`Yes`, `No`) per
   proposal not yet asked this session, read by `get_answer` or delivered by the Manager (local
   ADR 0041); a no is not asked again. Apply each yes.
5. **Report** per goal: standing, `landsOn` against due; finished in `last` — `doneInWindow` and its
   `finished` items; next step — held items in progress and not blocked, then each held plan's first
   `ready` entry in plan order (`get_plan_items`) not in progress, blocked or archived; open
   impediments, ready to resolve marked. Then what this tick attached, and *needs you*: proposals
   waiting or with `ask` refused, unplaced candidates, refusals; when it holds any, open with the
   `show-me` skill's card if it has one. `weekly` → full sentences, else `../../resources/chat-reports.md`.
6. **Pace.** Under `/loop`, end the turn: 300 s while an `ask` waits, else 1800 s.

## Never

- `transition`, `set_blocked`, `comment`, edit an entry's text, or tag an entry to stand for a goal.
- `delete_goal`; `create_goal`, `update_goal`, `link_change` or `moveFromOtherGoal: true` without a yes.
- Raise or resolve an impediment without the person's yes (local ADR 0035): `readyToResolve` is a reading.
- Attach a session (local ADR 0037 is not built), write a file, or commit.
