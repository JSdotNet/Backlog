# Landing modes

How `execute-plan` lands each entry, and who is present while it does. Two independent
choices, made once when the run starts — the only time an unattended run needs the person
before its end. `<slug>` is the plan tag without its `+`; every branch below uses it, never
the tag.

## Names

| Thing | Name |
| --- | --- |
| Entry branch | `<slug>/<id>`, `<id>` the entry's Backlog id |
| Integration branch (branch mode) | `plan/<slug>` |
| Worktree | A short root the orchestrator picks, `<drive>:/<slug-initials>/<first 8 of id>`, confirmed absent — long paths break the MAUI heads and test executables |

Before the first dispatch, `git ls-remote --heads origin <slug> plan/<slug> '<slug>/*'`: a
branch named exactly `<slug>` blocks every `<slug>/<id>`, and a plan tagged `+plan` collides
with `plan/<slug>`, so stop and say so. An existing `<slug>/<id>` is that entry's earlier work:
when `git worktree list --porcelain` shows a worktree on it, brief that path with no
`worktree add` — the sub-agent fetches and checks it is clean before continuing; otherwise
brief `git worktree add <path> <slug>/<id>` (no `-b`).

## Landing

| Mode | Entry branch cut from | Entry pull request targets | Who merges it | A dependent starts when |
| --- | --- | --- | --- | --- |
| **Per item** | `origin/<base>` | `<base>` | Auto-merge once the person approves | Every `after:` has merged |
| **Branch** | `origin/plan/<slug>` | `plan/<slug>` | This skill | Every `after:` has merged into `plan/<slug>` |
| **Stacked** | `origin/<slug>/<parent-id>`, else `origin/<base>` | That parent's branch, else `<base>` | Bottom-up: auto-merge once approved when attended, the person when unattended | Every `after:` has merged, or has an open pull request and the rule below holds |

**This repository auto-merges every non-draft pull request** (`.github/workflows/auto-merge.yml`,
no branch filter; it arms on `opened` and `ready_for_review`). Into `<base>` that means on
green required checks, so approving an entry's gates *is* the merge decision. Into
`plan/<slug>` or a stack parent, with no required checks, it means the moment it leaves draft.
Hence: in branch and stacked mode every entry pull request opens as a **draft**, attended or
not, and leaves draft only when this skill says so below. Pull requests into `plan/**` get
the full build (`pull-request.yml`); into a stack parent they get none until restacked onto
`<base>`. A pull request this skill takes out of draft into `<base>` is watched
(`gh pr checks --watch`, then its state) so its merge is acted on in the same run.

### Branch

- Create `plan/<slug>` only when `ls-remote` finds none:
  `git push origin origin/<base>:refs/heads/plan/<slug>`. An existing one is resumed; never
  force it. Suits a graph with diamonds.
- **Merge an entry** once its review is settled — approved when attended, landed when
  unattended — and its checks are green: `gh pr checks <n> --watch` in the background. No check
  reported yet is not red: retry for 10 minutes before reading it as failed. Then
  `gh pr ready <n>`; the workflow merges it, and `gh pr merge <n>` with the method
  `gh repo view --json mergeCommitAllowed,squashMergeAllowed` allows covers a slow workflow —
  "already merged" is success. Not mergeable → one `delivery:update-pr-branch` pass by a
  sub-agent in the entry's worktree, then block.
- **The closing pull request** `plan/<slug>` → `<base>` opens at Stop whenever `plan/<slug>`
  is ahead of `<base>`, even with entries still blocked or waiting — its body lists them. One
  already open (`gh pr list --head plan/<slug> --base <base> --state open`) gets its body
  updated instead.
  Opened through the repository's own pull-request rules; a draft when unattended. Entries
  merged into `plan/<slug>` stay In progress until it merges into `<base>`.

### Stacked

Suits a chain.

- **Parent.** An entry's stack parent is its `after:` latest in plan order that is not merged.
  Every other `after:` must be reachable from the parent's branch
  (`git merge-base --is-ancestor <its merge or head commit> origin/<slug>/<parent-id>`). A
  merged one that is not yet reachable is made so by refreshing the parent first. Otherwise
  the entry waits, and Stop names it.
- **Record the cut.** The first line of every stacked pull request's body is
  `Stacked on <slug>/<parent-id>@<sha>`, the parent head it was cut from, and the second
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

**Attended** (default): each sub-agent returns at every gate the repository declares —
Personal Validation, and `create-pr` when `.devbook/config.json` lists it — with its review
handoff and its application running, and the person decides each. A spawned worker counts as
unattended to the engine; this brief overrides that, because the orchestrator answers it.

**Unattended**: nobody answers a gate, so every entry *parks* at its first one with
`approval: pending`, never self-approved, and lands as a draft pull request whose body is the
review handoff — the draft stands in for every gate, as the delivery engine's own
`draft-pr-contract.md` has it. Personal Validation moves onto the drafts.

- **Per item** cannot run unattended: nothing merges, so only the first frontier would ever
  run. Ask for branch or stacked instead.
- **Branch**: entries merge into `plan/<slug>` per above; the closing draft is the person's one
  gate.
- **Stacked**: nothing merges; dependents stack on open drafts, so the chain runs end to end.
  The person validates and merges bottom-up; invoking this skill again restacks.
- **A failure stops only its dependents.** An entry that returns `blocked`, or whose checks are
  red after one `delivery:fix-pr-checks` pass by a sub-agent in its worktree, gets
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
  detached because an entry worktree or this checkout may hold the branch: `plan/<slug>` in
  branch mode, `<base>` per item, and in stacked mode `<slug>/<id>` of the latest `after:`
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
