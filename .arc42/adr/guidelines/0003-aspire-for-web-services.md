# ADR 0003: .NET Aspire for orchestration

```meta
status: active
related: [".arc42/07-deployment-view.md", ".tech/tooling.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0003 (decided 2025-11-10,
`guide/adrs/0003-recommend-aspire-for-aspnet-projects.md`), imported 2026-08-27.

## Decision

Web-facing and service-boundary projects are orchestrated with .NET Aspire. The
minimum adoption set:

1. An **AppHost** project that orchestrates every dependent resource, and a
   **ServiceDefaults** project applied by all of them.
2. Telemetry — logs, metrics, traces — through Aspire's OpenTelemetry wiring
   (see [0010](0010-opentelemetry-observability.md)).
3. Health checks per service and its dependencies, visible locally.
4. Resilience defaults on outbound calls (see
   [0015](0015-resilience-for-outbound-dependencies.md)).
5. **Service discovery variables instead of hard-coded endpoints.**

Every host calls `builder.AddServiceDefaults()` before building. Aspire projects
live under `src/Aspire/`. Secrets go to user secrets, environment variables, or a
vault — never into an Aspire project.

## How Backlog applies it

- `src/Aspire/Backlog.Aspire.AppHost` orchestrates the whole product locally;
  `src/Aspire/Backlog.Aspire.ServiceDefaults` carries the shared wiring.
- `Backlog.Modules.Sync.Api` calls `builder.AddServiceDefaults()` as its first
  statement after `CreateBuilder`.
- ServiceDefaults is not web-only here: it registers an `IMauiInitializeService`
  so the MAUI desktop and mobile heads get the same telemetry wiring as a service.
- The `desktop`, `mobile-android`, `ide-vscode-build`, and `ide-vscode-host`
  resources use `WithExplicitStart()`. Seeing them `NotStarted` is expected.
- **Every host binds `localhost:0`.** Ports are assigned per run; read them from
  the dashboard or AppHost output. Never hard-code one, and use `--isolated` for
  worktree or parallel sessions.

## Deviations and gaps

- There is no `Backlog.Aspire.Extensions` project. The recommended
  strongly-typed resource factories earn their keep once infrastructure
  resources are shared across modules; today the AppHost wires project
  resources directly.
- Health check endpoints are not yet exposed per service.
