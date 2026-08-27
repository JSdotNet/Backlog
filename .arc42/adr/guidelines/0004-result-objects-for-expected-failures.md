# ADR 0004: Result objects for expected failures

```meta
status: active
related: [".arc42/05-building-block-view.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0004 (decided 2026-06-01,
`guide/adrs/0004-standardize-result-objects-for-expected-failures.md`), imported
2026-08-27.

## Decision

`Result` / `Result<T>` is the contract for **expected** outcomes at application
boundaries. Exceptions are for the genuinely unexpected.

1. Application handlers and use cases return `Result` / `Result<T>` for expected
   business outcomes.
2. Domain models keep enforcing their own invariants, and may raise
   domain-specific exceptions internally to do so.
3. The application layer translates those into a failed `Result` before the
   outcome crosses a boundary.
4. Delivery adapters — UI, HTTP, messaging — map `Result` states onto their own
   transport (see [0017](0017-http-error-contract-and-problem-details.md) for
   HTTP).
5. Unexpected technical faults — I/O failure, infrastructure outage,
   serialization fault — stay exception-driven.

A result carries: success, an optional value, a machine-readable code, a
human-readable message, and optionally validation detail.

## How Backlog applies it

- `src/Core/Backlog.SharedKernel/Results/` holds `Result`, `Result<T>`, and
  `Error`. Module handlers return them; the desktop and mobile hosts branch on
  them.
- `Error` is a `readonly record struct (Code, Message, ErrorType)` — a stable
  code such as `entry.not_found`, a message safe to show the person using the
  app, and a type a host maps to UI state or an HTTP status.
- `Result` refuses to be constructed inconsistently: a success carrying an error,
  or a failure carrying `Error.None`, throws.

## Deviations and gaps

- The shape is richer than the organization's sketch — one `Error` value object
  rather than loose `ErrorCode` / `ErrorMessage` strings, plus an `ErrorType`
  classification. Same contract, better typed.
- The sync service does not use `Result` yet: its endpoints return
  `Results.Ok` / `NotFound` straight from an in-memory store. The rule applies
  the moment it grows real handlers.
