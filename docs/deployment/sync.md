# Sync service deployment

Backlog deploys the cloud sync tier through Bicep in `infra/sync/` and the Azure
Developer CLI (`azd`), driven by the `Deploy Sync` GitHub Actions workflow. The
decision this implements is local ADR 0005,
[`.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`](../../.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md);
read that first for *why* the containers are split, why the TTLs are what they are,
and why nothing domain-shaped is allowed into the telemetry.

This is a different deployment from `infra/foundry/`, and deliberately so. Foundry is
a shared AI resource in an existing group, deployed by `az deployment group` from a
self-hosted runner that is already signed in. Sync is the product's own cloud tier,
deployed by `azd` from a GitHub-hosted runner authenticating with an OIDC federated
credential. Neither workflow touches the other's resources.

Both are callable as reusable workflows, which is how `Deploy All` runs them together —
see [`all.md`](all.md). That changes nothing about how sync deploys on its own.

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

Five things are done by hand, once, before the workflow can run. They are manual on
purpose: each one either costs money, grants trust, holds a secret, or cannot be undone
by re-running a template.

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

The subject must be the **environment** form, not the branch form:

```powershell
az ad app federated-credential create --id <app-object-id> --parameters '{\"name\":\"backlog-sync-environment\",\"issuer\":\"https://token.actions.githubusercontent.com\",\"subject\":\"repo:JSdotNet/Backlog:environment:backlog-sync\",\"audiences\":[\"api://AzureADTokenExchange\"]}'
```

This is the one that has to exist, and a `repo:JSdotNet/Backlog:ref:refs/heads/main`
credential on its own will never authenticate anything. GitHub derives the subject from the
job: when a job declares an `environment`, the token's subject is
`repo:<org>/<repo>:environment:<name>` **on every trigger**, a manual run and a call from
`Deploy All` alike — and the `sync` job always declares one. An earlier version of this document had it the other way
round, presenting the branch subject as primary and the environment subject as an optional
extra for `workflow_dispatch` runs. Nobody caught it because the workflow has never got past
its first step (see *Current state* below).

The environment subject is not scoped to a branch, so the environment's protection rules
are the only branch restriction there can be; the credential carries none. **Today
`backlog-sync` has none** — no deployment branch policy and no protection rules (checked
2026-10-20) — so any ref able to start a workflow that declares the environment can mint a
token that holds Contributor and User Access Administrator on the resource group.

A deployment branch policy is recommended, but it has to admit what deploys: `main`, for
the nightly and for Deploy All, **and the `v*` tags**, because a pushed `v*` tag runs
Release All on `refs/tags/v…` and that run deploys the sync service. A `main`-only policy
would fail every tag-push release at its sync job, and so publish nothing.

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

One optional variable: `AZURE_COSMOS_LOCATION` places the Cosmos account in a different
region from everything else. It defaults to `swedencentral`, next to the Foundry account,
because West Europe and North Europe both refuse to create new Cosmos accounts for this
subscription ("high demand in <region>", `ServiceUnavailable`, zone redundancy off or not —
verified 2026-09-14; the error points at a region-access request, `aka.ms/cosmosdbquota`).
Set it to `westeurope` once that request is granted.

One more, on the **repository** rather than the environment: `BACKLOG_SYNC_SERVICE_URL`,
read by `Release All` (`.github/workflows/release-all.yml`). The sideloaded APK has
no Settings field for the address, so the workflow compiles this value into it through the
`BacklogSyncServiceUrl` MSBuild property. Set it once, after the first deploy, to the
`SYNC_SERVICE_URI` that `Deploy Sync` prints under "Service endpoint" in its step summary;
it only changes if the Container Apps environment is recreated. It is a variable rather
than a secret because the address is public. A tag or manual release fails when it is
unset, and the nightly skips — desktop included, so the two apps keep one number — with a
note in its summary. `build/Install-AndroidApp.ps1`
passes the same property from `-SyncServiceUrl`, or from the address the desktop app's
Devices page saved in `%LOCALAPPDATA%\Backlog\sync-service.json`.

### 4. Set a budget alert

