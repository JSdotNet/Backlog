# Copilot Model Selection Overrides

Backlog is a .NET Aspire product (`src/`, `tests/`, `harness/`) that also carries an
unusually large checked-in knowledge base (`.arc42/`, `.domain/`, `.backlog/`,
`.tech/`, `.design/`) driving what gets built. Use the Azure Foundry OpenAI models
below for orchestration and specialist runs until Claude 5 works with the active
Anthropic provider.

The current Anthropic provider injects the deprecated `temperature` parameter, which
causes Claude 5 models to fail. Do not select `claude-opus-5`, `claude-sonnet-5`, or
`claude-fable-5` until that provider stops sending `temperature`.

## Recommended models

| Use | Model | Wire model | Max prompt tokens | Max output tokens |
| --- | --- | --- | ---: | ---: |
| Default | `gpt-5.4` | `gpt-5.4` | 922000 | 128000 |
| Premium fallback | `gpt-5.5` | `gpt-5.5` | 922000 | 128000 |
| Fast or cheaper routine work | `gpt-5.6-luna` | `gpt-5.6-luna` | 922000 | 128000 |
| Optional balanced alternative | `gpt-5.6-sol` | `gpt-5.6-sol` | 922000 | 128000 |

## Conditional Claude fallbacks

Use these older Claude models only if the active provider accepts them without
injecting unsupported parameters.

| Model | Wire model | Max prompt tokens | Max output tokens |
| --- | --- | ---: | ---: |
| `claude-sonnet-4-6` | `claude-sonnet-4-6` | 1000000 | 128000 |
| `claude-sonnet-4-5` | `claude-sonnet-4-5` | 200000 | 64000 |
| `claude-haiku-4-5` | `claude-haiku-4-5` | 200000 | 64000 |

## Reasoning model parameter guardrail

Do not configure sampling parameters for reasoning models. In particular, omit
`temperature`, `top_p`, penalties, `logprobs`, `logit_bias`, and `max_tokens`.
