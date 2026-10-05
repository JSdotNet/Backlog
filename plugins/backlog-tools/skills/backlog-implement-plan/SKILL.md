---
name: backlog-implement-plan
description: Implement a whole Backlog import plan by delegating its prompt entries to sub-agents — each entry run through backlog-run-plan-item in its own worktree and landed through its own pull request, entries that wait on nothing in parallel, an entry's dependents started once its pull request merges. Reads the plan from the Backlog MCP server by its +tag, or from a plan file backlog-import-plan wrote. Stops at task and test entries, which only the person does. Optional branch mode lands the whole plan on one integration branch and opens a single pull request to the base branch.
disable-model-invocation: true
---

# Implement a Backlog plan

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

This skill is the layer above a flow: it orders the plan and hands out entries, and changes
no file itself. Every entry is one flow, run by one sub-agent per `assets/item-brief.md`.
Read `../backlog-import-plan/assets/backlog-import-grammar.md` — `## Metadata line`,
`## Entry kinds` and `## Two levels` are what the plan's lines mean.

## Inputs

- **The plan.** A `+tag` → `get_plan_items` on the `backlog` MCP server; a path → the plan
  file, statuses then read per entry with `read_item` when the server is present. Neither
  given → ask. Without the server, an entry's state is its pull request's (open, merged).
- **Mode.** *Per item* (default): every entry's pull request targets the base branch and the
  person merges it. *Branch*: `plan/<tag-without-sigil>` is created from the base branch and
  pushed; entry pull requests target it and this skill merges each once its Personal
  Validation is approved and its checks are green; the plan ends in one pull request
  `plan/<tag>` → base. Create the branch remotely (`git push origin <base>:refs/heads/plan/<tag>`)
  so this checkout stays as it is. Ask when the request does not say.
- **Parallelism.** At most 3 sub-agents at once unless the person names another number.

## Workflow

1. **Graph.** Skip the `plan` entry. Keep `prompt`, `task`, `test` steps with their type,
   status, `after:` edges, `repo:` and title. Refuse a cycle. An entry is *done* when its
   status is Done/Archived or its pull request has merged; *ready* when it is a `prompt`,
   not done, and every `after:` is done. Show the graph and the first frontier; confirm the
   mode and go.
2. **Dispatch.** For each ready entry not already running, start one sub-agent with the
   brief, filled in: the entry text, its id, the tag, the repository, the base branch (the
   base branch or `plan/<tag>`). Entries in another repository than this checkout's are
   listed and left for a session there.
3. **Gate.** A sub-agent returns at Personal Validation with its review handoff. Relay it to
   the person verbatim with the entry's `<n> - <Title>`; send the decision back to that
   sub-agent, which then opens its pull request or revises. One gate at a time, in plan order.
4. **Merge.** Per item: wait for the person to merge, checking the open pull requests when
   they say so or when this skill is invoked again. Branch: merge the approved pull request
   into `plan/<tag>`. On a merge, `transition` the entry to Done, remove its worktree
   (`git worktree remove`), and go to step 2 with the new frontier.
5. **Stop.** No entry ready and none running: list what blocks — each `task`/`test` the
   person has to do, with the prompts waiting on it, and each pull request still open.
   Invoking this skill again resumes from the plan's state; nothing is kept in the session.
   All prompts done, in branch mode: open the `plan/<tag>` → base pull request through the
   repository's own pull-request rules and say it needs the person's approval and merge.
6. **Report.** Per entry: done before, landed now (pull request), waiting (on what), manual.

## Never

- Edit or commit in this checkout; run a `task` or `test`; mark an entry Done before its
  pull request merges; merge into the base branch, or skip an entry's Personal Validation.
