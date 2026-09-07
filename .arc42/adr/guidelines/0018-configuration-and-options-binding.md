# ADR 0018: Configuration and options binding

```meta
status: proposed
related: [".arc42/07-deployment-view.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0018 (decided 2026-06-04,
`guide/adrs/0018-configuration-and-options-binding.md`), imported 2026-08-27.

**Status: proposed.** The decision is accepted upstream and binds any work that
reaches this ground. No typed options class exists and nothing calls
`ValidateOnStart()`, so nothing in the code applies this decision yet.

## Decision

**Bind, validate, fail fast.**

- Configuration reaches application code as a **typed options class**, not as
  `IConfiguration["Some:Key"]` scattered through feature code.
- Options that matter at runtime are **validated at startup** — data annotations
  or a custom validator, with `ValidateOnStart()`. Invalid or missing critical
  configuration fails the start rather than surfacing later as a confusing
  runtime error.
- **Each module or technical capability owns its configuration section**, with a
  stable, explicit name — `Modules:Sync:…`, `Integrations:GitHub:…`.
- **Secrets never live in source control.** They come from environment variables,
  a secret manager, or `dotnet user-secrets` locally. Checked-in configuration
  files hold structure and non-secret defaults only.
- Inject `IOptions<T>` for static configuration, `IOptionsSnapshot<T>` for
  request-scoped refresh, `IOptionsMonitor<T>` for long-lived services that must
  observe change.

## How Backlog applies it

- Aspire supplies endpoints through **service discovery variables**, so no host
  reads or hardcodes another host's address — the strongest form of the rule
  (see [0003](0003-aspire-for-web-services.md)).
- Local runs use `aspire start --isolated`, which keeps user-secrets state per
  run rather than shared across sessions.
- No secret is checked in; external credentials stay on the user's machine
  because all capture runs locally (`.arc42/02-constraints.md`).

## Deviations and gaps

- **A first typed options class now exists**, and it applies the whole rule
  rather than part of it. `Options/SyncTokenOptions.cs` binds
  `Modules:Sync:Tokens` (`SigningKey`, `Issuer`, `Audience`,
  `LifetimeMinutes`), owned by `Backlog.Modules.Sync.Api` under a stable,
  explicit section name, and is validated at startup: a missing or too-short
  signing key fails the start rather than the first sync request. Development
  is the one carve-out — it mints and warns about an ephemeral key so no
  configuration is needed locally — and outside Development the secret is
  never checked in, per the rule above.
- This is also the first real exercise of the section-naming convention
  (`Modules:Sync:…`); no conflict has arisen because it is still the only
  section a module owns outside what Aspire injects.
