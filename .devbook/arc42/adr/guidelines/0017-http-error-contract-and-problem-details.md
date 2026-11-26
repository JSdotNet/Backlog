# ADR 0017: HTTP error contract and Problem Details

```meta
date: 2026-11-26
related: [".devbook/arc42/05-building-block-view.md#cloud-service", ".devbook/arc42/09-architecture-decisions.md", ".devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md"]
```

Inherited from the organization's ADR 0017 (decided 2026-06-04,
`guide/adrs/0017-http-error-contract-and-problem-details.md`), imported
2026-08-27.

**Status: proposed.** The decision is accepted upstream and binds any work that
reaches this ground. The sync API returns bare status codes with no Problem
Details body and no exception-handling middleware, so nothing in the code
applies this decision yet.

## Decision

```meta
```

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

```meta
```

- The rule binds exactly one surface: `Backlog.Modules.Sync.Api`.
- **The OAuth 2.1 authorization server is the one exception**
  ([local ADR 0058 §8](../0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md#8-a-remote-agent-reaches-backlog-through-oauth-21-on-the-sync-service)).
  Its endpoints are `/.well-known/oauth-authorization-server`, `/oauth/authorize`,
  `/oauth/consent` and `/oauth/token`. They are protocol endpoints, so a failure
  answers RFC 6749's `{error, error_description}` body or an error on the
  redirect, the shape a connector client reads.
- `Error.Type` in `Backlog.SharedKernel` already classifies failures — the
  classification a mapping helper needs to turn a `Result` into the right status
  code without the endpoint deciding.

## Deviations and gaps

```meta
```

- **Implemented for every endpoint on the surface but the authorization
  server's.** `Backlog.Modules.Sync.Api`
  maps its `Result` outcomes to RFC 7807 Problem Details through
  `Endpoints/SyncResults.cs`, with `type` set to
  `https://backlog.jsdotnet.dev/problems/{code}` and a `code` extension per
  failure rather than a bare status code. The codes are declared once, in
  `Backlog.Modules.Sync.Abstractions.SyncErrorCodes`:

  | Code | Status | Raised by |
  |---|---|---|
  | `pairing.code_not_found` | 404 | Pairing |
  | `pairing.code_expired` | 409 | Pairing |
  | `pairing.code_used` | 409 | Pairing |
  | `pairing.code_malformed` | 400 | Pairing |
  | `device.credential_invalid` | 401 | Pairing |
  | `device.name_required` | 400 | Pairing |
  | `inbox.item_not_found` | 404 | Inbox |
  | `inbox.capture_invalid` | 400 | Inbox |
  | `sync.push_batch_too_large` | 400 | Task sync |
  | `sync.cursor_malformed` | 400 | Task sync |
  | `sync.cursor_not_yours` | 403 | Task sync |
  | `sync.cursor_expired` | 400 | Task sync |
  | `sync.task_too_large` | 413 | Task sync |
  | `sync.replica_busy` | 429 | Task sync |
  | `sync.replica_unavailable` | 503 | Task sync |

  Most of those statuses come from `Error.Type` through the outcome table under
  **Decision**. Five are decided by the code instead, and each one is a case that
  table has no row for:
  401 for a credential that did not match, however the handler classified it;
  403 for a correctly-signed cursor naming somebody else's owner — deliberately
  not the 404 this service answers every other "that is not yours" with, because
  a cursor is not a guess and can only have been obtained; and 413, 429, and 503
  for the three ways the replica refuses, which `Error.Type` cannot tell apart
  because it carries no classification for "come back later". Adding one would be
  a shared-kernel change for one host's convenience.
- **The centralized piece is there too.** `AddProblemDetails()` plus
  `UseExceptionHandler()` and `UseStatusCodePages()` mean an unhandled exception
  and a framework-generated `401` come back in the same shape a handler's `409`
  does, and the `traceId` extension is added by one customization so it is on all
  of them. What is left is narrower than "no middleware": no failure carries the
  optional `errors` extension keyed by field, because the edge checks refuse on
  the first thing they find rather than collecting.
- **The `status: proposed` marker above is stale**, and so is the sentence under
  it saying nothing in the code applies this decision. Promoting it to `active`
  is a decision rather than a correction, so it is left alone here and named as
  the thing to settle.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-26 | Recorded the OAuth 2.1 authorization server's endpoints as the exception: they answer RFC 6749 error bodies. | Local ADR 0058 §8 makes the exception, and this record still said every endpoint. |
| 2026-09-07 | Applied to every endpoint of `Backlog.Modules.Sync.Api`: codes declared in `SyncErrorCodes`, with central exception handling and status code pages. | The Cosmos task replica added the task sync endpoints and their error codes. |
| 2026-09-07 | Applied first through `Endpoints/SyncResults.cs`, with a `code` extension per failure. | Device pairing and short-lived device tokens needed a failure contract. |
| 2026-08-27 | Marked proposed locally. | The sync API returned bare status codes, so nothing applied it yet. |
| 2026-08-27 | Imported from the organization's ADR 0017, decided 2026-06-04. | The guidelines moved into the repository from the guidelines MCP server. |
