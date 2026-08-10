---
applyTo: "**"
description: Repository-wide file and folder naming conventions for assets outside the knowledge folders, which the knowledge-base plugin governs separately.
---

# File and folder naming

Naming **inside** the knowledge folders (`.arc42/`, `.domain/`, `.backlog/`,
`.tech/`, `.design/`, and any `_meta/`) is governed by the `knowledge-base`
plugin's `knowledge-naming.instructions.md`. This file covers only the rest of
the repository.

## Casing

Use kebab-case for files and folders (`.github/tools/knowledge-meta/`,
`copilot-orch-context.md`). Keep any casing an external tool requires, such as
`SKILL.md`, `README.md`, `CODEOWNERS`, and workflow filenames the platform
expects.

## No redundant suffixes

A name should not repeat what its location already says.

- Instruction files are named after the area they govern:
  `.github/instructions/naming.instructions.md`, not
  `naming-conventions.instructions.md`.
- Skill folders are named after the task they orchestrate, and the folder name
  must match the `name` field in that skill's `SKILL.md` frontmatter.

## Underscore prefix marks tooling assets

The leading underscore marks assets that exist **for tooling rather than for
reading** — templates, schemas, and generated artifacts (`_template.md`,
`_schema.json`, `_meta/`). Files inside an already-prefixed folder are not
prefixed again: `_meta/graph.json`, never `_meta/_graph.json`.

Do not use the prefix for documents meant to be read as content, even when
tooling also parses them.
