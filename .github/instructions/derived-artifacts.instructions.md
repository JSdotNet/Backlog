---
applyTo: "**/_meta/**"
description: Convention for derived index artifacts — where generated, machine-readable views of canonical Markdown live, how they are named, and what every such file must declare.
---

# Derived metadata artifacts (`_meta/`)

This repository keeps **Markdown canonical and derived data generated** (see
`.arc42/02-constraints.md#technical-constraints`). Generated, machine-readable
views of that Markdown — graphs, outlines, search indexes, rollups — are
*derived metadata artifacts*, and they all follow one convention so a new one
can be added anywhere without inventing placement or naming rules again.

This convention is deliberately generic: it applies to any current or future
generated artifact, not just the knowledge graph.

## Location

A derived artifact lives in an `_meta/` subfolder **of the thing it
describes**:

```
.tech/_meta/graph.json        # derived from .tech only
.domain/_meta/graph.json      # derived from .domain only
_meta/graph.json              # repo-root: spans multiple source folders
```

- **Scoped artifact** — derived from exactly one folder: it belongs in that
  folder's own `_meta/`. Co-locating it means the folder stays
  self-contained, and moving or removing the folder takes its derived data
  with it.
- **Cross-cutting artifact** — derived from two or more source folders: it
  belongs in the repository-root `_meta/`.

Never nest `_meta/` deeper than one level below its scope, and never put a
derived artifact anywhere other than an `_meta/` folder.

The underscore prefix marks the folder as tooling machinery rather than
readable content — see `.github/instructions/naming.instructions.md`.

## File naming

```
<artifact>.<format>
```

- **`<artifact>`** — kebab-case, describing *what the artifact is*, not what
  produced it or what it covers. The enclosing folder already states the
  scope, so `.tech/_meta/graph.json` — not `tech-graph.json`.
- **`<format>`** — the real file extension (`json`, `ndjson`, `csv`).
- Files inside `_meta/` are **not** underscore-prefixed again; the folder
  already carries that signal.
- Use the same `<artifact>` name for the same kind of artifact in every scope,
  so tooling can glob `**/_meta/graph.json` across scopes.

## Required envelope

Every derived JSON artifact carries the same top-level envelope before its
payload:

```jsonc
{
  "schemaVersion": 1,
  "generatedBy": ".github/tools/knowledge-meta/build.mjs",
  "scope": ".tech",
  "sources": [".tech"]
  // ...artifact-specific payload
}
```

- **schemaVersion** (required) — integer, incremented whenever the payload
  shape changes, so consumers can detect drift.
- **generatedBy** (required) — repo-relative path to the generator, so anyone
  finding the file knows how to regenerate it.
- **scope** (required) — the folder this artifact describes, or `"."` for a
  repository-wide artifact.
- **sources** (required) — the folders actually read to produce it.

## Rules

- **Derived artifacts are generated, never hand-edited.** Treat any manual edit
  as a bug; the generator is the only writer.
- **Committed to source control.** They are checked in so they can be read
  without a build step, reviewed in diffs, and consumed by tooling that has no
  Node.js available.
- **Deterministic output.** No timestamps, no random ordering, no absolute
  paths. Running the generator twice on unchanged input must produce a
  byte-identical file, so CI can diff the committed artifact to detect
  staleness.
- **One generator, one artifact per scope.** A generator that produces several
  scopes writes each to its own `_meta/`; it does not merge them into one
  file.
- **CI enforces freshness.** Every derived artifact needs a workflow that
  regenerates it and fails when the committed copy differs.
- **Generators live in `.github/tools/<tool-name>/`** with a `README.md`
  documenting usage and output shape.

## Adding a new derived artifact

1. Decide the scope: one folder (scoped) or several (repository-root).
2. Add the generator under `.github/tools/<tool-name>/`, with a README.
3. Emit the required envelope and keep the output deterministic.
4. Write it to `<scope>/_meta/<artifact>.<format>`.
5. Add a CI workflow that runs the generator and fails on a stale artifact.
6. Reference it from the instructions file of the folder it describes.

## Current artifacts

| Path | Scope | Contents | Generator |
|---|---|---|---|
| `_meta/graph.json` | repository-wide | reference graph | `.github/tools/knowledge-meta/build.mjs` |
| `_meta/index.json` | repository-wide | ordered reading outline | same |
| `.arc42/_meta/*.json` | `.arc42` | both of the above, scoped | same |
| `.domain/_meta/*.json` | `.domain` | both of the above, scoped | same |
| `.backlog/_meta/*.json` | `.backlog` | both of the above, scoped | same |
| `.tech/_meta/*.json` | `.tech` | both of the above, scoped | same |
| `.design/_meta/*.json` | `.design` | both of the above, scoped | same |

`index.json` carries the **reading order** of an area, which a viewer uses
instead of sorting filenames alphabetically. Its source of truth is the
`order` field on the file-level `meta` block of each directory's root
document — see `.github/instructions/chapter-metadata.instructions.md`.
