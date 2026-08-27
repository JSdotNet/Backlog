# ADR 0017: HTTP error contract and Problem Details

```meta
status: active
related: [".arc42/05-building-block-view.md#cloud-service", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0017 (decided 2026-06-04,
`guide/adrs/0017-http-error-contract-and-problem-details.md`), imported
2026-08-27.

## Decision

Every non-success HTTP response uses **RFC 7807 Problem Details**, and `Result`
outcomes map to status codes consistently at the delivery boundary.

| Application outcome | HTTP status |
|---|---|
| Validation failure | `400 Bad Request` |
| Unauthorized | `401 Unauthorized` |
| Forbidden | `403 Forbidden` |
| Not found | `404 Not Found` |
| Business-rule conflict | `409 Conflict` |

The body carries `type`, `title`, `status`, `detail`, and `traceId`, optionally
extended with `code`, `errors`, `correlationId`, or `resourceId`.

**Unexpected exceptions are handled once**, at the boundary: centralized
exception handling turns them into a `500` Problem Details response. No broad
try/catch scattered through endpoints and handlers, one log entry at the
boundary, and no stack trace or internal detail in the response.

Boundary validation failures may carry a structured `errors` extension keyed by
field. Domain invariants stay enforced in the domain model regardless.

## How Backlog applies it

- The rule binds exactly one surface: `Backlog.Modules.Sync.Api`.
- `Error.Type` in `Backlog.SharedKernel` already classifies failures — the
  classification a mapping helper needs to turn a `Result` into the right status
  code without the endpoint deciding.

## Deviations and gaps

- **Not implemented.** The sync endpoints return bare `Results.NotFound()` with
  no body, and there is no exception-handling middleware, no Problem Details
  wiring, and no `Result`-to-HTTP mapping helper.
- This is the natural companion change to giving the sync service real handlers
  (see [0004](0004-result-objects-for-expected-failures.md) and
  [0007](0007-minimal-apis-over-controllers.md)); doing one without the other
  leaves the API's failure contract undefined.
