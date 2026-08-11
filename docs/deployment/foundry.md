# Azure Foundry deployment

Backlog deploys Azure AI Foundry model deployments through Bicep in `infra/foundry/` and the manual `Deploy Foundry` GitHub Actions workflow.

The playground target is:

| Setting | Value |
| --- | --- |
| Tenant/domain | `innovadis.com` |
| Subscription | `8b559814-3419-498f-8d8d-1bd4ea69b15c` |
| Resource group | `JS-Backlog` |
| Default GitHub environment | `playground` |

## Resources

`infra/foundry/main.bicep` creates or updates one Azure AI Services resource (`kind: AIServices`) and child model deployments under `Microsoft.CognitiveServices/accounts/deployments`.

The template defaults the Azure region to the resource group's location. Override `location` only when the Foundry account must be placed in a specific model-supported region.

## Model deployments

| Deployment | Model | Role | Prompt tokens | Output tokens | Default |
| --- | --- | --- | ---: | ---: | --- |
| `gpt-5-4` | `gpt-5.4` | Default coding and architecture model | 922000 | 128000 | Yes |
| `gpt-5-5` | `gpt-5.5` | Premium fallback | 922000 | 128000 | Yes |
| `gpt-5-6-luna` | `gpt-5.6-luna` | Fast, cheaper routine model | 922000 | 128000 | Yes |
| `gpt-5-6-sol` | `gpt-5.6-sol` | Balanced alternative | 922000 | 128000 | Optional |

Token budgets are captured as deployment tags and documentation. They are not sent as unsupported Azure deployment runtime parameters.

Do not configure custom sampling parameters for GPT-5 reasoning models. The deployment artifacts intentionally do not set `temperature`, `top_p`, `presence_penalty`, `frequency_penalty`, `logprobs`, `top_logprobs`, `logit_bias`, or `max_tokens`.

Claude deployments are not included. Add them only after confirming that the Azure provider and model availability support Anthropic deployments without deprecated or unsupported parameters. Do not configure Claude 5 in this repository while the current provider sends deprecated `temperature` values.

## GitHub environment setup

Create or update the `playground` GitHub environment with these variables:

| Variable | Description |
| --- | --- |
| `AZURE_CLIENT_ID` | Client ID for the Azure app registration or managed identity used by GitHub OIDC. |
| `AZURE_TENANT_ID` | Tenant ID for `innovadis.com`. |

Grant the Azure identity enough access on `JS-Backlog` to create or update Azure AI Services accounts and deployments. Microsoft Foundry guidance requires permissions equivalent to Cognitive Services Contributor on the Foundry resource or resource group.

For an environment-scoped OIDC trust, use a subject like:

```text
repo:JSdotNet/Backlog:environment:playground
```

## Run from GitHub Actions

Open **Actions -> Deploy Foundry -> Run workflow** and choose:

| Input | Default | Notes |
| --- | --- | --- |
| `mode` | `what-if` | Use `validate` for template validation only, `what-if` for a safe preview, and `deploy` to apply changes. |
| `environment_name` | `playground` | GitHub environment containing Azure OIDC variables and approvals. |
| `subscription_id` | `8b559814-3419-498f-8d8d-1bd4ea69b15c` | Explicit subscription target. |
| `resource_group` | `JS-Backlog` | Existing resource group target. |
| `foundry_account_name` | `backlog-foundry` | Must be acceptable as an Azure AI Services account/custom subdomain name. |
| `location` | blank | Blank uses the resource group location. |
| `include_balanced_model` | `false` | Set to `true` to deploy `gpt-5-6-sol`. |

The workflow always builds and validates the Bicep template before previewing or deploying. It does not run automatically on push.

## Run locally

Authenticate and select the target subscription:

```powershell
az login --tenant innovadis.com
az account set --subscription 8b559814-3419-498f-8d8d-1bd4ea69b15c
```

Build and validate the template:

```powershell
az bicep build --file infra\foundry\main.bicep
az deployment group validate `
  --resource-group JS-Backlog `
  --template-file infra\foundry\main.bicep `
  --parameters infra\foundry\playground.bicepparam accountName=backlog-foundry
```

Preview changes without applying them:

```powershell
az deployment group what-if `
  --resource-group JS-Backlog `
  --template-file infra\foundry\main.bicep `
  --parameters infra\foundry\playground.bicepparam accountName=backlog-foundry
```

Apply only after reviewing the what-if output and confirming model availability, quota, and approvals:

```powershell
az deployment group create `
  --name backlog-foundry `
  --resource-group JS-Backlog `
  --template-file infra\foundry\main.bicep `
  --parameters infra\foundry\playground.bicepparam accountName=backlog-foundry
```

After deployment, get the Foundry Models endpoint:

```powershell
az cognitiveservices account show `
  --name backlog-foundry `
  --resource-group JS-Backlog `
  --query "properties.endpoints.'Azure AI Model Inference API'" `
  --output tsv
```
