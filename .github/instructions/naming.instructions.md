---
applyTo: "**"
description: Repository-wide file and folder naming conventions, including the underscore prefix that marks tooling assets.
---

# File and folder naming

## Underscore prefix marks tooling assets

Anything that exists **for tooling rather than for reading** carries a leading
underscore, so a human scanning a folder can tell content from machinery at a
glance.

- **Tooling folders** are prefixed: `_index/` (derived artifacts). Files
  *inside* such a folder are not prefixed again — the folder already carries
  the signal, so it is `_index/graph.json`, never `_index/_graph.json`.
- **Tooling files** sitting alongside content are prefixed individually:
  `_template.md`, `_schema.json`.

Use the prefix when the asset is a template, a schema, a generated artifact, or
input consumed only by a generator or viewer. Do not use it for documents meant
to be read as content, even if tooling also parses them — the `.domain`,
`.arc42`, `.backlog`, and `.tech` Markdown files are read by both humans and
tooling and stay unprefixed.

## Area folders keep their dot prefix

Top-level knowledge areas keep the existing leading-dot convention and are
**not** renamed: `.arc42/`, `.domain/`, `.backlog/`, `.tech/`. The dot marks a
repository-level area; the underscore marks tooling within one.

## No redundant suffixes

A name should not repeat what its location already says.

- Instruction files are named after the area they govern:
  `.github/instructions/tech.instructions.md`, not `tech-knowledge.…`.
- Derived artifacts are named after what they are, not their scope:
  `.tech/_index/graph.json`, not `.tech/_index/tech-graph.json`.

## Casing

Use kebab-case for files and folders (`knowledge-graph/`,
`chapter-metadata.instructions.md`). Keep any casing that an external tool
requires, such as `SKILL.md` and `README.md`.

## Reference

- `.github/instructions/derived-artifacts.instructions.md` — placement, naming,
  and envelope rules for generated artifacts under `_index/`.
