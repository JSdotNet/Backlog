# Landing modes

How `execute-plan` lands each entry, and who is present while it does. Two independent
choices, made once when the run starts — the only time an unattended run needs the person
before its end. `<slug>` is the plan tag without its `+`; every branch below uses it, never
the tag.

## Names

| Thing | Name |
| --- | --- |
| Entry branch, `<entry-branch>` | `entryBranch` below, `<id>` the entry's Backlog id |
| Integration branch (branch mode), `<integration>` | `integrationBranch` below |
| A stack parent's branch, `<parent-branch>` | That entry's `<entry-branch>` |
| Entry worktree | The entry session's own, made by the host when `start_session` starts it |
| App worktree (**Manual entries**) | A short root the orchestrator picks, `<drive>:/<slug-initials>/<first 8 of id>`, confirmed absent — long paths break the MAUI heads and test executables |

## Repository conventions

Read once at the start of the run from `.devbook/backlog-tools.json` in this checkout's
repository, committed there because they are the team's. No file, or a key absent, is the
default, so a repository with no configuration behaves as it always did.

| Key | Default | Meaning |
| --- | --- | --- |
| `entryBranch` | `{slug}/{id}` | Pattern of an entry branch; must hold `{slug}` and `{id}` |
| `integrationBranch` | `plan/{slug}` | Pattern of the integration branch; must hold `{slug}` and literal text |
| `prTitle` | `{title}` | Pattern of a pull request title; must hold `{title}`, the conventional subject |
| `integrationBuilds` | unset | `true` or `false`: whether pull requests into the integration branch get a build |

Placeholders are `{slug}`, `{id}`, `{ticket}` and, in `prTitle` only, `{title}`. Any other, a
missing required one, or a rendered name `git check-ref-format --branch` refuses stops the run
and names the key.

- **Ticket.** An opaque value this skill only substitutes: it never looks one up, and never
  assumes a tracker. An entry's ticket is its `Ticket:` line (grammar's `## Ticket line`),
  else `ticket:<value>` from the request, else the `plan` entry's `Ticket:` line. The integration
  branch and the closing pull request take the last two only.
- **Refuse, never invent.** When a pattern holds `{ticket}` and a name or title still to be
  created has no ticket, stop before anything is dispatched, naming the key and the entries, and
  say how to supply one. An entry whose branch and pull request already exist needs none.
- **Resume reads what exists.** A branch already in use is kept, whatever the configuration says
  now. An entry's branch is the first found: the `headRefName` of a pull request on the
  configured name; on the old default `<slug>/<id>`; a pull request from
  `gh pr list --state all --search "<id>" --json number,headRefName` whose head holds the id; a
  remote or local branch of either name. None → the configured name. The integration branch is
  the configured name when it exists, else `plan/<slug>` when that exists, else the configured
  name. Report any old name kept.
- **Build check (branch mode).** At the start of the run, warn in the graph when
  `integrationBuilds` is `false`, or when it is unset and neither `.github/workflows/` nor an
  `azure-pipelines*.yml` shows a pull request trigger with no branch filter or with a filter
  matching `<integration>`. Say entries would merge with no build, and name the key. A warning
  only; an unattended run goes on, and the closing pull request's body repeats it.

Before the first dispatch, `git ls-remote --heads origin`, checking every name this run will
create: an existing branch equal to a leading path of the name blocks it (`<slug>` blocks
`<slug>/<id>`), as does a name that is itself a leading path of an existing branch; a plan tagged
`+plan` collides this way with the default `plan/<slug>`. Stop and say so, unless the branch is
the one Resume above kept. A `<entry-branch>` that `ls-remote` or
`git branch --list <entry-branch>` finds is that entry's earlier work, and its brief switches
without `-c`. When `git worktree list --porcelain` shows a worktree on it, an earlier entry
session holds it — resume that session rather than start a new one, finding it as the
`list_sessions` row whose `branch` is `<entry-branch>`, else whose `cwd` is that worktree. None
found — an earlier run's fallback sub-agent, gone with that run — and the worktree is clean with
its branch pushed → `git worktree remove` it and start the entry again; otherwise report the
entry waiting on the person.

