# Sync service deployment

Backlog deploys the cloud sync tier through Bicep in `infra/sync/` and the Azure
Developer CLI (`azd`), driven by the `Deploy Sync` GitHub Actions workflow. The
decision this implements is local ADR 0005,
[`.arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`](../../.arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md);
read that first for *why* the containers are split, why the TTLs are what they are,
and why nothing domain-shaped is allowed into the telemetry.

This is a different deployment from `infra/foundry/`, and deliberately so. Foundry is
a shared AI resource in an existing group, deployed by `az deployment group` from a
self-hosted runner that is already signed in. Sync is the product's own cloud tier,
deployed by `azd` from a GitHub-hosted runner authenticating with an OIDC federated
credential. Neither workflow touches the other's resources.

## Deployment target

One environment. A personal tool does not earn a staging ring.

| Setting | Value |
| --- | --- |
| Runner | `ubuntu-latest` (GitHub-hosted), all steps in `pwsh` |
| GitHub environment | `backlog-sync` |
| azd environment | `backlog-sync` (same name) |
| Authentication | OIDC federated credential — no stored secret |
| Resource group | *(created by hand; see below)* |
| Infrastructure | `infra/sync/main.bicep`, resource-group scoped |
| Service definition | `azure.yaml` at the repository root |

## Manual prerequisites

Four things are done by hand, once, before the workflow can run. They are manual on
purpose: each one either costs money, grants trust, or cannot be undone by re-running
a template.

### 1. Create the resource group

`infra/sync/main.bicep` is resource-group scoped and does **not** create the group, so
a mis-typed environment name cannot quietly spawn a second one.

```powershell
az group create --name <resource-group> --location <region> --subscription <subscription-id>
```

### 2. Configure the OIDC federated credential

Create an app registration, give its service principal **Contributor** and **User
Access Administrator** on the resource group — the second is needed because the
template creates role assignments — and add a federated credential for this
repository.

```powershell
az ad app create --display-name backlog-sync-deploy
```

```powershell
az ad app federated-credential create --id <app-object-id> --parameters '{\"name\":\"backlog-sync-main\",\"issuer\":\"https://token.actions.githubusercontent.com\",\"subject\":\"repo:JSdotNet/Backlog:ref:refs/heads/main\",\"audiences\":[\"api://AzureADTokenExchange\"]}'
```

Add a second credential with subject `repo:JSdotNet/Backlog:environment:backlog-sync`
if you also want manual `workflow_dispatch` runs against the environment to
authenticate.

### 3. Set the GitHub environment variables

On the `backlog-sync` environment. All five are required; the workflow's first step
fails and names the missing one rather than letting `azd` fail halfway.

| Variable | What it is |
| --- | --- |
| `AZURE_CLIENT_ID` | Application (client) id of the app registration above |
| `AZURE_TENANT_ID` | Directory (tenant) id |
| `AZURE_SUBSCRIPTION_ID` | Subscription holding the resource group (must be a GUID) |
| `AZURE_RESOURCE_GROUP` | The group created in step 1 |
| `AZURE_LOCATION` | Azure region, e.g. `swedencentral` |

### 4. Set a budget alert

ADR 0005 puts the expected cost well under €5/month, and the point of a budget alert
is to be told when that stops being true — Cosmos serverless bills per request, so a
sync bug that loops is a cost bug before it is anything else.

```powershell
az consumption budget create --budget-name backlog-sync --amount 10 --time-grain Monthly --category Cost --resource-group <resource-group>
```

> **Still outstanding:** current Cosmos serverless pricing has not been re-verified
> against the numbers ADR 0005 quotes. Check it before the first `provision` run.

## Resources

`infra/sync/main.bicep` creates the following in the target resource group.

