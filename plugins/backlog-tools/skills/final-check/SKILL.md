---
name: final-check
description: Check a finished Backlog plan once, at the end, in the session it was written in — bring the checkout up to the plan's landed tip, review what each entry actually implemented against the plan and the specification it was planned from, and confirm the whole builds and its tests pass. Reports per entry; changes nothing. Run after execute-plan has landed every entry.
disable-model-invocation: true
---

# Final check of a finished plan

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

It runs in the session that wrote the plan, which still holds the specification. Read
`../import-plan/assets/backlog-import-grammar.md` (`## Entry kinds`, `## Read-first line`)
and `../execute-plan/assets/landing-modes.md`.

## Workflow

1. **Plan.** The `+tag` from the request, else the plan this session wrote, else ask.
   `get_plan_items` on the `backlog` MCP server, or the plan text in this session without it.
   Per step: type, status, and its pull request
   (`gh pr list --head <slug>/<id> --state all --json number,state,baseRefName,mergedAt,url`).
2. **Update.** `git fetch origin`. The tip is `origin/<base>`, except while work is still
   open on its way there: in branch mode `origin/plan/<slug>` while its closing pull request
   is open (**The closing pull request** in `landing-modes.md`), in stacked mode the top of the
   unmerged stack. A clean checkout with no commits of its own →
   `git merge --ff-only <tip>`; otherwise `git worktree add --detach` at the tip in the
   scratch directory and check there, removed at the end. Never stash, rebase or switch a
   branch with work on it.
3. **Review.** Per `prompt` entry, read its instructions, its `- [ ]` checks and its
   `Read first:` sections, then find each promised behaviour at the tip — `file:line` for
   every verdict. Verdicts: *implemented*, *partial* (what is missing), *missing*, *not landed*
   (pull request open or absent). Then across entries: two that built the same thing,
   leftovers (`TODO`, skipped tests, dead flags) in the plan's merged diffs, and anything the
   specification asked for that no entry covered. More than five entries → one fresh-context
   reviewer sub-agent per entry, briefed with the entry and the tip, never the verdict.
4. **Build.** The build and test commands the repository's own instructions name, and its
   checks (the devbook check where there is one), at the tip; read what the test run reported,
   not its exit code. Then `gh run list --branch <base> --limit 5` for red CI on the tip.
5. **Report.** One table: `<n> - <Title>`, status, pull request, verdict, evidence. Then build,
   tests and CI with their failures verbatim, the cross-entry findings, the `task`/`test`
   entries still open — the sign-off ready to tick or not, and why. Close with the fixes as a
   question; a fix the person accepts goes through the repository's gate as its own change.

## Never

- Edit, commit, push, merge, transition an entry or tick a check.
- Call an entry implemented from its pull request's title or a green build alone.
