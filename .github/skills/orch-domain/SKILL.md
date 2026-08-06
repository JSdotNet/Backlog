---
name: orch-domain
description: 'Orchestrate changes to .domain/ (bounded-context domain model, features, model diagrams, dependencies, naming) for this repository. Use for any create/update/refresh of .domain/context-map.md or a bounded context''s domain.md, features.md, model.md, flow.md, dependencies.md, or naming.md. Enforces domain.instructions.md structure/templates and chapter-metadata.instructions.md metadata blocks before saving.'
---

# Orchestrate Domain Knowledge (`.domain/`)

Route every `.domain/` change through this skill instead of editing the folder
directly, so bounded-context modeling stays consistent with
`domain-design:domain-architect`'s expertise and with this repository's own
structure and metadata conventions.

## Input Expectations

- Target scope: root `context-map.md`, or one bounded context (existing or
  new) and which of its files (`domain.md`, `features.md`, `model.md`,
  `flow.md`, `dependencies.md`, `naming.md`) are in scope.
- Change goal (e.g. new aggregate, refined feature breakdown, new
  cross-context dependency, term/alias cleanup).
- Whether this is new bounded-context scaffolding or a refinement of an
  existing context.

## Workflow Stages

> Agent transitions require explicit user approval before switching. If
> `domain-design:domain-architect` is not installed, perform the modeling
> step directly using the same instructions files and continue.

### Stage 1: Context Loading
- Load `.github/instructions/domain.instructions.md` and
  `.github/instructions/chapter-metadata.instructions.md` (task-scoped, not
  baseline context).
- Load only the relevant bounded-context files already in `.domain/` (not the
  whole folder) plus `.domain/context-map.md` for cross-context relationships.
- Note existing `related`/`depends-on`/`aliases` entries that the change may
  need to update elsewhere.

**Agents:** none (context loading only)

### Stage 2: Domain Modeling
- Hand off to `domain-design:domain-architect` for the actual modeling
  decisions: aggregate boundaries, invariants, domain services, domain
  events, feature breakdown, or naming/alias resolution.
- Draft or refresh content using the exact templates in
  `domain.instructions.md` (`domain.md`, `features.md`, `model.md`,
  `flow.md`, `dependencies.md`, `naming.md`).
- Keep `model.md` structural (Mermaid class diagram) and `flow.md`
  lifecycle/process-oriented (Mermaid state/sequence diagrams) — do not mix
  the two.

**Agents:** `domain-design:domain-architect`

### Stage 3: Metadata & Cross-Reference Enforcement
- Add or update the chapter metadata block (`status` required; `related`,
  `issue` optional) on every new/edited Aggregate, Domain Service, Domain
  Event, Shared Value Objects/Enums, Feature/Sub-feature, or `Term` chapter.
- Add or update the file-level metadata block on every touched file,
  including `context-map.md`, `model.md`, `flow.md`, and `dependencies.md`
  (which carry a file-level block only, no per-chapter blocks).
- Set `status` from this folder's allowed values: `draft`, `proposed`,
  `active`, `deprecated` (no `done`).
- Update `depends-on` on `features.md` chapters and `aliases`/`related` on
  `naming.md` terms as needed; omit empty optional fields per the
  omit-when-empty rule.
- If a chapter heading or file was renamed/moved, update every `related`,
  `depends-on`, or `implements` entry elsewhere that references its old
  `<path>#<heading-slug>` or `<path>`.

**Agents:** `domain-design:domain-architect`

### Stage 4: Consistency Review
- Confirm `naming.md` aliases still resolve to the correct canonical chapter
  via `related`.
- Confirm `dependencies.md` uses explicit DDD relationship terminology (ACL,
  Customer/Supplier, Partnership, OHS + Published Language) for every row.
- Confirm no new top-level metadata field was invented without updating
  `chapter-metadata.instructions.md` or `domain.instructions.md`
  first.
- Summarize changed files/chapters for the user.

**Agents:** `domain-design:domain-architect`

## Usage Pattern

```text
Invoke: orch-domain
- Context: order-management
- Files: domain.md, features.md
- Goal: add a new "Split Order" aggregate behavior and its feature entry
```

## Output Expectations

- `.domain/` files updated following the exact templates in
  `domain.instructions.md`.
- Every touched chapter and file carries a correct metadata block per
  `chapter-metadata.instructions.md`.
- Cross-references (`related`, `depends-on`, `aliases`) kept in sync across
  the changed and any dependent files.
- Changed paths summarized for the user.

## Reference

- `.github/instructions/domain.instructions.md`
- `.github/instructions/chapter-metadata.instructions.md`
- `.github/instructions/workflow-routing.instructions.md`
