---
name: execute-plan
description: Execute a whole Backlog import plan by running each prompt entry in its own nested session — each entry run through run-plan-item in its own worktree and landed through its own pull request, entries that wait on nothing in parallel, an entry's dependents started once it lands. Reads the plan from the Backlog MCP server by its +tag, or from a plan file import-plan wrote. Lands on one integration branch or as stacked pull requests, always unattended — it asks the person nothing, every entry parks at its first gate as a draft pull request, and Personal Validation moves onto the drafts. Stops at task and test entries, which only the person does, with the application started for them to validate against. Names its session for the plan and each entry session for its entry, and hands ready entries in another repository to a new session in that repository's checkout.
disable-model-invocation: true
---

# Execute a Backlog plan

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

This skill is the layer above a flow: it orders the plan and hands out entries, and changes
no file itself. Every entry is one flow, run by one nested session per `assets/item-brief.md`.
Read `../import-plan/assets/backlog-import-grammar.md` — `## Metadata line`,
`## Entry kinds` and `## Two levels` are what the plan's lines mean — and
`assets/landing-modes.md`, which defines the modes, the branch names and every merge, and
`assets/handoff.md`, which names this session and its entry sessions and hands an entry in
another repository to a session there.

A run is always unattended: it asks the person nothing, from the first step to the report.

## Inputs

- **The plan.** A `+tag` → `get_plan_items` on the `backlog` MCP server; a path → the plan
  file, statuses then read per entry with `read_item` when the server is present. Neither
  given → stop and say so. Without the server, an entry's state is its pull request's (open, merged).
- **Landing**: *branch* (default) or *stacked*, taken from the request
  (`/backlog-tools:execute-plan +tag stacked`); none given → branch. A leftover `unattended` in
  the request is ignored; `attended` or *per item* stops the run, saying neither mode exists —
  nobody answers a gate here.
- **Ticket** (optional): `ticket:<value>`, for a repository whose conventions need one, when the
  plan states none. Per **Repository conventions** in `assets/landing-modes.md`.
- **Parallelism.** At most 3 entry sessions working at once — started or messaged by this run,
  or an earlier one's `list_sessions` row on a `<entry-branch>` branch of this plan, and not yet returned a line since — unless the person names another number.

## Workflow

