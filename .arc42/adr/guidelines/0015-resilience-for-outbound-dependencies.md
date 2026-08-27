# ADR 0015: Resilience for outbound dependencies

```meta
status: active
related: [".arc42/06-runtime-view.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0015 (decided 2026-06-04,
`guide/adrs/0015-resilience-strategy-for-outbound-dependencies.md`), imported
2026-08-27.

## Decision

Resilience is applied **at the adapter boundary only** — where the application
crosses a technical boundary. Domain models and handlers contain no retry loops.

**Timeouts are mandatory** for every outbound network call. Default or infinite
timeout behavior is not allowed.

**Retries are opt-in and only for transient failures** — network blips, HTTP 408,
429, and retryable 5xx. Never retry a validation failure, an authentication or
authorization failure, a known business-rule failure, or a non-idempotent command
without idempotency protection.

**Circuit breakers** protect dependencies whose repeated failure would exhaust
resources or cascade latency. Their state changes are observable in logs and
metrics.

**Backoff is exponential with jitter**, retry counts stay low, and retry policies
are never nested across call layers.

**Fallbacks are rare and explicit.** A fallback never masquerades as a successful
primary-path result.

## How Backlog applies it

- `AddServiceDefaults()` calls `ConfigureHttpClientDefaults(http =>
  http.AddStandardResilienceHandler())`, so every `HttpClient` resolved from DI
  gets Polly's standard pipeline — timeout, retry with jittered backoff, and
  circuit breaker — without per-adapter wiring.
- The adapters registered through `AddHttpClient` therefore inherit it:
  `AzureFoundryChatClient`, `ClaudeAdminTransport`, and the mobile
  `CloudSyncClient`.
- `CopilotCliLauncher` is not an HTTP adapter — it launches a CLI process. The
  resilience pipeline does not reach it; process timeouts and cancellation are
  its equivalent obligation.
- Because capture runs locally, most of these calls happen on the user's machine,
  where a hung request is a hung UI. The timeout rule is a UX requirement here,
  not only an operational one.

## Deviations and gaps

- **Two clients live outside DI and therefore outside the pipeline.**
  `TokenTransport` in `Backlog.Infrastructure.GitHub` falls back to
  `new HttpClient()` when none is injected, with no explicit timeout at all;
  `DevToolService.Marketplace` is a static client that at least sets a 20-second
  timeout. Resolving both from `IHttpClientFactory` closes the hole.
- No adapter sets a purposeful per-dependency timeout; all of them inherit the
  standard handler's defaults.
- Circuit-breaker state changes are not surfaced as metrics.
