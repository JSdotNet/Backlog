---
name: catch-up
description: Bring a long-running session's work level with its base branch — update the branch, find what landed on the base since the session last worked from it, and re-check every output the session produced against it, editing only what is now stale. Use when asked to "update the branch and see if the artifacts need further updates", catch up, or re-check this session's artifacts, import plan, pull request or branch after main moved on. Repository changes it makes go through the repository's gate.
---

# Catch up with the base branch

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

The outputs are what this session made: its published artifacts, branch, pull requests, plans.

## Workflow

1. **Place.** `git rev-parse --show-toplevel` and the branch. A folder that resolves to the main
   checkout (the worktree was removed) is read-only: no merge, no switch; compare at
   `origin/<base>`. A source file missing from the scratch directory is read back from its artifact.
2. **Update.** `git fetch origin`; the base is the pull request's base, else the default branch
   (`origin/HEAD`). First note `git merge-base HEAD origin/<base>`: where the session last worked
   from. In a worktree session use the host's own sync-with-base tool when it offers one; else
   `git rev-list --count origin/<base>..HEAD` of 0 → `git merge --ff-only`, otherwise `git merge`.
   Never rebase or stash. A conflict is part of this update, not a new change: resolve it, rebuild
   and test what it touched, commit; push only when the branch already had an upstream.
3. **What moved.** `git log --oneline <old-base>..origin/<base>` and `git diff --stat` over it.
   Read the changed decision records (`.devbook/arc42/adr/`), domain chapters (`.devbook/domain/`)
   and the code areas the outputs name; `gh pr list --state all --search "<area>"` for pull
   requests, open or merged, covering the same ground.
4. **Re-check each output** against the updated base:
   - **Artifact:** read it back by URL, edit only the stale parts, republish to the same URL.
   - **Import plan** (`import-plan`): update the plan text, keeping every `id:` and the `+tag`,
     against `../import-plan/assets/backlog-import-grammar.md` as it is now; republish its review view to the same
     link per that skill's step 6.
   - **Branch diff:** a semantic duplicate of, or contradiction with, what landed → fix it here.
   - **Pull request:** conflicts, checks (`gh pr checks`), and whether its description still
     matches; rewrite the description when it does not.
   Any other repository edit these need runs through the repository's code gate (its `flow-*`).
5. **Report**, opened with the `show-me` skill's orientation card when it has one. One table:
   what moved on the base (commit or pull request), the output it touches, what changed or why
   nothing did. Then the base before and after, and anything left for the person. Keep the
   report's prose to `../../resources/chat-reports.md`.

## Never

- Switch the main checkout, reset or force-push a branch; publish an artifact or plan view to a new URL.
- Edit an output nothing on the base made stale.
