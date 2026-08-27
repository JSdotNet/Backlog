# ADR 0012: Authentication with external identity providers

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md#authentication-and-authorization", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0012 (decided 2026-06-04,
`guide/adrs/0012-authentication-external-identity-providers.md`), imported
2026-08-27.

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
  requires no login (`.arc42/02-constraints.md`).
- Two places where this decision binds:
  - **GitHub OAuth**, for issue sync and webhook registration. Credentials stay
    on the user's machine, since all capture runs locally.
  - **Cloud connection**, which uses device-based auth — JWT device sessions
    rather than a user identity.
- The token-validation rules apply to the device-session JWT as written: partial
  validation is not an option just because the issuer is our own.

## Deviations and gaps

- **No external IdP is integrated.** There is no Google, Entra ID, or Facebook
  sign-in and none is planned while the product stays single-user.
- Device-session auth is not an OIDC flow, and is not required to be: it
  authenticates a machine to the sync service, not a person to the product. If a
  user identity is ever introduced, this decision governs it and OIDC is the
  route.
- The refresh-token and distributed-cache rules presuppose a multi-user cloud
  tier that does not exist here.
