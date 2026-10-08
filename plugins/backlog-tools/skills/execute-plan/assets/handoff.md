# Session names and handoff

How `execute-plan` names what it starts, and how it hands an entry in another repository to a
session there. `<tag>` is the plan tag as written, `+` included; `<repo>` a repository's name
without its owner.

## Names

| What | Name |
| --- | --- |
| This session | `<tag>:execute-plan`, plus ` @<repo>` when the plan's entries span more than one repository |
| Each sub-agent | `<tag>:<n> - <Title>` — the name `run-plan-item` would give the entry's own session — as the label the host shows for it |
| A handoff session | Titles itself by the first row, in its own repository |

The session is titled once, before Graph, where the host allows it. The sub-agent label is
separate from the brief: the plan-progress band keys on the brief's first line, never on the
label.

## Handoff

Each session owns the entries of its own checkout's repository: only those are ever ready,
dispatched or started an app for, and Stop counts only those. An entry that would be ready but
for its `repo:` — a *foreign* entry — is never dispatched from here, because a sub-agent works
in this checkout's worktrees only. It is handed to a session in its own repository instead,
which runs this skill for the same plan.

- **When.** At Dispatch and at Stop, for a repository with a foreign entry and **no entry In
  progress** — one In progress means a session there is still running the plan and will pick
  the rest up itself. One handoff per repository per run.
- **Where.** That repository's main checkout, found beside this one's.
  `git rev-parse --path-format=absolute --git-common-dir` gives this repository's `.git`;
  the folder holding it is the main checkout, and its siblings are the candidates. The one
  whose `origin` remote is `<owner>/<repo>` — the owner from the entry's repository when the
  plan gives it, else this repository's owner — is the checkout. None or several → attended,
  ask the person for the path once; unattended, report it as waiting on the person.
- **What.** One prompt: `/backlog-tools:execute-plan <tag> <landing> <attendance>`, with the
  parallelism when the person named one. It resumes from the plan's state like any invocation.
- **How.** Start the session in that folder through the host's way of starting one elsewhere —
  one the person opens with a click counts. Without one, print the prompt in a fenced block
  under the folder's path. Never edit, branch or open a worktree in the other checkout from here.
- **Then.** The handed-off entries are no longer this run's: the run goes on with its own,
  never waits on the other session, and Stop and Report list each handoff with its repository,
  path and entries. A handoff only the person can open — a click, a printed prompt — is
  waiting on the person, and is reported as that, never as started; an unattended run's push
  notification carries it.

## Across repositories

- **Manual entries.** Stop starts an app only for this repository's `task` and `test`
  entries; another repository's are its own session's.
- **`after:` in another repository** reads its Backlog status, or without the server its pull
  request through `gh pr list --repo <owner>/<repo>`.
- **Branch mode.** `plan/<slug>` and its closing pull request are per repository.
- **Stacked.** A stack parent is in the entry's own repository. An entry whose latest unmerged
  `after:` lives elsewhere waits until that one merges, cut from `origin/<base>`.
