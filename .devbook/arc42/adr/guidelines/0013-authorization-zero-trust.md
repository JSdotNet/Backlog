# ADR 0013: Authorization and the Zero Trust model

```meta
date: 2026-11-25
related: [".devbook/arc42/08-crosscutting-concepts.md#authentication-and-authorization", ".devbook/arc42/09-architecture-decisions.md", ".devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md", ".devbook/arc42/adr/0059-tokenless-mcp-calls-get-the-scopes-the-person-opens-to-them.md"]
```

Inherited from the organization's ADR 0013 (decided 2026-06-04,
`guide/adrs/0013-authorization-zero-trust.md`), imported 2026-08-27.

**Status: applied for the MCP surface, proposed elsewhere.** The decision is
accepted upstream and binds any work that reaches this ground. Local ADR 0058
(2026-11-11) applies it to the MCP surface, the desktop listener and the remote
`/mcp` endpoint, and to device and owner identity on the sync service. That
application is built, from 2026-11-11 to 2026-11-24 (PR #1878 and #1971), and local
ADR 0059 amends it. Module-to-module calls stay as below.

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
- The **MCP surface** binds too, by local ADR 0058. Each agent is a principal of
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
- **The MCP listener keeps the only audit store.** It records every admitted call
  and every refusal, per installation, in `mcp-audit.db`, a SQLite file kept for 30
  days. The sync service logs refused device tokens as warnings, and its `/mcp`
  logs its own refusals, but it keeps no audit store. Module-to-module calls stay
  unaudited.
- **The MCP listener takes grant tokens** (local ADR 0058 §3), and a call with no
  `Authorization` header gets the Unauthenticated row's scopes. That row is empty by
  default and widened only with Windows Hello (local ADR 0059). The shared token is
  retired. The listener accepts `BACKLOG_MCP_TOKEN` only under a debug setting that is
  off by default, and that caller holds neither `telemetry:write` nor `agents:grant`.
- Module-to-module calls do not carry a `ClaimsPrincipal`, since no in-process
  operation is security-relevant today.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | Status: local ADR 0058's application to the MCP surface and to device and owner identity is built (PR #1878, #1971), and local ADR 0059 amends it. The MCP listener keeps the only audit store, and a tokenless call gets the Unauthenticated row's scopes. | Plan `zero-trust-auth` landed on main, and PR #1992 brought this record level with it. |
| 2026-11-12 | The MCP listener takes grant tokens only. The shared token is retired, and `BACKLOG_MCP_TOKEN` is accepted only under a debug setting that is off by default. | Plan `zero-trust-auth` Phase 1, step `remove-settings-token` (PR #1828). |
| 2026-11-11 | Applied for the MCP surface, proposed elsewhere: local ADR 0058 (then numbered 0052) applies it to the desktop listener, the remote `/mcp` endpoint, and device and owner identity on the sync service. | The always-on listener's one plaintext token opened every tool (PR #1753). |
| 2026-09-07 | A fallback-deny policy and an owner-scoped, resource-based check exist for the sync service. | Device pairing and short-lived device tokens built the first surface with anything to protect (PR #413). |
| 2026-08-27 | Imported from the organization's ADR 0013 and marked proposed for Backlog. | No authorization policy, resource check or audit existed, and the baseline was single-user (`715dbe0b2`, `dfc9b0023`). |
| 2026-06-04 | Decided by the organization: verify explicitly, least privilege, assume breach (NIST SP 800-207), with named policies under fallback-deny, resource-based checks, no implicit trust between modules, and audit. | The organization's guideline, as this record's opening line dates it. |
