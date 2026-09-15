---
description: Rebuild the generated _meta/devbook.db the Devbook panels read, and optionally the installed generator's _meta/*.json indexes.
argument-hint: "[--json]"
allowed-tools: Bash(pwsh:*), Bash(node:*), Bash(git status:*), Bash(git diff:*)
---

Rebuild the derived devbook layer for this repository.

The artifact the app reads is **one generated SQLite file**, `_meta/devbook.db`
(local ADR 0004): the reference graph, the resolved reading outline, every
chapter's text and hashes, and the FTS5 index. It is git-ignored and rebuilt per
machine. Run the repo-native writer from the repository root:

```
node tools/devbook/build-database.mjs
```

It writes `_meta/devbook.db` and nothing else. A `_meta/knowledge.db` left by an
earlier build is served by the app until this run replaces it and can be deleted
afterwards.

When `$ARGUMENTS` contains `--json`, also refresh the JSON indexes the installed
generator writes (`_meta/graph.json`, `_meta/index.json` — ignored outputs, but
its reference and reading-order errors are real):

```
./build/Update-KnowledgeIndex.ps1
```

Run it unscoped: `--check` reports a false pass and `-Scope` leaves the root
graph stale.

Then report back:

- Whether `_meta/devbook.db` was written, and how many chapters and references
  the writer reported.
- Any reference or reading-order problem either tool reported — those are
  errors in the Markdown's `meta` blocks or a folder's `_reading-order.json`,
  not in the generated output, and they are fixed in the chapter that carries
  the bad reference.

Rules:

- **Never hand-edit anything under a `_meta/` folder.** The Markdown is canonical
  and these files are derived from it; an edit there is overwritten by the next
  run and is invisible to review. `_reading-order.json` beside each knowledge
  folder is the one authored exception, and no generator writes it.
- **Do not edit `.github/tools/knowledge-meta/` or `build/Update-KnowledgeIndex.ps1`.**
  Both are the unchanged install from `knowledge-base`, the `devbook` plugin's
  predecessor; re-syncing them from `devbook` is the contract v6 follow-up, and
  they are never maintained here. `tools/devbook/` is repo-native and imports
  the installed generator's exports.
- Nothing here is committed: the database and the JSON are both git-ignored. If
  a chapter had to be fixed, say so and let the user decide what to commit.
