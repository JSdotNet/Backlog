# Azure Foundry deployment

Backlog deploys Azure AI Foundry model deployments through Bicep in `infra/foundry/` and the manual `Deploy Foundry` GitHub Actions workflow.

The workflow runs on a **self-hosted runner** that already has Azure access; it does not sign in to Azure itself. A deployment target is a pair:

1. a **GitHub environment** that carries the subscription/resource group the run deploys to, and
2. a **parameter file** `infra/foundry/<environment_name>.bicepparam` that carries the per-environment resource name, region, SKU, capacity, and tags.

The workflow's `environment_name` input selects both. If the parameter file is missing, or no subscription is resolved from the inputs or the environment, the run fails in its first step before touching Azure. The subscription and resource group inputs default to the `backlog-ai` target, so a run works even when the GitHub environment carries no variables.

## Deployment target

| Setting | Value |
| --- | --- |
| Runner | self-hosted (Windows, `pwsh`) |
| GitHub environment | `backlog-ai` |
| Tenant/domain | `innovadis.com` (`b44c759d-a7bb-46bb-8aae-ba8d84789609`) |
| Subscription | `8235e3b9-4cd0-4426-879a-471503d9e4fc` (Microsoft Azure Sponsorship) |
| Resource group | `JS-AI` |
| Region | `swedencentral` |
| Foundry account | `backlog-foundry` |
| Parameter file | `infra/foundry/backlog-ai.bicepparam` |
| Speech model | `gpt-4o-transcribe`, enabled via `includeSpeechModel = true` |

The resource group is `westeurope` but the Foundry account is placed in `swedencentral`, so `backlog-ai.bicepparam` sets `location` explicitly instead of inheriting the resource group location.

To add a second target later, add a parameter file and a GitHub environment of the same name — the workflow needs no change.

## Quota prerequisite

The Sponsorship subscription has **no `GlobalStandard` quota** for the required models yet. Verified in both `swedencentral` and `westeurope`:

| Quota | Used | Limit |
| --- | ---: | ---: |
| `OpenAI.GlobalStandard.gpt-5.4` | 0 | 0 |
| `OpenAI.GlobalStandard.gpt-5.5` | 0 | 0 |
| `OpenAI.GlobalStandard.gpt-5.6-luna` | 0 | 0 |
| `OpenAI.GlobalStandard.gpt-5.6-sol` | 0 | 0 |

The speech model is the exception — it already has quota in this subscription:

| Quota | Used | Limit |
| --- | ---: | ---: |
| `OpenAI.GlobalStandard.gpt-4o-transcribe` | 0 | 400 |

That does not make the deployment succeed on its own: the template deploys the account and all
selected models together, so the missing chat quota fails the whole run.

The models themselves are available in both regions, and the template validates cleanly against a subscription that has quota. But every mode — including `validate` — fails preflight in this subscription with `InsufficientQuota` until a quota increase is granted:

```text
This operation require 1 new capacity in quota One Thousand Tokens Per Minute - gpt-5.4 - GlobalStandard, which is bigger than the current available capacity 0.
```

Request quota for the Sponsorship subscription in the Azure AI Foundry portal (Management center -> Quota) before running the workflow. Then set `deploymentCapacity` in `backlog-ai.bicepparam` to a value the granted quota covers; the template default is `1` (one thousand tokens per minute per deployment).

Check the current numbers with:

```powershell
az cognitiveservices usage list --location swedencentral --subscription 8235e3b9-4cd0-4426-879a-471503d9e4fc --query "[?contains(name.value,'GlobalStandard') && contains(name.value,'gpt-5.')].{quota:name.value, used:currentValue, limit:limit}" --output table
```

## Resources

`infra/foundry/main.bicep` creates or updates one Azure AI Services resource (`kind: AIServices`) and child model deployments under `Microsoft.CognitiveServices/accounts/deployments`.

The template defaults the Azure region to the resource group's location. Override `location` only when the Foundry account must be placed in a specific model-supported region.

## Model deployments

Every environment deploys the same required model set. What may differ per environment is the account name, region, account SKU, deployment SKU, capacity, content filter policy, tags, and whether the optional balanced and speech models are included.

