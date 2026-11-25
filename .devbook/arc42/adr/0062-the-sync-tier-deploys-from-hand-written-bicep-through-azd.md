# ADR 0062: The sync tier deploys from hand-written Bicep through `azd`, not `aspire publish` and `aspire deploy`

```meta
date: 2026-11-25
related: [".devbook/arc42/07-deployment-view.md", ".devbook/arc42/adr/guidelines/0003-aspire-for-web-services.md", ".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md", ".devbook/arc42/adr/0039-notifications-are-an-owned-document-kind-and-push-only-nudges.md", ".devbook/arc42/09-architecture-decisions.md"]
c4: [backlog.cloud]
c4-touches: [backlog.cloudDb, backlog.attachments, azureFoundry]
```

Backlog's cloud tier stays on hand-written Bicep: `infra/sync` deployed by `azd`, and
`infra/foundry` by `az deployment group`. The AppHost stays the model for local runs
and tests. It does not generate the infrastructure, although the **Infrastructure**
rule of org ADR 0002 asks for that.

## Status

```meta
```

Proposed, 2026-11-25. Plan `app-defaults-rollout`, entry 37 (`backlog-deviation-adrs`).

A **local** decision. It records a deliberate difference from
[org ADR 0002: Aspire, Configuration and Infrastructure](https://github.com/JSdotNet/app-defaults/blob/main/defaults/arc42/adr/0002-aspire-configuration-and-infrastructure.md).
That ADR's other rules still apply. Plan entry 36 (`backlog-apphost-config`) moves
settings, secrets, options validation and alert rules into the AppHost, and that work
does not depend on this record.

## Context

```meta
```

Org ADR 0002 makes the AppHost the infrastructure code: `aspire publish` generates the
Bicep, `aspire deploy` applies it, and no second IaC tool is used.

Backlog's AppHost and its deployment describe two different topologies:

| | AppHost (`src/Aspire/`) | Azure (`infra/`) |
| --- | --- | --- |
| What runs | `sync`, the Cosmos and Azurite emulators, three web harnesses, the storybook, the Foundry test service | One Container App, `sync` |
| What it provisions | Emulators only | Cosmos with 11 containers and a data-plane role, Blob storage with a lifecycle policy, Notification Hubs with two rules, Web PubSub, Key Vault, a managed identity, four role assignments, Log Analytics, App Insights, the registry |
| Foundry | A local test service | A separate resource group with its own lifecycle and a cross-group role for transcription |

Most of the AppHost exists for development. The desktop and the phone, the product's
main channels, are not resources Aspire deploys at all.

The deployed service is live and holds the owner's replica. Cosmos container names,
partition keys and role assignments are in data that cannot be recreated. Moving to a
deployment Aspire generates would rename or recreate resources unless every name were
pinned through customization hooks. That would cost about as much Bicep-shaped C#
as the Bicep it replaces.

## Decision

```meta
```

**Keep.** The sync tier deploys from `infra/sync/main.bicep` through `azd`, and
Foundry from `infra/foundry/main.bicep` through `az deployment group`, as chapter 07
describes. Neither is generated from the AppHost.

- No change to a deployed resource is made in the portal. Every change lands in the
  Bicep, as org ADR 0002 requires.
- The AppHost keeps modelling every resource that runs locally. Settings, secrets and
  alert rules move into it under plan entry 36. A setting the AppHost declares is
  passed to the Bicep as a parameter of the same name.

**Revisit** when `aspire deploy` can adopt existing resources by name, or when the
cloud tier gains a second deployable.

## Consequences

```meta
```

- Two descriptions of the cloud tier have to stay in step: the AppHost for local runs
  and the Bicep for Azure. A resource added to one and not the other shows up as a
  local run that works and a deploy that fails, or the reverse.
- Agents and people deploy with `azd` and `build/Deploy-Azure.ps1`, not `aspire
  deploy`.
- An app-defaults review reads this record for the 0002 Infrastructure row.

## Rejected

```meta
```

- **Move to `aspire publish` and `aspire deploy` (XL).** Every Bicep resource, the
  Cosmos containers, the role assignments, the separate Foundry group and the
  lifecycle policy would be re-expressed through AppHost customization hooks with
  names pinned to today's. The live replica would then be redeployed under the new
  generator. That is a large risk to the owner's only cloud data, and the change adds
  no capability Backlog lacks.
- **Generate only new resources from the AppHost.** Two IaC sources for one resource
  group is the drift org ADR 0002 exists to prevent.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | Proposed: keep `azd` and hand-written Bicep for the sync tier and Foundry; revisit when `aspire deploy` can adopt existing resources. | Step 15 of plan `app-defaults-rollout` (PR #1967) listed the difference from org ADR 0002 as XL with no local decision. |
