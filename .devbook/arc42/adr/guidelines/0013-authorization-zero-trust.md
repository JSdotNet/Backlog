# ADR 0013: Authorization and the Zero Trust model

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#authentication-and-authorization", ".devbook/arc42/09-architecture-decisions.md", ".devbook/arc42/adr/0052-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md"]
```

Inherited from the organization's ADR 0013 (decided 2026-06-04,
`guide/adrs/0013-authorization-zero-trust.md`), imported 2026-08-27.

**Status: applied for the MCP surface, proposed elsewhere.** The decision is
accepted upstream and binds any work that reaches this ground. Local ADR 0052
(2026-11-11) applies it to the MCP surface, the desktop listener and the remote
`/mcp` endpoint, and to device and owner identity on the sync service. That
application is decided and not yet built. Module-to-module calls stay as below.

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
- The **MCP surface** binds too, by local ADR 0052. Each agent is a principal of
  its own: a grant whose 15-minute token carries scopes, checked by named policies
  under fallback-deny, with every call audited. Windows Hello confirms the actions
  that are hard to undo, and no agent can approve or accept a devbook decision.
- **Owner and device identity** follow the same ADR: one GitHub account, matched by
  its numeric user id, and a hardware-bound key per device.
- **Module boundaries are already treated as real boundaries** in code — a module
  is reached through its `.Abstractions` surface, enforced by
  `tests/Backlog.ArchitectureTests/ModuleBoundaryTests.cs` — though today that is
  a structural boundary, not an authorization one.
- The desktop runs as the person using the machine, over data on that machine.
  There is no privilege to escalate locally.

## Deviations and gaps

- **A fallback-deny policy and a resource-based, owner-scoped check now exist
  for the sync service.** `Backlog.Modules.Sync.Api` requires bearer
  authentication by default, and its `OwnerScopeFilter` reads the owner out of
  the presented token and refuses to serve a query outside it — the
  resource-based authorization this decision asks for, applied to the one
  surface (`sync`) that has anything to protect. It is single-owner
  authorization, not the multi-tenant, role-based policy set this decision
  otherwise describes, and it does not reach any other module.
- **No audit log yet.** Sensitive-operation auditing is not implemented anywhere,
  local or cloud. Local ADR 0052 decides one for every MCP call, in its first
  delivery phase.
- **The MCP listener still takes one shared token** until local ADR 0052's grants
  are built.
- Module-to-module calls do not carry a `ClaimsPrincipal`, since no in-process
  operation is security-relevant today.