1. **Graph.** Title this session per **Names** in `assets/handoff.md`. Skip the `plan`
   entry. Keep `prompt`, `task`, `test` steps with their type, status, `after:` edges, `repo:` and title. Refuse a cycle. Read **Repository conventions** in `assets/landing-modes.md`, resolve every
   branch name by it, and refuse there a missing ticket or a bad pattern. Read each entry's pull request
   (`gh pr list --head <entry-branch> --state all --json number,state,isDraft,baseRefName,headRefName,body`).
   An entry is *done* when its status is Done/Archived or its pull request merged — into
   `<integration>` counts in branch mode; *ready* when it is a `prompt` in this checkout's
   repository, not done, not running, not blocked, and the landing mode lets its `after:` edges start it.
   **Sources.** `git fetch origin`, then check that every entry not done can reach what it
   is told to read: each `Read first:` reference (grammar's `## Read-first line`) and each
   Devbook chapter `read_item` lists. A path in this checkout's repository —
   `git show <start>:<path>`, `<start>` the `origin/` ref the entry starts from per
   `assets/landing-modes.md`, and its `#anchor` the slug of a `#` heading in that file; a
   path in another repository, `owner/name` from the reference or the entry's single
   `repo:` — `gh api repos/<owner/name>/contents/<path>`; a claude.ai artifact — the host's
   artifact tool; any other URL — a fetch. A path missing while the entry still has a
   not-done `after:` ancestor is *pending*: that work may write it. Show the graph, the
   mode, the branch names, any build warning, the first frontier and, per entry, every reference that failed and why.
   Hold every entry that names a failed reference, without asking: `set_blocked` plus a
   `comment` `source not reachable: <reference>`; without the server, skip those entries this
   run and say so. On a later invocation re-check every entry held this way and unblock the
   ones whose sources now resolve. Then go.
2. **Resume.** Before dispatching, act on what an earlier run left: an entry merged into
   `<integration>` or `<base>`, or a closing pull request merged into `<base>`, since →
   `transition` its entries to Done — the sign-off too, once every repository's closing pull
   request has merged; then restack, merge what is settled, re-check blocked entries, resume an
   entry's earlier session, and tear down a manual entry's app once it is Done — all per `assets/landing-modes.md`.
3. **Dispatch.** For each ready entry, re-read its status and skip it when no longer ready;
   `transition` it to In progress and start its session as `assets/item-brief.md` says; the
   session links itself to the entry. The host starts a session only on a turn the person typed
   or one chained to it by sessions finishing; a start it refuses goes to a sub-agent per **Fallback**
   there. An entry that would be ready but for its repository is
   handed to a session there, per **Handoff** in `assets/handoff.md`.
4. **Gate.** None reaches this session: an entry session parks at its first gate and ends with
   `pr <url>`, a draft whose body is the review handoff. Every message to one follows
   **Talking to an entry session** in `assets/item-brief.md`.
5. **Land.** Branch: merge each settled entry into `<integration>`; once merged, `transition` it
   to Done. Stacked: a dependent may start on its parent's open pull request. On any merge into
   `<base>`, `transition` the entry to Done and restack its children. A landed entry's session
   and worktree are the host's and stay. Re-read the graph (`get_plan_items`) so an entry filed
   meanwhile — a follow-up — is dispatched; go to step 3. A failure blocks the entry and its dependents only.
6. **Wait.** While entries run, end the turn; entry sessions wake this session through
   `wake_when_done` once none is still working, and a sub-agent by finishing. On every wake: `list_sessions` (`linked: true`), a row's `cwd` its worktree, and a sub-agent's per **Fallback**; the
   delivery surface's `list_runs` for each working entry's worktree, which the band reads;
   `list_events` with a small `limit` for each session that ended a turn — the wake note quotes only 400
   characters. Keep one one-shot 2-hour fallback wake-up pending while entry
   sessions work, replacing the previous one, so a lost notification does not end the run — a
   wake-up's turn can start no session, so a long one rarely breaks the chain. A merge only the person makes is
   never waited for: the run goes on to Stop.
7. **Stop.** No entry ready and none running: list what blocks — each `task`/`test` the person
   has to do with the prompts waiting on it, each blocked entry with its reason and
   dependents, each pull request waiting on the person, each handoff to another repository
   with its path and entries. Branch mode: open the closing pull request and link it on the
   sign-off entry. Then start the app for the next manual entry and hand it over, per
   **Manual entries** in `assets/landing-modes.md`. Invoking this skill again resumes from the plan's
   state; nothing is kept in the session but what the pull requests and Backlog hold.
8. **Report**, opened with the `show-me` skill's orientation card when it has one. Per entry: done before, landed now (pull request, draft or merged), waiting
   (on what), blocked (why), manual, handed off (where); then the running app's URLs and its
   worktree, the plan's tasks as the two links `open_window` answers for `surface: "tasks"`,
   `plan: <tag>`, `target: "web"` — the web link, which opens in Claude Desktop's browser pane, and
   the `backlog://` link, which opens a native window; when the server does not answer it, no links
   and no word of them — and the landed entries' sessions, listed as ready to archive and never archived here (a
   sub-agent has none). Keep the report's prose to `../../resources/chat-reports.md`. The run ends with a push notification carrying the counts and the links **Manual entries** names.

## Never

- Edit or commit in this checkout or another repository's; run a `task` or `test` — starting the app for one is
  setup, not running it; tick a manual entry's checks or mark it Done — except the sign-off,
  once the person has merged its closing pull requests; mark an entry Done
  before it reaches `<base>` — or `<integration>` in branch mode; merge into `<base>` directly; force a branch other than a restacked entry's own;
  ask the person anything, approve a gate for the person, or take a pull request out of draft
  other than per **Merge an entry** in `assets/landing-modes.md`.