An entry's later passes — `delivery:update-pr-branch`, `delivery:fix-pr-checks` — are messages
to its own session, per **Talking to an entry session** in `item-brief.md`. Refresh and Restack
run git in an entry's worktree, its session's `cwd`, and only while that session is idle — it
has returned its line and has had no message since.

## Landing

| Mode | Entry branch cut from | Entry pull request targets | Who merges it | A dependent starts when |
| --- | --- | --- | --- | --- |
| **Per item** | `origin/<base>` | `<base>` | Auto-merge once the person approves | Every `after:` has merged |
| **Branch** | `origin/<integration>` | `<integration>` | This skill | Every `after:` has merged into `<integration>` |
| **Stacked** | `origin/<parent-branch>`, else `origin/<base>` | That parent's branch, else `<base>` | Bottom-up: auto-merge once approved when attended, the person when unattended | Every `after:` has merged, or has an open pull request and the rule below holds |

**A repository may auto-merge every non-draft pull request** (the Backlog repository does,
`.github/workflows/auto-merge.yml`). Into `<base>` that means on green required checks, so
approving an entry's gates *is* the merge decision. Into `<integration>` or a stack parent, with
no required checks, it means the moment it leaves draft. Hence: in branch and stacked mode every
entry pull request opens as a **draft**, attended or not, and leaves draft only when this skill
says so below. Whether pull requests into `<integration>` get a build is the repository's:
**Build check** above. Into a stack parent, assume none until restacked onto `<base>`. A pull request this skill takes out of draft into `<base>` is watched
(`gh pr checks --watch`, then its state) so its merge is acted on in the same run.

**Every check watch runs in the foreground**, on the turn that needs it: one
`gh pr checks <n> --watch` call after another, each under the shell's 10-minute limit, until it
ends. A watch in the background wakes this session on a turn the host lets start no session, so
the dependents it unblocks would wait on the person. Attended, a gate another entry returns
meanwhile is relayed only once the watch ends: check `list_sessions` between calls.

### Branch

- Create `<integration>` only when `ls-remote` finds none:
  `git push origin origin/<base>:refs/heads/<integration>`. An existing one is resumed; never
  force it. Suits a graph with diamonds.
- **Merge an entry** once its review is settled — approved when attended, landed when
  unattended — and its checks are green: `gh pr checks <n> --watch`, in the foreground. No check
  reported yet is not red: retry for 10 minutes before reading it as failed. Then
  `gh pr ready <n>`; the workflow merges it, and `gh pr merge <n>` with the method
  `gh repo view --json mergeCommitAllowed,squashMergeAllowed` allows covers a slow workflow —
  "already merged" is success. Not mergeable → one `delivery:update-pr-branch` pass, then
  block.
- **The closing pull request** `<integration>` → `<base>` opens at Stop whenever `<integration>`
  is ahead of `<base>`, even with entries still blocked or waiting — its body lists them. One
  already open (`gh pr list --head <integration> --base <base> --state open`) gets its body
  updated instead.
  Opened through the repository's own pull-request rules, titled by `prTitle` with the plan's
  ticket; a draft when unattended. An entry
  is Done once merged into `<integration>`; the sign-off below is what waits on `<base>`.
- **The sign-off carries it.** Merging the closing pull request is the plan's sign-off, so the
  `sign-off-plan` entry is where the task list shows it. Once its pull request is open or
  found open, `read_item` the sign-off: unless its notes already carry the pull request's URL,
  `link_change` it there and `comment` the URL with one line — merge it to sign the plan off,
  after reading the review. `link_change` is not idempotent and `read_item` does not show
  links, so that comment is the record that the link exists. One per repository the plan
  touched. Once every one has merged into `<base>`, Resume moves the sign-off to Done with
  the rest. A plan without a sign-off entry (a feature-level plan) links nothing.

### Stacked

Suits a chain.

- **Parent.** An entry's stack parent is its `after:` latest in plan order that is not merged.
  Every other `after:` must be reachable from the parent's branch
  (`git merge-base --is-ancestor <its merge or head commit> origin/<parent-branch>`). A
  merged one that is not yet reachable is made so by refreshing the parent first. Otherwise
  the entry waits, and Stop names it.
