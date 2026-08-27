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

## Deviations and gaps

- **No module owns telemetry yet.** The organization's shape — an
  `Observability/` folder per module with its own `ActivitySource` and counters —
  does not exist here. Instrumentation is what the libraries provide plus
  nothing.
- No custom activities around handlers, and no business metrics.
- `IActivitySource` in `Backlog.Modules.Dashboard.Abstractions` is **not**
  OpenTelemetry: it is a domain port over sources of developer activity, such as
  GitHub. The name collision is unfortunate; do not wire one to the other.
