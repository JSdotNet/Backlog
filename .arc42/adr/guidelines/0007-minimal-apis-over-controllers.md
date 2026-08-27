# ADR 0007: Minimal APIs over controllers

```meta
status: active
related: [".arc42/05-building-block-view.md#cloud-service", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0007 (decided 2025-11-10,
`guide/adrs/0007-minimal-apis-over-controllers.md`), imported 2026-08-27.

## Decision

New HTTP APIs are Minimal APIs. Controllers are not used.

- Related endpoints are grouped with `MapGroup`.
- Endpoint lambdas stay thin and delegate to handlers (see
  [0006](0006-cqrs-for-api-projects.md)); the mapping from payload to command and
  from `Result` to HTTP response is all they do.
- Request and response models are records.
- **Shallow validation at the edge** via endpoint filters — required fields,
  format, range — returning `400 Bad Request`. It catches malformed requests; it
  does not enforce business invariants. The domain model remains authoritative.
- Cross-cutting endpoint concerns (logging, telemetry, error handling) are
  endpoint filters, registered with `AddEndpointFilter<T>()`.
- Responses use the `Results` helpers, so status codes stay consistent.
- OpenAPI is the built-in ASP.NET one: `AddOpenApi()` plus `MapOpenApi()`, with
  **Scalar** as the API UI. Swashbuckle is not used.

## How Backlog applies it

- `Backlog.Modules.Sync.Api` is the only HTTP surface in the product. It is a
  Minimal API: `app.MapGroup("/api/sync")` with `MapGet` / `MapPost` and
  `Results.Ok` / `Created` / `NoContent` / `NotFound`.
- There is no controller anywhere in the solution, and no MVC dependency.

## Deviations and gaps

- OpenAPI and Scalar are not wired up. The sync surface is three endpoints
  consumed by first-party clients; the moment a second consumer appears, or the
  surface grows, `AddOpenApi()` / `MapOpenApi()` / `MapScalarApiReference()`
  should be added.
- No endpoint filters and no edge validation yet — the endpoints take primitives
  and a `CaptureRequest` record and trust them.
- Endpoints call `SyncStore` directly instead of delegating to a handler.
