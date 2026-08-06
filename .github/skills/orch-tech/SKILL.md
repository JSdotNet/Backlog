---
name: orch-tech
description: 'Orchestrate changes to .tech/ (this project''s technology graph of platforms, runtimes, frameworks, libraries, packages, services, and tools) for this repository. Use for any create/update of .tech/technology-graph.md or a layer file (shared.md, desktop.md, mobile.md, ide.md, cloud.md, tooling.md). Enforces tech.instructions.md structure and chapter-metadata.instructions.md metadata blocks, and keeps the graph diagram in sync with depends-on edges.'
---

# Orchestrate Technology Knowledge (`.tech/`)

Route every `.tech/` change through this skill instead of editing the folder
directly, so the technology graph stays consistent with `.arc42`'s architecture
decisions and with this repository's structure and metadata conventions.

## Input Expectations

- Target scope: `technology-graph.md`, or one or more layer files
  (`shared.md`, `desktop.md`, `mobile.md`, `ide.md`, `cloud.md`, `tooling.md`).
- Change goal (e.g. add a technology, pin a version, promote a `candidate` to
  `adopted`, retire a technology, add a new layer).
- Whether the change follows a decision already recorded in `.arc42`, or is
  still an open choice.

## Workflow Stages

> Agent transitions require explicit user approval before switching. If
> `architecture:architect` is not installed, perform the reasoning step
> directly using the same instructions files and continue.

### Stage 1: Context Loading
- Load `.github/instructions/tech.instructions.md` and
  `.github/instructions/chapter-metadata.instructions.md` (task-scoped, not
  baseline context).
- Load `.tech/technology-graph.md` plus only the layer files in scope.
- Load the grounding `.arc42` chapters only when the change touches a stack
  decision: `04-solution-strategy.md`, `07-deployment-view.md`,
  `09-architecture-decisions.md`.

**Agents:** none (context loading only)

### Stage 2: Technology Reasoning
- Hand off to `architecture:architect` when the change implies a real decision
  (new technology, replacement, or status promotion/demotion).
- Confirm the technology belongs in exactly one layer; anything used by two or
  more channels belongs in `shared.md`.
- If the change is a genuine architecture decision, record it as an ADR first
  (`orch-adr`) and let `.tech` record the outcome with a `related` link.

**Agents:** `architecture:architect`

### Stage 3: Authoring & Metadata Enforcement
- Draft or update chapters using the technology chapter template in
  `tech.instructions.md`; keep each chapter short.
- Add or update the chapter metadata block on every touched technology chapter:
  `status` and `kind` required; `version`, `depends-on`, `alternatives`,
  `related`, `issue` optional and omitted when empty.
- Set `status` from this folder's ladder: `candidate`, `trial`, `adopted`,
  `hold`, `retired`.
- Ensure every `depends-on` entry resolves to an existing `.tech` chapter; use
  `related` for `.arc42`/`.domain`/`.backlog` links instead.
- Update the file-level metadata block on every touched file.

**Agents:** `architecture:architect`

### Stage 4: Graph Sync & Review
- Update the Mermaid diagram in `.tech/technology-graph.md` so its nodes and
  edges match the `depends-on` fields exactly.
- Regenerate the derived index:
  `node .github/tools/knowledge-graph/build-graph.mjs --scope .tech`, and
  confirm it reports no broken references.
- Update the layer table and "Open questions" section when layers or open
  choices change.
- Verify with the `knowledge-graph` canvas scoped to `.tech`, and with the
  `knowledge-canvas` canvas (open the changed file; check the metadata/lint
  panel is clean apart from the intentional no-meta sections of
  `technology-graph.md`).
- Summarize changed files/chapters for the user.

**Agents:** `architecture:architect`

## Usage Pattern

```text
Invoke: orch-tech
- Files: cloud.md, technology-graph.md
- Goal: promote ASP.NET Core Minimal APIs from candidate to adopted and pin the version
```

## Output Expectations

- `.tech/` files updated following `tech.instructions.md`.
- Every touched chapter and file carries a correct metadata block.
- All `depends-on` references resolve, and the graph diagram matches them.
- Changed paths summarized for the user.

## Reference

- `.github/instructions/tech.instructions.md`
- `.github/instructions/chapter-metadata.instructions.md`
- `.github/instructions/workflow-routing.instructions.md`