| Deployment | Model | Role | Prompt tokens | Output tokens | Default |
| --- | --- | --- | ---: | ---: | --- |
| `gpt-5-4` | `gpt-5.4` | Default coding and architecture model | 922000 | 128000 | Yes |
| `gpt-5-5` | `gpt-5.5` | Premium fallback | 922000 | 128000 | Yes |
| `gpt-5-6-luna` | `gpt-5.6-luna` | Fast, cheaper routine model | 922000 | 128000 | Yes |
| `gpt-5-6-sol` | `gpt-5.6-sol` | Balanced alternative | 922000 | 128000 | Optional |
| `gpt-4o-transcribe` | `gpt-4o-transcribe` | Speech-to-text | n/a | n/a | Optional, on for `backlog-ai` |

All of these are available in `swedencentral` and `westeurope` with the `GlobalStandard` SKU.

Microsoft's MAI models are **not** an option for speech. The deployable Microsoft-format catalog is
`MAI-Image-2`, `MAI-Image-2e`, `MAI-Image-2.5`, `MAI-Image-2.5-Flash`, `MAI-Image-2.5-Pro`,
`MAI-Thinking-1`, and the `Phi-4` family — no voice or speech entry, in any region checked
(`swedencentral`, `westeurope`, `eastus`, `eastus2`, `westus3`, `northcentralus`, `japaneast`).
Speech in Foundry is OpenAI-format today: `gpt-4o-transcribe`, `gpt-4o-transcribe-diarize`,
`gpt-4o-mini-transcribe`, `whisper`, `tts`, `tts-hd`, and the `gpt-audio`/`gpt-realtime` families.

Note that `.domain/capture/features.md` specifies speech-to-text capture as *on-device*
transcription. `gpt-4o-transcribe` is a cloud fallback, not a replacement for that design.

Token budgets are captured as deployment tags and documentation. They are not sent as unsupported Azure deployment runtime parameters.

Do not configure custom sampling parameters for GPT-5 reasoning models. The deployment artifacts intentionally do not set `temperature`, `top_p`, `presence_penalty`, `frequency_penalty`, `logprobs`, `top_logprobs`, `logit_bias`, or `max_tokens`.

Claude deployments are not included. Add them only after confirming that the Azure provider and model availability support Anthropic deployments without deprecated or unsupported parameters. Do not configure Claude 5 in this repository while the current provider sends deprecated `temperature` values.

## Per-environment parameters

These are the parameters a `<environment_name>.bicepparam` file may set. Only `accountName` is required.

| Parameter | Default | Notes |
| --- | --- | --- |
| `accountName` | *(required)* | Must be acceptable as an Azure AI Services account and custom subdomain name, and globally unique as a custom subdomain. |
| `location` | resource group location | Set when the account must live in a specific model-supported region, as `backlog-ai` does. |
| `accountSkuName` | `S0` | Azure AI Services account SKU. |
| `deploymentSkuName` | `GlobalStandard` | Default SKU for deployments that do not pin their own; must be available in the target subscription and region. |
| `deploymentCapacity` | `1` | Default capacity for deployments that do not pin their own, in thousands of tokens per minute; bounded by the target subscription's quota. |
| `includeBalancedModel` | `false` | Deploys `gpt-5-6-sol` in addition to the required set. |
| `includeSpeechModel` | `false` | Deploys `gpt-4o-transcribe`. Set to `true` in `backlog-ai.bicepparam`. |
| `contentFilterPolicyName` | `Microsoft.DefaultV2` | Responsible AI policy applied to every deployment. |
| `tags` | workload/environment/managedBy | Applied to the account and, extended with model role and token budget tags, to each deployment. |

## Adding a model

Model deployments live in `main.bicep`. Each entry may pin `skuName` and `capacity`: an empty
`skuName` falls back to `deploymentSkuName`, and a zero `capacity` falls back to
`deploymentCapacity`. Speech models need this, because `whisper`, `tts`, and `tts-hd` offer only
the `Standard` SKU while the chat models are `GlobalStandard`, and speech quota is far smaller
than chat quota.

Confirm the model name, region availability, SKU, and quota with `az cognitiveservices model list`
and `az cognitiveservices usage list` before adding an entry — a name that is not in the catalog
fails preflight.

## Adding a subscription target

