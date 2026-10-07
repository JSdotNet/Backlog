# ADR 0024: Claude Code's telemetry arrives as OTLP/HTTP logs on the in-app MCP listener

```meta
date: 2026-10-08
related: [".devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md", ".devbook/arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".devbook/arc42/adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md", ".devbook/arc42/adr/0010-backup-is-the-database-committed-to-a-repository.md", ".devbook/arc42/adr/guidelines/0010-opentelemetry-observability.md", ".devbook/domain/sessions/domain.md#claude-api-request-log", ".devbook/domain/sessions/features.md#cost-as-claude-code-reports-it"]
```

The desktop application receives Claude Code's OpenTelemetry events at `/v1/logs`, an
OTLP/HTTP logs endpoint on the loopback listener that already serves `/mcp`. It reads
both OTLP encodings, keeps only `claude_code.api_request` events, and stores each one
as a row of `backlog.db`, once per `request_id`.

## Status

```meta
```

Proposed, 2026-10-08, and built in the same change: the `cost-receiver` item of the
`pr-session-pages-redesign` plan, run unattended. It becomes accepted when the
repository owner approves that change at its Personal Validation gate, on its draft
pull request.

A **local** decision, numbered in the local sequence. Every bare ADR number below means
the local one. It extends ADR 0012 with a second protocol on the same listener, and
changes none of that record's seven decisions.

## Context

```meta
```

**The cost of a run is Claude Code's figure.** The Sessions chapter
[Cost as Claude Code reports it](../../domain/sessions/features.md#cost-as-claude-code-reports-it)
takes a run's cost from the `claude_code.api_request` events Claude Code sends through
OpenTelemetry. Each event carries Claude Code's own cost estimate and the request's
tokens, model, effort and duration, and names the session, the prompt, the sub-agent
and the skill it ran under. Nothing on this machine receives them yet.

**Claude Code exports, it does not report.** Telemetry leaves Claude Code through its
OTLP exporter, configured by `OTEL_*` environment variables: a protocol (`grpc`,
`http/protobuf` or `http/json`), an endpoint, and headers. A receiver is an OTLP
server; there is no hook or tool call that carries the same events.

**The app already has a loopback listener with a guard.** ADR 0012 put a Streamable
HTTP MCP endpoint in the desktop process: `127.0.0.1` and `::1` only, a configurable
port, an `Origin` check and a bearer token in front of every path on it. The hook
telemetry route `/telemetry` already sits beside `/mcp` behind that guard, for the
reason that it is the same trust boundary: a local session reporting on itself to the
application that serves its tools.

## Decision

```meta
```

### 1. The endpoint is `/v1/logs` on the MCP listener

The route is mapped on the listener ADR 0012 §1 built, on the same port, and behind the
same guard: the `Origin` check and the bearer token cover it because they cover every
path. The web harness maps it too, behind its `Origin` check, the way it maps `/mcp`.

`/v1/logs` because OTLP/HTTP fixes that path for the logs signal; an exporter given
only the listener's root finds it. The listener binds loopback only, so the endpoint
is reachable from this machine and nowhere else.

### 2. Logs only, both HTTP encodings, no gRPC

The endpoint reads `application/json` and `application/x-protobuf`, with or without
`gzip`, and answers `200` with an empty `ExportLogsServiceResponse` in the encoding it
was sent. A body it cannot read is `400`, an encoding it does not take is `415`, a body
over 8 MiB is `413`. A batch the database could not take is `503` with a `Retry-After`:
OTLP/HTTP retries a `503` and not a `500`, and storing is idempotent by `request_id`
(§4), so the retry is safe and the batch is not lost.

Metrics and traces are not received. The events are a logs signal, and the logs signal
is all a run's cost needs. gRPC is not received either, because the listener is plain
HTTP/1.1 on Kestrel and a second protocol stack would serve no event the HTTP
encodings do not.

The reader is written by hand, in `ClaudeCodeOtlpLogs`. The OpenTelemetry .NET SDK
exports and does not receive, and its protobuf types are internal. The messages a
receiver needs are five, and every field it does not use is skipped by wire type, so a
newer exporter's fields do not break it.

### 3. Only `claude_code.api_request` is kept

Every other event — prompts, tool results, hook runs, plugin loads, errors — is dropped
on arrival. An event is an api_request when its body, its `event_name` or its
`event.name` attribute says so.

Of each, these are stored: `session.id`, `event.timestamp`, `model`, `effort`,
`cost_usd_micros`, the input, output, cache-read and cache-creation tokens,
`duration_ms`, `query_source`, `agent.name`, `skill.name`, `prompt.id` and
`request_id`. The identity attributes Claude Code adds to every event — the account,
the organization, the e-mail address — are not stored. Neither is anything a prompt or
a tool said.

`cost_usd_micros` is taken as sent; where only `cost_usd` arrives it is scaled to whole
micro-dollars, so a sum over many requests is exact.

### 4. One row per request in `backlog.db`, idempotent by `request_id`

The events go into the `claude_api_requests` table of the app's local database, the
same `backlog.db` the tasks live in, through the Sessions context's
`IClaudeApiRequestStore`. `request_id` is the primary key and the insert ignores a
duplicate, because an OTLP exporter retries a batch it did not see acknowledged and the
first copy is the one kept. An api_request without a `request_id` is dropped: it could
not be told from its own retry.

The table is created by idempotent DDL on every open, as ADR 0003 and ADR 0006 have the
other tables created. Nothing replicates it.

### 5. Settings shows what to paste

A **Claude Code** page in Settings, offered behind the Sessions switch, shows the
endpoint URL and a `settings.json` `env` block that sends the logs there. The block
uses the logs signal's own variables — `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT`,
`OTEL_EXPORTER_OTLP_LOGS_PROTOCOL`, `OTEL_EXPORTER_OTLP_LOGS_HEADERS` — so the bearer
token goes to this endpoint and to no other exporter a person has configured.

## Consequences

```meta
```

- A Claude Code session started with the block in its settings reports every model
  request to the app while the app runs. Requests made while the app is closed are lost:
  the exporter does not keep what it could not send.
- The token in the block is the MCP server's. Rotating it means pasting the block again,
  as it already means re-registering the MCP server.
- `backlog.db` gains a table that grows with use, roughly one row per model request.
  Because ADR 0010 backs up the database whole, the rows travel into the backup
  repository with the tasks.
- The cost and stage views the Sessions chapters describe can now be built on stored
  figures. This decision builds the receiving end only.

## Alternatives considered

```meta
```

- **A port of its own for telemetry.** A second listener would be a second port to
  configure and to collide, and a second guard to keep equal to the first, for the same
  local caller.
- **Reading cost from the transcripts.** The transcripts carry tokens, not Claude
  Code's price; a cost worked out from them is the second guess the Sessions chapter
  refuses.
- **A collector in front of the app.** An OpenTelemetry Collector would receive every
  encoding and forward to the app, but it is a process a person has to install and run
  for one event kind.
- **Keeping every event.** The other events carry prompts, tool output and file paths.
  Storing them would make `backlog.db` hold what a session said, which nothing in the
  product reads.
