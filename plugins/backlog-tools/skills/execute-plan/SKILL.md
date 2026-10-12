---
name: execute-plan
description: Execute a whole Backlog import plan by delegating its prompt entries to sub-agents — each entry run through run-plan-item in its own worktree and landed through its own pull request, entries that wait on nothing in parallel, an entry's dependents started once it lands. Reads the plan from the Backlog MCP server by its +tag, or from a plan file import-plan wrote. Lands per item, on one plan/<slug> integration branch, or as stacked pull requests; attended, or unattended with every entry a draft pull request and Personal Validation moved onto it. Stops at task and test entries, which only the person does, with the application started for them to validate against. Names its session for the plan and each sub-agent for its entry, and hands ready entries in another repository to a new session in that repository's checkout.
disable-model-invocation: true
---

# Execute a Backlog plan

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

This skill is the layer above a flow: it orders the plan and hands out entries, and changes
no file itself. Every entry is one flow, run by one sub-agent per `assets/item-brief.md`.
Read `../import-plan/assets/backlog-import-grammar.md` — `## Metadata line`,
`## Entry kinds` and `## Two levels` are what the plan's lines mean — and
`assets/landing-modes.md`, which defines the modes, the branch names and every merge, and
`assets/handoff.md`, which names this session and its sub-agents and hands an entry in
another repository to a session there.

## Inputs

- **The plan.** A `+tag` → `get_plan_items` on the `backlog` MCP server; a path → the plan
  file, statuses then read per entry with `read_item` when the server is present. Neither
  given → ask. Without the server, an entry's state is its pull request's (open, merged).
- **Landing**: *per item*, *branch* or *stacked*. **Attendance**: *attended* or *unattended*.
  Taken from the request (`/backlog-tools:execute-plan +tag unattended stacked`); otherwise
  asked in one question. Unattended per item is refused with the reason.
- **Parallelism.** At most 3 sub-agents at once unless the person names another number.

## Workflow

1. **Graph.** Title this session per **Names** in `assets/handoff.md`. Skip the `plan`
   entry. Keep `prompt`, `task`, `test` steps with their type, status, `after:` edges, `repo:` and title. Refuse a cycle. Read each entry's pull request
   (`gh pr list --head <slug>/<id> --state all --json number,state,isDraft,baseRefName,body`).
   An entry is *done* when its status is Done/Archived or its pull request merged — into
   `plan/<slug>` counts in branch mode; *ready* when it is a `prompt` in this checkout's
   repository, not done, not running, not blocked, and the landing mode lets its `after:` edges start it. Show the graph, the
   modes and the first frontier, then go: from here an unattended run asks nothing.
2. **Resume.** Before dispatching, act on what an earlier run left: an entry or closing pull
   request merged into `<base>` since → `transition` its entries to Done and remove their
   worktrees; then restack, merge what is settled, re-check blocked entries, and tear down a
   manual entry's app once it is Done — all per `assets/landing-modes.md`.
3. **Dispatch.** For each ready entry, `transition` it to In progress and `link_session` it
   with this session's id, then start one background sub-agent with the brief filled in,
   labelled `<tag>:<n> - <Title>`; re-read an entry's status just before, and skip it when it
   is no longer ready. An entry that would be ready but for its repository is handed to a
   session there, per **Handoff** in `assets/handoff.md`.
   The plugin's plan-progress band and pane know each sub-agent's entry by the brief's first line, so
   that line keeps its shape.
4. **Gate.** *Attended*: a sub-agent returns `gate` with a review handoff. Relay it verbatim
   with the entry's `<n> - <Title>` as it arrives — several waiting are listed in plan order
   and each is decided on its own — and send the decision back to that sub-agent. *Unattended*:
   no gate here; the sub-agent returns `pr <url>`, a draft.
5. **Land.** Branch: merge each settled entry into `plan/<slug>`. Stacked: a dependent may
   start on its parent's open pull request. On any merge into `<base>`, `transition` the entry
   to Done, restack its children, and `git worktree remove` it. Go to step 3. A failure blocks
   the entry and its dependents only.
6. **Wait.** While sub-agents or check watches run, end the turn: their completion wakes this
   session. Unattended, keep one one-shot 30-minute fallback wake-up pending as well,
   replacing the previous one, so a lost notification does not end the run. A merge only the
   person makes is never waited for: the run goes on to Stop.
7. **Stop.** No entry ready and none running: list what blocks — each `task`/`test` the person
   has to do with the prompts waiting on it, each blocked entry with its reason and
   dependents, each pull request waiting on the person, each handoff to another repository
   with its path and entries. Branch mode: open the closing pull
   request. Then start the app for the next manual entry and hand it over, per **Manual
   entries** in `assets/landing-modes.md`. Invoking this skill again resumes from the plan's
   state; nothing is kept in the session but what the pull requests and Backlog hold.
8. **Report.** Per entry: done before, landed now (pull request, draft or merged), waiting
   (on what), blocked (why), manual, handed off (where); then the running app's URLs and its worktree. An
   unattended run ends with a push notification carrying the counts and the links **Manual entries** names.

## Never

- Edit or commit in this checkout or another repository's; run a `task` or `test` — starting the app for one is
  setup, not running it; tick a manual entry's checks or mark it Done; mark an entry Done
  before it reaches `<base>`; merge into `<base>` directly; force a branch other than a restacked entry's own;
  approve a gate for the person, or take a pull request out of draft before its review is
  settled.