ADR 0005 puts the expected cost well under €5/month, and the point of a budget alert
is to be told when that stops being true — Cosmos serverless bills per request, so a
sync bug that loops is a cost bug before it is anything else.

```powershell
az consumption budget create --budget-name backlog-sync --amount 10 --time-grain Monthly --category Cost --resource-group <resource-group>
```

> **Still outstanding:** current Cosmos serverless pricing has not been re-verified
> against the numbers ADR 0005 quotes. Check it before the first `provision` run.

### 5. Set the device-token signing key

The service signs the device tokens it issues with an HMAC key, and outside
Development it refuses to start without one. The key is a GitHub **environment
secret**, `SYNC_TOKEN_SIGNING_KEY`, on the same `backlog-sync` environment as the
variables above. Mint it locally — 32 random bytes, base64:

```powershell
$bytes = [byte[]]::new(32)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[Convert]::ToBase64String($bytes)
```

Paste the output into the secret and nowhere else. The workflow's first step checks it
has the shape the service will accept — base64, at least 32 bytes decoded — and fails by
name if it does not, so a bad key is caught before `azd provision` rather than as a
container that never comes up. How it travels from there to the running service is in
[*The device-token signing key*](#the-device-token-signing-key) below, along with what
rotating it does.

## Current state

Verified 2026-09-09: **none of the first four prerequisites above had been done**. The
fifth was added on 2026-09-14 and had not been set either. The table below is that day's
state. Since then the workflow has completed a run: Deploy Sync succeeded from
`workflow_dispatch` on 2026-10-20 at 20:44 UTC (run 39494451800).

| Prerequisite | State |
| --- | --- |
| 1. Resource group | Not created. No sync group exists in the subscription. |
| 2. Federated credential | Not created. No `backlog-sync-deploy` app registration exists in the tenant. |
| 3. Environment variables | All five unset on `backlog-sync`. |
| 4. Budget alert | Not created. |
| 5. Signing key secret | Not set. |

So every run then failed in `Verify deployment target`, before touching Azure, with:

```text
Set these variables on the GitHub environment 'backlog-sync': AZURE_LOCATION,
AZURE_RESOURCE_GROUP, AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_SUBSCRIPTION_ID.
```

That is the step doing its job — it names what is missing rather than letting `azd` fail
halfway — but it meant the later steps had not executed by then, so nothing past step one
of this document was proven by a workflow run until the run above.

The template itself *has* been provisioned, from a local run of `build/Deploy-Azure.ps1`
into `JS-AI` on 2026-09-14 — and every revision of the container app it produced sits in
`ActivationFailed` with zero replicas, because the template of that day handed the service
no signing key and the service refuses to start without one. That is the gap prerequisite 5
closes; the first revision provisioned with a key is the first one that can come up.

## Resources

`infra/sync/main.bicep` creates the following in the target resource group.

| Resource | Notes |
| --- | --- |
| Cosmos DB account | Serverless, session consistency, `disableLocalAuth: true` |
| Cosmos database `backlog` | One database, four containers |
| Container `tasks` | Partition `/ownerId`, default indexing policy |
| Container `sessions` | Partition `/ownerId`, lean custom index |
| Container `devices` | Partition `/id` (the device id), default indexing policy |
| Container `pairingCodes` | Partition `/id` (the code hash), nothing indexed |
| User-assigned managed identity | The only principal the service runs as |
| Container Apps environment | Consumption workload profile, logs via Azure Monitor |
| Container app | Scale-to-zero (`minReplicas: 0`), external ingress on 8080 |
| Container registry | Basic, admin user off |
| Key Vault | RBAC authorization, soft delete, purge protection off. Empty and unread today; see below |
| Log Analytics workspace | 30-day retention, 1 GB/day ingestion cap |
| Application Insights | Workspace-based, ingesting into that workspace |
| Notification Hubs namespace `ntfns-<token>` | Free tier; the Android push (local ADR 0038 §4) |
| Notification hub `backlog` | Rules `sync-send` (Send) and `sync-manage` (Listen, Manage, Send); no FCM credential |

The container registry is not in ADR 0005's resource list. It is there because `azd`
deploying a container app has to push the built image somewhere; it issues no
credentials of its own, and the container app pulls from it with the same managed
identity it uses for everything else.

### Retention

| Container | Setting | Effect |
| --- | --- | --- |
| `tasks` | `defaultTtl: -1` | TTL enabled, nothing expires by default |
| `sessions` | `defaultTtl: 31536000` | Every record expires at 12 months |
| `devices` | none | A registration lasts until the device is forgotten |
| `pairingCodes` | `defaultTtl: -1` | TTL enabled; every code carries its own |

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

`pairingCodes` works the same way as `tasks` but with nothing to configure: every
code is written with a `ttl` of its own ten-minute window plus a ten-minute grace,
so Cosmos reaps it shortly after it could last have been used. The grace is what
lets the service keep answering "that code has expired" rather than "not one this
service issued" for a while after the window closes. Reaping is lazy and the
emulator does not do it at all, so the service's own expiry check — not the TTL —
is what turns a stale code away.

### The `sessions` index

Excluded by default (`/*`), with only these paths indexed:

`/ownerId/?`, `/machineId/?`, `/repositoryAlias/?`, `/startedAt/?`, `/lastActivityAt/?`

A session record is read by owner and by recency and by nothing else. Branch, turn
count and duration are carried but not indexed, because nothing queries on them.

### The Android push credential

The hub relays the push to Firebase Cloud Messaging (FCM v1), and it needs the Firebase
project's credential for that: the project id, the service account's client email and its
private key. The template does not declare it. It is entered by hand on the `backlog` hub
(Azure portal, the hub, **Settings > Google (FCM v1)**) once Firebase is set up — the
Backlog plan entry for the FCM credential. Until then the hub keeps the phones'
installations and drops every send, and the service carries on: a push is best effort, and
a phone finds every notification on its next pull. The namespace and hub names are in the
`NOTIFICATION_HUBS_NAMESPACE_NAME` and `NOTIFICATION_HUBS_HUB` outputs.

## Identity, and why there are no keys but two

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
what `.devbook/arc42/07-deployment-view.md` used to say. A system-assigned identity does not
exist until its container app does, so the `AcrPull` assignment the app needs in order
to pull its own image cannot be granted before the app is created. Splitting the
identity out breaks that cycle and lets one deployment grant every role.

**The one telemetry connection string** is `APPLICATIONINSIGHTS_CONNECTION_STRING`. It is
a telemetry ingestion endpoint, not a data credential — it opens nothing and reaches no
replica — and it is held as a container app secret rather than a plain environment variable.

**The two keys that remain** are the Notification Hubs connection strings, because
Notification Hubs supports no managed identity. Bicep reads them from the hub's two rules
with `listKeys()` and passes them straight into container app secrets
(`notification-hubs-send-connection-string`, `notification-hubs-manage-connection-string`),
so no person handles either, and no output echoes them. The service sends with the
Send-only rule; it keeps the phones' installations with the Manage rule, because Notification
Hubs requires Manage to create or delete one.

**The data-plane role is account-scoped**, and ADR 0005 says plainly why that is not
per-partition isolation: Cosmos cannot authorize device-session JWTs it has never
heard of, so keeping a device inside its own partition is a check in the service code,
in front of a credential that can see everything. Nothing in this template changes
that, and nothing in it can.

## The device-token signing key

The identity model above is about how the service reaches Cosmos; it says nothing
about how the service signs the device tokens it issues. That key —
`Modules:Sync:Tokens:SigningKey`, at least 32 bytes, base64 — is a required setting
outside Development: `Backlog.Modules.Sync.Api` binds it into `SyncTokenOptions`,
validates it with `ValidateOnStart()`, and refuses to come up without one. Development
mints and warns about an ephemeral key so no configuration is needed locally, but that
path is Development-only by construction and stays that way: the point of failing at
start is that a misconfigured deployment is found by the deploy, not by the first
device that syncs.

Deployed, the key travels like this:

| Step | Where | What |
| --- | --- | --- |
| 1 | GitHub environment `backlog-sync` | Secret `SYNC_TOKEN_SIGNING_KEY` (prerequisite 5) |
| 2 | `.github/workflows/deploy-sync.yml` | Set in the step environment of `Verify deployment target`, which checks its shape, and of the four `azd` steps (preview, provision, the `service`-mode environment refresh, deploy) — no other step sees it |
| 3 | `infra/sync/main.parameters.json` | `tokenSigningKey: ${SYNC_TOKEN_SIGNING_KEY}` — `azd` substitutes from the process environment |
| 4 | `infra/sync/main.bicep` | `@secure() @minLength(44) param tokenSigningKey`, no default |
| 5 | Container app `configuration.secrets` | `sync-token-signing-key` |
| 6 | Container `env` | `Modules__Sync__Tokens__SigningKey`, a `secretRef` — `__` is how .NET reads the `:` path from an environment variable |

Three properties fall out of that, and they are the reason it is a container app
secret rather than a Key Vault read:

- **A missing key fails the provision, not the app.** The parameter has no default and
  ARM rejects a value under 44 characters, so an empty substitution cannot deploy a
  container that restarts forever. The workflow fails earlier still, by name.
- **The value is never on disk or in a log.** It goes from the GitHub secret straight
  into the process environment; the workflow never passes it to `azd env set`, so it
  is not written to `.azure/<env>/.env` and does not appear in `azd env get-values` or
  the run summary. `@secure()` keeps it out of the deployment history, and
  `az containerapp show` does not return secret values — `az containerapp secret show`
  can, for a principal that can already write the app, which is the same trust as
  being able to redeploy it with any key. GitHub masks the secret in the log as well,
  though nothing in the workflow prints it.
- **No round-trip on cold start.** The app scales to zero; a Key Vault call on every
  start would be paid on every first request for one secret.

Locally, `build/Deploy-Azure.ps1` makes the same check on the same variable, and fills
it in from the deployed app's secret when the shell has not set it — see *Run locally*.

**Rotation.** Set a new value on the GitHub secret (or in the shell) and run a
`provision`. What that invalidates, and what each device does about it:

- **Device tokens** (`LifetimeMinutes`, 30 minutes) signed under the old key are refused
  with a 401. The desktop's `SyncAuthenticationHandler` drops a token that earns a 401
  and the next call re-mints from the device's registration credential — it does not
  wait for the token's expiry — so a rotation costs each device one failed request, not
  half an hour. The registration credential itself is hashed (`Sha256CredentialHasher`),
  not signed, and the registration lives in the `devices` container
  (`CosmosDeviceRegistry`), so neither the key nor the restart a rotation causes
  touches a pairing.
- **Pull cursors** are signed with a key *derived* from the same secret
  (`HmacSyncCursorCodec`: `HMACSHA256(SigningKey, "backlog.sync.cursor.v1")`). A cursor
  from before the rotation no longer verifies and the service answers 400
  `sync.cursor_malformed`. The desktop treats that, like `sync.cursor_expired`, as
  "forget the cursor and pull from the beginning" (`TaskSyncSession`,
  `SessionSyncSession`), so a rotation is one full re-pull per device rather than an
  error. On a personal-scale replica that is worth knowing, not worth avoiding.

**The Key Vault this template provisions stays empty.** ADR 0005 lists it, the
service's managed identity holds *Key Vault Secrets User* on it, and no code reads it:
the container app is handed no vault endpoint, because a `Sync__KeyVault__Endpoint`
that nothing consumed was configuration that looked live and was not. It is reserved
for the day the signing key wants vault-managed rotation, or for the webhook secrets
ADR 0005 also names — that change adds
`Azure.Extensions.AspNetCore.Configuration.Secrets` to the service and a vault secret
named `Modules--Sync--Tokens--SigningKey` (`--` is that provider's section separator),
and is product code rather than infrastructure.

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

### The daily ingestion cap

The workspace carries a daily ingestion cap — `workspaceCapping.dailyQuotaGb`, from the
`logDailyQuotaGb` parameter, default 1 GB. It is an emergency stop, not a budget knob.

In September 2026 a sibling project (spec-manager) paid about $688 for one month of Log
Analytics: 23 stale container revisions each logged a full stack trace every five seconds
after a schema change, and nothing bounded the volume. Backlog cannot reach that state
the same way — the container app runs in `Single` revision mode, so Azure retires the
previous revision itself, and `minReplicas: 0` keeps no replica alive without traffic —
but the workspace should still refuse to bill an incident without limit.

Once the cap is reached Azure discards everything for the rest of the day, exceptions
included. That is intended: on such a day the volume *is* the incident, and the first
lines already say what went wrong. 1 GB is far above a normal day for a scale-to-zero
personal tool and bounds a repeat of that incident at roughly $3/day. There is no alert
on reaching it; Azure records the event in the `_LogOperation` table, and an Action
Group plus alert rule can be added if a silent day ever turns out to matter.

After a provision, confirm the cap is in place:

```powershell
az monitor log-analytics workspace show --name <workspace> --resource-group <group> --query workspaceCapping
```

`dailyQuotaGb` should read `1.0`; `-1.0` means no cap.

### Exception triage

`.github/workflows/app-insights-exceptions.yml` reads the component's `exceptions` table
and files one GitHub issue per distinct `problemId`, labelled
`app-insights-exception`; a problem that keeps occurring gets a comment on its open
issue rather than a second issue. It signs in with the same federated credential and the
same `backlog-sync` environment variables as `Deploy Sync`, so prerequisites 2 and 3
above are its prerequisites too. Reading telemetry through the query API needs
**Monitoring Reader** on the component; the Contributor role prerequisite 2 grants on
the resource group already includes it. The component is found by listing the group;
set `APPLICATIONINSIGHTS_NAME` on the environment only if the group ever holds a second
one. `workflow_dispatch` takes a `dry_run` input that reports without filing.

The nightly schedule is off. The workflow runs by hand only until the `backlog-sync`
environment holds `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and
`AZURE_RESOURCE_GROUP`: with them blank its preflight fails, and the schedule filed a
failed run every day. To turn it back on:

1. Set the variables on the `backlog-sync` environment (prerequisite 3 above).
2. Run the workflow once from the Actions tab with `dry_run` ticked, and see it succeed.
3. Restore the `schedule:` block under `on:`, beside `workflow_dispatch`:

   ```yaml
   schedule:
     # 04:23 UTC, a quiet hour; scheduled jobs are best-effort, so leave slack.
     - cron: '23 4 * * *'
   ```

Verified 2026-09-15: the query API answers for `appi-u47q76w3zjori` in `JS-AI`, and its
`exceptions` table is empty over thirty days — not because nothing failed, but because
nothing exports to it. `AddServiceDefaults()` wires only the OTLP exporter, which is the
Aspire dashboard's, and no `Azure.Monitor.OpenTelemetry` package is referenced, so
`APPLICATIONINSIGHTS_CONNECTION_STRING` reaches the container and is never read. Until
the sync service exports to Azure Monitor, every run of the workflow reports "no
exceptions" and is telling the truth about the table, not about the service.

## Local development

No Azure account is needed to build or run the sync path. The Aspire AppHost starts
the **Cosmos DB preview (vNext) emulator** as a container, declares the `backlog`
database and all four containers, and hands `sync` a reference to it.

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
artifact of how the emulator authenticates; the deployed template issues no Cosmos key at
all, and the account it provisions would refuse one. Its only keys are the two
Notification Hubs SAS connection strings described above.

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

## The service version stops older clients

The anonymous root (`GET /`) says which build is running in three fields, each with its
own job:

| Field | Comes from | What reads it |
| --- | --- | --- |
| `version` | `<Version>` in `Backlog.Modules.Sync.Api.csproj` (`0.1.0`) | every desktop and phone: the compatibility check below |
| `release` | the release number Release All deployed the build under (`0.1.123`), `null` when none did | Release All's post-deploy check, and anyone asking which release is live |
| `commit` | the sha the build was cut from | `build/Get-SyncServiceVersion.ps1` and the System tools row: is a deploy due |

`version` is the compatibility number, not the SDK default and not the release number. Every desktop and phone compares it with its own app version, numerically, a
missing part counting as zero. When the service's is higher, the app stops syncing:
it sends no sync request, the desktop footer and Devices settings and the phone's
shell say "The sync service is newer than this version of Backlog. Update Backlog to
keep syncing.", and each offers the update window. Nothing waiting is lost — desktop
watermarks do not move and the phone outbox holds its captures — and the app asks the
service again every ten minutes, so rolling the service back lets it sync again. A
root that does not answer, or reports a version the app cannot read, stops nothing.

So bump `<Version>` deliberately, and only in the change that breaks older clients: a
version above what the released clients carry (desktop `0.1.x`, phone `0.1.x`)
stops all of them the moment it is deployed. An ordinary deploy leaves it as it is — and
so does every release. Release All deploys the service under each release number, but it
stamps that number as `release` (the MSBuild property `BacklogRelease`, written into the
assembly's metadata) and never sets `Version`. Were the release number the service's
`version`, every release would raise it past the apps phones and desktops still have
installed, and stop them all until they updated.

**Deploy the service before the first client that carries the check ships.** A
service built before `<Version>` was set reports the SDK default `1.0.0`, which is
higher than every released desktop (`0.1.N.0`) and phone (`0.1.N`). So the first
desktop or phone release that carries this check pauses sync on every device that
installs it, until the service is redeployed. Deploy the service at `0.1.0` first,
or later at any version no higher than every released client, and only then release
the desktop and the phone. After that, deploy order does not matter until
`<Version>` is raised. Release All keeps the order anyway: it deploys the service before it
publishes the apps of the same release.

## Run from GitHub Actions

**The infrastructure is manual; the service is not.** Nothing triggers this workflow on its
own, and every infrastructure run — `preview`, `provision`, `deploy` — is started by a person,
here or through `Deploy All`. The service, though, is deployed unattended: every
**Release All** run that publishes a release, the nightly included, calls this workflow in
`service` mode with the release number, so the production service is redeployed whenever a
release goes out. The push-to-`main` trigger this workflow used to carry stays parked.

A `service` run:

1. verifies the target as every mode does, and checks `release` is `MAJOR.MINOR.PATCH`;
   when Release All passes its `release_tag` and that tag already exists at another commit,
   it stops there — a re-run of an older run never puts older service code into production
   under a number a newer run released;
2. signs in and recreates the azd environment;
3. reads the last provision's outputs back with `azd env refresh` — no preview, no
   provision;
4. runs `azd deploy sync` with the MSBuild property `BacklogRelease` set to the release
   number in that step's environment. azd builds the image with
   `dotnet publish /t:PublishContainer` in its own process environment, and MSBuild reads
   environment variables as properties, so the project writes the number into the
   assembly's `BacklogRelease` metadata. `Version`, and so `GET /`'s `version`, stays the
   project's compatibility number (see *The service version stops older clients*);
5. reads `GET /` back until its `release` is that number and its `commit` the run's, and
   fails after ten minutes otherwise. Release All publishes nothing until this passes.

The image tag stays azd's own (`azd-deploy-<timestamp>`); the release number is what
`GET /` reports as `release`.

Open **Actions -> Deploy Sync -> Run workflow**:

| Input | Default | Notes |
| --- | --- | --- |
| `mode` | `preview` | `preview` runs `azd provision --preview` and stops. `provision` applies infrastructure. `deploy` applies infrastructure and then deploys the service. `service` deploys the service alone, provisioning nothing. |
| `environment_name` | `backlog-sync` | Selects the GitHub environment and names the azd environment. |

Every infrastructure mode runs the preview first, so a `deploy` run still shows what it is
about to change before it changes it. A dispatched run carries no release number, so a
`deploy` or `service` run from here reports `"release": null` on `GET /` — visibly not a
release; the number comes only through Release All's call (`release` input). Its `version`
is the project's `<Version>` either way.

## Run locally

`build/Deploy-Azure.ps1` wraps this. A local run uses your own sign-in, so of the
prerequisites only 1 and 5 apply — and the script's default `-SyncResourceGroup` is
`JS-AI`, the group Foundry already lives in, so on the Sponsorship subscription nothing
needs creating. The signing key is resolved into the process environment before `azd`
runs, in every mode — `azd` resolves the parameter file before it knows whether it is
going to change anything — from the first of these that applies:

1. **Your shell.** Set it for the session only to rotate the key, or to deploy one you
   chose. The script checks the shape the way the workflow does and stops by name when it
   is wrong.

   ```powershell
   $env:SYNC_TOKEN_SIGNING_KEY = '<base64 key from prerequisite 5>'
   ```

2. **The deployed app.** When the shell has nothing, the script finds the container app
   the environment already provisioned (by its `azd-env-name` and `azd-service-name`
   tags) and reads its `sync-token-signing-key` secret back. A re-run keeps its key, so
   no device token is invalidated and nothing has to be pasted between sessions.

3. **Freshly minted,** when nothing is deployed yet. The provision makes it the app's
   secret; copy it from there into the GitHub environment secret with the command the
   script prints, or the next workflow run rotates it.

The value stays in the process: the script never prints it and never `azd env set`s
it — that would write it to `.azure/backlog-sync/.env` on disk, where
`azd env get-values` would print it back.

Its default component is `all`; pass `-Component sync` to deploy the sync tier alone:

```powershell
./build/Deploy-Azure.ps1 -Component sync -Mode deploy
```

It selects the azd environment rather than recreating it, so it is safe to re-run. The steps
below are what it does.

Sharing the group with Foundry has one hazard: **never run `azd down`** on the
`backlog-sync` environment. With `AZURE_RESOURCE_GROUP` set, `azd down` deletes that
resource group — the Foundry account with it. Remove the sync resources by hand instead.

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
azd env set AZURE_RESOURCE_GROUP JS-AI
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

## Run from the System tools pane

The desktop app's System tools pane can carry the sync service as a row: whether the
deployed build is behind the repository, and an **Update** that runs the local deploy
above. It is a `command` application in the tools catalog, with the one property that
only this row uses — `available`, the command that answers which build the service
*should* be running. `detect` and `available` each print one short commit sha; the pane
compares the two and offers Update when they differ.

```json
{
  "id": "backlog-sync-service",
  "name": "Backlog sync service (Azure)",
  "provider": "command",
  "group": "Backlog cloud",
  "enabled": true,
  "note": "Update deploys the sync tier from this machine with your own Azure sign-in.",
  "detect":    { "command": "pwsh", "args": ["-NoProfile", "-File", "D:\\Repos\\Backlog\\build\\Get-SyncServiceVersion.ps1", "-Side", "deployed"] },
  "available": { "command": "pwsh", "args": ["-NoProfile", "-File", "D:\\Repos\\Backlog\\build\\Get-SyncServiceVersion.ps1", "-Side", "wanted"] },
  "install":   { "command": "pwsh", "args": ["-NoProfile", "-File", "D:\\Repos\\Backlog\\build\\Deploy-Azure.ps1", "-Component", "sync", "-Mode", "deploy"] }
}
```

Replace the path with your clone's. `build/Get-SyncServiceVersion.ps1` does the two
lookups:

- **`-Side deployed`** asks the service. Its anonymous root (`GET /`) reports the commit
  the running container was built from — the SDK stamps `HEAD` into the assembly's
  informational version on every build, so `azd deploy` needs no extra step. The
  container app is found through `az` by the same tags `Deploy-Azure.ps1` uses; pass
  `-Endpoint <url>` in the `args` to skip that and make the check a single unauthenticated
  request.
- **`-Side wanted`** asks the repository: the newest `origin/main` commit touching what
  the image is built from (`src/Modules/Sync`, `infra/sync`, `azure.yaml`, the service
  defaults, the props files). When that commit is already in the deployed build's
  history it prints the deployed sha back, so a merge that touched nothing the service
  ships does not read as an update due.

A service deployed before the root endpoint reported its commit answers without one, and
the row reads *not installed* with the script's message in the pane's transcript. One
deploy by hand — or pressing Install, which runs the same thing — brings it onto a build
that reports.

## Cost

Indicative, at single-user volume, per ADR 0005: Cosmos serverless a few cents per
month, Container Apps nothing while scaled to zero, Key Vault and Log Analytics inside
the free grants. Well under €5/month. The daily ingestion cap bounds the one line that
could run away: even a day of runaway logging costs about $3, not hundreds.

The container registry is the one line item ADR 0005 did not budget for — Basic is a
small fixed monthly charge rather than a consumption one, so it is the only thing here
that costs money while nobody is syncing.
