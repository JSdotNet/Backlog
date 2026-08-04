# 09. Architecture Decisions

```meta
status: active
```

This chapter links to the authoritative Architecture Decision Records rather than
restating them. ADRs are maintained in the organization's guidance corpus
(`jsdotnet-project-guidelines`) and primarily affect the **cloud service** (.NET)
part of Prompt Backlog. The desktop, mobile, and IDE channels use their own platform
stacks and are intentionally out of scope for the .NET ADR set.

## Cloud-service ADR alignments (pending MCP verification)

```meta
status: draft
related: [".arc42/05-building-block-view.md#cloud-service", ".arc42/07-deployment-view.md#cloud-deployment-azure"]
```

The `jsdotnet-project-guidelines` MCP server was not available while drafting this
branch, so the mapping below is a **local fallback** based on repository context and
must be re-verified before implementation. It is intended to prevent obviously wrong
technology choices, not to serve as a final ADR inventory.

| ADR | Current alignment for the cloud service |
|---|---|
| **0001 - Adopt .NET 10** | Expected target framework for the cloud service. |
| **0003 - .NET Aspire for ASP.NET** | Expected orchestration / ServiceDefaults guidance if the sync layer is implemented as an ASP.NET workload. |
| **0005 - Modular Monolith Structure** | Relevant if the cloud service grows beyond a single small API and needs standard project boundaries. |
| **0006 - CQRS for ASP.NET API** | Relevant for separating sync commands from query-style status/read endpoints. |
| **0007 - Minimal APIs over Controllers** | Expected endpoint style for sync, webhook, and PC-registry APIs. |
| **0010 - OpenTelemetry Observability** | Relevant for traces, metrics, and logs in the cloud service. |
| **0012 - Authentication with External Identity Providers (OIDC)** | Relevant only for the GitHub OAuth callback / any future external identity flow, not for device-session auth. |
| **0013 - Authorization & Zero Trust** | Relevant for device/team authorization, least-privilege checks, and audit logging. |
| **0014 - Persistence Strategy & Repository Boundaries** | Relevant for sync-state persistence and data ownership boundaries. |
| **0015 - Resilience for Outbound Dependencies** | Relevant for GitHub, FCM, and APNs outbound calls. |
| **0016 - Messaging & Integration-Event Delivery** | Relevant only if webhook forwarding or sync delivery is implemented with durable asynchronous messaging. |
| **0017 - HTTP Error Contract & Problem Details** | Expected error contract for the cloud API surface. |
| **0018 - Configuration & Options Binding** | Relevant for strongly typed settings and externalized secrets. |

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
