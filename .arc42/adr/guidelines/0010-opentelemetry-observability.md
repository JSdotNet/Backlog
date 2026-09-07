# ADR 0010: OpenTelemetry for observability

```meta
status: active
related: [".arc42/08-crosscutting-concepts.md#observability", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0010 (decided 2025-11-26,
`guide/adrs/0010-adopt-opentelemetry-for-observability.md`), imported 2026-08-27.

## Decision

OpenTelemetry is the observability framework — one vendor-neutral standard for
traces, metrics, and logs, rather than a different library per pillar.

**Instrument** HTTP in and out, database calls, and application-level operations.
The first two come from the standard instrumentation packages; the third is
written by hand.

**Create an activity** for a command or query handler, an operation with several
internal steps, a background job, or a call the libraries do not already cover.
**Do not** create one for trivial getters, already-instrumented operations, inner
loops, or pure functions.

**Emit metrics** for business KPIs (counters), durations (histograms), and
point-in-time values such as queue depth (gauges).

**Follow the semantic conventions** — `http.*`, `db.*`, `messaging.*` — and give
custom attributes a domain prefix.

**Correlate logs with traces**: log structurally, and carry `TraceId` / `SpanId`
so a log line can be found from a span and back.

Exporter endpoints come from environment variables, never from code.

## How Backlog applies it

- `src/Aspire/Backlog.Aspire.ServiceDefaults/Extensions.cs` configures
  OpenTelemetry once — logging with formatted messages and scopes, metrics and
  traces with HTTP client and runtime instrumentation. Export is over OTLP when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set, which the AppHost supplies.
- Every host that calls `AddServiceDefaults()` inherits it, **including the MAUI
  desktop and mobile heads**: ServiceDefaults registers an
  `IMauiInitializeService` so a local app is instrumented the same way a service
  is.
- Locally the Aspire dashboard is the trace and log surface; the Aspire MCP
  server reads the same signals during QA.
- **The Sync module owns its own signal**, in the organization's shape:
  `src/Modules/Sync/Backlog.Modules.Sync/Observability/SyncTelemetry.cs` declares
  one `ActivitySource` and one `Meter`, both named `Backlog.Modules.Sync` — for
  the module rather than for the host, so the signal reads the same whichever
  process the handlers end up in — and `Backlog.Modules.Sync.Api` opts in with
  `AddSource` / `AddMeter`. Custom attributes carry the `backlog.sync.` prefix
  the rule asks for. The owner and device ids go on spans only and never on a
  metric: a tag whose cardinality is one per person is a bill rather than a
  dimension.
- **Database calls arrive through the client integration**, for the one store
  that has one. `Backlog.Infrastructure.Cosmos` registers its client with
  `AddAzureCosmosClient`, and the Aspire integration wires that client's
  OpenTelemetry up with it rather than the adapter instrumenting anything by
  hand. The local SQLite store is reached through `Microsoft.Data.Sqlite`
  directly and has no equivalent.

## Deviations and gaps

- **Only one module owns telemetry.** Sync has its `Observability/` folder; no
  other module does, and instrumentation everywhere else is what the libraries
  provide plus nothing.
- Custom activities and metrics exist for the two task-sync operations only —
  `sync.push_tasks`, `sync.pull_tasks`, and the `backlog.sync.cursor_rejected`
  counter tagged by the reason a cursor was refused. No handler outside the Sync
  module starts an activity, and no business KPI is counted anywhere.
- `IActivitySource` in `Backlog.Modules.Dashboard.Abstractions` is **not**
  OpenTelemetry: it is a domain port over sources of developer activity, such as
  GitHub. The name collision is unfortunate; do not wire one to the other.