1. Create a `<environment_name>.bicepparam` file in `infra/foundry/`, copying `backlog-ai.bicepparam` and giving the account a globally unique custom subdomain name.
2. Create the GitHub environment with the same name and set its variables (see below).
3. Confirm model availability and quota in the target subscription and region.
4. Grant the runner's Azure identity access on the target resource group, then run the workflow in `validate`, `what-if`, and only then `deploy` mode.

## Runner and Azure access

The workflow runs on a self-hosted Windows runner (`runs-on: [self-hosted]`, all steps in `pwsh`).
There is no `Azure/login` step and no GitHub OIDC federated credential: the runner is expected to
already be authenticated to Azure, and the workflow only selects the target subscription.

Prepare the runner once, **as the account the runner service runs under**:

```powershell
az login --tenant innovadis.com
az bicep install
```

A managed identity on the runner host works equally well (`az login --identity`). Either way the
identity needs enough access on the target resource group to create or update Azure AI Services
accounts and deployments — Microsoft Foundry guidance requires permissions equivalent to
**Cognitive Services Contributor** on the Foundry resource or resource group.

The `Select Azure subscription` step fails fast with a clear message when the runner is not signed
in, or when the signed-in identity cannot see the target subscription. Neither the Azure CLI nor the
Bicep CLI is installed by the workflow; both must be present on the runner.

## GitHub environment setup

The subscription and resource group inputs already default to the `backlog-ai` target, so these
variables are optional. Set them on an environment when you want a target that does not have to be
typed into every run:

| Variable | Description | Value for `backlog-ai` |
| --- | --- | --- |
| `AZURE_SUBSCRIPTION_ID` | Subscription the environment deploys to. Used when the run leaves `subscription_id` blank. | `8235e3b9-4cd0-4426-879a-471503d9e4fc` |
| `AZURE_RESOURCE_GROUP` | Existing resource group in that subscription. Used when the run leaves `resource_group` blank. | `JS-AI` |

`AZURE_CLIENT_ID` and `AZURE_TENANT_ID` are no longer used — the runner's own identity supplies both.

## Run from GitHub Actions

Open **Actions -> Deploy Foundry -> Run workflow** and choose:

| Input | Default | Notes |
| --- | --- | --- |
| `mode` | `what-if` | Use `validate` for template validation only, `what-if` for a safe preview, and `deploy` to apply changes. |
| `environment_name` | `backlog-ai` | Selects the GitHub environment and `infra/foundry/<environment_name>.bicepparam`. |
| `subscription_id` | `8235e3b9-4cd0-4426-879a-471503d9e4fc` | Blank falls back to the environment's `AZURE_SUBSCRIPTION_ID`. Must be a GUID when set. |
| `resource_group` | `JS-AI` | Blank falls back to the environment's `AZURE_RESOURCE_GROUP`. |
| `foundry_account_name` | blank | Blank uses the parameter file's `accountName`. |
| `location` | blank | Blank uses the parameter file value, or the resource group location. |
| `include_balanced_model` | `from-parameter-file` | Choose `true`/`false` to override the parameter file for one run. |
| `include_speech_model` | `from-parameter-file` | Choose `true`/`false` to override the parameter file for one run. |

The workflow always builds and validates the Bicep template before previewing or deploying. It does not run automatically on push.

## Run locally

The Bicep CLI is a separate download from the Azure CLI; install it once:

```powershell
az bicep install
```

Authenticate and select the target subscription:

```powershell
az login --tenant innovadis.com
az account set --subscription 8235e3b9-4cd0-4426-879a-471503d9e4fc
```

Build and validate the template:

```powershell
az bicep build --file infra\foundry\main.bicep --stdout
```

```powershell
az deployment group validate --resource-group JS-AI --template-file infra\foundry\main.bicep --parameters infra\foundry\backlog-ai.bicepparam
```

Preview changes without applying them:

```powershell
az deployment group what-if --resource-group JS-AI --template-file infra\foundry\main.bicep --parameters infra\foundry\backlog-ai.bicepparam
```

Apply only after reviewing the what-if output and confirming model availability, quota, and approvals:

```powershell
az deployment group create --name backlog-foundry --resource-group JS-AI --template-file infra\foundry\main.bicep --parameters infra\foundry\backlog-ai.bicepparam
```

After deployment, get the Foundry Models endpoint:

```powershell
az cognitiveservices account show --name backlog-foundry --resource-group JS-AI --query "properties.endpoints.'Azure AI Model Inference API'" --output tsv
```
