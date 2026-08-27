# ADR 0006: Lightweight CQRS, no mediator

```meta
status: active
related: [".arc42/05-building-block-view.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0006 (decided 2025-11-10,
`guide/adrs/0006-cqrs-recommendation-for-aspnet-api.md`), imported 2026-08-27.

## Decision

A request becomes a command or a query, handled by a dedicated handler.

- Commands, queries, and DTOs are C# **records** — immutable, explicit about
  intent.
- Handlers are injected, and stay free of delivery concerns: no `HttpContext`, no
  UI types.
- Handlers return `Result` / `Result<T>` (see
  [0004](0004-result-objects-for-expected-failures.md)); they never hand back a
  domain entity.
- Command, query, handler, and result stay **together per feature** — a vertical
  slice.
- Validation happens at the edge, before the handler runs.
- The handler interfaces — `ICommandHandler<TCommand, TResult>`,
  `ICommandHandler<TCommand>`, `IQueryHandler<TQuery, TResult>` — are declared
  **once** in the shared core project. No module redeclares them, and the core
  project depends on no web framework or infrastructure.

## How Backlog applies it

- `src/Core/Backlog.SharedKernel/Handlers/` declares `ICommandHandler` and
  `IQueryHandler` once for the whole solution.
- **No mediator.** There is no MediatR dependency and no dispatch indirection: a
  caller depends on the handler interface it needs, and DI resolves it.
- `Backlog.Modules.Backlog` organizes its handlers as feature slices under
  `Features/` — see [0009](0009-feature-slices-module-structure.md).

## Deviations and gaps

- The pattern is applied in-process, not only behind HTTP. The organization
  framed CQRS as an ASP.NET API concern; here the desktop and mobile hosts are
  the delivery layer for most modules, and the same rule holds — the host
  dispatches, the module decides.
- The sync service's endpoints still call a store directly rather than a handler.
