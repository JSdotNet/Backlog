# 09. Architecture Decisions

```meta
status: active
```

This chapter links to the authoritative Architecture Decision Records rather than
restating them. ADRs are maintained in the organization's guidance corpus
(`jsdotnet-project-guidelines`) and govern the **cloud service** (.NET) part of
Prompt Backlog. The desktop, mobile, and IDE channels use their own platform stacks
and are intentionally out of scope for the .NET ADRs.

## Cloud-service ADRs (applicable)

```meta
status: active
related: [".arc42/05-building-block-view.md#cloud-service", ".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

The following ADRs apply to the ASP.NET Core cloud sync layer. Consult the ADR text
for full context and consequences before implementing.

| ADR | Relevance to the cloud service |
|---|---|
| **0001 — Adopt .NET 10** | Target framework for the cloud service. |
| **0003 — .NET Aspire for ASP.NET** | Orchestration / ServiceDefaults for the web service. |
| **0005 — Modular Monolith Structure** | Canonical solution layout if the cloud service grows beyond a single API. |
| **0006 — CQRS for ASP.NET API** | Command/query handlers for sync and webhook endpoints. |
| **0007 — Minimal APIs over Controllers** | Endpoint style for the sync / webhook / PC-registry APIs. |
| **0010 — OpenTelemetry Observability** | Traces/metrics/logs for the cloud service. |
| **0012 — Authentication with External IdPs (OIDC)** | GitHub OAuth and device-session auth. |
| **0013 — Authorization & Zero Trust** | Device/team authorization and audit logging. |
| **0014 — Persistence Strategy & Repository Boundaries** | Sync-state persistence (Cosmos DB / PostgreSQL). |
| **0015 — Resilience for Outbound Dependencies** | Polly policies for GitHub, FCM/APNs calls. |
| **0016 — Messaging & Integration-Event Delivery** | Reliable webhook-forwarding / sync delivery, if event-driven. |
| **0017 — HTTP Error Contract & Problem Details** | RFC 7807 responses for the cloud API. |
| **0018 — Configuration & Options Binding** | Strongly typed options; secrets via Key Vault. |

## Local system decisions

```meta
status: active
related: [".arc42/04-solution-strategy.md"]
```

Decisions specific to Prompt Backlog that are not covered by an org-level ADR are
captured as solution strategy in `.arc42/04-solution-strategy.md`:

- **Local-first, markdown-canonical** storage with SQLite as a derived index.
- **Thin cloud, rich desktop** responsibility split.
- **Conflict policy**: new items always create; edits are last-write-wins.
- **Capture/Inbox kept as one pipeline** for now, with a possible future split.

If any of these harden into formally governed decisions, promote them to ADRs via the
`orch-adr` skill and link them here rather than duplicating the content.
