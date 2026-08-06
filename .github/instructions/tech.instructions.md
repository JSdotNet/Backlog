---
applyTo: ".tech/**"
description: Structure and authoring rules for the technology knowledge folder, holding the project's technology graph of platforms, runtimes, frameworks, libraries, packages, services, and tools.
---

# Technology knowledge (`.tech`)

`.tech` is the durable record of **which technologies this project itself is
built with, and how they depend on each other** — the technology graph. It is
complementary to `.arc42` (system architecture), `.domain` (domain model), and
`.backlog` (work items).

`.tech` answers "what do we build on, at which version, with what maturity, and
what depends on what". `.arc42` stays the place for *why* an architecture looks
the way it does; `.tech` links back to it rather than restating rationale.

> `.tech` describes **this repository's own stack**. It is not the same thing as
> the product's `technology-stack` bounded context in `.domain/`, which models
> the feature that lets a *user* track *their* stacks. Keep the two separate and
> cross-link with `related` when they touch.

## Structure

`.tech/` contains one root graph artifact plus one file per technology layer.

```
.tech/
  technology-graph.md   # root: layers, graph diagram, how to read it
  shared.md             # cross-channel technologies (formats, protocols, contracts)
  desktop.md            # desktop channel stack
  mobile.md             # mobile channel stack
  ide.md                # IDE extension stacks
  cloud.md              # optional cloud service stack
  tooling.md            # development, AI, build, CI/CD, and governance tooling
  _index/graph.json     # derived: generated graph index, never hand-edited
```

Add a new layer file only when a technology genuinely does not belong to an
existing layer, and register it in `technology-graph.md` in the same change.

## File responsibilities

- **technology-graph.md** — Root strategic view of the whole stack.
  - Lists the layers and what each layer file covers.
  - Renders the technology graph as a Mermaid diagram (nodes = technologies,
    edges = `depends-on`).
  - Explains the status ladder and how to read/extend the graph.
  - Its `##` sections do **not** carry per-chapter metadata blocks; the file
    carries a file-level block only (same rule as `.domain/context-map.md`).
- **`_index/graph.json`** — Derived, generated graph index for this folder.
  Never hand-edited; see `.github/instructions/derived-artifacts.instructions.md`
  and `.github/tools/knowledge-graph/README.md`.
- **`<layer>.md`** — One `## <Technology Name>` chapter per technology used (or
  under consideration) in that layer. Each chapter is an addressable node in
  the graph and carries a chapter metadata block.

## Technology chapter template

```markdown
## <Technology Name>

\`\`\`meta
status: candidate
kind: framework
version: "9.0"
depends-on: [".tech/shared.md#net-runtime"]
related: [".arc42/04-solution-strategy.md#technology-choices"]
\`\`\`

One or two sentences: what it is used for in this project.

- **Used for** — the concrete responsibility it carries here.
- **Why** — the decisive reason it was picked (link the ADR/arc42 section rather
  than restating the full rationale).
- **Alternatives** — what else was considered, if the choice is still open.
```

Keep chapters short. A technology chapter is a graph node with just enough
context to be understood, not a design document.

## Metadata fields

`.tech` uses the common fields from
`.github/instructions/chapter-metadata.instructions.md` (`status` required;
`related` and `issue` optional) plus the folder-specific fields below.

### status

Maturity of the technology **in this project**, on a tech-radar-style ladder:

| Value | Meaning |
|---|---|
| `candidate` | Named as the intended choice, not yet validated by real use. |
| `trial` | Being tried out in a limited, reversible way. |
| `adopted` | In active use and the default choice for its role. |
| `hold` | Kept but no longer expanded; avoid new usage. |
| `retired` | No longer used; kept for history. |

While the project is in bootstrap, most entries are legitimately `candidate`.

### Folder-specific fields

- **kind** (required) — the node type in the graph. One of: `language`,
  `runtime`, `framework`, `library`, `package`, `tool`, `service`, `platform`,
  `protocol`, `format`.
- **version** (optional) — the pinned or targeted version, as a quoted string
  (e.g. `"9.0"`, `"^5.2"`). Omit when no version is committed to yet.
- **depends-on** (optional) — list of `<path>#<heading-slug>` references to
  other `.tech` chapters this technology sits on top of. These are the edges of
  the technology graph.
- **alternatives** (optional) — list of plain-string names that were considered
  instead. Like `.domain`'s `aliases`, this is a plain-string list, **not** a
  reference field.

Omit every optional field that has no value (no `related: []`, no
`version: null`).

## Authoring guidance

- Every technology appears exactly **once**, in the layer that owns it. If two
  layers use the same technology, document it in `shared.md` and point at it
  with `depends-on` from the layer chapters.
- `depends-on` must reference an existing `.tech` chapter. Do not point it at
  `.arc42`/`.domain`/`.backlog` — use `related` for those.
- Keep `technology-graph.md`'s Mermaid diagram in sync with the `depends-on`
  edges in the layer files whenever a node or edge is added, removed, or
  renamed, and regenerate the derived index in the same change:
  `node .github/tools/knowledge-graph/build-graph.mjs --scope .tech`.
- Ground stack claims in `.arc42` (especially
  `.arc42/04-solution-strategy.md#technology-choices` and
  `.arc42/09-architecture-decisions.md`) rather than inventing new choices here.
  If `.tech` and `.arc42` disagree, `.arc42` wins and `.tech` is corrected.
- A change of technology *decision* belongs in an ADR first; `.tech` records the
  outcome and links to it.
- Do not add a new metadata field without updating this file (folder-specific)
  or `chapter-metadata.instructions.md` (universal) first — the visualization
  tooling depends on a fixed schema.
