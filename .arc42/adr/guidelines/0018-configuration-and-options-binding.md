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

- **No typed options classes exist.** Nothing in `src/` binds an options type or
  calls `ValidateOnStart()`. Nothing needs to yet — there is no configured
  external dependency beyond what Aspire injects — but the first one that arrives
  should arrive as an options class, not as a configuration lookup.
- No configuration section naming is in use, so the ownership rule is untested.
