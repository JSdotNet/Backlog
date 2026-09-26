---
name: devbook-tech
description: Structure and authoring rules for the technology devbook folder, holding the project's technology graph of platforms, runtimes, frameworks, libraries, packages, services, and tools.
---

# Technology documentation (`tech/`)

`tech/` is the durable record of **which technologies this project itself is
built with, and how they depend on each other** — the technology graph. It is
complementary to `arc42/` (system architecture), `domain/` (domain model), and
and the rest of the devbook folders.

`tech/` answers "what do we build on, at which version, with what maturity, and
what depends on what". `arc42/` stays the place for *why* an architecture looks
the way it does; `tech/` links back to it rather than restating rationale.

> `tech/` describes **this repository's own stack**. If the product itself
> models technologies as a domain concept, that lives in `domain/` — keep the
> two separate and cross-link with `related` when they touch.

> `tech/` is also the **registry for AI tooling** — Claude Code, an MCP server,
> a model provider — with its version and its maturity here. How that tooling is
> actually used across the development flow, and how far that use has been
> adopted, is `ai/`. A `ai/` chapter points at the `tech/` chapter with
> `depends-on`; the reverse link is never written. See
> `devbook-ai.md`.

## Context-loading policy

- `tech/` is **not** baseline repository context. Load it for stack, dependency,
  upgrade, or technology-selection tasks, and when implementation work needs to
  know which technology owns a responsibility.
- When `tech/` is needed as task context, load only the relevant layer file(s)
  instead of reading the whole folder by default.

## Structure

`tech/` contains one root graph artifact plus one file per technology layer.
Choose layers that match how the system is actually split — by deployable
channel, by tier, or by service — and keep the set small.

```
.devbook/tech/
  technology-graph.md   # root: layers, graph diagram, how to read it
  shared.md             # cross-layer technologies (formats, protocols, contracts)
  <layer>.md            # one file per layer, e.g. backend.md, web.md, desktop.md
  tooling.md            # development, AI, build, CI/CD, and governance tooling
  _meta/graph.json      # derived: generated reference graph, never hand-edited
  _meta/index.json      # derived: generated reading outline, never hand-edited
```

Add a new layer file only when a technology genuinely does not belong to an
existing layer, and register it in `technology-graph.md`'s layer table in the
same change. Reading order needs no declaration: `technology-graph.md` is read
first, then `shared.md`, then the layer files alphabetically, then `tooling.md`
last — the order shown in the tree above. See
`devbook-chapter-metadata.md`.

## File responsibilities

- **technology-graph.md** — Root strategic view of the whole stack.
  - Lists the layers and what each layer file covers.
  - Renders the technology graph as a Mermaid diagram (nodes = technologies,
    edges = `depends-on`).
  - Explains the status ladder and how to read/extend the graph.
  - Its `##` sections do **not** carry per-chapter metadata blocks; the file
    carries a file-level block only (same rule as `.devbook/domain/context-map.md`).
  - It is `tech/`'s root document, so it is the first file read in the folder.
- **`_meta/*.json`** — Derived, generated indexes for this folder.
  Never hand-edited; see `devbook-derived-artifacts.md`
  and the devbook-meta tooling README (`.devbook/_tools/devbook-meta/README.md`).
- **`<layer>.md`** — One `## <Technology Name>` chapter per technology used (or
  under consideration) in that layer. Each chapter is an addressable node in
  the graph and carries a chapter metadata block.

## Technology chapter template

```markdown
## <Technology Name>

\`\`\`meta
status: candidate
type: framework
version: "9.0"
depends-on: [".devbook/tech/shared.md#net-runtime"]
related: [".devbook/arc42/04-solution-strategy.md#technology-choices"]
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

`tech/` uses the common fields from
`devbook-chapter-metadata.md` (`status` and `type` required;
`related`, `issue`, `effort`, and `roadmap` optional) plus the folder-specific
fields below.

### status

Maturity of the technology **in this project**, on a tech-radar-style ladder:

| Value | Meaning |
|---|---|
| `candidate` | Named as the intended choice, not yet validated by real use. |
| `trial` | Being tried out in a limited, reversible way. |
| `adopted` | In active use and the default choice for its role. |
| `hold` | Kept but no longer expanded; avoid new usage. |
| `retired` | No longer used; kept for history. |

Early in a project most entries are legitimately `candidate`.

There is no `approved` or `accepted` rung here. The two decision rungs are
`domain/`'s alone — see `devbook-domain.md`. This folder's value is a rating of
a technology, and stacking a decision about a chapter on top of it would put two
unrelated statements in one field.

**`status` is required on every `tech/` block, with no resting value to omit** —
unlike `domain/`, `arc42/`, and `design/`, where an absent status means settled
content. Here the value is a *rating*, and being stated is its entire purpose: a
technology nobody has rated is not thereby `candidate`, and a radar assembled
from omissions renders blank. `adopted` is the most common value precisely
because it is the interesting one.

### type

What kind of technology the chapter describes. Required on every technology
chapter. One of: `language`, `runtime`, `framework`, `library`, `package`,
`tool`, `service`, `platform`, `protocol`, `format`.

This field was previously called `kind`. The old name still parses, so an
existing `tech/` folder keeps working after a generator sync, but it reports a
warning — rename it to `type`.

### Folder-specific fields

- **version** (optional) — the pinned or targeted version, as a quoted string
  (e.g. `"9.0"`, `"^5.2"`). Omit when no version is committed to yet.
- **depends-on** (optional) — list of `<path>#<heading-slug>` references to
  other `tech/` chapters this technology sits on top of. These are the edges of
  the technology graph.
- **alternatives** (optional) — list of plain-string names that were considered
  instead. Like `domain/`'s `aliases`, this is a plain-string list, **not** a
  reference field.

Omit every optional field that has no value (no `related: []`, no
`version: null`).

## Authoring guidance

- Every technology appears exactly **once**, in the layer that owns it. If two
  layers use the same technology, document it in `shared.md` and point at it
  with `depends-on` from the layer chapters.
- `depends-on` must reference an existing `tech/` chapter. Do not point it at
  `arc42/`/`domain/` — use `related` for those.
- Keep `technology-graph.md`'s Mermaid diagram in sync with the `depends-on`
  edges in the layer files whenever a node or edge is added, removed, or
  renamed, and run the check in the same change:
  `node .devbook/_tools/devbook-meta/build.mjs --scope tech --check`.
- Ground stack claims in `arc42/` (especially
  `.devbook/arc42/04-solution-strategy.md#technology-choices` and
  `.devbook/arc42/09-architecture-decisions.md`) rather than inventing new choices here.
  If `tech/` and `arc42/` disagree, `arc42/` wins and `tech/` is corrected.
- A change of technology *decision* belongs in an ADR first; `tech/` records the
  outcome and links to it.
- Do not add a new metadata field without updating this file (folder-specific)
  or `devbook-chapter-metadata.md` (universal) first — the visualization
  tooling depends on a fixed schema.
