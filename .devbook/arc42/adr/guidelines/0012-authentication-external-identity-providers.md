# ADR 0012: Authentication with external identity providers

```meta
related: [".devbook/arc42/08-crosscutting-concepts.md#authentication-and-authorization", ".devbook/arc42/09-architecture-decisions.md", ".devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md", ".devbook/domain/sync/domain.md#device-assertion"]
```

Inherited from the organization's ADR 0012 (decided 2026-06-04,
`guide/adrs/0012-authentication-external-identity-providers.md`), imported
2026-08-27.

**Status: proposed.** The decision is accepted upstream and binds any work that
reaches this ground. GitHub is the one external identity provider, and only
the owner signs in with it, to enrol the first device and to recover (local
ADR 0058 §1). The device-token validation below applies its token rules.

## Decision

**OpenID Connect over OAuth 2.0** is the protocol for every external identity
provider integration. Plain OAuth 2.0 without OIDC is not permitted — it asserts
authorization, not identity.

- Use `Microsoft.Identity.Web` for Entra ID,
  `Microsoft.AspNetCore.Authentication.OpenIdConnect` for other OIDC providers,
  and `…Authentication.JwtBearer` for API-tier token validation.
- **API authentication is stateless**: a JWT is validated on every request —
  issuer, audience, lifetime, and signing key, all of them — with no server-side
  session.
- Authentication configuration lives in **one dedicated security layer** with an
  `AddApplicationAuthentication()`-style extension method. It is never inline in
  a feature module.
- Access tokens are short-lived (15–60 minutes). Refresh tokens are held
  server-side, rotated on use, and never in `localStorage`.
- Public clients use the authorization code flow with **PKCE**.

## How Backlog applies it

- The product is **personal and account-free by constraint**: standalone mode
  requires no login (`.devbook/arc42/02-constraints.md`).
- Two places where this decision binds:
  - **GitHub OAuth**, for issue sync and webhook registration. Credentials stay
    on the user's machine, since all capture runs locally.
  - **Cloud connection**, which authenticates the device by a JWT device
    session. The owner signs in only at enrolment and recovery.
- The token-validation rules apply to the device-session JWT as written: partial
  validation is not an option just because the issuer is our own.

## Deviations and gaps

- **GitHub is the only external IdP, for the owner alone.** The sync service
  matches the owner by the numeric id GitHub's user API answers (local ADR 0058
  §1). There is no Google, Entra ID, or Facebook sign-in, and none is planned
  while the product stays single-user.
- Device-session auth is not an OIDC flow, and is not required to be: it
  authenticates a machine to the sync service, not a person to the product. If a
  user identity is ever introduced, this decision governs it and OIDC is the
  route.
- **Device-session JWT validation now exists**, in
  `Backlog.Modules.Sync.Api` (`Security/SyncAuthenticationExtensions.cs`).
  Issuer, audience, lifetime, and signature are all validated per request with
  no server-side session, and a mismatched signing algorithm is rejected rather
  than accepted — the token-validation rule above is no longer aspirational for
  this one surface. The token is short-lived (30 minutes, within the 15–60
  minute band this decision names). A device obtains it by signing a
  `private_key_jwt` assertion (RFC 7523) with its non-exportable key (local ADR
  0058 §2). Only a device on a host with no key store, the mobile web harness
  today, exchanges a registration credential instead. There is no refresh token,
  because a fresh assertion plays that role.
- The refresh-token and distributed-cache rules presuppose a multi-user cloud
  tier that does not exist here.
