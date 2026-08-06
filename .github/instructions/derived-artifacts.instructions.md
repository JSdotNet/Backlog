---
applyTo: "**/_index/**"
description: Convention for derived index artifacts — where generated, machine-readable views of canonical Markdown live, how they are named, and what every such file must declare.
---

# Derived index artifacts (`_index/`)

This repository keeps **Markdown canonical and derived data generated** (see
`.arc42/02-constraints.md#technical-constraints`). Generated, machine-readable
views of that Markdown — graphs, search indexes, rollups — are *derived index
artifacts*, and they all follow one convention so a new one can be added
anywhere without inventing placement or naming rules again.

This convention is deliberately generic: it applies to any current or future
generated artifact, not just the knowledge graph.

## Location

A derived artifact lives in an `_index/` subfolder **of the thing it
describes**:

```
.tech/_index/graph.json        # derived from .tech only
.domain/_index/graph.json      # derived from .domain only
_index/graph.json              # repo-root: spans multiple source folders
```

- **Scoped artifact** — derived from exactly one folder: it belongs in that
  folder's own `_index/`. Co-locating it means the folder stays
  self-contained, and moving or removing the folder takes its derived data
  with it.
- **Cross-cutting artifact** — derived from two or more source folders: it
  belongs in the repository-root `_index/`.

Never nest `_index/` deeper than one level below its scope, and never put a
derived artifact anywhere other than an `_index/` folder.

The underscore prefix marks the folder as tooling machinery rather than
readable content — see `.github/instructions/naming.instructions.md`.

## File naming

```
<artifact>.<format>
```

- **`<artifact>`** — kebab-case, describing *what the artifact is*, not what
  produced it or what it covers. The enclosing folder already states the
  scope, so `.tech/_index/graph.json` — not `tech-graph.json`.
- **`<format>`** — the real file extension (`json`, `ndjson`, `csv`).
- Files inside `_index/` are **not** underscore-prefixed again; the folder
  already carries that signal.
- Use the same `<artifact>` name for the same kind of artifact in every scope,
  so tooling can glob `**/_index/graph.json` across scopes.

## Required envelope

Every derived JSON artifact carries the same top-level envelope before its
payload:

```jsonc
{
  "schemaVersion": 1,
  "generatedBy": ".github/tools/knowledge-graph/build-graph.mjs",
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
  scopes writes each to its own `_index/`; it does not merge them into one
  file.
- **CI enforces freshness.** Every derived artifact needs a workflow that
  regenerates it and fails when the committed copy differs.
- **Generators live in `.github/tools/<tool-name>/`** with a `README.md`
  documenting usage and output shape.

## Adding a new derived artifact

1. Decide the scope: one folder (scoped) or several (repository-root).
2. Add the generator under `.github/tools/<tool-name>/`, with a README.
3. Emit the required envelope and keep the output deterministic.
4. Write it to `<scope>/_index/<artifact>.<format>`.
5. Add a CI workflow that runs the generator and fails on a stale artifact.
6. Reference it from the instructions file of the folder it describes.

## Current artifacts

| Path | Scope | Generator |
|---|---|---|
| `_index/graph.json` | repository-wide | `.github/tools/knowledge-graph/build-graph.mjs` |
| `.arc42/_index/graph.json` | `.arc42` | same |
| `.domain/_index/graph.json` | `.domain` | same |
| `.backlog/_index/graph.json` | `.backlog` | same |
| `.tech/_index/graph.json` | `.tech` | same |
