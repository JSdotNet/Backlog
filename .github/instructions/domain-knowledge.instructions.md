---
applyTo: ".domain/**"
description: Structure and authoring rules for the domain knowledge folder, organized per bounded context.
---

# Domain knowledge (`.domain`)

`.domain` is the durable, ubiquitous-language record of the domain model,
organized by bounded context. It is the authoritative source for "what the
domain looks like" — complementary to `.arc42` (system architecture) and
`.backlog` (work items).

## Structure

Each bounded context gets its own subfolder, named in kebab-case after the
context (e.g. `.domain/order-management/`). Use the same name consistently
across `.domain`, ADRs, and code module names where practical.

```
.domain/
  <bounded-context-name>/
    domain.md
    features.md
    model.md
    dependencies.md
```

When starting a new bounded context, create the folder and all four files
using the templates below — do not invent a different file set.

## File responsibilities

- **domain.md** — One chapter per Aggregate or Domain Service in the context.
  - Aggregate chapters include sub-chapters for their owned Entities, Value
    Objects, and Enums.
  - Domain Service chapters describe the service's responsibility and the
    aggregates/policies it coordinates.
  - Value Objects and Enums **shared across multiple aggregates** within the
    context get their own separate chapter — do not duplicate them under
    each aggregate that uses them.
- **features.md** — The features and sub-features this bounded context
  supports, in business language. Group sub-features under their parent
  feature.
- **model.md** — The domain model itself: relationships between aggregates,
  entities, and value objects, ideally as a Mermaid class diagram, plus
  supporting narrative.
- **dependencies.md** — Outbound dependencies on other bounded contexts or
  modules, and known inbound dependents. Note the integration pattern (sync
  call, event, shared kernel, ACL) for each.

## Authoring guidance

- Use ubiquitous language consistently; if a term's meaning differs from
  another context, say so explicitly rather than silently reusing the word.
- Ground new or changed content in existing ADRs and design guidance from
  `jsdotnet-project-guidelines` / `jsdotnet-project-design` before writing;
  do not invent structure that conflicts with a recorded decision.
- For domain modeling work (new bounded contexts, aggregate design,
  ubiquitous language questions), route through `domain-design:domain-architect`
  per `.github/instructions/workflow-routing.instructions.md`.
- Keep these documents current as the model evolves — treat drift between
  `.domain` and the actual code/domain-design MCP guidance as a defect.
- Every Aggregate, Domain Service, Shared Value Objects, and Shared Enums
  chapter in `domain.md`, and every Feature/Sub-feature chapter in
  `features.md`, must carry the metadata block described in
  `.github/instructions/chapter-metadata.instructions.md` (status,
  dependencies, cross-folder tags, GitHub issue link) — required for the
  planned visualization tooling.
- The metadata block's `status` field uses `draft`, `proposed`, `active`, or
  `deprecated` in this folder. Domain knowledge describes the current (or
  agreed-future) model, not a task queue, so there is no `done`: `active`
  means "this is the current model", `deprecated` means superseded.

## Templates

### domain.md

```markdown
# Domain: <Bounded Context Name>

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

## Aggregate: <AggregateName>

\`\`\`meta
status: draft
related: []
issue: null
\`\`\`

Responsibility, lifecycle, and invariants of the aggregate (what it
guarantees to be true at all times, and why it exists as a consistency
boundary).

### Entities

#### <EntityName>

Role within the aggregate, identity, and lifecycle notes.

### Value Objects

#### <ValueObjectName>

Meaning, equality semantics, and validation rules.

### Enums

#### <EnumName>

Values and what each one means in business terms.

## Aggregate: <NextAggregateName>

...

## Domain Service: <DomainServiceName>

\`\`\`meta
status: draft
related: []
issue: null
\`\`\`

Responsibility of the service, which aggregates/policies it coordinates, and
why the behavior does not belong on a single aggregate.

## Shared Value Objects

\`\`\`meta
status: draft
related: []
issue: null
\`\`\`

> Value Objects used by more than one aggregate in this bounded context.

### <SharedValueObjectName>

Meaning, equality semantics, validation rules, and which aggregates use it.

## Shared Enums

\`\`\`meta
status: draft
related: []
issue: null
\`\`\`

> Enums used by more than one aggregate in this bounded context.

### <SharedEnumName>

Values and what each one means in business terms, and which aggregates use it.
```

### features.md

```markdown
# Features: <Bounded Context Name>

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Feature: <FeatureName>

\`\`\`meta
status: draft
depends-on: []
related: []
issue: null
\`\`\`

Short description of the capability and the business value it delivers.

### Sub-feature: <SubFeatureName>

\`\`\`meta
status: draft
depends-on: []
related: []
issue: null
\`\`\`

Description of the sub-feature and how it fits under the parent feature.

### Sub-feature: <NextSubFeatureName>

...

## Feature: <NextFeatureName>

...
```

### model.md

```markdown
# Domain Model: <Bounded Context Name>

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) — this
> file focuses on structure and relationships.

## Model diagram

\`\`\`mermaid
classDiagram
    class AggregateName {
        +Identity Id
        +Value fields...
    }
    class EntityName
    class ValueObjectName

    AggregateName "1" --> "many" EntityName : contains
    AggregateName --> ValueObjectName : has
\`\`\`

## Relationship notes

- Describe cardinalities, ownership direction, and any relationships that
  aren't obvious from the diagram alone (e.g. why an association is one-way,
  or why two aggregates only relate by id reference rather than direct
  object reference).
```

### dependencies.md

```markdown
# Dependencies: <Bounded Context Name>

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| <OtherContext> | <e.g. async event, REST call, shared kernel> | <reason this context needs it> |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| <OtherContext> | <e.g. subscribes to event X> | <what would break if changed> |

## Notes

- Flag any dependency that crosses a bounded-context boundary without an
  anti-corruption layer or published language, so it can be revisited.
- Link to the relevant `domain-interaction-diagram` / `context-mapping`
  artifact if one exists for this relationship, instead of duplicating it.
```
