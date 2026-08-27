---
description: Regenerate the derived _meta/index.json and _meta/graph.json artifacts for the knowledge folders.
argument-hint: "[scope, e.g. .domain]"
allowed-tools: Bash(pwsh:*), Bash(node:*), Bash(git status:*), Bash(git diff:*)
---

Regenerate the derived knowledge indexes for this repository.

Scope requested: `$ARGUMENTS` (empty means every adopted knowledge folder plus the
repository-wide rollup in `_meta/`).

Run the update script from the repository root:

```
./build/Update-KnowledgeIndex.ps1
```

When a scope was given, pass it through as `-Scope <scope>`. The script reports
which index files were added, updated, or removed, and says so plainly when
nothing changed.

Then report back:

- Which `_meta/*.json` files changed, grouped by knowledge folder, or that the
  indexes were already current.
- Any reference or reading-order problem the generator reported — those are
  errors in the Markdown's `meta` blocks, not in the generated output, and they
  are fixed in the chapter that carries the bad reference.

Rules:

- **Never hand-edit anything under a `_meta/` folder.** The Markdown is canonical
  and these files are derived from it; an edit there is overwritten by the next
  run and is invisible to review.
- **Do not edit `.github/tools/knowledge-meta/` or `build/Update-KnowledgeIndex.ps1`.**
  Both are installed copies of the `knowledge-base` plugin's tooling, re-synced
  from the plugin rather than maintained here.
- Do not commit or push unless the user asks. If the refresh produced changes,
  say so and let them decide — the nightly
  `.github/workflows/knowledge-meta-nightly.yml` refresh reconciles `main`
  anyway, so an unrelated branch usually should not carry this diff.
