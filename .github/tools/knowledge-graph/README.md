# Knowledge graph tooling

Derives a single machine-readable graph from the `meta` blocks embedded in
`.arc42/`, `.domain/`, `.backlog/`, and `.tech/`.

Markdown stays canonical; this index is **derived output** — never edit
`.index/knowledge-graph.json` by hand.

## Usage

```bash
# Regenerate .index/knowledge-graph.json
node .github/tools/knowledge-graph/build-graph.mjs

# Validate references without writing (exit 1 on a broken reference)
node .github/tools/knowledge-graph/build-graph.mjs --check

# Write somewhere else
node .github/tools/knowledge-graph/build-graph.mjs --out build/graph.json
```

Run the generator whenever you add, rename, or re-link a chapter or file in a
knowledge folder. `.github/workflows/knowledge-graph.yml` enforces both that
every reference resolves and that the committed index is current.

## Files

| File | Role |
|---|---|
| `graph.mjs` | Graph construction. Imported by the CLI *and* by the `knowledge-graph` canvas, so the written index and the live view can never disagree. |
| `build-graph.mjs` | CLI wrapper: writes the index, prints stats, exits non-zero on broken references. |

Metadata parsing itself lives in
`.github/extensions/knowledge-canvas/metadata.mjs`, which is the single
implementation of the schema defined in
`.github/instructions/chapter-metadata.instructions.md`.

## Output shape

Cytoscape.js `elements` JSON, consumable directly by Cytoscape and trivially
mappable to D3, vis.js, or Sigma.

```jsonc
{
  "schemaVersion": 1,
  "folders": [".arc42", ".domain", ".backlog", ".tech"],
  "stats": { "nodes": 321, "edges": 540, "nodesByFolder": { }, "nodesByStatus": { } },
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

The output is deterministic — no timestamp — so re-running it on unchanged
Markdown produces a byte-identical file.

### Node types

| Type | Meaning |
|---|---|
| `file` | A knowledge document. `id` is the repo-relative path. |
| `chapter` | A heading that carries a `meta` block. `id` is `<path>#<heading-slug>`. |
| `heading` | A structural heading with no `meta` block, materialized only when something references it (e.g. a `.domain` term pointing at a Value Object sub-chapter covered by its parent aggregate's block). |
| `external` | A reference target outside the knowledge folders. |

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
click-to-inspect neighbourhoods. It rebuilds from disk on open, so it never
shows a stale index; the `refresh_graph` action rebuilds on demand.
