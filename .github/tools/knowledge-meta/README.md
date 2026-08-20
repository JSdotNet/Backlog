# Knowledge metadata tooling

Derives machine-readable indexes from the `meta` blocks embedded in
`.arc42/`, `.domain/`, `.backlog/`, `.tech/`, and `.design/`:

- **`graph.json`** — the reference graph between chapters and files.
- **`index.json`** — the ordered reading outline of each area.

Markdown stays canonical; these indexes are **derived output** — never edit
them by hand. Placement and naming follow
the `knowledge-derived-artifacts` instructions.

## Usage

```bash
# Regenerate every adopted scope
node .github/tools/knowledge-meta/build.mjs

# One scope only
node .github/tools/knowledge-meta/build.mjs --scope .tech

# Validate references without writing (exit 1 on a broken reference)
node .github/tools/knowledge-meta/build.mjs --check

# Point at a repository other than the working directory
node .github/tools/knowledge-meta/build.mjs --root ../other-repo
```

The repository root defaults to the working directory. Only knowledge folders
that actually exist produce a scope, so a repository that adopts just `.domain`
and `.arc42` never grows `_meta/` folders for the rest. The generator exits `2`
when no knowledge folder is present at all.

Run the generator whenever you add, rename, or re-link a chapter or file in a
knowledge folder. `.github/workflows/knowledge-meta.yml` enforces both that
every reference resolves and that the committed indexes are current.

## Outputs

Two artifacts per adopted scope, each co-located with what it describes:

| Path | Scope |
|---|---|
| `_meta/graph.json`, `_meta/index.json` | repository-wide rollup across all adopted knowledge folders |
| `.arc42/_meta/*.json` | `.arc42` only |
| `.domain/_meta/*.json` | `.domain` only |
| `.backlog/_meta/*.json` | `.backlog` only |
| `.tech/_meta/*.json` | `.tech` only |
| `.design/_meta/*.json` | `.design` only |

A scoped graph contains every node in its folder, plus any node **outside** it
that an in-scope node references. Those boundary nodes are flagged
`outOfScope: true` so a viewer can draw them as stubs instead of pretending
they belong to the scope. Inbound references from other folders are not
followed, so a scoped graph stays about its own folder.

## Files

| File | Role |
|---|---|
| `metadata.mjs` | Parses the `meta` blocks — the single implementation of the schema defined by the `knowledge-chapter-metadata` instructions. Shared with the `knowledge-graph` canvas. |
| `graph.mjs` | Graph construction, scope discovery, and scope projection. Imported by the CLI *and* by the `knowledge-graph` canvas, so the written indexes and the live view can never disagree. |
| `outline.mjs` | Reading-order resolution from the `order` field on each directory's root document. |
| `build.mjs` | CLI wrapper: writes both artifacts per scope, prints stats, exits non-zero on errors. |

This folder is self-contained — copy it into a repository as
`.github/tools/knowledge-meta/` and it runs with no other files installed.

## Output shape: `graph.json`

The required envelope from the derived-index convention, followed by
Cytoscape.js `elements` JSON — consumable directly by Cytoscape and trivially
mappable to D3, vis.js, or Sigma.

```jsonc
{
  "schemaVersion": 1,
  "generatedBy": ".github/tools/knowledge-meta/build.mjs",
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

`aliases` (`.domain`), `alternatives` (`.tech`), and `feature-flag` (`.domain`)
are plain-string fields, not references, so they stay node attributes and
produce no edges. `feature-flag` accepts a scalar or a list but is always
emitted as a list, so a consumer never has to branch on shape.

## Output shape: `index.json`

The same envelope, followed by `entries` — a nested, **ordered** tree of the
area's readable content. A viewer walks `entries` top to bottom instead of
sorting filenames.

```jsonc
{
  "schemaVersion": 1,
  "generatedBy": ".github/tools/knowledge-meta/build.mjs",
  "scope": ".domain",
  "sources": [".domain"],
  "problems": [],
  "entries": [
    { "type": "file", "name": "context-map.md", "path": ".domain/context-map.md",
      "title": "Context Map", "status": "draft", "root": true },
    { "type": "directory", "name": "ordering", "path": ".domain/ordering",
      "title": "Domain: Ordering",
      "children": [
        { "type": "file", "name": "domain.md", "path": ".domain/ordering/domain.md",
          "title": "Domain: Ordering", "status": "draft", "root": true },
        { "type": "file", "name": "features.md", "path": ".domain/ordering/features.md",
          "title": "Features: Ordering", "status": "draft" }
      ] }
  ]
}
```

At the repository scope the top level is `type: "area"` — one entry per
knowledge folder, in canonical area order.

Ordering comes from the `order` field on the file-level `meta` block of each
directory's **root document**, which always sorts first. A directory with no
root document falls back to filename sort — which is why the numbered `.arc42`
chapters need no declaration. Entries listed but missing are errors; entries
present but unlisted are warnings and get appended alphabetically. See
the `knowledge-chapter-metadata` instructions.

`_`-prefixed folders (such as `_meta/` itself) are tooling, not content, and
are excluded from the outline.

## Viewing

Open the **Knowledge graph** canvas in Copilot CLI for an Obsidian-style
force-directed view with folder colouring, status shading, search, filters, and
click-to-inspect neighbourhoods. Open it scoped to one folder:

```text
open the knowledge graph canvas with scope .tech
```

The canvas has a scope selector, rebuilds from disk on open (so it never shows
a stale index), and exposes `refresh_graph` and `set_scope` actions. It also
serves the live outline at `/api/outline?scope=<scope>` for tools that want the
reading order without reading the committed `index.json`.
