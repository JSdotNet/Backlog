# Copilot Model Selection Overrides

Backlog is a .NET Aspire product (`src/`, `tests/`, `harness/`) that also carries an
unusually large checked-in knowledge base (`.arc42/`, `.domain/`, `.backlog/`,
`.tech/`, `.design/`) driving what gets built. Configure orchestration and specialist
runs from the Azure Foundry model catalog/deployment entries below.

In the Copilot App model configuration UI, **Wire model** must match the Azure Foundry
deployment/model ID exactly. Prefer the GPT-family Microsoft Foundry entries because
the current Anthropic provider injects the deprecated `temperature` parameter, causing
Claude 5 requests to fail.

## Azure Foundry catalog entries

| Foundry display name | Wire model | Max prompt tokens | Max output tokens | Intended use |
| --- | --- | ---: | ---: | --- |
| `gpt-5.4` | `gpt-5.4` | 922000 | 128000 | Default configured model |
| `gpt-5.5` | `gpt-5.5` | 922000 | 128000 | Premium fallback |
| `gpt-5.6-luna` | `gpt-5.6-luna` | 922000 | 128000 | Fast or cheaper routine work |
| `gpt-5.6-sol` | `gpt-5.6-sol` | 922000 | 128000 | Optional balanced alternative |
| `claude-sonnet-4-6` | `claude-sonnet-4-6` | 1000000 | 128000 | Conditional older Claude fallback |
| `claude-sonnet-4-5` | `claude-sonnet-4-5` | 200000 | 64000 | Conditional older Claude fallback |
| `claude-haiku-4-5` | `claude-haiku-4-5` | 200000 | 64000 | Conditional older Claude fallback |

Configure older Claude Foundry catalog entries only when the Foundry catalog/provider
accepts requests without the deprecated `temperature` parameter. Do not select
`claude-opus-5`, `claude-sonnet-5`, or `claude-fable-5` until the provider stops
sending `temperature`.

## Reasoning model parameter guardrail

Do not configure sampling parameters for reasoning models. In particular, omit
`temperature`, `top_p`, penalties, `logprobs`, `logit_bias`, and `max_tokens`.
