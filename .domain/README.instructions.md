# Domain knowledge (`.domain`)

This folder holds the durable, ubiquitous-language record of the domain model,
organized by bounded context. It is the authoritative source for "what the
domain looks like" — complementary to `.arc42` (system architecture) and
`.backlog` (work items).

## Structure

```
.domain/
  _template/            Copy this when creating a new bounded context
    domain.md
    features.md
    model.md
    dependencies.md
  <bounded-context-name>/
    domain.md
    features.md
    model.md
    dependencies.md
```

Each bounded context gets its own subfolder, named in kebab-case after the
context (e.g. `.domain/order-management/`). Use the same name consistently
across `.domain`, ADRs, and code module names where practical.

## File responsibilities

- **domain.md** — One chapter per Aggregate or Domain Service in the context.
  - Aggregate chapters include sub-chapters for their Entities, Value Objects,
    and Enums (i.e., the aggregate's owned building blocks).
  - Domain Service chapters describe the service's responsibility and the
    aggregates/policies it coordinates.
  - Value Objects and Enums that are **shared across multiple aggregates**
    within the context get their own separate chapter (do not duplicate them
    under each aggregate that uses them).
- **features.md** — The features and sub-features this bounded context
  supports, in business language. Group sub-features under their parent
  feature.
- **model.md** — The domain model itself: relationships between aggregates,
  entities, and value objects, ideally as a Mermaid class or object diagram,
  plus supporting narrative.
- **dependencies.md** — Outbound dependencies on other bounded contexts or
  modules (what this context calls/consumes/subscribes to), and known inbound
  dependents where relevant. Note integration pattern (sync call, event,
  shared kernel, ACL) for each.

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
