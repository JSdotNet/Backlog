# Copilot Orchestration Repo Context

Repo-specific startup and QA context for `orch-*` orchestration runs in `JSdotNet/Backlog`.
General orchestration routing and enforcement come from the `copilot-app` plugin; this file
only supplies what is specific to this repository.

## Application

**Runnable application:** none

Backlog is currently a documentation, backlog, and architecture-knowledge repository. It
contains checked-in knowledge folders (`.arc42/`, `.domain/`, `.backlog/`, `.tech/`,
`.design/`), repository governance assets under `.github/`, and Node-based generator tooling
under `.github/tools/knowledge-meta/`. There is **no application runtime, no AppHost, no
service, no build, and no test suite**.

Do not search for an AppHost, a solution file, or a dev server — none exist. When the product
implementation is scaffolded, replace this section and the ones below with the real values.

## How to Run

Not applicable — there is nothing to start.

The only executable asset is the knowledge-metadata generator, run from the repository root:

```powershell
node .github/tools/knowledge-meta/build.mjs
```

It regenerates derived artifacts under `_meta/` folders. It is not an application, and it is
run by CI (`.github/workflows/knowledge-meta.yml`) rather than as part of an orchestration
startup. Never hand-edit its output.

## Base URLs

None. This repository exposes no HTTP endpoints, dashboards, or UI entry points.

## Test Credentials

None required, because nothing runs and no authenticated surface exists.

If credentials become necessary later, record only a **pointer** here (for example the name
of the secret store, vault, or user-secrets entry). Never place actual secrets, tokens, or
passwords in this file or anywhere else in the repository.

GitHub operations use the repository account, as stated in `.github/github-app.yml`; pull
requests are created through the `pr-jsdotnet` skill.

## MCP Servers

Authority order and fallbacks are defined in
`.github/instructions/mcp-usage.instructions.md`. This repository relies on:

- **`jsdotnet-project-guidelines`** — authoritative source for repository guidance and
  conventions. Query it before changing governed instruction, skill, or knowledge assets.
- **`jsdotnet-project-design`** — authoritative source for design and UX guidance, including
  the color scheme and design tokens materialized into `.design/`.

If an authoritative MCP source is unavailable, read the checked-in instruction files directly
and state that authoritative guidance could not be verified.

## Healthy Startup

There is no startup to observe, so there are no logs, traces, or health endpoints to check.

A run is "healthy" here when the repository-level checks pass instead:

- Governed Markdown keeps the `meta` blocks required by the `knowledge-base` plugin's
  `knowledge-chapter-metadata.instructions.md`.
- Instruction files keep valid `applyTo` and `description` frontmatter.
- Derived `_meta/` artifacts are regenerated rather than hand-edited, and
  `node .github/tools/knowledge-meta/build.mjs --check` passes with a clean
  `git diff` over `*_meta/*.json`.

## QA Depth

skipped

The QA validation phase has nothing to validate: no runnable application, no automated tests,
and no runtime telemetry. Skip it cleanly rather than searching for an AppHost or attempting
end-to-end validation. Verification for changes in this repository is documentation review —
Markdown structure, metadata blocks, frontmatter validity, and cross-reference integrity.

## Repo-Native Orchestration Skills

The knowledge-folder orchestrations (`orch-arc42-content`, `orch-domain`, `orch-backlog`,
`orch-tech`, `orch-design`) are provided by the `knowledge-base` plugin, not by this
repository. Only one skill under `.github/skills/` remains repo-native:

- `orch-fallback` — generic entrypoint for any task category with no dedicated `orch-*` skill,
  repo-native or plugin-provided.

`pr-jsdotnet` also lives under `.github/skills/`, but it is a pull-request workflow rather
than an orchestration; see `.github/copilot-instructions.md`.

