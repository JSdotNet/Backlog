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

- Governed Markdown keeps the metadata blocks required by
  `.github/instructions/chapter-metadata.instructions.md`.
- Instruction files keep valid `applyTo` and `description` frontmatter.
- Derived `_meta/` artifacts are regenerated rather than hand-edited, per
  `.github/instructions/derived-artifacts.instructions.md`.

## QA Depth

skipped

The QA validation phase has nothing to validate: no runnable application, no automated tests,
and no runtime telemetry. Skip it cleanly rather than searching for an AppHost or attempting
end-to-end validation. Verification for changes in this repository is documentation review —
Markdown structure, metadata blocks, frontmatter validity, and cross-reference integrity.

## Repo-Native Orchestration Skills

These live under `.github/skills/` and **take precedence** over the plugin-provided `orch-*`
skills for the task categories they cover.

- `orch-arc42-content` — direct content edits to `.arc42/` chapters (refreshing an existing
  chapter, section, or diagram, not authoring a new ADR, TDR, or blueprint).
- `orch-domain` — changes to `.domain/`: bounded-context domain model, features, model
  diagrams, flows, dependencies, and naming.
- `orch-backlog` — changes to `.backlog/`: durable work-item artifacts grouped by concern,
  their Items and Sub-items, including publishing to GitHub Issues.
- `orch-tech` — changes to `.tech/`: the technology graph of platforms, runtimes, frameworks,
  libraries, packages, services, and tools.
- `orch-design` — changes to `.design/`: UX principles, dark-mode color tokens,
  typography/layout, interaction guidelines, content editing, accessibility, and component
  libraries.
- `orch-fallback` — generic entrypoint for any task category with no dedicated `orch-*` skill,
  repo-native or plugin-provided.

