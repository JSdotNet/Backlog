---
name: naming
description: Repository-wide file and folder naming conventions for assets outside the knowledge folders, which the devbook plugin governs separately.
paths:
  - "**"
---

# File and folder naming

Naming **inside** `.devbook/` — including the underscore prefix that marks tooling
assets such as `_meta/` and `_tools/` — is `devbook-naming.md`'s. This file covers
only the rest of the repository.

## Casing

Use kebab-case for files and folders (`plugins/backlog-tools/`,
`context-loading.md`). Keep any casing an external tool requires, such as
`SKILL.md`, `README.md`, `CODEOWNERS`, and workflow filenames the platform
expects.

## No redundant suffixes

A name should not repeat what its location already says.

- Rule files are named after the area they govern: `.agents/rules/naming.md`,
  not `naming-conventions.md`. The host wrappers in `.claude/rules/` and
  `.github/instructions/` carry the same name.
- Skill folders are named after the task they orchestrate, and the folder name
  must match the `name` field in that skill's `SKILL.md` frontmatter.
