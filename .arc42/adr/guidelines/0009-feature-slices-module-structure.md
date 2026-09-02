# ADR 0009: Feature slices inside a module

```meta
status: active
related: [".arc42/05-building-block-view.md", ".arc42/adr/0002-backlog-module-owns-the-entry-text-language.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0009 (decided 2026-04-07,
`guide/adrs/0009-feature-slices-module-structure.md`), imported 2026-08-27. It
refines [0005](0005-modular-monolith-structure.md) and supersedes the
organization's ADR 0008 for physical layout.

## Decision

How a feature is physically added to a module.

**Two core projects.** `Backlog.Modules.<Module>` holds domain logic, handlers,
and feature slices. `Backlog.Modules.<Module>.Abstractions` is the module's
public surface: DTOs, port interfaces, domain event declarations. Callers outside
the module depend on Abstractions only, never on the implementation project.

**One namespace per feature.** Features sit under a `Features` sub-namespace,
each in its own nested namespace — `…Features.CreateEntry`,
`…Features.GetEntryById`. A feature folder holds its command or query record and
its handler, and nothing else.

**DTOs are centralized** in `Abstractions/DataTransferObjects/`, so payload types
cannot drift into domain or handler namespaces.

**Endpoints map, they do not decide.** An endpoint receives the DTO, maps it onto
the internal command, dispatches, and maps the result back.

**One registration extension method** per module, living in the module project
rather than in Abstractions, wires handlers and infrastructure.

The intended reflex: to find the code for a feature, open
`{Module}/Features/{FeatureName}/`.

## How Backlog applies it

- `Backlog.Modules.Tasks` follows the layout: `DomainModels/`, `Features/`,
  the `IBacklogRepository` port at the module root, `Services/`, `Extensions/`.
- `Backlog.Modules.Tasks.Abstractions` publishes the vocabulary enums, the
  DTOs, the `ITaskItems` service port, and `EntryTextParser`.
- `tests/Backlog.ArchitectureTests/ModuleSurfaceTests.cs` keeps the published
  surface from quietly growing.

## Deviations and gaps

- **`EntryTextParser` sits in Abstractions**, although this decision describes
  Abstractions as contracts rather than behavior. Local ADR 0002 takes that
  position deliberately: the entry text format *is* the published contract of
  this context — an editor that cannot read and write it cannot edit an entry at
  all. Read
  `.arc42/adr/0002-backlog-module-owns-the-entry-text-language.md` before
  treating it as an inconsistency.
- Modules other than `Backlog` and `Roadmap` have not been carved into feature
  slices yet.
