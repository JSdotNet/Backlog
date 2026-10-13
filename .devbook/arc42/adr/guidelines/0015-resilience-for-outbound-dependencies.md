# ADR 0015: Resilience for outbound dependencies

```meta
related: [".devbook/arc42/06-runtime-view.md", ".devbook/arc42/09-architecture-decisions.md", ".devbook/tech/shared.md#microsoftextensionshttpresilience"]
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
  `ClaudeAdminTransport`, the mobile `CloudSyncClient`, and the sync service's
  `sync-token` client, `DevicePairingClient`, and `InboxAttachmentSyncSource`.
  `AzureFoundryChatClient` and the three replication clients used to inherit
  it, and now carry their own pipeline. See below.
- The replication clients are `TaskSyncClient`, `SessionSyncClient`, and
  `AnnotationSyncClient`. `SyncClientRegistration.WithReplicationPipeline`
  removes the default pipeline from each and adds the replication pipeline.
  That pipeline never retries an unsafe method, so a push goes out once and
  only a pull retries. One attempt gets 100 seconds and one call 5 minutes in
  total. Each client's `HttpClient.Timeout` is 5 minutes 30 seconds, so the
  token fetch in front of the pipeline is bounded too.
- `CopilotCliLauncher` is not an HTTP adapter — it launches a CLI process. The
  resilience pipeline does not reach it; process timeouts and cancellation are
  its equivalent obligation.
- Because capture runs locally, most of these calls happen on the user's machine,
  where a hung request is a hung UI. The timeout rule is a UX requirement here,
  not only an operational one.

## Deviations and gaps

- **One client lives outside DI and therefore outside the pipeline.**
  `DevToolService.Marketplace` is a static client with a 20-second timeout;
  resolving it from `IHttpClientFactory` closes the hole. The hosts compose
  `TokenTransport` with the named `GitHub` client from `GitHubHttpRegistration`.
  Its pipeline retries reads only and allows 100 s per attempt and 5 minutes in
  total. Since 2026-10 that client also carries every GitHub call that names no
  account, sent with the token the `gh` CLI holds. When GitHub answers that token
  with a 401, the transport does not resend the call.
- **The Cosmos client is outside the pipeline by construction, and says so.**
  `CosmosClient` owns its own transport and never resolves through
  `IHttpClientFactory`, so `AddStandardResilienceHandler()` cannot reach it —
  this is not an adapter that was missed, it is a dependency the mechanism does
  not cover. Its resilience is the SDK's own options, stated in the one place the
  client is built (`Backlog.Infrastructure.Cosmos`,
  `Extensions/CosmosReplicaRegistration.cs`): a 10-second `RequestTimeout`,
  nine retries on rate-limited requests within ten seconds in total, and
  Gateway connection mode. Nine is the SDK's own default. A push writes eight
  documents of one owner at once, all on one logical partition, so a large
  push can meet the partition's burst limit. A rate limit that outlives these
  retries fails the whole push and costs the device a sync cycle. The
  mandatory-timeout rule is met; what is
  not is the circuit breaker, which the SDK has no equivalent for, and the retry
  is the SDK's 429-only policy rather than the standard pipeline's transient set.
- **Six HTTP clients now set a purposeful per-dependency pipeline; the rest
  still inherit the standard handler's defaults.** The named `capture-feeds`
  client in `Backlog.Infrastructure.Capture` (`FeedFetcher`) configures its own
  — 1 retry, a 10 s attempt timeout, 15 s total — rather than the standard
  handler's, and the response body read is bounded by the same clock so a slow
  body cannot outlast the timeout that stopped the headers. The reason is where
  the call runs: capture is triggered by a button press on the user's own
  machine, so a hung request is a hung UI, not a background retry nobody is
  watching. The `azure-foundry-chat` client
  (`Backlog.Infrastructure.AzureFoundry`, `AzureFoundryRegistration`) goes the
  other way for the opposite reason: a chat completion regularly takes longer
  than the standard 10 s attempt, and every retry re-sends and re-pays the
  prompt, so one answer gets the whole two-minute budget — attempt and total
  are equal, so a slow answer is waited for once rather than retried into the
  budget, while a fast 429 or 503 is still retried inside it. Under the
  defaults the 30 s total timeout surfaced as Polly's
  `TimeoutRejectedException` in the Home page's Ask handler and took the page
  down; the client now translates every transport failure into its own
  `AzureFoundryException` at the adapter boundary, which is the other half of
  the same fix. The named `GitHub` client (`Backlog.Infrastructure.GitHub`,
  `GitHubHttpRegistration`) removes the host's default pipeline and puts its own
  on. It carries writes that are not safe to send twice: an issue `POST`, a ref
  `POST`, and the backup's database `PUT`. Its pipeline therefore retries reads
  only and sends every unsafe method once. The backup `PUT` and a branch snapshot
  download run to megabytes, so each attempt gets 100 s and a call 5 minutes in
  total.
  The three replication clients in `Backlog.Infrastructure.Sync`
  change the defaults because a push is not idempotent. The standard pipeline
  re-sends a push whose attempt ran past 10 seconds or answered 503, and
  neither says the batch did not land. A replica that already holds every
  item refuses each one as not later. The session then reports as refused
  what the service kept. The longer attempt fits a full batch against a cold
  replica, where 10 seconds did not.
- Circuit-breaker state changes are not surfaced as metrics.