| Resource | Notes |
| --- | --- |
| Cosmos DB account | Serverless, session consistency, `disableLocalAuth: true` |
| Cosmos database `backlog` | One database, two containers |
| Container `tasks` | Partition `/ownerId`, default indexing policy |
| Container `sessions` | Partition `/ownerId`, lean custom index |
| User-assigned managed identity | The only principal the service runs as |
| Container Apps environment | Consumption workload profile, logs via Azure Monitor |
| Container app | Scale-to-zero (`minReplicas: 0`), external ingress on 8080 |
| Container registry | Basic, admin user off |
| Key Vault | RBAC authorization, soft delete, purge protection off |
| Log Analytics workspace | 30-day retention |
| Application Insights | Workspace-based, ingesting into that workspace |

The container registry is not in ADR 0005's resource list. It is there because `azd`
deploying a container app has to push the built image somewhere; it issues no
credentials of its own, and the container app pulls from it with the same managed
identity it uses for everything else.

### Retention

| Container | Setting | Effect |
| --- | --- | --- |
| `tasks` | `defaultTtl: -1` | TTL enabled, nothing expires by default |
| `sessions` | `defaultTtl: 31536000` | Every record expires at 12 months |

`sessions` is straightforward: the whole record is history, and the retention is a
container setting exactly as ADR 0005 describes.

`tasks` is not, and the difference matters. A Cosmos container has **one** TTL, and
ADR 0005 wants two behaviours out of it — tombstones expiring at 180 days while *"a
live task document carries no expiry"*. A container-level 180 days would delete live
tasks. So the container enables TTL without a default, and the 180 days is stamped on
the individual document by the write that sets `deleted_at`. Cosmos still performs the
deletion — no reaper runs, and no scheduled job can fail silently, which is the
property ADR 0005 was buying — but the value comes from the writer.

That value is provisioned, not hard-coded: the container app receives it as
`Sync__Cosmos__TaskTombstoneTtlSeconds` (15552000), from the `taskTombstoneTtlSeconds`
parameter. Changing the retention is a parameter change, not a code change.

### The `sessions` index

Excluded by default (`/*`), with only these paths indexed:

`/ownerId/?`, `/machineId/?`, `/repositoryAlias/?`, `/startedAt/?`, `/lastActivityAt/?`

A session record is read by owner and by recency and by nothing else. Branch, turn
count and duration are carried but not indexed, because nothing queries on them.

## Identity, and why there are no keys

The service reaches Cosmos as a **user-assigned managed identity** holding the
built-in **Cosmos DB Data Contributor** data-plane role. Three things follow:

- **No account keys.** `disableLocalAuth: true` on the account means Cosmos will not
  accept a key even if one existed. This is a property of the account, not a
  convention the service is trusted to keep.
- **No connection strings for data.** The container app gets
  `Sync__Cosmos__AccountEndpoint` — an endpoint, not a credential — and
  `AZURE_CLIENT_ID` so `DefaultAzureCredential` presents the right identity.
- **No registry credentials.** The registry has `adminUserEnabled: false`; the same
  identity holds `AcrPull`.

The identity is user-assigned rather than system-assigned, which is a deviation from
what `.arc42/07-deployment-view.md` used to say. A system-assigned identity does not
exist until its container app does, so the `AcrPull` assignment the app needs in order
to pull its own image cannot be granted before the app is created. Splitting the
identity out breaks that cycle and lets one deployment grant every role.

**The one connection string that remains** is
`APPLICATIONINSIGHTS_CONNECTION_STRING`. It is a telemetry ingestion endpoint, not a
data credential — it opens nothing and reaches no replica — and it is held as a
container app secret rather than a plain environment variable.

**The data-plane role is account-scoped**, and ADR 0005 says plainly why that is not
per-partition isolation: Cosmos cannot authorize device-session JWTs it has never
heard of, so keeping a device inside its own partition is a check in the service code,
in front of a credential that can see everything. Nothing in this template changes
that, and nothing in it can.

## Observability carries no domain data

Log Analytics and Application Insights carry application observability only —
requests, dependencies, traces, exceptions, metrics. No task content, no session
content, no owner-identifying payload.

