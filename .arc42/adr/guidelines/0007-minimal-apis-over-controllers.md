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
- **The lambdas delegate.** Every route in `Endpoints/` maps its payload onto a
  command or query and hands it to an `ICommandHandler` / `IQueryHandler`
  resolved from DI — `PushTasks`, `PullTasks`, `ListInbox`, `CaptureInboxItem`,
  `AcknowledgeInboxItem`, and the five device slices. Turning a `Result` into a
  response is the other half, and it happens in one place,
  `Endpoints/SyncResults.cs`.
- **Cross-cutting concerns are endpoint filters**, registered with
  `AddEndpointFilter<T>()` on the group rather than per route.
  `Security/OwnerScopeFilter` resolves whose data a request is about before the
  endpoint runs; `Endpoints/ReplicaFaultFilter` turns the two faults the task
  replica raises as exceptions into the same problem body every other failure
  gets.

## Deviations and gaps

- **Scalar is not wired up.** `AddOpenApi()` and `MapOpenApi()` are — the
  document is served in Development only, because a deployed sync service has no
  reason to publish a map of its surface anonymously — and
  `MapScalarApiReference()` is what is still missing. The surface is ten routes
  under `/api/sync` now rather than the three this bullet was written against,
  so "first-party clients need no UI" is a thinner argument than it was.
- **Edge validation is written inline rather than as a filter.** The rule asks
  for the shallow checks in an endpoint filter; `InboxEndpoints` refuses an empty
  or oversized capture and `TaskSyncEndpoints` refuses an oversized push batch,
  both in the endpoint method itself. Each returns the `400` the rule asks for
  and neither reaches a handler, so what deviates is the placement rather than
  the check.
