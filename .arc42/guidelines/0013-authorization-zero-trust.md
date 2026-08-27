# ADR 0013: Authorization and the Zero Trust model

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md#authentication-and-authorization", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0013 (decided 2026-06-04,
`guide/adrs/0013-authorization-zero-trust.md`), imported 2026-08-27.

## Decision

Three principles, from NIST SP 800-207:

**Verify explicitly.** Every request to a protected resource is authenticated and
authorized regardless of origin. There is no trusted network zone and no
"skip auth for internal calls" escape hatch.

**Least privilege.** Request the minimum scopes. Express authorization as
**named policies over claims**, registered centrally — role strings scattered
through feature code as `[Authorize(Roles = "Admin")]` are prohibited. Feature
code references a policy name constant, never the policy logic. A fallback policy
denies anything not explicitly opened with `AllowAnonymous`.

**Assume breach.** Log every access attempt, allowed or denied. Keep access
tokens short-lived. Treat module boundaries as trust boundaries.

**Resource-based authorization** — `IAuthorizationService` with resource handlers
— is required wherever ownership decides access, so a record cannot be reached by
guessing an id.

**No implicit trust between modules.** A call from one module to another is not
pre-authorized by being in-process: a security-relevant contract carries the
originating `ClaimsPrincipal`. Background workers use their own narrow service
identity, not an ambient elevated one.

**Audit** every write to an aggregate, every authorization failure, every
administrative operation, and every authentication event, with timestamp, actor,
action, resource type and id, outcome, and correlation id.

## How Backlog applies it

- The **sync service** is where this binds: device sessions are the principal,
  and a device may only reach its own sync state.
- **Module boundaries are already treated as real boundaries** in code — a module
  is reached through its `.Abstractions` surface, enforced by
  `tests/Backlog.ArchitectureTests/ModuleBoundaryTests.cs` — though today that is
  a structural boundary, not an authorization one.
- The desktop runs as the person using the machine, over data on that machine.
  There is no privilege to escalate locally.

## Deviations and gaps

- **No authorization policies exist yet**, because there is nothing multi-tenant
  to protect: the architecture baseline is explicitly single-user
  (`.arc42/02-constraints.md`). The rules land the moment the cloud tier serves
  more than one person's devices.
- **No audit log.** Sensitive-operation auditing is not implemented anywhere,
  local or cloud.
- Module-to-module calls do not carry a `ClaimsPrincipal`, since no in-process
  operation is security-relevant today.
