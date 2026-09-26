---
name: devbook-naming
description: File and folder naming conventions inside devbook folders, including the one dotted folder, .devbook/, and underscore-prefixed tool-interpreted data.
---

# File and folder naming in devbook folders

## Underscore prefix marks tool-interpreted data

Anything that exists **for tooling rather than for reading directly** carries a
leading underscore, so a human scanning a folder can tell specification from
machinery at a glance. Folders with a leading underscore are always machinery:
what a generator reads, what it writes, or the generator itself.

- **Tooling folders** are prefixed: `_meta/` (derived artifacts),
  `.devbook/_tools/` (the generators). Files *inside* such a folder are not
  prefixed again — the folder already carries the signal, so it is
  `_meta/graph.json`, never `_meta/_graph.json`.
- **Tooling files** sitting alongside content are prefixed individually:
  `_template.md`, `_schema.json`.

Use the prefix when the asset is a template, a schema, a generated artifact,
input consumed only by a generator or viewer, or the generator. Do not use it for documents meant
to be read as content, even if tooling also parses them — the `domain/`,
`arc42/`, `tech/`, `design/`, and `ai/` Markdown files are read by both
humans and tooling and stay unprefixed.

## One dot, on the parent

The dot marks the one specification area a repository has, `.devbook/`; the underscore
marks tool-interpreted data inside it. The five folders under the parent carry neither:
`arc42/`, `domain/`, `tech/`, `design/`, `ai/` — `.devbook/domain/`, never
`.devbook/.domain/`, and never a dotted name in prose either. Write a folder as
`domain/` when it is the thing meant, `.devbook/domain/…` when a path is meant, and
`domain` bare when it is the kind in a field or a stamp. See
`devbook-chapter-metadata.md`.

## No redundant suffixes

A name should not repeat what its location already says.

- Derived artifacts are named after what they are, not their scope:
  `.devbook/tech/_meta/graph.json`, not `.devbook/tech/_meta/tech-graph.json`.
- Files within a bounded context are named after their role, not the context:
  `.devbook/domain/ordering/features.md`, not `.devbook/domain/ordering/ordering-features.md`.

## Casing

Use kebab-case for files and folders (`.devbook/domain/order-management/`,
`technology-graph.md`). Keep any casing that an external tool requires, such as
`README.md`.

## Reference

- `devbook-derived-artifacts.md` (a layered plugin's rule) — placement, naming,
  and envelope rules for generated artifacts under `_meta/`.
