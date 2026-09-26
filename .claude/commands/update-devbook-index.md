---
description: Check the devbook corpus builds into the generated database, say where the app keeps it, and optionally refresh the installed generator's _meta/*.json indexes.
argument-hint: "[--json]"
allowed-tools: Bash(pwsh:*), Bash(node:*), Bash(git status:*), Bash(git diff:*)
---

Check the derived devbook layer for this repository.

The database the app reads is **one generated SQLite file per repository path**
(local ADR 0004 for what it is, local ADR 0015 for where and who writes it). The
app builds it itself, in the background, when a repository's devbook is first read
and whenever an input changed, into its own storage — never into the repository:

```
<devbook cache folder>/_databases/<name>-<hash>/devbook.db
```

The devbook cache folder is `devbook-cache` under the storage folder unless the
Storage settings tab names another; for a debug build and the desktop web harness
that is `%LOCALAPPDATA%\Backlog.Debug\devbook-cache`. So there is nothing to
rebuild by hand. To force a rebuild, delete that repository's folder under
`_databases/`; the next read builds it again.

What this command runs is the check CI runs — the Node writer building the whole
corpus into a temporary file and deleting it:

```
node tools/devbook/build-database.mjs --check
```

When `$ARGUMENTS` contains `--json`, also refresh the JSON indexes the installed
generator writes (`_meta/graph.json`, `_meta/index.json` — ignored outputs, but
its reference and reading-order errors are real):

```
./build/Update-KnowledgeIndex.ps1
```

Run it unscoped: `--check` reports a false pass and `-Scope` leaves the root
graph stale.

Then report back:

- Whether the check built, and how many chapters and references it reported.
- Any reference or reading-order problem either tool reported — those are
  errors in the Markdown's `meta` blocks or a folder's `_reading-order.json`,
  not in the generated output, and they are fixed in the chapter that carries
  the bad reference.
- Any `devbook.db` still inside the repository (`git status --short --ignored`
  shows `.devbook/_meta/` or `_meta/`): the old writer left it there, the app
  never reads it, and the user can delete it. Do not delete it yourself.

Rules:

- **Never hand-edit anything under a `_meta/` folder.** The Markdown is canonical
  and these files are derived from it. `_reading-order.json` beside each
  knowledge folder is the one authored exception, and no generator writes it.
- **Never write the database into the repository.** `build-database.mjs` takes
  `--out <file>` for a comparison or a debugging session; point it at the temp
  folder.
- **Do not edit `.github/tools/knowledge-meta/` or `build/Update-KnowledgeIndex.ps1`.**
  Both are the unchanged install from `knowledge-base`, the `devbook` plugin's
  predecessor, and are never maintained here. `tools/devbook/` is repo-native.
- Nothing here is committed. If a chapter had to be fixed, say so and let the
  user decide what to commit.
