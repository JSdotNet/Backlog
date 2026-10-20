---
name: close-merged
description: Move the Backlog entries whose pull request has merged — into the default branch or a plan/<slug> branch — to Done. Use when asked to close, finish, tidy or sync the merged items, when entries still read In progress after their pull request merged, or right after merging a pull request linked to a Backlog entry. Resolves each merged pull request to its entry through the backlog MCP server and transitions it; changes nothing in the repository.
---

# Close the entries whose pull request merged

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

An entry is done when its pull request merges into the default branch, or into its plan's
`plan/<slug>` branch — not when it opens. `run-plan-item` and `execute-plan` move it when
they find the merge; this skill catches every merge they did not — one made on GitHub's own
page, or by auto-merge after the session ended.

## Workflow

1. **Place.** Read `owner/name` off the git remote and the default branch
   (`gh repo view --json defaultBranchRef`). Without a `backlog` MCP server in the live tool
   list there is nothing to move: say so and stop.
2. **Open entries.** `list_entries` for the repository; keep status `in_progress` and
   `ready` of type `prompt`. A `task` or `test` is the person's to tick — the plan's sign-off
   is one, and `execute-plan`'s Resume moves it once every repository's closing pull request
   has merged into the default branch. None → say so and stop. The oldest `startedOn` among
   the `in_progress` ones bounds the search below; with none, 30 days back.
3. **Merged pull requests.** `gh pr list --state merged --search "merged:>=<bound>"
   --limit 500 --json number,title,mergedAt,url,baseRefName`; keep those whose
   `baseRefName` is the default branch or starts with `plan/` — or a base the user named, for
   a plan run against another one. 500 back → say the oldest may be cut off. One merged into a stacked parent's branch finishes nothing yet: it is moved
   once restacked onto the default branch and merged there. When the user named pull
   requests, take only those.
4. **Resolve.** For each, `find_item` with `externalId` the bare number and `repoId` the
   `owner/name` — the shape `link_change` records. Not found → skip it. Two matches →
   list it under *needs a person*; never pick one.
5. **Move.** An entry the step 2 list kept → `transition` to `done`. A refused move comes
   back with the legal ones: report it, never route around it.
6. **Report.** A table: entry title, pull request, the branch it merged into, from → to.
   Then every refused move and every *needs a person* row. Nothing to commit.

## Never

- Move an entry whose pull request is only open, or merged into a branch that is neither the
  default branch nor a `plan/` branch.
- Tick sub-items, edit the entry's text, or `link_change` — this skill only transitions.
- Push, merge, or close anything on GitHub.
