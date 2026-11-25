# ADR 0065: Sync identity stays GitHub sign-in and device keys, and every anonymous sync route gets a rate limit

```meta
date: 2026-11-25
related: [".devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md", ".devbook/arc42/adr/guidelines/0012-authentication-external-identity-providers.md", ".devbook/arc42/adr/guidelines/0013-authorization-zero-trust.md", ".devbook/arc42/08-crosscutting-concepts.md#authentication-and-authorization", ".devbook/arc42/09-architecture-decisions.md"]
c4: [backlog.cloud]
c4-touches: [backlog.desktop, backlog.mobile, github]
```

The sync service keeps the identity model local ADR 0058 built: one owner, proven by
GitHub sign-in at enrolment and recovery, and devices proven by hardware-bound keys.
No external OIDC provider is added. The rate-limiting part of org ADR 0008 is adopted:
every anonymous route of the sync service gets a policy, not only the connector and
enrolment routes.

## Status

```meta
```

Proposed, 2026-11-25. Plan `app-defaults-rollout`, entry 37 (`backlog-deviation-adrs`).

A **local** decision. It records one deliberate difference from, and one move to,
[org ADR 0008: Security](https://github.com/JSdotNet/app-defaults/blob/main/defaults/arc42/adr/0008-security.md).
It names org ADR 0008 for the choice local ADR 0058 already made, and changes nothing
in 0058.

## Context

```meta
```

Org ADR 0008 has users sign in at an external OIDC provider, and APIs validate the
provider's JWTs in full. Backlog has one human user. Local ADR 0058 settled how that
user and that user's devices are proven:

| Principal | Proven by (local ADR 0058) | Org ADR 0008 default |
| --- | --- | --- |
| The owner | GitHub OAuth, matched by numeric user id, at enrolment and recovery only | Sign-in at an OIDC provider |
| A device | A non-exportable TPM or Keystore key, exchanged for a 30-minute JWT the service issues and validates in full | Not covered: devices are not users |
| A remote agent | OAuth 2.1 on the sync service, one static client, PKCE | The provider is the authorization server |

GitHub *is* the external identity provider here. The org ADR's other authorization
rules already hold: a fallback-deny policy, named scope policies, an owner-scope filter
per record, and an audit entry per call.

Rate limiting does not hold. Org ADR 0008 puts a policy on every public or anonymous
endpoint. The sync service has two policies: one on the connector's authorization and
`/mcp` routes, one on owner enrolment. Three anonymous device routes have none:

| Route (`DeviceEndpoints`) | Why it matters |
| --- | --- |
| Register a device | Spends an enrolment code |
| Redeem a pairing code | A short single-use code that a caller could try to guess |
| Exchange a device assertion for a token | The first call of every sync, and a store read per call |

The root status route (`/`) is anonymous too.

## Decision

```meta
```

1. **Keep.** No external OIDC provider and no `Microsoft.Identity.Web`. The owner signs
   in with GitHub, and devices and agents authenticate as local ADR 0058 says.
2. **Move.** Every anonymous route of the sync service carries an ASP.NET Core
   rate-limiting policy, partitioned per client address, and a rejected call answers
   429 Problem Details. A route made anonymous later carries one too, and an
   architecture test fails on an `.AllowAnonymous()` route without
   `.RequireRateLimiting(...)`.

## Follow-up

```meta
```

| Work | Size | Entry |
| --- | --- | --- |
| Add a per-address policy to the register, pairing-code and token routes and to `/`; answer 429 Problem Details; add the architecture test | S | `+app-defaults-rollout`: *Rate-limit every anonymous sync route* |

## Consequences

```meta
```

- A burst of failed pairing-code guesses from one address is slowed at the edge, not
  only by the code's single use and expiry.
- A phone behind carrier NAT shares an address with others. The token-route limit has
  to allow a normal sync cadence for several devices at one address.
- An app-defaults review reads this record for the 0008 row.

## Rejected

```meta
```

- **Move to an external OIDC provider such as Entra ID (L).** A tenant, app
  registrations and token validation would be added for one person who already signs
  in with GitHub. Device keys would stay anyway, because org ADR 0008 does not cover
  devices.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | Proposed: keep GitHub sign-in and device keys (local ADR 0058); rate-limit every anonymous sync route. | Step 15 of plan `app-defaults-rollout` (PR #1967) listed the org ADR 0008 difference as L with no local decision. |