- **Record the cut.** The first line of every stacked pull request's body is
  `Stacked on <parent-branch>@<sha>`, the parent head it was cut from, and the second
  `Do not mark ready until the base is <base>; re-run execute-plan after merging the parent.`
  A resumed run reads the first, because a merged parent's branch is deleted.
- **Refresh** a parent still open that has to pick up `<base>`: in its worktree
  `git rebase origin/<base>`, `git push --force-with-lease`, then restack its children as below.
- **Restack** when a parent merges into `<base>` or is refreshed, before anything else,
  transitively up the chain, parent before child. For each child, in its worktree:
  `git rebase --onto <new parent tip> <sha from its Stacked on line>`,
  `git push --force-with-lease`, update the `Stacked on` line, and `gh pr edit <child> --base
  <new target>` — a no-op when GitHub has already retargeted it. A conflict → `set_blocked`
  on the child with the files, and the chain above it waits.
- **Leaving draft.** A stacked pull request leaves draft only once its base is `<base>`:
  attended and approved, this skill runs `gh pr ready` after the restack that retargets it;
  unattended, the person does.

## Attendance

**Attended** (default): each entry session ends its turn at every gate the repository
declares — Personal Validation, and `create-pr` when `.devbook/config.json` lists it — with its
review handoff and its application running, and the person decides each — those and every
other question it would ask, per `item-brief.md`. A session started by another counts as
unattended to the engine, which would park it; the brief overrides that, because the
orchestrator answers it.

**Unattended**: nobody answers a gate, so every entry *parks* at its first one with
`approval: pending`, never self-approved, and lands as a draft pull request whose body is the
review handoff — the draft stands in for every gate, as the delivery engine's own
`draft-pr-contract.md` has it. Personal Validation moves onto the drafts.

- **Per item** cannot run unattended: nothing merges, so only the first frontier would ever
  run. Ask for branch or stacked instead.
- **Branch**: entries merge into `<integration>` per above; the closing draft is the person's one
  gate.
- **Stacked**: nothing merges; dependents stack on open drafts, so the chain runs end to end.
  The person validates and merges bottom-up; invoking this skill again restacks.
- **A failure stops only its dependents.** An entry that returns `blocked`, or whose checks are
  red after one `delivery:fix-pr-checks` pass, gets
  `set_blocked` with the reason and a `comment`; nothing outside its downstream waits on it. A
  resumed run re-checks every blocked entry and clears the block when the cause is gone.

## Manual entries

At Stop — and at every Stop a resumed run reaches — the run starts the application for the
person's next manual entry, so there is something to validate against. This is setup.
None qualifying → nothing starts.

- **Which.** The `task` and `test` entries that are not done and whose `after:` edges have
  all landed: every `test`, and a `task` whose `after:` chain includes a `prompt` — the
  sign-off shape. A `task` that waits only on tasks or decisions needs no app; Stop lists it
  as before. All that qualify at one Stop share one app, keyed by the first in plan order.
- **Where.** One worktree, its path per **Names** with that first entry's id, detached at the
  branch holding the landed work — `git worktree add --detach <path> origin/<branch>`,
  detached because an entry worktree or this checkout may hold the branch: `<integration>` in
  branch mode, `<base>` per item, and in stacked mode `<entry-branch>` of the latest `after:`
  prompt in plan order, whose branch carries the others. The path is confirmed absent unless
  an earlier Stop left it for this app.
- **Start** it there with the repository's `run` skill and leave it running. An app an
  earlier Stop left is reused; when its branch has moved since, stop it,
  `git fetch origin <branch>`, `git checkout --detach origin/<branch>`, and start it again.
- **Hand over** a review shaped like Personal Validation: each qualifying entry's
  `<n> - <Title>` with its `- [ ]` checks; the app's URLs as the run reported them, never
  hard-coded; the "What could not be proved" sections of the landed entries' pull request
  bodies; in branch mode, the closing pull request's link. Unattended the same holds: the app
  stays up, and the end-of-run push notification carries the app's URL and, in branch mode,
  the closing pull request's link.
- **Tear down.** Once every entry sharing the app is Done, a resumed run stops it the way
  `run` says, scoped to that worktree's own `--apphost` path, `git worktree remove`s it, and
  the next Stop picks the next entry.
