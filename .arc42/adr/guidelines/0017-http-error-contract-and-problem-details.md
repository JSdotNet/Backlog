# ADR 0017: HTTP error contract and Problem Details

```meta
status: proposed
related: [".arc42/05-building-block-view.md#cloud-service", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0017 (decided 2026-06-04,
`guide/adrs/0017-http-error-contract-and-problem-details.md`), imported
2026-08-27.

**Status: proposed.** The decision is accepted upstream and binds any work that
reaches this ground. The sync API returns bare status codes with no Problem
Details body and no exception-handling middleware, so nothing in the code
applies this decision yet.

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

- **Implemented for the device and inbox endpoints.** `Backlog.Modules.Sync.Api`
  maps its `Result` outcomes to RFC 7807 Problem Details through
  `Endpoints/SyncResults.cs`, with `type` set to
  `https://backlog.jsdotnet.dev/problems/{code}` and a `code` extension per
  failure (`pairing.code_not_found`, `pairing.code_expired`,
  `pairing.code_used`, `pairing.code_malformed`, `device.credential_invalid`,
  `device.name_required`, `inbox.item_not_found`) rather than a bare status
  code.
- **What is still missing is the centralized piece**: there is no
  exception-handling middleware turning an unexpected exception into a `500`
  Problem Details response. The mapping that exists is per-endpoint, applied by
  the handlers that call it, not a boundary-wide guarantee yet.