This is a rule about what the service logs, not something a Bicep template can
enforce, and the reason is worth keeping in view: a telemetry pipeline samples and
drops under load. Anything a dashboard answers from would inherit that sampling and
quietly answer wrongly, precisely when the system is busiest.

OpenTelemetry already flows through `AddServiceDefaults()`, so this is wiring rather
than design.

## Local development

No Azure account is needed to build or run the sync path. The Aspire AppHost starts
the **Cosmos DB preview (vNext) emulator** as a container, declares the `backlog`
database and both containers, and hands `sync` a reference to it.

```powershell
aspire start --isolated --non-interactive --apphost src\Aspire\Backlog.Aspire.AppHost\Backlog.Aspire.AppHost.csproj
```

Two things about the local resource are deliberate:

- **The TTLs and the `sessions` indexing policy are not declared in the AppHost.** The
  emulator honours neither, and declaring them there would create a second place for
  them to drift from `infra/sync/main.bicep`, which is where ADR 0005 puts them.
- **Nothing waits on the emulator.** `sync` references it but does not `WaitFor` it,
  because `mobile-web-harness` waits on `sync` — a wait here would put the emulator's
  startup in front of an unrelated harness on every run.

The emulator container is `Persistent`, so it survives between AppHost runs rather
than paying its cold start each time. Docker must be running.

Locally the service receives `ConnectionStrings__backlog` — and, because it is an
emulator, the emulator's well-known account key with it. That is a local-only
artifact of how the emulator authenticates; the deployed template issues no key at
all, and the account it provisions would refuse one.

### The `azure-environment` resource

Declaring an Azure resource makes Aspire add an `azure-environment` entry to the
dashboard, carrying **Reprovision all**, **Delete Azure resources** and **Change
Azure context** commands. It sits `NotStarted`, which is correct and expected —
local runs use the emulator and provision nothing.

**Those commands are not how this repository provisions anything.** They would act
on whatever Azure context the AppHost is pointed at, bypassing `infra/sync/` and
`azd` entirely, and **Delete Azure resources** deletes a resource group. Provision
through the `Deploy Sync` workflow or `azd` as described above, and leave that
resource alone.

## Run from GitHub Actions

A push to `main` that touches `infra/sync/`, `azure.yaml`, `src/Modules/Sync/`, the
service defaults, either `Directory.*.props`, or the workflow itself runs a full
provision and deploy. Nothing else on `main` triggers it.

For a manual run, open **Actions -> Deploy Sync -> Run workflow**:

| Input | Default | Notes |
| --- | --- | --- |
| `mode` | `preview` | `preview` runs `azd provision --preview` and stops. `provision` applies infrastructure. `deploy` applies infrastructure and then deploys the service. |
| `environment_name` | `backlog-sync` | Selects the GitHub environment and names the azd environment. |

Every mode runs the preview first, so a `deploy` run still shows what it is about to
change before it changes it.

## Run locally

Install the Azure Developer CLI (it is a separate download from the Azure CLI):

```powershell
winget install Microsoft.Azd
```

Sign in and create the environment:

```powershell
azd auth login
```

```powershell
azd env new backlog-sync --subscription <subscription-id> --location <region>
```

```powershell
azd env set AZURE_RESOURCE_GROUP <resource-group>
```

Preview, then apply:

```powershell
azd provision --preview
```

```powershell
azd provision
```

```powershell
azd deploy sync
```

The template alone can be compiled without any Azure access at all, which is the
fastest way to check a change to it:

```powershell
az bicep build --file infra\sync\main.bicep --stdout
```

## Cost

Indicative, at single-user volume, per ADR 0005: Cosmos serverless a few cents per
month, Container Apps nothing while scaled to zero, Key Vault and Log Analytics inside
the free grants. Well under €5/month.

The container registry is the one line item ADR 0005 did not budget for — Basic is a
small fixed monthly charge rather than a consumption one, so it is the only thing here
that costs money while nobody is syncing.
