# Knowledge graph tooling

Derives machine-readable graphs from the `meta` blocks embedded in
`.arc42/`, `.domain/`, `.backlog/`, and `.tech/`.

Markdown stays canonical; these indexes are **derived output** — never edit
them by hand. Placement and naming follow
`.github/instructions/derived-index.instructions.md`.

## Usage

```bash
# Regenerate every scope
node .github/tools/knowledge-graph/build-graph.mjs

# One scope only
node .github/tools/knowledge-graph/build-graph.mjs --scope .tech

# Validate references without writing (exit 1 on a broken reference)
node .github/tools/knowledge-graph/build-graph.mjs --check
```

Run the generator whenever you add, rename, or re-link a chapter or file in a
knowledge folder. `.github/workflows/knowledge-graph.yml` enforces both that
every reference resolves and that the committed indexes are current.

## Outputs

One artifact per scope, each co-located with what it describes:

| Path | Scope |
|---|---|
| `.index/graph.json` | repository-wide rollup across all knowledge folders |
| `.arc42/.index/graph.json` | `.arc42` only |
| `.domain/.index/graph.json` | `.domain` only |
| `.backlog/.index/graph.json` | `.backlog` only |
| `.tech/.index/graph.json` | `.tech` only |

A scoped graph contains every node in its folder, plus any node **outside** it
that an in-scope node references. Those boundary nodes are flagged
`outOfScope: true` so a viewer can draw them as stubs instead of pretending
they belong to the scope. Inbound references from other folders are not
followed, so a scoped graph stays about its own folder.

## Files

| File | Role |
|---|---|
| `graph.mjs` | Graph construction and scope projection. Imported by the CLI *and* by the `knowledge-graph` canvas, so the written indexes and the live view can never disagree. |
| `build-graph.mjs` | CLI wrapper: writes one artifact per scope, prints stats, exits non-zero on broken references. |

Metadata parsing itself lives in
`.github/extensions/knowledge-canvas/metadata.mjs`, which is the single
implementation of the schema defined in
`.github/instructions/chapter-metadata.instructions.md`.

## Output shape

The required envelope from the derived-index convention, followed by
Cytoscape.js `elements` JSON — consumable directly by Cytoscape and trivially
mappable to D3, vis.js, or Sigma.

```jsonc
{
  "schemaVersion": 1,
  "generatedBy": ".github/tools/knowledge-graph/build-graph.mjs",
  "scope": ".tech",
  "sources": [".tech"],
  "stats": { "nodes": 57, "edges": 120, "nodesByFolder": { }, "nodesByStatus": { } },
  "problems": [],
  "elements": {
    "nodes": [
      { "data": {
          "id": ".tech/desktop.md#winui-3",
          "label": "WinUI 3",
          "type": "chapter",
          "folder": "tech",
          "path": ".tech/desktop.md",
          "status": "candidate",
          "kind": "framework",
          "depends-on": [".tech/desktop.md#windows-app-sdk"]
      } }
    ],
    "edges": [
      { "data": {
          "id": "depends-on:.tech/desktop.md#winui-3->.tech/desktop.md#windows-app-sdk",
          "source": ".tech/desktop.md#winui-3",
          "target": ".tech/desktop.md#windows-app-sdk",
          "type": "depends-on"
      } }
    ]
  }
}
```

Output is deterministic — no timestamp — so re-running it on unchanged Markdown
produces byte-identical files.

### Node types

| Type | Meaning |
|---|---|
| `file` | A knowledge document. `id` is the repo-relative path. |
| `chapter` | A heading that carries a `meta` block. `id` is `<path>#<heading-slug>`. |
| `heading` | A structural heading with no `meta` block, materialized only when something references it (e.g. a `.domain` term pointing at a Value Object sub-chapter covered by its parent aggregate's block). |
| `external` | A reference target outside the knowledge folders. |

Nodes carrying `outOfScope: true` sit outside the current scope and are
included only because an in-scope node references them.

### Edge types

| Type | Source |
|---|---|
| `contains` | Document structure (file → chapter, chapter → sub-chapter). |
| `depends-on` | The `depends-on` metadata field. |
| `related` | The `related` metadata field. |
| `implements` | The `implements` metadata field (`.backlog`). |

`aliases` (`.domain`) and `alternatives` (`.tech`) are plain-string fields, not
references, so they stay node attributes and produce no edges.

## Viewing

Open the **Knowledge graph** canvas in Copilot CLI for an Obsidian-style
force-directed view with folder colouring, status shading, search, filters, and
click-to-inspect neighbourhoods. Open it scoped to one folder:

```text
open the knowledge graph canvas with scope .tech
```

The canvas has a scope selector, rebuilds from disk on open (so it never shows
a stale index), and exposes `refresh_graph` and `set_scope` actions.
